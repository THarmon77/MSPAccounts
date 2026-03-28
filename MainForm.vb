Imports System.Windows.Forms
Imports System.Drawing
Imports System.ComponentModel

''' <summary>
''' Main plugin UI — 4 tabs: Dashboard, Locations, Settings, Logs.
''' All DB access goes through IControlCenter host methods (GetDataSet/GetSQL/SetSQL).
''' Long operations (deploy, rotate) use BackgroundWorker to avoid freezing the UI.
''' </summary>
Public Class MainForm
    Inherits Form

    Private ReadOnly _host As LabTech.Interfaces.IControlCenter

    ' Tab pages
    Private _tabControl As TabControl
    Private _tabDashboard As TabPage
    Private _tabLocations As TabPage
    Private _tabSettings As TabPage
    Private _tabLogs As TabPage

    ' Dashboard controls
    Private _dgvDashboard As DataGridView
    Private _btnRefreshDashboard As Button
    Private _lblDashInfo As Label

    ' Locations controls
    Private _dgvLocations As DataGridView
    Private _btnEnableLocation As Button
    Private _btnDisableLocation As Button
    Private _btnDeployNow As Button
    Private _btnRotateNow As Button
    Private _lblLocationsStatus As Label

    ' Settings controls
    Private _dgvSettings As DataGridView
    Private _btnSaveSettings As Button
    Private _lblSettingsStatus As Label

    ' Logs controls
    Private _dgvLogs As DataGridView
    Private _btnRefreshLogs As Button
    Private _lblLogsStatus As Label

    ' Background workers for long-running operations
    Private _deployWorker As BackgroundWorker
    Private _rotateWorker As BackgroundWorker

    Public Sub New(host As LabTech.Interfaces.IControlCenter)
        _host = host
        InitializeComponent()
        LoadDashboard()
        LoadLocations()
        LoadSettings()
        LoadLogs()
    End Sub

    Private Sub InitializeComponent()
        Me.Text = PluginName & " v" & mVersion
        Me.Size = New Size(1040, 680)
        Me.MinimumSize = New Size(800, 520)
        Me.StartPosition = FormStartPosition.CenterScreen
        Me.Font = New Font("Segoe UI", 9.0F)

        _tabControl = New TabControl()
        _tabControl.Dock = DockStyle.Fill

        _tabDashboard = New TabPage("Dashboard")
        _tabLocations = New TabPage("Locations")
        _tabSettings = New TabPage("Settings")
        _tabLogs = New TabPage("Logs")

        _tabControl.TabPages.Add(_tabDashboard)
        _tabControl.TabPages.Add(_tabLocations)
        _tabControl.TabPages.Add(_tabSettings)
        _tabControl.TabPages.Add(_tabLogs)

        Me.Controls.Add(_tabControl)

        BuildDashboardTab()
        BuildLocationsTab()
        BuildSettingsTab()
        BuildLogsTab()
        BuildWorkers()
    End Sub

    ' =========================================================
    ' DASHBOARD TAB
    ' =========================================================

    Private Sub BuildDashboardTab()
        _dgvDashboard = New DataGridView()
        _dgvDashboard.Dock = DockStyle.Fill
        _dgvDashboard.ReadOnly = True
        _dgvDashboard.AllowUserToAddRows = False
        _dgvDashboard.AllowUserToDeleteRows = False
        _dgvDashboard.SelectionMode = DataGridViewSelectionMode.FullRowSelect
        _dgvDashboard.RowHeadersVisible = False
        _dgvDashboard.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells
        _dgvDashboard.BackgroundColor = SystemColors.Window
        _dgvDashboard.BorderStyle = BorderStyle.None

        Dim pnl As New Panel()
        pnl.Dock = DockStyle.Top
        pnl.Height = 38
        pnl.Padding = New Padding(4, 4, 4, 0)

        _btnRefreshDashboard = New Button()
        _btnRefreshDashboard.Text = "Refresh"
        _btnRefreshDashboard.Size = New Size(75, 26)
        _btnRefreshDashboard.Location = New Point(4, 4)
        AddHandler _btnRefreshDashboard.Click, AddressOf BtnRefreshDashboard_Click

        _lblDashInfo = New Label()
        _lblDashInfo.AutoSize = True
        _lblDashInfo.Location = New Point(86, 8)
        _lblDashInfo.ForeColor = Color.Gray

        pnl.Controls.Add(_btnRefreshDashboard)
        pnl.Controls.Add(_lblDashInfo)

        _tabDashboard.Controls.Add(_dgvDashboard)
        _tabDashboard.Controls.Add(pnl)
    End Sub

    Private Sub LoadDashboard()
        Try
            Dim ds As DataSet = _host.GetDataSet(
                "SELECT d.LocationID, d.LocationName, " &
                "CASE d.Status WHEN 0 THEN 'Not Deployed' WHEN 1 THEN 'Active' " &
                "WHEN 2 THEN 'Error' WHEN 3 THEN 'Migrating' ELSE 'Unknown' END AS Status, " &
                "d.MachineCount, d.DeployedCount, d.ErrorCount, " &
                "d.LastRotated, d.RotationDue, d.LastError " &
                "FROM `" & TblDeployments & "` d ORDER BY d.LocationName")
            If ds IsNot Nothing AndAlso ds.Tables.Count > 0 Then
                _dgvDashboard.DataSource = ds.Tables(0)
                ColorRows(_dgvDashboard, "Status",
                          "Active", Color.FromArgb(220, 255, 220),
                          "Error", Color.FromArgb(255, 220, 220),
                          "Migrating", Color.FromArgb(255, 255, 200))
                _lblDashInfo.Text = ds.Tables(0).Rows.Count & " locations  |  " & Now.ToString("HH:mm:ss")
                _lblDashInfo.ForeColor = Color.Gray
            Else
                _lblDashInfo.Text = "No deployment records found."
            End If
        Catch ex As Exception
            _lblDashInfo.Text = "Error: " & ex.Message
            _lblDashInfo.ForeColor = Color.Red
        End Try
    End Sub

    Private Sub BtnRefreshDashboard_Click(sender As Object, e As EventArgs)
        LoadDashboard()
    End Sub

    ' =========================================================
    ' LOCATIONS TAB
    ' =========================================================

    Private Sub BuildLocationsTab()
        _dgvLocations = New DataGridView()
        _dgvLocations.Dock = DockStyle.Fill
        _dgvLocations.ReadOnly = True
        _dgvLocations.AllowUserToAddRows = False
        _dgvLocations.AllowUserToDeleteRows = False
        _dgvLocations.SelectionMode = DataGridViewSelectionMode.FullRowSelect
        _dgvLocations.RowHeadersVisible = False
        _dgvLocations.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells
        _dgvLocations.BackgroundColor = SystemColors.Window
        _dgvLocations.BorderStyle = BorderStyle.None

        Dim pnl As New Panel()
        pnl.Dock = DockStyle.Top
        pnl.Height = 70
        pnl.Padding = New Padding(4, 4, 4, 0)

        _btnEnableLocation = New Button()
        _btnEnableLocation.Text = "Enable Location"
        _btnEnableLocation.Size = New Size(120, 26)
        _btnEnableLocation.Location = New Point(4, 4)
        AddHandler _btnEnableLocation.Click, AddressOf BtnEnableLocation_Click

        _btnDisableLocation = New Button()
        _btnDisableLocation.Text = "Disable Location"
        _btnDisableLocation.Size = New Size(120, 26)
        _btnDisableLocation.Location = New Point(130, 4)
        AddHandler _btnDisableLocation.Click, AddressOf BtnDisableLocation_Click

        _btnDeployNow = New Button()
        _btnDeployNow.Text = "Deploy Now"
        _btnDeployNow.Size = New Size(100, 26)
        _btnDeployNow.Location = New Point(268, 4)
        AddHandler _btnDeployNow.Click, AddressOf BtnDeployNow_Click

        _btnRotateNow = New Button()
        _btnRotateNow.Text = "Rotate Password"
        _btnRotateNow.Size = New Size(120, 26)
        _btnRotateNow.Location = New Point(374, 4)
        AddHandler _btnRotateNow.Click, AddressOf BtnRotateNow_Click

        _lblLocationsStatus = New Label()
        _lblLocationsStatus.AutoSize = True
        _lblLocationsStatus.Location = New Point(4, 38)
        _lblLocationsStatus.ForeColor = Color.Gray

        pnl.Controls.Add(_btnEnableLocation)
        pnl.Controls.Add(_btnDisableLocation)
        pnl.Controls.Add(_btnDeployNow)
        pnl.Controls.Add(_btnRotateNow)
        pnl.Controls.Add(_lblLocationsStatus)

        _tabLocations.Controls.Add(_dgvLocations)
        _tabLocations.Controls.Add(pnl)
    End Sub

    Private Sub LoadLocations()
        Try
            Dim ds As DataSet = _host.GetDataSet(
                "SELECT l.LocationID, l.Name AS LocationName, " &
                "CASE d.Status WHEN 0 THEN 'Not Deployed' WHEN 1 THEN 'Active' " &
                "WHEN 2 THEN 'Error' WHEN 3 THEN 'Migrating' ELSE 'Not Configured' END AS Status, " &
                "d.PasswordID, d.MachineCount, d.DeployedCount, d.RotationDue " &
                "FROM locations l " &
                "LEFT JOIN `" & TblDeployments & "` d ON d.LocationID = l.LocationID " &
                "WHERE l.LocationID > 1 ORDER BY l.Name")
            If ds IsNot Nothing AndAlso ds.Tables.Count > 0 Then
                _dgvLocations.DataSource = ds.Tables(0)
                ColorRows(_dgvLocations, "Status",
                          "Active", Color.FromArgb(220, 255, 220),
                          "Error", Color.FromArgb(255, 220, 220),
                          "Migrating", Color.FromArgb(255, 255, 200))
                _lblLocationsStatus.Text = ds.Tables(0).Rows.Count & " locations"
                _lblLocationsStatus.ForeColor = Color.Gray
            End If
        Catch ex As Exception
            _lblLocationsStatus.Text = "Error loading locations: " & ex.Message
            _lblLocationsStatus.ForeColor = Color.Red
        End Try
    End Sub

    Private Function GetSelectedLocationID() As Integer
        If _dgvLocations.SelectedRows.Count = 0 Then
            MessageBox.Show("Select a location first.", PluginName,
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return -1
        End If
        Return CInt(_dgvLocations.SelectedRows(0).Cells("LocationID").Value)
    End Function

    Private Sub BtnEnableLocation_Click(sender As Object, e As EventArgs)
        Dim locationID As Integer = GetSelectedLocationID()
        If locationID < 0 Then Return
        Try
            Dim existingPW As String = _host.GetSQL(
                "SELECT COALESCE(PasswordID, 0) FROM `" & TblDeployments & "` WHERE LocationID = " & locationID)
            If existingPW Is Nothing OrElse existingPW = "-9999" OrElse existingPW = "0" Then
                Dim newPassword As String = PasswordManager.GeneratePassword(_host)
                Dim pwID As Integer = PasswordManager.CreateCWPassword(_host, locationID, newPassword)
                If pwID = 0 Then
                    MessageBox.Show("Failed to create CW password entry.", PluginName,
                                    MessageBoxButtons.OK, MessageBoxIcon.Error)
                    Return
                End If
                _host.SetSQL(
                    "UPDATE `" & TblDeployments & "` " &
                    "SET Status = 1, PasswordID = " & pwID & ", " &
                    "RotationDue = DATE_ADD(NOW(), INTERVAL " & DefaultRotationDays & " DAY), " &
                    "UpdatedAt = NOW() WHERE LocationID = " & locationID)
            Else
                _host.SetSQL(
                    "UPDATE `" & TblDeployments & "` " &
                    "SET Status = 1, UpdatedAt = NOW() WHERE LocationID = " & locationID)
            End If
            _lblLocationsStatus.Text = "Location enabled. iSync2 will deploy to online machines within 6 minutes."
            _lblLocationsStatus.ForeColor = Color.Green
            LoadLocations()
            LoadDashboard()
        Catch ex As Exception
            _lblLocationsStatus.Text = "Error: " & ex.Message
            _lblLocationsStatus.ForeColor = Color.Red
        End Try
    End Sub

    Private Sub BtnDisableLocation_Click(sender As Object, e As EventArgs)
        Dim locationID As Integer = GetSelectedLocationID()
        If locationID < 0 Then Return
        If MessageBox.Show(
            "Disable this location? The TritonTech account will NOT be removed from machines " &
            "(must be removed manually). Timers will ignore this location.",
            PluginName, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then Return
        Try
            _host.SetSQL(
                "UPDATE `" & TblDeployments & "` " &
                "SET Status = 0, UpdatedAt = NOW() WHERE LocationID = " & locationID)
            _lblLocationsStatus.Text = "Location disabled."
            _lblLocationsStatus.ForeColor = Color.Gray
            LoadLocations()
            LoadDashboard()
        Catch ex As Exception
            _lblLocationsStatus.Text = "Error: " & ex.Message
            _lblLocationsStatus.ForeColor = Color.Red
        End Try
    End Sub

    Private Sub BtnDeployNow_Click(sender As Object, e As EventArgs)
        Dim locationID As Integer = GetSelectedLocationID()
        If locationID < 0 Then Return
        If _deployWorker.IsBusy Then
            MessageBox.Show("A deployment is already running.", PluginName,
                            MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        SetLocationButtonsEnabled(False)
        _lblLocationsStatus.Text = "Deploying to online machines... (may take several minutes)"
        _lblLocationsStatus.ForeColor = Color.Navy
        _deployWorker.RunWorkerAsync(locationID)
    End Sub

    Private Sub BtnRotateNow_Click(sender As Object, e As EventArgs)
        Dim locationID As Integer = GetSelectedLocationID()
        If locationID < 0 Then Return
        If _rotateWorker.IsBusy Then
            MessageBox.Show("A rotation is already running.", PluginName,
                            MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        Dim pwIDStr As String = _host.GetSQL(
            "SELECT COALESCE(PasswordID, 0) FROM `" & TblDeployments & "` WHERE LocationID = " & locationID)
        If pwIDStr Is Nothing OrElse pwIDStr = "-9999" OrElse pwIDStr = "0" Then
            MessageBox.Show("No password entry found. Enable the location first.", PluginName,
                            MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        If MessageBox.Show(
            "Generate a new password, write it to the CW Passwords tab, and push to all online machines in this location?",
            PluginName, MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
        SetLocationButtonsEnabled(False)
        _lblLocationsStatus.Text = "Rotating password on all online machines... (may take several minutes)"
        _lblLocationsStatus.ForeColor = Color.Navy
        _rotateWorker.RunWorkerAsync(locationID)
    End Sub

    Private Sub SetLocationButtonsEnabled(enabled As Boolean)
        _btnEnableLocation.Enabled = enabled
        _btnDisableLocation.Enabled = enabled
        _btnDeployNow.Enabled = enabled
        _btnRotateNow.Enabled = enabled
    End Sub

    ' =========================================================
    ' SETTINGS TAB
    ' =========================================================

    Private Sub BuildSettingsTab()
        _dgvSettings = New DataGridView()
        _dgvSettings.Dock = DockStyle.Fill
        _dgvSettings.AllowUserToAddRows = False
        _dgvSettings.AllowUserToDeleteRows = False
        _dgvSettings.SelectionMode = DataGridViewSelectionMode.CellSelect
        _dgvSettings.RowHeadersVisible = False
        _dgvSettings.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.AllCells
        _dgvSettings.BackgroundColor = SystemColors.Window
        _dgvSettings.BorderStyle = BorderStyle.None
        _dgvSettings.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2

        Dim pnl As New Panel()
        pnl.Dock = DockStyle.Top
        pnl.Height = 38
        pnl.Padding = New Padding(4, 4, 4, 0)

        _btnSaveSettings = New Button()
        _btnSaveSettings.Text = "Save Changes"
        _btnSaveSettings.Size = New Size(110, 26)
        _btnSaveSettings.Location = New Point(4, 4)
        AddHandler _btnSaveSettings.Click, AddressOf BtnSaveSettings_Click

        _lblSettingsStatus = New Label()
        _lblSettingsStatus.AutoSize = True
        _lblSettingsStatus.Location = New Point(122, 8)
        _lblSettingsStatus.ForeColor = Color.Gray
        _lblSettingsStatus.Text = "Edit SettingValue cells, then click Save Changes."

        pnl.Controls.Add(_btnSaveSettings)
        pnl.Controls.Add(_lblSettingsStatus)

        _tabSettings.Controls.Add(_dgvSettings)
        _tabSettings.Controls.Add(pnl)
    End Sub

    Private Sub LoadSettings()
        Try
            Dim ds As DataSet = _host.GetDataSet(
                "SELECT SettingKey, SettingValue, UpdatedAt " &
                "FROM `" & TblSettings & "` ORDER BY SettingKey")
            If ds IsNot Nothing AndAlso ds.Tables.Count > 0 Then
                _dgvSettings.DataSource = ds.Tables(0)
                If _dgvSettings.Columns.Contains("SettingKey") Then
                    _dgvSettings.Columns("SettingKey").ReadOnly = True
                    _dgvSettings.Columns("SettingKey").Width = 220
                End If
                If _dgvSettings.Columns.Contains("UpdatedAt") Then
                    _dgvSettings.Columns("UpdatedAt").ReadOnly = True
                End If
            End If
        Catch ex As Exception
            _lblSettingsStatus.Text = "Error loading: " & ex.Message
            _lblSettingsStatus.ForeColor = Color.Red
        End Try
    End Sub

    Private Sub BtnSaveSettings_Click(sender As Object, e As EventArgs)
        Try
            Dim saved As Integer = 0
            For Each row As DataGridViewRow In _dgvSettings.Rows
                If row.IsNewRow Then Continue For
                If row.Cells("SettingKey").Value Is Nothing Then Continue For
                Dim key As String = row.Cells("SettingKey").Value.ToString()
                Dim val As String = ""
                If row.Cells("SettingValue").Value IsNot Nothing Then
                    val = row.Cells("SettingValue").Value.ToString()
                End If
                _host.SetSQL(
                    "UPDATE `" & TblSettings & "` " &
                    "SET SettingValue = '" & val.Replace("'", "''") & "', UpdatedAt = NOW() " &
                    "WHERE SettingKey = '" & key.Replace("'", "''") & "'")
                saved += 1
            Next
            _lblSettingsStatus.Text = saved & " settings saved at " & Now.ToString("HH:mm:ss")
            _lblSettingsStatus.ForeColor = Color.Green
            LoadSettings()
        Catch ex As Exception
            _lblSettingsStatus.Text = "Error saving: " & ex.Message
            _lblSettingsStatus.ForeColor = Color.Red
        End Try
    End Sub

    ' =========================================================
    ' LOGS TAB
    ' =========================================================

    Private Sub BuildLogsTab()
        _dgvLogs = New DataGridView()
        _dgvLogs.Dock = DockStyle.Fill
        _dgvLogs.ReadOnly = True
        _dgvLogs.AllowUserToAddRows = False
        _dgvLogs.AllowUserToDeleteRows = False
        _dgvLogs.SelectionMode = DataGridViewSelectionMode.FullRowSelect
        _dgvLogs.RowHeadersVisible = False
        _dgvLogs.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
        _dgvLogs.BackgroundColor = SystemColors.Window
        _dgvLogs.BorderStyle = BorderStyle.None

        Dim pnl As New Panel()
        pnl.Dock = DockStyle.Top
        pnl.Height = 38
        pnl.Padding = New Padding(4, 4, 4, 0)

        _btnRefreshLogs = New Button()
        _btnRefreshLogs.Text = "Refresh"
        _btnRefreshLogs.Size = New Size(75, 26)
        _btnRefreshLogs.Location = New Point(4, 4)
        AddHandler _btnRefreshLogs.Click, AddressOf BtnRefreshLogs_Click

        _lblLogsStatus = New Label()
        _lblLogsStatus.AutoSize = True
        _lblLogsStatus.Location = New Point(86, 8)
        _lblLogsStatus.ForeColor = Color.Gray

        pnl.Controls.Add(_btnRefreshLogs)
        pnl.Controls.Add(_lblLogsStatus)

        _tabLogs.Controls.Add(_dgvLogs)
        _tabLogs.Controls.Add(pnl)
    End Sub

    Private Sub LoadLogs()
        Try
            ' Try errorlog table first — filtered to [TritonAM] entries
            Dim ds As DataSet = _host.GetDataSet(
                "SELECT TimeStamp, Message FROM errorlog " &
                "WHERE Message LIKE '%[TritonAM]%' " &
                "ORDER BY TimeStamp DESC LIMIT 200")
            If ds IsNot Nothing AndAlso ds.Tables.Count > 0 AndAlso ds.Tables(0).Rows.Count > 0 Then
                _dgvLogs.DataSource = ds.Tables(0)
                _lblLogsStatus.Text = ds.Tables(0).Rows.Count & " entries from errorlog"
                Return
            End If
        Catch
            ' errorlog may not be accessible from plugin context — fall through
        End Try
        Try
            ' Fallback: show deployment activity summary from our own table
            Dim ds2 As DataSet = _host.GetDataSet(
                "SELECT UpdatedAt AS ActivityTime, LocationName, " &
                "CASE Status WHEN 0 THEN 'Not Deployed' WHEN 1 THEN 'Active' " &
                "WHEN 2 THEN 'Error' WHEN 3 THEN 'Migrating' ELSE 'Unknown' END AS Status, " &
                "DeployedCount, ErrorCount, LastError " &
                "FROM `" & TblDeployments & "` " &
                "WHERE UpdatedAt IS NOT NULL " &
                "ORDER BY UpdatedAt DESC")
            If ds2 IsNot Nothing AndAlso ds2.Tables.Count > 0 Then
                _dgvLogs.DataSource = ds2.Tables(0)
                _lblLogsStatus.Text = "Showing deployment activity (errorlog not accessible in this context)"
            End If
        Catch ex As Exception
            _lblLogsStatus.Text = "Error loading logs: " & ex.Message
            _lblLogsStatus.ForeColor = Color.Red
        End Try
    End Sub

    Private Sub BtnRefreshLogs_Click(sender As Object, e As EventArgs)
        LoadLogs()
    End Sub

    ' =========================================================
    ' BACKGROUND WORKERS
    ' =========================================================

    Private Sub BuildWorkers()
        _deployWorker = New BackgroundWorker()
        AddHandler _deployWorker.DoWork, AddressOf DeployWorker_DoWork
        AddHandler _deployWorker.RunWorkerCompleted, AddressOf DeployWorker_Completed

        _rotateWorker = New BackgroundWorker()
        AddHandler _rotateWorker.DoWork, AddressOf RotateWorker_DoWork
        AddHandler _rotateWorker.RunWorkerCompleted, AddressOf RotateWorker_Completed
    End Sub

    Private Sub DeployWorker_DoWork(sender As Object, e As DoWorkEventArgs)
        Dim locationID As Integer = CInt(e.Argument)
        Try
            Dim pwIDStr As String = _host.GetSQL(
                "SELECT COALESCE(PasswordID, 0) FROM `" & TblDeployments & "` WHERE LocationID = " & locationID)
            If pwIDStr Is Nothing OrElse pwIDStr = "-9999" OrElse pwIDStr = "0" Then
                e.Result = "Error: No password entry. Enable the location first."
                Return
            End If
            Dim password As String = PasswordManager.ReadCWPassword(_host, CInt(pwIDStr))
            If String.IsNullOrEmpty(password) Then
                e.Result = "Error: Could not read password from CW vault."
                Return
            End If

            Dim targetHours As String = _host.GetSQL(
                "SELECT SettingValue FROM `" & TblSettings & "` WHERE SettingKey = 'target_last_contact_hours'")
            If targetHours Is Nothing OrElse targetHours = "-9999" Then targetHours = "1"
            Dim osFilter As String = _host.GetSQL(
                "SELECT SettingValue FROM `" & TblSettings & "` WHERE SettingKey = 'target_os_filter'")
            If osFilter Is Nothing OrElse osFilter = "-9999" Then osFilter = "NOT LIKE '%Server%'"

            Dim ds As DataSet = _host.GetDataSet(
                "SELECT c.ComputerID, c.ComputerName FROM computers c " &
                "WHERE c.LocationID = " & locationID & " " &
                "AND c.OS " & osFilter & " " &
                "AND TIMESTAMPDIFF(HOUR, c.LastContact, NOW()) < " & targetHours & " " &
                "AND (c.UserAccounts IS NULL OR c.UserAccounts NOT LIKE '%:" & AccountName & ":%')")

            If ds Is Nothing OrElse ds.Tables.Count = 0 OrElse ds.Tables(0).Rows.Count = 0 Then
                e.Result = "No eligible machines found (online, non-server, missing TritonTech)."
                Return
            End If

            Dim deployed As Integer = 0
            Dim errors As Integer = 0
            For Each row As DataRow In ds.Tables(0).Rows
                Try
                    AccountDeployer.DeployAccount(_host, CInt(row("ComputerID")), password)
                    deployed += 1
                Catch ex As Exception
                    errors += 1
                    Logger.Err(_host, "MainForm.DeployNow: " & row("ComputerName").ToString() & ": " & ex.Message)
                End Try
            Next
            _host.SetSQL(
                "UPDATE `" & TblDeployments & "` " &
                "SET DeployedCount = " & deployed & ", ErrorCount = ErrorCount + " & errors & ", " &
                "UpdatedAt = NOW() WHERE LocationID = " & locationID)
            e.Result = "Done: " & deployed & " deployed, " & errors & " error(s)."
        Catch ex As Exception
            e.Result = "Error: " & ex.Message
        End Try
    End Sub

    Private Sub DeployWorker_Completed(sender As Object, e As RunWorkerCompletedEventArgs)
        SetLocationButtonsEnabled(True)
        If e.Result IsNot Nothing Then
            _lblLocationsStatus.Text = e.Result.ToString()
            If e.Result.ToString().StartsWith("Error") Then
                _lblLocationsStatus.ForeColor = Color.Red
            Else
                _lblLocationsStatus.ForeColor = Color.Green
            End If
        End If
        LoadLocations()
        LoadDashboard()
    End Sub

    Private Sub RotateWorker_DoWork(sender As Object, e As DoWorkEventArgs)
        Dim locationID As Integer = CInt(e.Argument)
        Try
            Dim pwIDStr As String = _host.GetSQL(
                "SELECT COALESCE(PasswordID, 0) FROM `" & TblDeployments & "` WHERE LocationID = " & locationID)
            If pwIDStr Is Nothing OrElse pwIDStr = "-9999" OrElse pwIDStr = "0" Then
                e.Result = "Error: No password entry found."
                Return
            End If
            Dim newPassword As String = PasswordManager.GeneratePassword(_host)
            PasswordManager.UpdateCWPassword(_host, CInt(pwIDStr), newPassword)
            AccountDeployer.RotatePasswordAllMachines(_host, locationID, newPassword)
            _host.SetSQL(
                "UPDATE `" & TblDeployments & "` " &
                "SET LastRotated = NOW(), " &
                "RotationDue = DATE_ADD(NOW(), INTERVAL " & DefaultRotationDays & " DAY), " &
                "UpdatedAt = NOW() WHERE LocationID = " & locationID)
            e.Result = "Rotation complete. New password written to CW Passwords tab."
        Catch ex As Exception
            e.Result = "Error: " & ex.Message
        End Try
    End Sub

    Private Sub RotateWorker_Completed(sender As Object, e As RunWorkerCompletedEventArgs)
        SetLocationButtonsEnabled(True)
        If e.Result IsNot Nothing Then
            _lblLocationsStatus.Text = e.Result.ToString()
            If e.Result.ToString().StartsWith("Error") Then
                _lblLocationsStatus.ForeColor = Color.Red
            Else
                _lblLocationsStatus.ForeColor = Color.Green
            End If
        End If
        LoadLocations()
        LoadDashboard()
    End Sub

    ' =========================================================
    ' HELPERS
    ' =========================================================

    ''' <summary>
    ''' Colors DataGridView rows based on the value of a specific column.
    ''' statusColorPairs: alternating (statusText, color) pairs.
    ''' Non-matched rows get SystemColors.Window (default).
    ''' </summary>
    Private Sub ColorRows(grid As DataGridView, columnName As String,
                          ParamArray statusColorPairs As Object())
        If Not grid.Columns.Contains(columnName) Then Return
        For Each row As DataGridViewRow In grid.Rows
            If row.IsNewRow Then Continue For
            Dim cellVal As String = ""
            If row.Cells(columnName).Value IsNot Nothing Then
                cellVal = row.Cells(columnName).Value.ToString()
            End If
            Dim matched As Boolean = False
            Dim i As Integer = 0
            Do While i < statusColorPairs.Length - 1
                If cellVal = statusColorPairs(i).ToString() Then
                    row.DefaultCellStyle.BackColor = CType(statusColorPairs(i + 1), Color)
                    matched = True
                    Exit Do
                End If
                i += 2
            Loop
            If Not matched Then
                row.DefaultCellStyle.BackColor = SystemColors.Window
            End If
        Next
    End Sub

End Class
