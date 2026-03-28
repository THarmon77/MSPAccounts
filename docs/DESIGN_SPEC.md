# Triton Account Manager — v3 Plugin Design Specification

**Plugin name:** Triton Account Manager  
**Version:** 3.0  
**Author:** Triton Technologies  
**Repository:** THarmon77/MSPAccounts (private)  
**Date drafted:** 2026-03-28  
**Status:** Pre-development — design finalized, coding not yet started

---

## Table of Contents

1. [Purpose & Scope](#purpose--scope)
2. [What Changed from v2 (Original)](#what-changed-from-v2-original)
3. [The TritonTech Account](#the-tritontech-account)
4. [Password Storage Architecture](#password-storage-architecture)
5. [Password Scope](#password-scope)
6. [Password Policy](#password-policy)
7. [Machine Targeting](#machine-targeting)
8. [Automatic Rotation](#automatic-rotation)
9. [New Machine Detection](#new-machine-detection)
10. [Migration from Legacy Accounts](#migration-from-legacy-accounts)
11. [Configuration File](#configuration-file)
12. [Module Map](#module-map)
13. [Database Schema (New)](#database-schema-new)
14. [UI Design — 4-Tab Layout](#ui-design--4-tab-layout)
15. [Deployment Architecture](#deployment-architecture)
16. [Security Design](#security-design)
17. [Build & Deploy Process](#build--deploy-process)
18. [Open Questions / Future Work](#open-questions--future-work)

---

## Purpose & Scope

The v3 plugin replaces the original MSP Accounts domain-account management tool with a focused, simplified tool that does one thing well:

> **Deploy and maintain a single shared local administrator account (`TritonTech`) on every managed workstation across all Triton-managed clients, with a unique password stored per location in the CW Automate Passwords tab, rotated on a 90-day schedule.**

### What this plugin does

- Creates a local Windows account named `TritonTech` on managed workstations
- Sets the account as a local Administrator
- Hides the account from the Windows login screen (Winlogon registry key)
- Stores the account's password in the CW Automate Passwords tab, linked to the location
- Rotates passwords on a 90-day schedule (configurable)
- Detects new machines every 6 minutes and auto-deploys the account
- Provides a one-time migration routine to replace legacy accounts (e.g., `TT_Service`)

### What this plugin does NOT do

- Does NOT manage domain/AD user accounts (that was the old plugin — removed entirely)
- Does NOT manage per-technician accounts
- Does NOT use any global password (removed — each location gets its own password)
- Does NOT require DPAPI or local machine encryption
- Does NOT need a custom master key (all encryption via CW Automate's native f_CWAESEncrypt)

---

## What Changed from v2 (Original)

| Area | v2 Original | v3 New |
|------|------------|--------|
| Account type | Domain Admin accounts per technician | Single shared local admin account |
| Account name | `TT_<TechName>` style | Fixed: `TritonTech` |
| Target machines | Workgroup only | All workstations (domain-joined, workgroup, Azure AD) |
| Password storage | MySQL AES_ENCRYPT with hardcoded key `"shinybrowncoat"` | `f_CWAESEncrypt()` — CW Automate native, appears in Passwords tab |
| Password scope | Per-user | Per-location (default); Per-client (option) |
| Encryption key | Hardcoded in source code | None needed — CW Automate handles it |
| DB tables | 3 tables (settings, users, userstatus) | 2 tables (settings, deployments) |
| Tech accounts | Yes — full per-tech management | No — removed entirely |
| Migration | N/A | Yes — detects and replaces legacy accounts |
| Modules removed | N/A | UserManagement.vb deleted |
| Modules added | N/A | Config.vb, Logger.vb, MigrationManager.vb, AccountDeployer.vb |

---

## The TritonTech Account

### Account Specifications

| Property | Value | Notes |
|----------|-------|-------|
| Username | `TritonTech` | Fixed — not configurable |
| Display Name | `Triton Technologies Support` | Configurable in Settings |
| Description | `Managed support account - Triton Technologies` | Configurable in Settings |
| Account type | Local Windows account | Not domain — works on all machine types |
| Group membership | `Administrators` (local) | Added via `net localgroup Administrators TritonTech /add` |
| Login screen | Hidden | Added to `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\SpecialAccounts\UserList` |
| Password expires | Never | Set via `net user TritonTech /passwordchg:no /expires:never` |
| Target OS | All Windows workstations | `OS NOT LIKE '%Server%'` filter — excludes servers |

### Creation Command Sequence

```batch
REM Step 1: Create the account
net user TritonTech <password> /add /fullname:"Triton Technologies Support" /comment:"Managed support account" /passwordchg:no /expires:never

REM Step 2: Add to local Administrators
net localgroup Administrators TritonTech /add

REM Step 3: Hide from login screen
reg add "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon\SpecialAccounts\UserList" /v TritonTech /t REG_DWORD /d 0 /f
```

### Verification Command

```batch
net user TritonTech
```

Look for `TritonTech` in output and `Local Group Memberships *Administrators` to confirm.

### Detection via computers.UserAccounts

CW Automate stores the list of local accounts in `computers.UserAccounts` as a colon-delimited string:
```
:Administrator::TritonTech::OtherUser:
```

SQL to check if TritonTech exists on a machine:
```sql
SELECT ComputerID FROM computers
WHERE ComputerID = 12345
AND UserAccounts LIKE '%:TritonTech:%'
```

---

## Password Storage Architecture

### The Key Decision

All passwords are stored using **CW Automate's built-in `f_CWAESEncrypt()` MySQL function**. This is the same function CW Automate uses internally for its own Passwords tab. Storing passwords this way means:

1. The encrypted blob is **readable in the CW Automate Passwords tab** natively — technicians can click the location → Passwords tab → see the TritonTech password in plaintext (after their CW credentials decrypt it)
2. **No custom encryption code needed** — the complexity and risk of key management is eliminated
3. **No master key needed in the plugin** — the key is the CW Automate server's own `serverId` from the config table

### f_CWAESEncrypt Function

```sql
-- Signature (MySQL stored function, built into CW Automate)
f_CWAESEncrypt(encryptLevel INT, encryptString VARCHAR(255))

-- encryptLevel controls what key is used:
--   0 = use serverId directly
--   1 = use serverId + salt
-- encryptString = the plaintext to encrypt

-- Example: store a password
UPDATE passwords SET Password = f_CWAESEncrypt(0, 'MyNewPassword123!')
WHERE PasswordID = 42;

-- The Password field in the passwords table is BLOB type
-- CW Automate decrypts it automatically when displaying in the Passwords tab
```

### Password Table Structure (CW Automate native)

```sql
-- Key fields in the passwords table
SELECT PasswordID, Title, Username, Password, ExpireDate, Last_User, Last_Date
FROM passwords
WHERE PasswordID = 42;
```

| Field | Type | Description |
|-------|------|-------------|
| `PasswordID` | int | Primary key, referenced by locations.PasswordID |
| `Title` | varchar | Display name in the Passwords tab |
| `Username` | varchar | Username stored with the password |
| `Password` | BLOB | AES-encrypted password blob (via f_CWAESEncrypt) |
| `ExpireDate` | date | When the password expires (used for display) |
| `Last_User` | varchar | Last user who changed it |
| `Last_Date` | datetime | When it was last changed |

### Location → Password Link

```sql
-- Each location has a PasswordID FK
SELECT l.LocationID, l.Name, p.PasswordID, p.Title
FROM locations l
LEFT JOIN passwords p ON l.PasswordID = p.PasswordID
WHERE l.LocationID = 42;
```

### Rotation Update Query

```sql
-- When rotating: update the existing password record for this location
UPDATE passwords p
INNER JOIN locations l ON l.PasswordID = p.PasswordID
SET p.Password   = f_CWAESEncrypt(0, 'NewGeneratedPassword456!'),
    p.Last_User  = 'TritonMSP_AutoRotate',
    p.Last_Date  = NOW(),
    p.ExpireDate = DATE_ADD(NOW(), INTERVAL 90 DAY)
WHERE l.LocationID = 42;
```

### If No Password Entry Exists Yet (first deployment)

```sql
-- Insert new password record and link it to the location
INSERT INTO passwords (Title, Username, Password, ExpireDate, Last_User, Last_Date)
VALUES (
    'TritonTech - <LocationName>',   -- Title visible in Passwords tab
    'TritonTech',                     -- Username
    f_CWAESEncrypt(0, 'InitialPassword123!'),
    DATE_ADD(NOW(), INTERVAL 90 DAY),
    'TritonMSP_Plugin',
    NOW()
);
-- Get the new PasswordID and link to location
SET @newPasswordID = LAST_INSERT_ID();
UPDATE locations SET PasswordID = @newPasswordID WHERE LocationID = 42;
```

---

## Password Scope

Two scope options are supported:

### Per-Location (Default)
- Each CW Automate **Location** gets its own unique password
- 3 locations under one client = 3 different passwords
- Most secure — compromise of one location doesn't expose others
- Default and recommended

### Per-Client (Optional)
- All locations under a single **Client** share one password
- Simpler management for clients with many small locations
- Selected in Settings tab

### Global (Removed)
- Global scope was explicitly removed from the design
- There is no single password for all managed endpoints

---

## Password Policy

### Default Values

| Setting | Default | Notes |
|---------|---------|-------|
| Minimum length | 12 | Characters |
| Minimum uppercase | 2 | A-Z |
| Minimum lowercase | 2 | a-z |
| Minimum numbers | 2 | 0-9 |
| Minimum special chars | 2 | `!@#$%^&*()-_=+[]{}` etc. |
| Rotation interval | 90 days | Compliance baseline |
| Auto-rotation | Enabled | Driven by ISync (daily check at midnight) |

### Password Generation

Uses `System.Security.Cryptography.RNGCryptoServiceProvider` — **not** `System.Random`.

```vb
''' <summary>
''' Generates a cryptographically random password meeting the configured policy.
''' Uses RNGCryptoServiceProvider — cryptographically secure.
''' </summary>
Public Shared Function GeneratePassword(minLength As Integer, minUpper As Integer,
                                         minLower As Integer, minNumbers As Integer,
                                         minSpecial As Integer) As String
    Const Upper   As String = "ABCDEFGHJKLMNPQRSTUVWXYZ"   ' no I, O (visually ambiguous)
    Const Lower   As String = "abcdefghjkmnpqrstuvwxyz"    ' no i, l, o
    Const Numbers As String = "23456789"                    ' no 0, 1
    Const Special As String = "!@#$%^&*()-_=+[]"

    Dim rng As New System.Security.Cryptography.RNGCryptoServiceProvider()
    Dim chars As New List(Of Char)

    ' Satisfy minimums first
    For i = 1 To minUpper   : chars.Add(Upper(NextRandom(rng, Upper.Length)))   : Next
    For i = 1 To minLower   : chars.Add(Lower(NextRandom(rng, Lower.Length)))   : Next
    For i = 1 To minNumbers : chars.Add(Numbers(NextRandom(rng, Numbers.Length))): Next
    For i = 1 To minSpecial : chars.Add(Special(NextRandom(rng, Special.Length))): Next

    ' Fill to minLength with random chars from all sets
    Dim allChars As String = Upper & Lower & Numbers & Special
    While chars.Count < minLength
        chars.Add(allChars(NextRandom(rng, allChars.Length)))
    End While

    ' Shuffle using Fisher-Yates
    For i = chars.Count - 1 To 1 Step -1
        Dim j As Integer = NextRandom(rng, i + 1)
        Dim tmp As Char = chars(i)
        chars(i) = chars(j)
        chars(j) = tmp
    Next

    Return New String(chars.ToArray())
End Function

Private Shared Function NextRandom(rng As System.Security.Cryptography.RNGCryptoServiceProvider,
                                    maxValue As Integer) As Integer
    Dim data(3) As Byte
    rng.GetBytes(data)
    Return Math.Abs(BitConverter.ToInt32(data, 0)) Mod maxValue
End Function
```

---

## Machine Targeting

### SQL Filter for Target Workstations

```sql
-- All online workstations (non-server) in active-management locations
SELECT c.ComputerID, c.Name, c.LocationID, c.Domain, c.UserAccounts
FROM computers c
WHERE c.OS NOT LIKE '%Server%'
AND TIMESTAMPDIFF(HOUR, c.LastContact, NOW()) < 1
ORDER BY c.LocationID, c.Name
```

### Why These Filters?

- `OS NOT LIKE '%Server%'` — excludes all Windows Server variants; covers standalone, workgroup, domain-joined, and Azure AD joined workstations
- `TIMESTAMPDIFF(HOUR, c.LastContact, NOW()) < 1` — only target online machines (checked in within last hour)
- No plan or group filter in the plugin — the CW Automate script that triggers this plugin is assigned to the correct service plan externally; the plugin doesn't need to re-filter

### Machine Types Supported

| Type | How TritonTech is Deployed |
|------|--------------------------|
| Standalone / workgroup | `net user /add` + `net localgroup Administrators /add` |
| Domain-joined | Same — local account, not domain account |
| Azure AD joined | Same — local account unaffected by AAD status |
| Windows Home | Same — `net user` works on Home editions |

---

## Automatic Rotation

### Rotation Flow

1. **ISync.Synchronize** fires at midnight every day
2. Query `plugin_triton_msp_deployments` for locations where `DATEDIFF(NOW(), LastRotated) >= RotationDays`
3. For each due location:
   a. Generate new password with `GeneratePassword()`
   b. Find all online workstations in that location with TritonTech account
   c. For each workstation: `SendCommand(computerID, 2, "cmd!!!/C net user TritonTech <newpwd>")`
   d. Poll until all commands complete (status >= 3)
   e. If all succeeded: update `passwords` table via `f_CWAESEncrypt(0, newpwd)`
   f. Update `plugin_triton_msp_deployments.LastRotated = NOW()`
   g. Log result to `plugin_triton_msp_deployments` log
4. Log summary to CW Automate log via `objHost.LogMessage()`

### Partial Failure Handling

If some machines in a location succeed and others fail:
- The password in the Passwords tab is **not updated** until ALL machines succeed
- Failed machines are logged
- Retry on next ISync2 cycle (6 minutes) — limited retries before escalating

### Manual Rotation

The Actions tab provides:
- **Rotate Single Location** — dropdown to select location → rotate now
- **Rotate Overdue** — rotate all locations past their due date
- **Rotate All** — force rotation of all locations regardless of schedule

---

## New Machine Detection

### Detection Flow

1. **ISync2.Synchronize** fires every 6 minutes
2. Query for online workstations in managed locations where `UserAccounts NOT LIKE '%:TritonTech:%'`
3. For each undeployed machine:
   a. Generate password for that location (or use existing location password)
   b. `SendCommand(computerID, 2, "cmd!!!/C net user TritonTech <pwd> /add ...")`
   c. Poll for completion
   d. Add to Administrators group, hide from login screen
   e. Update `plugin_triton_msp_deployments` with machine added
   f. Update `computers` refresh: `SendCommand(computerID, 123, "")` to refresh UserAccounts

### Offline Machine Handling

- Machines that haven't checked in within 1 hour are skipped
- They will be picked up on the next ISync2 cycle once they come online
- No retry queue needed — the query naturally finds them again when online

---

## Migration from Legacy Accounts

### One-Time Migration Routine

Replaces legacy accounts (e.g., `TT_Service`, `MSPAdmin`, or any configurable legacy name) with `TritonTech`.

### Migration Steps (per machine)

1. Check if `TritonTech` already exists → skip if yes
2. Check if legacy account exists (configurable name, default: `TT_Service`)
3. Create `TritonTech` with new password
4. Verify `TritonTech` creation succeeded
5. Delete legacy account: `net user TT_Service /delete`
6. Verify legacy account is gone
7. Update password vault entry for this location
8. Log result

### Migration SQL Marker

```sql
-- Set this to 1 after migration is complete for a location
UPDATE plugin_triton_msp_settings SET Value='1' WHERE Setting='MigrationComplete';
```

Migration is gated by the `MigrationComplete` flag in settings. The "Run Migration" button in the Actions tab runs it once and sets the flag. It can be re-run manually if needed.

### Migration UI Warning

The "Run Migration" action shows a warning dialog before executing:
> "This will delete legacy account '[TT_Service]' from all machines after creating TritonTech. This cannot be undone. Continue?"

---

## Configuration File

### Path

```
%SystemDrive%\Triton\MSPAccounts\config.xml
```

Resolved example: `C:\Triton\MSPAccounts\config.xml`

### Directory

The `C:\Triton` directory is Triton Technologies' standard hidden directory, deployed on all managed machines via CW Automate:
```batch
attrib +h C:\Triton
```

### What Goes In the Config File

**Only non-secret, non-sensitive settings** that override DB defaults for a specific workstation:
- Plugin data directory path (if moved from default)
- Debug logging level
- Any workstation-specific overrides

**What does NOT go in the config file:**
- Passwords
- Encryption keys
- Database connection strings
- Anything sensitive

### Config File Format

```xml
<?xml version="1.0" encoding="utf-8"?>
<TritonMSPConfig>
    <Setting name="DataDirectory" value="%SystemDrive%\Triton\MSPAccounts" />
    <Setting name="LogLevel" value="Info" />
    <!-- Add overrides here if needed -->
</TritonMSPConfig>
```

### Config.vb — Reading the Config

```vb
Public Module Config
    Private _settings As New Dictionary(Of String, String)

    Public Sub Load()
        Dim path As String = Environment.ExpandEnvironmentVariables(
            "%SystemDrive%\Triton\MSPAccounts\config.xml")
        If Not File.Exists(path) Then Return

        Dim doc As New Xml.XmlDocument()
        doc.Load(path)
        For Each node As Xml.XmlNode In doc.SelectNodes("//Setting")
            Dim name  As String = node.Attributes("name")?.Value
            Dim value As String = node.Attributes("value")?.Value
            If Not String.IsNullOrEmpty(name) Then
                _settings(name) = Environment.ExpandEnvironmentVariables(value ?? "")
            End If
        Next
    End Sub

    Public Function Get(key As String, Optional defaultValue As String = "") As String
        If _settings.ContainsKey(key) Then Return _settings(key)
        Return defaultValue
    End Function
End Module
```

---

## Module Map

### Files to Create (New)

| File | Interface | Purpose |
|------|-----------|---------|
| `PLUGIN DEFINITION/PluginMain.vb` | IPlugin | Entry point — update author/name/version |
| `ADD Menus/clsMenus.vb` | IMenu | Update menu label to "Triton Account Manager" |
| `ADD Tables/clsPermissions.vb` | IPermissions | Rewrite for new tables |
| `ADD Tabs/TabClass.vb` | ITabs2 | Update to host new 4-tab form |
| `ADD Timers/iSync.vb` | ISync | Rewrite for rotation check |
| `ADD Timers/iSync2.vb` | ISync2 | Rewrite for new machine detection |
| `Globals.vb` | Module | Shared constants — remove sqlPassword |
| `Config.vb` | Module | Read %SystemDrive%\Triton\MSPAccounts\config.xml |
| `Logger.vb` | Class | Wraps objHost.LogMessage() with levels |
| `PasswordManager.vb` | Class | RNGCryptoServiceProvider, f_CWAESEncrypt integration |
| `AccountDeployer.vb` | Class | Deploy/verify/remove TritonTech account via SendCommand |
| `MigrationManager.vb` | Class | One-time legacy account replacement |
| `MainForm.vb` | WinForms | 4-tab UI: Dashboard, Actions, Settings, Log |
| `MainForm.Designer.vb` | — | Auto-generated by WinForms designer |

### Files to DELETE (from original)

| File | Reason |
|------|--------|
| `UserManagement.vb` | Entire domain-account management system removed |
| `Reporting.vb` | Old email reporting replaced by Logger.vb + UI log tab |
| `ServiceManagement.vb` | Replaced by AccountDeployer.vb (expanded scope) |
| `PasswordManagement.vb` | Replaced by PasswordManager.vb (new security model) |
| `MSP_Accounts.vb` | Replaced by MainForm.vb |
| `MSP_Accounts.Designer.vb` | Replaced by MainForm.Designer.vb |
| `MSP_Accounts.resx` | Replaced by MainForm.resx |

---

## Database Schema (New)

### plugin_triton_msp_settings

Plugin configuration. Key-value pairs. Populated with defaults on first run.

```sql
CREATE TABLE `plugin_triton_msp_settings` (
    `SettingID`    int(11)       NOT NULL AUTO_INCREMENT,
    `Setting`      varchar(255)  NOT NULL,
    `Value`        varchar(1000) NOT NULL DEFAULT '',
    PRIMARY KEY (`SettingID`),
    UNIQUE KEY `Setting` (`Setting`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;
```

| Setting Key | Default Value | Description |
|------------|--------------|-------------|
| `AccountName` | `TritonTech` | Local account username to deploy |
| `DisplayName` | `Triton Technologies Support` | Full name for the account |
| `Description` | `Managed support account - Triton Technologies` | Account description |
| `HideFromLogin` | `1` | 1 = hide from Windows login screen |
| `RotationDays` | `90` | Days between password rotations |
| `AutoRotate` | `1` | 1 = enable automatic rotation (ISync) |
| `PasswordScope` | `Location` | `Location` or `Client` |
| `MinLength` | `12` | Minimum password length |
| `MinUpper` | `2` | Minimum uppercase characters |
| `MinLower` | `2` | Minimum lowercase characters |
| `MinNumbers` | `2` | Minimum numeric characters |
| `MinSpecial` | `2` | Minimum special characters |
| `LegacyAccountName` | `TT_Service` | Legacy account name to replace during migration |
| `MigrationComplete` | `0` | 1 = one-time migration has been run |
| `ConfigFilePath` | `%SystemDrive%\Triton\MSPAccounts\config.xml` | Config file path (expandable env vars) |

---

### plugin_triton_msp_deployments

Tracks deployment state and rotation history per location.

```sql
CREATE TABLE `plugin_triton_msp_deployments` (
    `DeploymentID`   int(11)      NOT NULL AUTO_INCREMENT,
    `LocationID`     int(11)      NOT NULL,
    `PasswordID`     int(11)      DEFAULT NULL,
    `Status`         varchar(50)  NOT NULL DEFAULT 'Pending',
    `LastDeployed`   datetime     DEFAULT NULL,
    `LastRotated`    datetime     DEFAULT NULL,
    `RotationDue`    datetime     DEFAULT NULL,
    `MachineCount`   int(11)      NOT NULL DEFAULT 0,
    `SuccessCount`   int(11)      NOT NULL DEFAULT 0,
    `FailureCount`   int(11)      NOT NULL DEFAULT 0,
    `LastLog`        text         DEFAULT NULL,
    `UpdatedAt`      datetime     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
    PRIMARY KEY (`DeploymentID`),
    UNIQUE KEY `LocationID` (`LocationID`),
    KEY `Status` (`Status`),
    KEY `RotationDue` (`RotationDue`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;
```

| Column | Description |
|--------|-------------|
| `LocationID` | CW Automate Location ID (FK to locations table) |
| `PasswordID` | FK to passwords table — the password entry in CW Passwords tab |
| `Status` | `Pending`, `Active`, `Failed`, `Migrating` |
| `LastDeployed` | When TritonTech was last deployed/verified |
| `LastRotated` | When password was last successfully rotated |
| `RotationDue` | `LastRotated + RotationDays days` — next rotation date |
| `MachineCount` | Total workstations in this location |
| `SuccessCount` | Machines with successful account deployment |
| `FailureCount` | Machines with deployment failures |
| `LastLog` | Text log from the last operation |

---

### CW Automate Native Tables Used (read/write)

| Table | Usage |
|-------|-------|
| `passwords` | Read/write password entries for the Passwords tab |
| `locations` | Read location names, ClientID, PasswordID FK |
| `clients` | Read client names |
| `computers` | Read ComputerID, UserAccounts, OS, LastContact, LocationID |
| `commands` | Read Status and Output after SendCommand() |
| `config` | Read-only — contains serverId used by f_CWAESEncrypt |

---

## UI Design — 4-Tab Layout

### Tab 1: Dashboard

- **Summary cards** at top: Total Deployed, Due for Rotation, Failures, Total Managed Machines
- **Location grid** showing:
  - Location Name
  - Client Name
  - Status pill (Active / Pending / Failed / Due Soon)
  - Machine count / success count
  - Last Rotated date
  - Rotation Due date
  - Per-row action buttons: **[Rotate]** **[Deploy]**
- Status bar at bottom: connected CW Automate server, logged-in tech name

### Tab 2: Actions

- **Deploy All** — deploy TritonTech to all managed locations
- **Rotate Overdue** — rotate all locations past their due date
- **Rotate Single Location** — dropdown picker → [Rotate Now]
- **Run Migration** — one-time legacy account replacement (with warning dialog)

### Tab 3: Settings

```
Account Settings
  Account Name:        [TritonTech          ]  (read-only — not configurable per deployment)
  Display Name:        [Triton Technologies Support    ]
  Description:         [Managed support account - Triton Technologies    ]
  [x] Hide account from Windows login screen

Rotation Settings
  Rotation Interval:   [90] days
  [x] Enable automatic rotation (daily check at midnight)
  [x] Enable new machine detection (every 6 minutes)

Password Policy
  Minimum Length:      [12]
  Minimum Uppercase:   [2]
  Minimum Lowercase:   [2]
  Minimum Numbers:     [2]
  Minimum Special:     [2]
  Password Scope:      ( ) Per-Location  ( ) Per-Client

Security
  Key Storage:         CW Automate Password Vault  [read-only — not changeable]
  CW Vault Entry:      Passwords tab, per-location  [read-only]
  Encryption:          f_CWAESEncrypt (CW native)   [read-only]

Plugin Data Directory
  Config File Path:    [%SystemDrive%\Triton\MSPAccounts\config.xml  ]
  Resolved Path:       C:\Triton\MSPAccounts\config.xml
```

### Tab 4: Log

- Filterable operation log
- Log levels: Trace, Debug, Info, Warn, Error
- Columns: Timestamp, Level, Location, Message
- [Clear Log] [Export Log] buttons
- Auto-scrolls to newest entry

---

## Deployment Architecture

```
Triton Technologies Internal
    │
    ├── CW Automate Server (self-hosted)
    │       ├── MySQL 8.0.41
    │       │       ├── plugin_triton_msp_settings
    │       │       ├── plugin_triton_msp_deployments
    │       │       ├── passwords          ← TritonTech passwords stored here
    │       │       ├── locations
    │       │       └── computers
    │       │
    │       └── Automate Agent (on server)
    │
    ├── Technician Workstations (where plugin DLL runs)
    │       └── CW Automate Control Center (client app)
    │               └── Triton Account Manager.dll  ← Plugin loaded here
    │                       ├── IPlugin, IMenu, IPermissions, ITabs2
    │                       ├── ISync (daily rotation check)
    │                       └── ISync2 (6-min new machine check)
    │
    └── Managed Client Workstations (where TritonTech account lives)
            ├── CW Automate Agent (receives commands)
            ├── Local account: TritonTech (Administrator)
            └── Winlogon registry (account hidden from login screen)
```

### Plugin DLL Location

```
C:\Program Files (x86)\LabTech\Plugins\Triton Account Manager.dll
```
(or the ConnectWise Automate equivalent path for your installation)

---

## Security Design

### What Was Eliminated

| Old Risk | Resolution |
|----------|-----------|
| Hardcoded `"shinybrowncoat"` encryption key | Eliminated — no custom encryption key |
| `System.Random` for password generation | Replaced with `RNGCryptoServiceProvider` |
| SHA-1 key derivation (`SHA()`) | Eliminated — using `f_CWAESEncrypt` (CW native) |
| SQL injection via string concatenation | Use `IParameterizedQuery` where available; sanitize all inputs |
| `On Error GoTo` error handling | Replaced with `Try/Catch/Finally` throughout |
| `MessageBox.Show` in background threads | Replaced with `objHost.LogMessage()` |
| Polling loops with no timeout | All loops have configurable timeout + iteration limit |

### No Master Key Required

The plugin does not need its own encryption key. CW Automate's `f_CWAESEncrypt()` function uses the server's own `serverId` (from the CW Automate `config` table) as the encryption key. This means:

- If the plugin DLL is removed, passwords remain visible in the CW Passwords tab (the server can still decrypt them)
- The key never leaves the CW Automate server
- The plugin never handles raw encryption keys

### Input Sanitization Rules

All values going into SQL strings must be sanitized:
- Location names: alphanumeric + spaces + hyphens only
- Passwords: no shell-special characters that could break `net user` commands
- Log messages: no SQL special characters

---

## Build & Deploy Process

### Prerequisites

- Visual Studio 2022
- .NET Framework 4.8 SDK
- `Interfaces.dll` v2024.2.72.0 (in repo root)
- Access to CW Automate test environment for testing

### Build Steps

```
1. git clone https://github.com/THarmon77/MSPAccounts.git
2. Open solution in Visual Studio 2022
3. Verify Interfaces.dll reference resolves
4. Set target framework: .NET Framework 4.8
5. Build → Build Solution
6. Output: bin\Release\Triton Account Manager.dll
```

### Deployment Steps

```
1. Back up existing plugin DLL (see Backup Procedures in README)
2. Back up plugin DB tables (SQL backup scripts in README)
3. Close CW Automate Control Center on ALL technician workstations
4. Replace DLL in C:\Program Files (x86)\LabTech\Plugins\
5. Reopen Control Center — plugin loads, IPermissions runs table creation
6. Verify tables created: SHOW TABLES LIKE 'plugin_triton_msp%';
7. Open Triton Account Manager from menu → verify UI loads
8. Check CW Automate log for any initialization errors
```

---

## Open Questions / Future Work

| Item | Status | Notes |
|------|--------|-------|
| Confirm exact `f_CWAESEncrypt` parameter in v25.0.436 | Pending test | Need to verify encryptLevel 0 vs 1 against actual CW Passwords tab display |
| IParameterizedQuery interface | Pending verification | Confirm it's in Interfaces.dll v2024.2.72.0 — use for SQL injection prevention |
| CW Manage ticket creation on failure | Future v3.1 | Create service ticket when deployment fails |
| Email notification on rotation | Future | Could use ICoreFunctionality.SendEmail |
| Multi-workstation parallel deployment | Design needed | ThreadPool vs Task.WhenAll — avoid overwhelming CW DB with simultaneous commands |
| Azure AD joined machine verification | Needs test | `net user` commands should work but needs real-world confirmation |
| Windows Home edition testing | Needs test | `net localgroup` may behave differently on Home |
