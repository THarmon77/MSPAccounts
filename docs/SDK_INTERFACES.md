# ConnectWise Automate Plugin SDK — Interface Reference

**Sourced from:** https://developer.connectwise.com/Products/ConnectWise_Automate/Integrating_with_Automate/SDK_%2F%2F_Plugins/Interfaces  
**Accessed:** 2026-03-28  
**CW Automate version:** v25.0.436 Patch 11 (self-hosted)  
**Interfaces.dll version:** 2024.2.72.0 (.NET 2.0 runtime, compatible with .NET 4.8)  
**Namespace:** `LabTech.Interfaces` (pre-rebranding name — still valid for all current CW Automate versions)

---

## Table of Contents

1. [Overview](#overview)
2. [IPlugin](#iplugin)
3. [IMenu / IMenu2](#imenu--imenu2)
4. [IPermissions](#ipermissions)
5. [ITabs / ITabs2 / ITabs3](#itabs--itabs2--itabs3)
6. [ISync](#isync)
7. [ISync2](#isync2)
8. [IControlCenter](#icontrolcenter)
9. [IControlCenter2](#icontrolcenter2)
10. [IControlCenter3](#icontrolcenter3)
11. [IControlCenter4](#icontrolcenter4)
12. [ICoreFunctionality](#icorefunctionality)
13. [Interface Casting Quick Reference](#interface-casting-quick-reference)
14. [Command IDs and Status Values](#command-ids-and-status-values)
15. [Complete Interface Hierarchy](#complete-interface-hierarchy)
16. [Interfaces.dll Notes](#interfacesdll-notes)

---

## Overview

The plugin interface provides a way to display and manipulate external data gathered by any means within the Control Center. It can also facilitate interaction with other various parts of the ConnectWise Automate system, allowing for more advanced and elaborate plugins.

All plugins must:
1. Reference `Interfaces.dll` in the Visual Studio project
2. Implement `IPlugin` as the entry point
3. Target .NET Framework 4.8 (backward-compatible with the 2.0 runtime in Interfaces.dll)

Plugin host objects are returned by the Automate system depending on which area the plugin operates within:
- **Control Center plugins** → `IControlCenter` (and castable to IControlCenter2/3/4)
- **Database Agent plugins** → limited `IControlCenter` (IPermissions, ISync, ISync2)
- **Tray plugins** → `ITrayHost`
- **Remote Agent plugins** → `IServiceHost` / `IServiceHost2` / `IServiceHost3` / `IServiceHost4`

---

## IPlugin

The required entry point for every plugin. Provides identity and compatibility information to CW Automate at load time.

```vb
Public Class PluginMain
    Implements LabTech.Interfaces.IPlugin
```

### Methods

| Object | Parameters | Description |
|--------|-----------|-------------|
| `Initialize` | `Host As IControlCenter` | Called when the plugin DLL is first loaded by CW Automate |
| `Decommission` | | Called when the plugin is unloaded (CC closes or plugin is removed) |

### Properties / Functions

| Object | Type | Description |
|--------|------|-------------|
| `Name` | String | Plugin display name shown in CW Automate |
| `Version` | Double | Plugin version number (e.g., 3.0) |
| `Author` | String | Author / company name |
| `IsLicensed` | Boolean | Return `True` to allow loading; `False` blocks the plugin |

### Example

```vb
Public Class PluginMain
    Implements LabTech.Interfaces.IPlugin

    Public ReadOnly Property Name() As String Implements LabTech.Interfaces.IPlugin.Name
        Get
            Return "Triton Account Manager"
        End Get
    End Property

    Public ReadOnly Property Version() As Double Implements LabTech.Interfaces.IPlugin.Version
        Get
            Return 3.0
        End Get
    End Property

    Public ReadOnly Property Author() As String Implements LabTech.Interfaces.IPlugin.Author
        Get
            Return "Triton Technologies"
        End Get
    End Property

    Public ReadOnly Property IsLicensed() As Boolean Implements LabTech.Interfaces.IPlugin.IsLicensed
        Get
            Return True  ' Internal plugin — no license enforcement
        End Get
    End Property

    Public Sub Initialize(Host As LabTech.Interfaces.IControlCenter) Implements LabTech.Interfaces.IPlugin.Initialize
        ' Plugin loaded. Host is available here but typically stored in IPermissions/ITabs.
    End Sub

    Public Sub Decommission() Implements LabTech.Interfaces.IPlugin.Decommission
        ' Plugin unloading. Clean up any open resources.
    End Sub
End Class
```

---

## IMenu / IMenu2

Adds menu items to the CW Automate Control Center menu bar. Clicking opens the main plugin form.

```vb
Public Class clsMenus
    Implements LabTech.Interfaces.IMenu
```

### Methods

| Object | Parameters | Description |
|--------|-----------|-------------|
| `Initialize` | `Host As IControlCenter` | Called when the menu interface is registered |
| `Decommission` | | Called when the menu is removed |
| `GetMenuItems` | | Returns the collection of menu items to add to the CC menu bar |

### Pattern

```vb
Public Sub Initialize(Host As LabTech.Interfaces.IControlCenter) Implements LabTech.Interfaces.IMenu.Initialize
    m_Host = Host
End Sub

' Hold a single form instance — only one can be open at a time
Private Shared _form As MainForm

Public Sub MenuClick()
    If _form Is Nothing OrElse _form.IsDisposed Then
        _form = New MainForm(m_Host)
    End If
    _form.Show()
    _form.BringToFront()
End Sub
```

---

## IPermissions

Called by the CW Automate **Database Agent** (not the UI) every time the Control Center loads. Use this to create or migrate custom database tables.

> **CRITICAL:** If your plugin implements any Control Center Interface AND interacts with custom tables or views in the Automate database, you MUST also implement IPermissions. Without it, the DB agent won't have access to your tables.

```vb
Public Class clsPermissions
    Implements LabTech.Interfaces.IPermissions
    Private m_Host As LabTech.Interfaces.IControlCenter
```

### Methods

| Object | Type | Parameters | Description |
|--------|------|-----------|-------------|
| `Initialize` | Sub | `Host As IControlCenter` | Called every time CC loads. Create/migrate tables here. |
| `Decommission` | Sub | | Called when plugin is unloaded |
| `GetPermissionsSet` | HashTable | `UserID As Integer`, `IsSuperAdmin As Boolean`, `UserClasses As String` | Called per user. Return a HashTable of `{tableName → permissionString}` to grant access. |

### Table Creation Pattern

```vb
Public Sub Initialize(Host As LabTech.Interfaces.IControlCenter) _
    Implements LabTech.Interfaces.IPermissions.Initialize

    m_Host = Host
    Try
        ' Always check if the table already exists before attempting CREATE
        Dim check As String = m_Host.GetSQL("SHOW TABLES LIKE 'plugin_triton_msp_settings';")
        If String.IsNullOrEmpty(check) Then
            m_Host.SetSQL(
                "CREATE TABLE `plugin_triton_msp_settings` (" &
                "  `SettingID`    int(11) NOT NULL AUTO_INCREMENT," &
                "  `Setting`      varchar(255) NOT NULL," &
                "  `Value`        varchar(1000) NOT NULL DEFAULT ''," &
                "  PRIMARY KEY (`SettingID`)," &
                "  UNIQUE KEY `Setting` (`Setting`)" &
                ") ENGINE=InnoDB DEFAULT CHARSET=utf8;")
            ' Insert defaults
            m_Host.SetSQL("INSERT INTO plugin_triton_msp_settings (Setting, Value) VALUES " &
                "('AccountName','TritonTech'),('RotationDays','90'),('PasswordScope','Location')," &
                "('MinLength','12'),('MinUpper','2'),('MinLower','2'),('MinNumbers','2'),('MinSpecial','2')," &
                "('AutoRotate','1'),('MigrationComplete','0')," &
                "('ConfigFilePath','%SystemDrive%\\Triton\\MSPAccounts\\config.xml')")
        End If
        ApplyMigrations()
    Catch ex As Exception
        m_Host.LogMessage("TritonMSP IPermissions.Initialize error: " & ex.Message)
    End Try
End Sub

Public Function GetPermissionsSet(UserID As Integer, IsSuperAdmin As Boolean, UserClasses As String) _
    As HashTable Implements LabTech.Interfaces.IPermissions.GetPermissionsSet

    Dim perms As New HashTable()
    ' Grant all users full access to plugin tables
    perms.Add("plugin_triton_msp_settings",    "SELECT,INSERT,UPDATE,DELETE")
    perms.Add("plugin_triton_msp_deployments", "SELECT,INSERT,UPDATE,DELETE")
    Return perms
End Function
```

---

## ITabs / ITabs2 / ITabs3

Adds a tab panel inside the CW Automate Control Center. `ITabs2` is the recommended version — it passes `IControlCenter` to Initialize.

```vb
Public Class TabClass
    Implements LabTech.Interfaces.ITabs2
    Private objHost As LabTech.Interfaces.IControlCenter

    Public Sub Initialize(Host As LabTech.Interfaces.IControlCenter) _
        Implements LabTech.Interfaces.ITabs2.Initialize
        objHost = Host
        ' Load tab content here
        LoadProperties(objHost)
    End Sub

    Public Sub Decommission() Implements LabTech.Interfaces.ITabs2.Decommission
        ' Cleanup
    End Sub
End Class
```

---

## ISync

Background timer fired by the CW Automate Database Agent.

**Fires:** Once per day at **midnight (00:00)**.

Use for: daily tasks like checking which locations are due for password rotation.

```vb
Public Class clsSync
    Implements LabTech.Interfaces.ISync
    Private m_Host As LabTech.Interfaces.IControlCenter
```

### Methods

| Object | Type | Parameters | Description |
|--------|------|-----------|-------------|
| `Initialize` | Sub | `Host As IControlCenter` | Called when the ISync interface is initialized |
| `Decommission` | Sub | | Called when the ISync interface is disposed |
| `Name` | String | | Unique string identifier used for debugging in CW Automate logs |
| `Synchronize` | Sub | | **Called every 24 hours at 12am.** Main work method. |

### Example — Daily Rotation Check

```vb
Public Sub Synchronize() Implements LabTech.Interfaces.ISync.Synchronize
    Try
        m_Host.LogMessage("TritonMSP ISync.Synchronize: checking rotation due dates")

        ' Find locations where password is overdue for rotation
        Dim ds As DataSet = m_Host.GetDataSet(
            "SELECT d.LocationID, d.LastRotated, s.Value AS RotationDays " &
            "FROM plugin_triton_msp_deployments d " &
            "CROSS JOIN plugin_triton_msp_settings s WHERE s.Setting = 'RotationDays' " &
            "AND DATEDIFF(NOW(), d.LastRotated) >= CAST(s.Value AS UNSIGNED) " &
            "AND d.Status = 'Active'")

        For Each row As DataRow In ds.Tables(0).Rows
            ' Queue rotation for this location
            Dim locationID As Integer = CInt(row("LocationID"))
            ' ... trigger rotation logic
            m_Host.LogMessage("TritonMSP: Queuing rotation for LocationID " & locationID)
        Next
    Catch ex As Exception
        m_Host.LogMessage("TritonMSP ISync.Synchronize error: " & ex.Message)
    End Try
End Sub
```

---

## ISync2

Background timer fired by the CW Automate Database Agent.

**Fires:** Every **6 minutes**.

Use for: new machine detection — finding workstations that have come online since last check and don't yet have the TritonTech account deployed.

> **Note from CW docs:** ISync2.Synchronize allows you to kick off using Automate's 6-minute loop. However, this is not a necessity. Integrations can utilize their own timing by kicking off when the plugin is initialized. This is especially recommended when dealing with time-sensitive integrations as it is not uncommon to see delays in larger environments.

```vb
Public Class clsSync2
    Implements LabTech.Interfaces.ISync2
    Private m_Host As LabTech.Interfaces.IControlCenter
```

### Methods

| Object | Type | Parameters | Description |
|--------|------|-----------|-------------|
| `Initialize` | Sub | `Host As IControlCenter` | Called when the ISync2 interface is initialized |
| `Decommission` | Sub | | Called when the ISync2 interface is disposed |
| `Name` | String | | Unique string identifier for debugging |
| `Synchronize` | Sub | | **Called every 6 minutes.** Main work method. |

### Example — New Machine Detection

```vb
Public Sub Synchronize() Implements LabTech.Interfaces.ISync2.Synchronize
    Try
        ' Find online workstations that don't have TritonTech in UserAccounts
        ' computers.UserAccounts format: ":username1::username2:"
        Dim ds As DataSet = m_Host.GetDataSet(
            "SELECT c.ComputerID, c.Name, c.LocationID " &
            "FROM computers c " &
            "WHERE c.OS NOT LIKE '%Server%' " &
            "AND TIMESTAMPDIFF(HOUR, c.LastContact, NOW()) < 1 " &
            "AND (c.UserAccounts NOT LIKE '%:TritonTech:%' OR c.UserAccounts IS NULL) " &
            "AND c.LocationID IN (SELECT DISTINCT LocationID FROM plugin_triton_msp_deployments WHERE Status='Active')")

        For Each row As DataRow In ds.Tables(0).Rows
            ' Queue account deployment for this machine
            Dim computerID As Integer = CInt(row("ComputerID"))
            ' ... trigger deploy logic
        Next
    Catch ex As Exception
        m_Host.LogMessage("TritonMSP ISync2.Synchronize error: " & ex.Message)
    End Try
End Sub
```

---

## IControlCenter

The **primary host object** passed to all Control Center plugin interfaces. Provides access to internal functions, forms, methods, and properties within the Control Center, plus built-in SQL access functions.

> NOTE: A limited version of IControlCenter is also passed to Database Agent interfaces (IPermissions, ISync, ISync2). The limited version supports SQL methods but not UI-related functions.

```vb
' Standard usage — store at module/class level
Private objHost As LabTech.Interfaces.IControlCenter

Public Sub Initialize(Host As LabTech.Interfaces.IControlCenter) _
    Implements LabTech.Interfaces.ITabs2.Initialize
    objHost = Host
End Sub
```

### Functions (return a value)

| Object | Returns | Parameters | Description |
|--------|---------|-----------|-------------|
| `FormMain` | Windows.Form | | Returns the Control Center's Main Form object |
| `SQLConnection` | String | | Returns the SQL connection string |
| `WindowsList` | Hashtable | | Returns hashtable of currently opened windows |
| `SendCommand` | Integer | `ComputerID As Integer`, `CmdID As Integer`, `Parameters As String` | Queues a command on the agent. **Returns the CmdID integer** — save this for status polling. |
| `GetCommandOutput` | String | `CmdID As Integer` | Returns string output of a completed command. Always check status >= 3 first. |
| `GetClient` | ClientInfo | `ID As Integer` | Returns a ClientInfo object for the given client ID |
| `SetClient` | Boolean | `Client As ClientInfo` | Saves a ClientInfo object. Returns True on success. |
| `GetComputer` | ComputerInfo | `ID As Integer` | Returns a ComputerInfo object for the given computer ID |
| `SetComputer` | Boolean | `Computer As ComputerInfo` | Saves a ComputerInfo object |
| `GetContact` | ContactInfo | `ID As Integer` | Returns a ContactInfo object |
| `SetContact` | Boolean | `Contact As ContactInfo` | Saves a ContactInfo object |
| `GetDevice` | DeviceInfo | `ID As Integer` | Returns a DeviceInfo object |
| `SetDevice` | Boolean | `Device As DeviceInfo` | Saves a DeviceInfo object |
| `GetGroup` | GroupInfo | `ID As Integer` | Returns a GroupInfo object |
| `SetGroup` | Boolean | `Group As GroupInfo` | Saves a GroupInfo object |
| `GetLocation` | LocationInfo | `ID As Integer` | Returns a LocationInfo object |
| `SetLocation` | Boolean | `Location As LocationInfo` | Saves a LocationInfo object |
| `GetReadingView` | String | `ID As Integer`, `HTML As Boolean` | Returns reading-view formatted ticket string |
| `GetSQL` | String | `SQL As String` | **Returns a single SQL string value.** For single-cell queries. |
| `GetTicket` | TicketInfo | `ID As Integer` | Returns a TicketInfo object |
| `SetTicket` | Boolean | `Ticket As TicketInfo` | Saves a TicketInfo object |
| `GetUser` | UserInfo | `ID As Integer` | Returns a UserInfo object |
| `GetValues` | ArrayList | `SQL As String` | **Returns an ArrayList of values** — ideal for populating combo boxes. |
| `GetID` | Integer | `Name As String`, `DataType As IDDataTypes`, `StrictMode As Boolean` | Returns numeric ID from a string name and data type |
| `GetDataSet` | DataSet | `SQL As String` | **Returns a full DataSet** — use for multi-row, multi-column queries. |
| `SetSQLWithID` | Integer | `SQL As String` | Executes a SQL INSERT and **returns the ID of the new row**. |

### Methods (no return value)

| Object | Parameters | Description |
|--------|-----------|-------------|
| `AlertMessage` | `strFeedback As String` | Creates a new alert visible in CW Automate |
| `LogMessage` | `strFeedback As String` | **Writes to the CW Automate log.** Use this everywhere instead of MessageBox. |
| `ReadProperty` | `Prop As LabTechProperty` | Gets the value of an Automate system property |
| `WriteProperty` | `Prop As LabTechProperty` | Sets the value of an Automate system property |
| `ReadStat` | `Stat As LabTechStat` | Gets an Automate stat value for a specified agent |
| `WriteStat` | `Stat As LabTechStat` | Sets an Automate stat value for a specified agent |
| `ReadState` | `State As LabTechState` | Gets an Automate script state value |
| `WriteState` | `State As LabTechState` | Sets an Automate script state value |
| `ReadExtraData` | `ID As Integer`, `ByRef Data As LabTechExtraData` | Gets the value of an extra data field |
| `WriteExtraData` | `ID As Integer`, `ByRef Data As LabTechExtraData` | Sets the value of an extra data field |
| `QueueScript` | `ID As Integer`, `DataType As IDDataTypes`, `ScriptID As Integer`, `Parameters As String`, `RunTime As Date`, `Repeat As Boolean` | Queues a CW Automate script for execution on an object |
| `SetSQL` | `SQL As String` | **Executes a SQL statement with no return value.** Use for INSERT/UPDATE/DELETE when you don't need the new row ID. |

### Properties

| Object | Type | Description |
|--------|------|-------------|
| `ClientExternalIDUsed` | Boolean | Shared Boolean flag accessible by multiple plugins |
| `ClientGUIDUsed` | Boolean | Shared Boolean flag accessible by multiple plugins |
| `ContactExternalIDUsed` | Boolean | Shared Boolean flag accessible by multiple plugins |
| `ContactGUIDUsed` | Boolean | Shared Boolean flag accessible by multiple plugins |
| `IndicationFlagUsed` | Boolean | Shared Boolean flag accessible by multiple plugins |
| `IndicationUsed` | Boolean | Shared Boolean flag accessible by multiple plugins |
| `TicketSync` | Boolean | Shared Boolean flag accessible by multiple plugins |

### SendCommand — Full Usage Pattern

```vb
''' <summary>
''' Send a shell command to a remote agent and wait for the result.
''' Must be called from a background thread — never from the UI thread.
''' </summary>
Private Function RunShellCommand(computerID As Integer, command As String,
                                  timeoutSeconds As Integer) As String
    ' CmdID 2 = Execute shell command
    ' Parameter format: "cmd!!!/C <your command>"
    Dim cmdID As Integer = objHost.SendCommand(computerID, 2, "cmd!!!/C " & command)

    Dim status As Integer = 0
    Dim elapsed As Integer = 0
    Dim pollIntervalMs As Integer = 3000

    Do While status < 3
        Threading.Thread.Sleep(pollIntervalMs)
        elapsed += pollIntervalMs \ 1000
        If elapsed >= timeoutSeconds Then
            Throw New TimeoutException("Command timed out after " & timeoutSeconds & "s. CmdID=" & cmdID)
        End If
        status = CInt(objHost.GetSQL("SELECT Status FROM commands WHERE CmdID=" & cmdID))
    Loop

    If status = 4 Then
        ' Status 4 = Error — output may still contain useful error text
        objHost.LogMessage("TritonMSP: Command CmdID=" & cmdID & " returned error status")
    End If

    Return objHost.GetCommandOutput(cmdID)
End Function
```

### SQL Helper Patterns

```vb
' Single value
Dim val As String = objHost.GetSQL(
    "SELECT Value FROM plugin_triton_msp_settings WHERE Setting='RotationDays'")

' Execute (no return)
objHost.SetSQL(
    "UPDATE plugin_triton_msp_settings SET Value='90' WHERE Setting='RotationDays'")

' Insert and get new row ID
Dim newID As Integer = objHost.SetSQLWithID(
    "INSERT INTO plugin_triton_msp_deployments (LocationID, Status) VALUES (42, 'Active')")

' Multi-value list — ideal for ComboBox
Dim locations As ArrayList = objHost.GetValues("SELECT Name FROM locations ORDER BY Name")
cbxLocations.Items.AddRange(locations.ToArray())

' TRICK: Store hidden ID behind CHAR(0) separator — only Name displays in ComboBox
cbxLocations.Items.AddRange(objHost.GetValues(
    "SELECT CONVERT(CONCAT(l.Name, CHAR(0), l.LocationID) USING latin1) " &
    "FROM locations l ORDER BY l.Name"
).ToArray())
' Retrieve the hidden ID from selected item:
Dim locationID As String = cbxLocations.SelectedItem.ToString().Split(Chr(0))(1)

' Full dataset for grid/table display
Dim ds As DataSet = objHost.GetDataSet(
    "SELECT l.LocationID, l.Name, p.Password, d.Status, d.LastRotated " &
    "FROM locations l " &
    "LEFT JOIN plugin_triton_msp_deployments d ON l.LocationID = d.LocationID " &
    "LEFT JOIN passwords p ON d.PasswordID = p.PasswordID " &
    "ORDER BY l.Name")
DataGridView1.DataSource = ds.Tables(0)

' CONCAT trick for multiple fields in GetSQL
Dim combined As String = objHost.GetSQL(
    "SELECT CONCAT(LocationID,'~',Name) FROM locations WHERE LocationID=42")
Dim parts() As String = combined.Split("~"c)
Dim locID = parts(0), locName = parts(1)
```

---

## IControlCenter2

Extends IControlCenter with additional functions, methods, and properties. **Must be cast from the IControlCenter host using CType().**

```vb
Private objHost2 As LabTech.Interfaces.IControlCenter2

Public Sub Initialize(Host As LabTech.Interfaces.IControlCenter) _
    Implements LabTech.Interfaces.ITabs2.Initialize
    ' Store base host
    objHost = Host
    ' Cast to extended version for user info and audit logging
    objHost2 = CType(Host, LabTech.Interfaces.IControlCenter2)
End Sub
```

### Additional Functions

| Object | Returns | Parameters | Description |
|--------|---------|-----------|-------------|
| `getFile` | Byte() | `computerID As Integer`, `Filename As Integer` | Downloads file to remote computer from Automate Share |
| `getScreenShot` | Byte() | `computerID As Integer`, `userID As Integer` | Returns byte array of screenshot from specified computer |
| `UploadFile` | Boolean | `Filename As String`, `filedata() As Byte` | Uploads file from remote computer to Automate Share |
| `UserCheckCommandSecurity` | Boolean | `Command As Integer` | Returns true if current user has access to the Automate command |
| `UserClientAccess` | Boolean | `Perm As ClientPermissions`, `ClientID As Integer` | Returns true if current user has access to the client |
| `UserComputerAccess` | Boolean | `Perm As ComputerPermissions`, `ComputerID As Integer` | Returns true if current user has access to the computer |
| `UserGroupAccess` | Boolean | `Perm As ComputerPermissions`, `GroupID As Integer` | Returns true if current user has access to the group |
| `UserSystemAccess` | Boolean | `Perm As UserPermissions` | Returns true if current user has system-level access |

### Additional Methods

| Object | Parameters | Description |
|--------|-----------|-------------|
| `AuditAction` | `Msg As String`, `Action As Integer`, `ID As Integer`, `Undo As String` | **Creates an audit record in the CW Automate audit log on a specified object ID.** |
| `RunRedirector` | `ID As Integer`, `ComputerID As Integer`, `IP As String`, `Console As Integer`, `TargetComputerID As Integer` | Runs a redirector to a remote computer and console session |

### Additional Properties

| Object | Type | Description |
|--------|------|-------------|
| `Tunnels` | Hashtable | Returns a hashtable of currently open tunnels |
| `UserClasses` | String | Returns the permission class names the current technician belongs to |
| `UserEmailAddress` | String | **Returns the current logged-in technician's email address** |
| `UserGetAccessGroups` | String | Returns the current technician's group access list |
| `UserID` | String | **Returns the current logged-in technician's numeric user ID as a string** |

### Usage Example

```vb
' Log which technician triggered a rotation
Dim techID   As String = objHost2.UserID
Dim techEmail As String = objHost2.UserEmailAddress

' Write to audit log — associates the action with the specific location object
objHost2.AuditAction(
    "TritonTech password rotated for LocationID " & locationID & " by " & techEmail,
    1,           ' Action type integer (1 = general action)
    locationID,  ' ID of the object being audited
    ""           ' Undo string (empty = not undoable)
)
```

---

## IControlCenter3

Extends IControlCenter2. Cast using `CType()`.

```vb
objHost3 = CType(Host, LabTech.Interfaces.IControlCenter3)
```

### Additional Properties

| Object | Type | Description |
|--------|------|-------------|
| `Plugins` | IPluginAccess | Returns ArrayLists of all loaded plugins — useful for inter-plugin communication |

---

## IControlCenter4

Extends IControlCenter3. Cast using `DirectCast()` or `CType()`.

```vb
objHost4 = DirectCast(Host, LabTech.Interfaces.IControlCenter4)
```

### Additional Methods — UI Navigation

These methods programmatically open screens inside the CW Automate Control Center — useful for "click here to view this computer" buttons in the plugin UI.

| Object | Parameters | Description |
|--------|-----------|-------------|
| `OpenComputerScreen` | `computerID As Integer` | Opens Computer Management screen for the specified computer |
| `OpenLocationScreen` | `locationID As Integer` | Opens Location screen for the specified location |
| `OpenClientScreen` | `clientID As Integer` | Opens Client screen for the specified client |
| `OpenNetworkDeviceScreen` | `networkDeviceID As Integer` | Opens Network Device screen |
| `OpenTicketScreen` | `ticketID As Integer` | Opens Ticket screen |
| `OpenInternalMonitorScreen` | `monitorID As Integer` | Opens Internal Monitor screen |
| `OpenGroupScreen` | `groupID As Integer` | Opens Group screen |
| `OpenRemoteMonitorScreen` | `monitorID As Integer` | Opens Remote Monitor screen |
| `OpenLabTechUrl` | `url As String`, `Optional external As Boolean = False` | Opens a URL inside CC or in an external browser |

### Usage Example

```vb
' From a DataGridView row click — jump to location screen
Private Sub dgvLocations_CellDoubleClick(sender As Object, e As DataGridViewCellEventArgs) Handles dgvLocations.CellDoubleClick
    Dim locationID As Integer = CInt(dgvLocations.Rows(e.RowIndex).Cells("LocationID").Value)
    objHost4.OpenLocationScreen(locationID)
End Sub
```

---

## ICoreFunctionality

Part of the IWebMainHost interface tree. Provides core utility functions including email sending.

```vb
' Typically obtained as part of IWebMainHost — access depends on host context
' In Control Center plugins, email can be sent via the host
```

### Key Methods

| Object | Parameters | Description |
|--------|-----------|-------------|
| `SendEmail` | `To As String`, `Subject As String`, `Body As String` | Sends email via CW Automate's configured mail server settings |

---

## Interface Casting Quick Reference

```vb
' ─────────────────────────────────────────────
' STANDARD — all Control Center plugins
' ─────────────────────────────────────────────
Private objHost As LabTech.Interfaces.IControlCenter
' Set in Initialize: objHost = Host

' ─────────────────────────────────────────────
' EXTENDED — current user info + audit logging
' ─────────────────────────────────────────────
Private objHost2 As LabTech.Interfaces.IControlCenter2
' Set in Initialize: objHost2 = CType(Host, LabTech.Interfaces.IControlCenter2)
' Adds: .UserID, .UserEmailAddress, .UserClasses, .AuditAction(), .UserCheckCommandSecurity()

' ─────────────────────────────────────────────
' EXTENDED — loaded plugins list
' ─────────────────────────────────────────────
Private objHost3 As LabTech.Interfaces.IControlCenter3
' Set in Initialize: objHost3 = CType(Host, LabTech.Interfaces.IControlCenter3)
' Adds: .Plugins

' ─────────────────────────────────────────────
' EXTENDED — UI navigation
' ─────────────────────────────────────────────
Private objHost4 As LabTech.Interfaces.IControlCenter4
' Set in Initialize: objHost4 = DirectCast(Host, LabTech.Interfaces.IControlCenter4)
' Adds: OpenLocationScreen(), OpenComputerScreen(), OpenClientScreen(), etc.

' ─────────────────────────────────────────────
' SAFE CAST — won't throw if interface not supported
' ─────────────────────────────────────────────
Dim safeHost2 = TryCast(Host, LabTech.Interfaces.IControlCenter2)
If safeHost2 IsNot Nothing Then
    Dim userID As String = safeHost2.UserID
End If
```

---

## Command IDs and Status Values

### Command Status Values

| Status | Meaning | Action |
|--------|---------|--------|
| 0 | Waiting to send | Continue polling |
| 1 | Sent to agent | Continue polling |
| 2 | Executing on agent | Continue polling |
| **3** | **Success** | **Read output** |
| **4** | **Error** | **Check output for error message** |

> Always check `status >= 3` before reading command output. A status of 4 (error) may still contain useful diagnostic output via `GetCommandOutput()`.

### Known Command IDs

| CmdID | Description | Parameter Format |
|-------|------------|-----------------|
| **2** | Execute shell command | `"cmd!!!/C <your command here>"` — the `!!!` separator is required between the executable and the arguments |
| 17 | Refresh hardware inventory | No parameters needed |
| 123 | Refresh system info / user accounts | No parameters needed |
| > 500 | Custom plugin commands | Define via ISvcCommand interface with a CommandNumber > 500 |

> **Shell command parameter format:** `"cmd!!!/C net user TritonTech MyPassword123! /add"` — the `!!!` is how CW Automate separates the executable from its arguments in the command parameter string.

### Safe Command Polling Pattern (with timeout)

```vb
''' <summary>
''' Poll command status until complete or timeout. Never call from UI thread.
''' </summary>
''' <param name="cmdID">The command ID returned by SendCommand()</param>
''' <param name="timeoutSeconds">Maximum seconds to wait before throwing TimeoutException</param>
''' <returns>Command status (3=success, 4=error)</returns>
Private Function WaitForCommand(cmdID As Integer, timeoutSeconds As Integer) As Integer
    Const PollMs As Integer = 3000
    Dim maxAttempts As Integer = (timeoutSeconds * 1000) \ PollMs
    Dim attempts As Integer = 0

    Do
        Threading.Thread.Sleep(PollMs)
        attempts += 1
        Dim status As Integer = CInt(
            objHost.GetSQL("SELECT Status FROM commands WHERE CmdID=" & cmdID))
        If status >= 3 Then Return status
        If attempts >= maxAttempts Then
            Throw New TimeoutException(
                String.Format("Command CmdID={0} timed out after {1}s", cmdID, timeoutSeconds))
        End If
    Loop
End Function
```

---

## Complete Interface Hierarchy

```
LabTech.Interfaces (Interfaces.dll v2024.2.72.0)
│
├── IPlugin                 Required entry point for every plugin
│
├── IMenu                   Adds menu items to the CC menu bar
├── IMenu2                  Extended menu interface
│
├── IPermissions            DB table creation (called on every CC load by DB agent)
│
├── ITabs                   Adds tab panel to CC
├── ITabs2                  Extends ITabs — passes IControlCenter to Initialize
├── ITabs3                  Further extended tab interface
│
├── ISync                   Daily timer — fires at midnight (00:00)
├── ISync2                  6-minute timer — fires every 6 minutes
│
├── IControlCenter          Primary host: SQL, SendCommand, GetLocation, etc.
│   ├── IControlCenter2     + UserID, UserEmailAddress, AuditAction() [CType cast]
│   ├── IControlCenter3     + Plugins list [CType cast]
│   └── IControlCenter4     + OpenLocationScreen() etc. [DirectCast]
│
├── IWebMainHost
│   ├── IASPHost
│   ├── ICoreFunctionality  SendEmail, etc.
│   └── IDatabaseAccess     Direct DB access
│
├── ITrayHost               Host for tray/taskbar plugins
│   └── ITray
│
├── IServiceHost            Remote agent host (on the agent service itself)
├── IServiceHost2
├── IServiceHost3
├── IServiceHost4
│
├── ISvcCommand             Custom command definition (CommandNumber > 500)
│
├── IAlert / IAlert2        Alert overrides
├── IFunction               Custom DB agent functions
│
└── Control Center Interfaces (passed IControlCenter host):
    ├── IAlert              Alert display override
    ├── IAvPlugin           Antivirus plugin
    ├── IAvSection
    ├── IAvTemplate
    ├── IClient             Hooks into client save/update
    ├── IContact
    ├── IControlAlert       Alert control
    ├── IControlCommand     Custom command name/handler in CC display
    ├── IControlMonitor     Monitor override
    ├── ICustomMenu         Custom context menus
    ├── IExport             Export functionality
    ├── IPolicy             Policy hooks
    ├── IReport             Report generation
    ├── ISpellCheck         Spell check hooks
    ├── ITicket             Ticket hooks
    ├── ITime               Time entry hooks
    └── ITimer              UI timer
```

---

## Interfaces.dll Notes

| Property | Value |
|----------|-------|
| File version | 2024.2.72.0 |
| .NET runtime | 2.0 (backward compatible with .NET 4.8 projects) |
| Namespace | `LabTech.Interfaces` |
| CW Automate compatibility | Confirmed working with v25.0.436 Patch 11 |
| Location in repo | `/Interfaces.dll` (root) |

### Project Setup in Visual Studio

1. Open Solution Explorer → right-click References → Add Reference → Browse
2. Select `Interfaces.dll` from the repo root
3. Set project **Target Framework** to **.NET Framework 4.8** (Application tab in project properties)
4. Build type: **Class Library** (compiles to `.dll`)
5. Output file must be named correctly for CW Automate to load it

### Namespace Note

Despite ConnectWise rebranding LabTech → ConnectWise Automate years ago, the SDK namespace remains `LabTech.Interfaces`. This is intentional and will not change — all code references must use `LabTech.Interfaces.*`.
