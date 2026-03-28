Imports System.Text

''' <summary>
''' Handles one-time migration from v2 legacy accounts (TT_Service, MSP_Admin, etc.)
''' to the new TritonTech managed account.
'''
''' Migration flow per machine:
'''   1. Deploy TritonTech with the location's current password
'''   2. Remove each detected legacy account
'''
''' Controlled by settings: migration_enabled ('0'/'1') and v2_migrated ('0'/'1').
''' When v2_migrated='0', iSync2 will call MigrateRecentMachines every 6 minutes
''' until all legacy accounts are gone, then MarkMigrationComplete() is called.
''' </summary>
Public Module MigrationManager

    ''' <summary>
    ''' Migrates a single machine: deploys TritonTech, then removes each legacy account.
    ''' DeployAccount failure is non-fatal (TritonTech may already exist).
    ''' </summary>
    Public Sub MigrateComputer(host As LabTech.Interfaces.IControlCenter, computerID As Integer, password As String)
        Dim computerName As String = host.GetSQL("SELECT ComputerName FROM computers WHERE ComputerID = " & computerID)
        If computerName Is Nothing OrElse computerName = "-9999" Then computerName = "ComputerID=" & computerID

        Logger.Info(host, "MigrationManager: Starting migration on " & computerName)

        Try
            AccountDeployer.DeployAccount(host, computerID, password)
        Catch ex As Exception
            Logger.Warn(host, "MigrationManager: DeployAccount warning on " & computerName & ": " & ex.Message)
        End Try

        Dim legacyNames As String = host.GetSQL("SELECT SettingValue FROM `" & TblSettings & "` WHERE SettingKey = 'legacy_account_names'")
        If legacyNames Is Nothing OrElse legacyNames = "-9999" OrElse legacyNames = "" Then
            Logger.Info(host, "MigrationManager: No legacy account names configured, skipping removal on " & computerName)
            Return
        End If

        Dim userAccounts As String = host.GetSQL(
            "SELECT UserAccounts FROM computers WHERE ComputerID = " & computerID)
        If userAccounts Is Nothing OrElse userAccounts = "-9999" Then userAccounts = ""

        For Each name As String In legacyNames.Split(","c)
            Dim trimmed As String = name.Trim()
            If trimmed = "" Then Continue For
            If userAccounts.Contains(":" & trimmed & ":") Then
                Try
                    AccountDeployer.RunCommand(host, computerID, "net user " & trimmed & " /delete")
                    Logger.Info(host, "MigrationManager: Removed legacy account '" & trimmed & "' from " & computerName)
                Catch ex As Exception
                    Logger.Warn(host, "MigrationManager: Could not remove '" & trimmed & "' from " & computerName & ": " & ex.Message)
                End Try
            End If
        Next

        Logger.Info(host, "MigrationManager: Migration complete on " & computerName)
    End Sub

    ''' <summary>
    ''' Finds recently-seen machines in deployed locations that have legacy accounts
    ''' but not yet TritonTech, and queues migration for each.
    ''' Called by iSync2 every 6 minutes while migration is pending.
    ''' </summary>
    Public Sub MigrateRecentMachines(host As LabTech.Interfaces.IControlCenter)
        Dim targetHours As String = host.GetSQL("SELECT SettingValue FROM `" & TblSettings & "` WHERE SettingKey = 'target_last_contact_hours'")
        If targetHours Is Nothing OrElse targetHours = "-9999" Then targetHours = "1"

        Dim osFilter As String = host.GetSQL("SELECT SettingValue FROM `" & TblSettings & "` WHERE SettingKey = 'target_os_filter'")
        If osFilter Is Nothing OrElse osFilter = "-9999" Then osFilter = "NOT LIKE '%Server%'"

        Dim legacyNames As String = host.GetSQL("SELECT SettingValue FROM `" & TblSettings & "` WHERE SettingKey = 'legacy_account_names'")
        If legacyNames Is Nothing OrElse legacyNames = "-9999" OrElse legacyNames = "" Then Return

        ' Build OR conditions for each legacy account name
        Dim conditions As New StringBuilder()
        For Each name As String In legacyNames.Split(","c)
            Dim trimmed As String = name.Trim()
            If trimmed = "" Then Continue For
            If conditions.Length > 0 Then conditions.Append(" OR ")
            conditions.Append("c.UserAccounts LIKE '%:" & trimmed.Replace("'", "''") & ":%'")
        Next
        If conditions.Length = 0 Then Return

        Dim ds As DataSet = host.GetDataSet(
            "SELECT c.ComputerID, c.LocationID, c.ComputerName " &
            "FROM computers c " &
            "INNER JOIN `" & TblDeployments & "` d ON d.LocationID = c.LocationID " &
            "WHERE d.Status = 1 " &
            "AND d.PasswordID IS NOT NULL " &
            "AND c.OS " & osFilter & " " &
            "AND TIMESTAMPDIFF(HOUR, c.LastContact, NOW()) < " & targetHours & " " &
            "AND (c.UserAccounts IS NULL OR c.UserAccounts NOT LIKE '%:" & AccountName & ":%') " &
            "AND (" & conditions.ToString() & ")")

        If ds Is Nothing OrElse ds.Tables.Count = 0 Then Return

        For Each row As DataRow In ds.Tables(0).Rows
            Dim computerID As Integer = CInt(row("ComputerID"))
            Dim locationID As Integer = CInt(row("LocationID"))
            Dim computerName As String = row("ComputerName").ToString()
            Try
                Dim passwordIDStr As String = host.GetSQL(
                    "SELECT PasswordID FROM `" & TblDeployments & "` WHERE LocationID = " & locationID)
                If passwordIDStr Is Nothing OrElse passwordIDStr = "-9999" Then
                    Logger.Warn(host, "MigrationManager: No PasswordID for LocationID=" & locationID & ", skipping " & computerName)
                    Continue For
                End If
                Dim password As String = PasswordManager.ReadCWPassword(host, CInt(passwordIDStr))
                If String.IsNullOrEmpty(password) Then
                    Logger.Warn(host, "MigrationManager: Cannot read password for LocationID=" & locationID & ", skipping " & computerName)
                    Continue For
                End If
                MigrateComputer(host, computerID, password)
            Catch ex As Exception
                Logger.Err(host, "MigrationManager: Migration failed for " & computerName & " (ID=" & computerID & "): " & ex.Message)
            End Try
        Next
    End Sub

    ''' <summary>
    ''' Returns True if migration is enabled (migration_enabled='1') and not yet complete (v2_migrated='0').
    ''' </summary>
    Public Function IsMigrationPending(host As LabTech.Interfaces.IControlCenter) As Boolean
        Dim enabled As String = host.GetSQL("SELECT SettingValue FROM `" & TblSettings & "` WHERE SettingKey = 'migration_enabled'")
        Dim migrated As String = host.GetSQL("SELECT SettingValue FROM `" & TblSettings & "` WHERE SettingKey = 'v2_migrated'")
        Return (enabled = "1") AndAlso (migrated = "0")
    End Function

    ''' <summary>
    ''' Marks v2 migration as complete. Call after all known machines have been migrated.
    ''' </summary>
    Public Sub MarkMigrationComplete(host As LabTech.Interfaces.IControlCenter)
        host.SetSQL(
            "UPDATE `" & TblSettings & "` " &
            "SET SettingValue = '1', UpdatedAt = NOW() " &
            "WHERE SettingKey = 'v2_migrated'")
        Logger.Info(host, "MigrationManager: v2 migration marked complete")
    End Sub

End Module
