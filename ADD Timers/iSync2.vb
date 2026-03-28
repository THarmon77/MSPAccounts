Public Class clsSync2
    Implements LabTech.Interfaces.ISync2
    Private m_Host As LabTech.Interfaces.IControlCenter

    Public Sub Initialize(ByVal Host As LabTech.Interfaces.IControlCenter) Implements LabTech.Interfaces.ISync2.Initialize
        m_Host = Host
    End Sub

    Public Sub Decommision() Implements LabTech.Interfaces.ISync2.Decommision
        m_Host = Nothing
    End Sub

    Public ReadOnly Property Name As String Implements LabTech.Interfaces.ISync2.Name
        Get
            Return PluginName & "_ISync2_v" & mVersion
        End Get
    End Property

    ''' <summary>
    ''' Called every 6 minutes by CW Automate.
    ''' 1. Detects newly-online machines in deployed locations that are missing TritonTech.
    ''' 2. If v2 migration is pending, dispatches migration for machines with legacy accounts.
    ''' </summary>
    Public Sub Syncronize() Implements LabTech.Interfaces.ISync2.Syncronize
        Try
            DeployToNewMachines()
            If MigrationManager.IsMigrationPending(m_Host) Then
                MigrationManager.MigrateRecentMachines(m_Host)
            End If
        Catch ex As Exception
            Logger.Err(m_Host, "iSync2: Syncronize fatal error: " & ex.Message)
        End Try
    End Sub

    Private Sub DeployToNewMachines()
        Dim targetHours As String = m_Host.GetSQL(
            "SELECT SettingValue FROM `" & TblSettings & "` WHERE SettingKey = 'target_last_contact_hours'")
        If targetHours Is Nothing OrElse targetHours = "-9999" Then targetHours = "1"

        Dim osFilter As String = m_Host.GetSQL(
            "SELECT SettingValue FROM `" & TblSettings & "` WHERE SettingKey = 'target_os_filter'")
        If osFilter Is Nothing OrElse osFilter = "-9999" Then osFilter = "NOT LIKE '%Server%'"

        ' Find machines that are online and in a deployed location but missing TritonTech
        Dim ds As DataSet = m_Host.GetDataSet(
            "SELECT c.ComputerID, c.LocationID, c.ComputerName " &
            "FROM computers c " &
            "INNER JOIN `" & TblDeployments & "` d ON d.LocationID = c.LocationID " &
            "WHERE d.Status = 1 " &
            "AND d.PasswordID IS NOT NULL " &
            "AND c.OS " & osFilter & " " &
            "AND TIMESTAMPDIFF(HOUR, c.LastContact, NOW()) < " & targetHours & " " &
            "AND (c.UserAccounts IS NULL OR c.UserAccounts NOT LIKE '%:" & AccountName & ":%')")

        If ds Is Nothing OrElse ds.Tables.Count = 0 Then Return

        For Each row As DataRow In ds.Tables(0).Rows
            Dim computerID As Integer = CInt(row("ComputerID"))
            Dim locationID As Integer = CInt(row("LocationID"))
            Dim computerName As String = row("ComputerName").ToString()
            Try
                Dim passwordIDStr As String = m_Host.GetSQL(
                    "SELECT PasswordID FROM `" & TblDeployments & "` WHERE LocationID = " & locationID)
                If passwordIDStr Is Nothing OrElse passwordIDStr = "-9999" Then
                    Logger.Warn(m_Host, "iSync2: No PasswordID for LocationID=" & locationID & ", skipping " & computerName)
                    Continue For
                End If
                Dim password As String = PasswordManager.ReadCWPassword(m_Host, CInt(passwordIDStr))
                If String.IsNullOrEmpty(password) Then
                    Logger.Warn(m_Host, "iSync2: Cannot read password for LocationID=" & locationID & ", skipping " & computerName)
                    Continue For
                End If
                AccountDeployer.DeployAccount(m_Host, computerID, password)
                Logger.Info(m_Host, "iSync2: Deployed to new machine " & computerName & " (ComputerID=" & computerID & ")")
            Catch ex As Exception
                Logger.Err(m_Host, "iSync2: Deploy failed for " & computerName & " (ID=" & computerID & "): " & ex.Message)
            End Try
        Next
    End Sub

End Class
