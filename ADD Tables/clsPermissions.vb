Public Class clsPermissions
    Implements LabTech.Interfaces.IPermissions
    ' Called by the DB agent on every CW Automate Control Center load.
    ' Creates and migrates plugin tables, seeds default settings.

    Private objHost As LabTech.Interfaces.IControlCenter

    Private Sub DoInitialSetup()
        Try
            CreateSettingsTable()
            CreateDeploymentsTable()
        Catch ex As Exception
            objHost.LogMessage("clsPermissions.DoInitialSetup Error: " & ex.Message)
        End Try
    End Sub

    Private Sub CreateSettingsTable()
        Dim checkResult As String = objHost.GetSQL("SHOW TABLES LIKE '" & TblSettings & "'")
        If checkResult IsNot Nothing AndAlso checkResult = "-9999" Then
            Dim sql As String =
                "CREATE TABLE `" & TblSettings & "` (" &
                "`SettingKey` VARCHAR(50) NOT NULL," &
                "`SettingValue` VARCHAR(500) DEFAULT NULL," &
                "`UpdatedAt` DATETIME DEFAULT NULL," &
                "PRIMARY KEY (`SettingKey`)" &
                ") ENGINE=InnoDB DEFAULT CHARSET=utf8"
            objHost.SetSQL(sql)
            InsertDefaultSettings()
        End If
    End Sub

    Private Sub InsertDefaultSettings()
        Dim defaults As String(,) = {
            {"account_name", "TritonTech"},
            {"account_full_name", "Triton Technologies Support"},
            {"account_comment", "Managed support account"},
            {"password_length", "20"},
            {"password_upper", "2"},
            {"password_lower", "2"},
            {"password_digit", "2"},
            {"password_special", "2"},
            {"rotation_days", "90"},
            {"hide_from_login", "1"},
            {"target_os_filter", "NOT LIKE '%Server%'"},
            {"target_last_contact_hours", "1"},
            {"migration_enabled", "1"},
            {"legacy_account_names", "TT_Service,MSP_Admin"},
            {"v2_migrated", "0"}
        }
        For i As Integer = 0 To defaults.GetUpperBound(0)
            Dim key As String = defaults(i, 0).Replace("'", "''")
            Dim val As String = defaults(i, 1).Replace("'", "''")
            objHost.SetSQL(
                "INSERT INTO `" & TblSettings & "` (SettingKey, SettingValue, UpdatedAt) " &
                "VALUES ('" & key & "', '" & val & "', NOW())")
        Next
    End Sub

    Private Sub CreateDeploymentsTable()
        Dim checkResult As String = objHost.GetSQL("SHOW TABLES LIKE '" & TblDeployments & "'")
        If checkResult IsNot Nothing AndAlso checkResult = "-9999" Then
            Dim sql As String =
                "CREATE TABLE `" & TblDeployments & "` (" &
                "`LocationID` INT NOT NULL," &
                "`LocationName` VARCHAR(100) DEFAULT NULL," &
                "`PasswordID` INT DEFAULT NULL," &
                "`Status` TINYINT NOT NULL DEFAULT 0 COMMENT '0=Not Deployed,1=Deployed,2=Error,3=Migrating'," &
                "`LastRotated` DATETIME DEFAULT NULL," &
                "`RotationDue` DATETIME DEFAULT NULL," &
                "`MachineCount` INT NOT NULL DEFAULT 0," &
                "`DeployedCount` INT NOT NULL DEFAULT 0," &
                "`ErrorCount` INT NOT NULL DEFAULT 0," &
                "`LastError` VARCHAR(500) DEFAULT NULL," &
                "`CreatedAt` DATETIME DEFAULT NULL," &
                "`UpdatedAt` DATETIME DEFAULT NULL," &
                "PRIMARY KEY (`LocationID`)" &
                ") ENGINE=InnoDB DEFAULT CHARSET=utf8"
            objHost.SetSQL(sql)
            PopulateDeploymentsFromLocations()
        End If
    End Sub

    Private Sub PopulateDeploymentsFromLocations()
        ' Seed one row per active non-system location so the UI and timers have a complete list.
        Dim sql As String =
            "INSERT INTO `" & TblDeployments & "` (LocationID, LocationName, Status, CreatedAt) " &
            "SELECT l.LocationID, l.Name, 0, NOW() " &
            "FROM locations l " &
            "WHERE l.LocationID > 1 " &
            "AND NOT EXISTS (SELECT 1 FROM `" & TblDeployments & "` d WHERE d.LocationID = l.LocationID)"
        objHost.SetSQL(sql)
    End Sub

    Public Function GetPermissionSet(ByVal UserID As Integer, ByVal IsSuperAdmin As Boolean, ByVal UserClasses As String) As System.Collections.Hashtable Implements LabTech.Interfaces.IPermissions.GetPermissionSet
        Dim ht As New Hashtable
        Try
            ht.Add(TblSettings, "SELECT,INSERT,UPDATE,DELETE")
            ht.Add(TblDeployments, "SELECT,INSERT,UPDATE,DELETE")
        Catch ex As Exception
            objHost.LogMessage("clsPermissions.GetPermissionSet Error: " & ex.Message)
        End Try
        Return ht
    End Function

    Public Sub Initialize(ByVal Host As LabTech.Interfaces.IControlCenter) Implements LabTech.Interfaces.IPermissions.Initialize
        objHost = Host
        DoInitialSetup()
    End Sub

    Public Sub Decommision() Implements LabTech.Interfaces.IPermissions.Decommision
        objHost = Nothing
    End Sub

    Public ReadOnly Property Name() As String Implements LabTech.Interfaces.IPermissions.Name
        Get
            Return PluginName & " Permissions v" & mVersion
        End Get
    End Property

End Class
