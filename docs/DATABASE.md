# Database Reference

**CW Automate version:** v25.0.436 Patch 11  
**MySQL version:** 8.0.41  
**Total tables in CW Automate DB:** 660 tables, 135 views  
**Schema reference:** https://automationtheory.org/schema/index.html

---

## Table of Contents

1. [f_CWAESEncrypt — Password Encryption Function](#f_cwaesencrypt--password-encryption-function)
2. [CW Automate Native Tables Used by Plugin](#cw-automate-native-tables-used-by-plugin)
3. [Plugin Tables (v3 New)](#plugin-tables-v3-new)
4. [Plugin Tables (v2 Legacy — Reference Only)](#plugin-tables-v2-legacy--reference-only)
5. [Key SQL Queries](#key-sql-queries)
6. [IControlCenter SQL Methods Reference](#icontrolcenter-sql-methods-reference)

---

## f_CWAESEncrypt — Password Encryption Function

### What It Is

`f_CWAESEncrypt` is a **MySQL stored function built into CW Automate** that encrypts strings using AES with the CW Automate server's own identity key (`serverId` from the config table). It is the same function CW Automate uses internally to store all passwords in its Passwords tab.

**This is how our plugin stores TritonTech passwords** — using this function means the encrypted blob is natively readable in the CW Automate UI without any plugin involvement.

### Signature

```sql
f_CWAESEncrypt(encryptLevel INT, encryptString VARCHAR(255))
-- Returns: BLOB (the encrypted bytes)
```

| Parameter | Type | Description |
|-----------|------|-------------|
| `encryptLevel` | INT | Encryption key level. Use `0` for standard password storage. |
| `encryptString` | VARCHAR(255) | The plaintext string to encrypt. |

### Usage Examples

```sql
-- Store a new password in the passwords table
UPDATE passwords
SET Password   = f_CWAESEncrypt(0, 'NewPassword123!'),
    Last_User  = 'TritonMSP_Plugin',
    Last_Date  = NOW(),
    ExpireDate = DATE_ADD(NOW(), INTERVAL 90 DAY)
WHERE PasswordID = 42;

-- Insert a new password record
INSERT INTO passwords (Title, Username, Password, ExpireDate, Last_User, Last_Date)
VALUES (
    'TritonTech - Acme Corp HQ',
    'TritonTech',
    f_CWAESEncrypt(0, 'NewPassword123!'),
    DATE_ADD(NOW(), INTERVAL 90 DAY),
    'TritonMSP_Plugin',
    NOW()
);
```

### Critical Notes

- The encryption key is derived from `serverId` in CW Automate's `config` table
- This key **never leaves the CW Automate server** — the plugin never touches it
- If the plugin is uninstalled, passwords remain readable in the CW Automate Passwords tab (the server still has the key)
- The plugin does NOT need its own master key, DPAPI, or any custom encryption — `f_CWAESEncrypt` handles everything
- The `Password` field in the `passwords` table is type `BLOB` — the encrypted output of `f_CWAESEncrypt` is stored as binary

### Verification

To confirm the function works in your environment:
```sql
-- Test: encrypt a string and see the blob
SELECT HEX(f_CWAESEncrypt(0, 'TestPassword123!'));
-- Should return a non-empty hex string

-- Then verify by looking at it through the CW Automate UI:
-- Go to any location → Passwords tab → the password should be visible
```

---

## CW Automate Native Tables Used by Plugin

### passwords

Stores all password vault entries. Displayed in the CW Automate Passwords tab per location/client.

```sql
DESCRIBE passwords;
```

| Column | Type | Key | Description |
|--------|------|-----|-------------|
| `PasswordID` | int(11) | PRI | Primary key, referenced by locations.PasswordID |
| `Title` | varchar(255) | | Display name shown in Passwords tab |
| `Username` | varchar(255) | | Username associated with the password |
| `Password` | BLOB | | AES-encrypted password (via f_CWAESEncrypt) |
| `URL` | varchar(255) | | Optional URL for web credentials |
| `Notes` | text | | Optional notes |
| `ClientID` | int(11) | | Client this password belongs to (0 = global) |
| `LocationID` | int(11) | | Location this password belongs to (0 = client-level) |
| `ExpireDate` | date | | Password expiry date (shown in UI) |
| `Last_User` | varchar(255) | | Last user/system that updated this password |
| `Last_Date` | datetime | | Timestamp of last update |
| `SubType` | int(11) | | Password category/subtype |

### Key Queries

```sql
-- Get password entry for a specific location
SELECT p.PasswordID, p.Title, p.Username, p.ExpireDate, p.Last_User, p.Last_Date
FROM passwords p
INNER JOIN locations l ON l.PasswordID = p.PasswordID
WHERE l.LocationID = 42;

-- Check if a TritonTech password entry already exists for a location
SELECT COUNT(*) FROM passwords p
INNER JOIN locations l ON l.PasswordID = p.PasswordID
WHERE l.LocationID = 42 AND p.Username = 'TritonTech';

-- Create new password entry and link to location (two-step)
INSERT INTO passwords (Title, Username, Password, ExpireDate, Last_User, Last_Date, LocationID)
VALUES ('TritonTech - LocationName', 'TritonTech',
        f_CWAESEncrypt(0, 'GeneratedPassword123!'),
        DATE_ADD(NOW(), INTERVAL 90 DAY), 'TritonMSP_Plugin', NOW(), 42);

SET @newPwdID = LAST_INSERT_ID();
UPDATE locations SET PasswordID = @newPwdID WHERE LocationID = 42;
```

---

### locations

All CW Automate locations (sites). Each location belongs to a client.

```sql
DESCRIBE locations;
```

| Column | Type | Key | Description |
|--------|------|-----|-------------|
| `LocationID` | int(11) | PRI | Primary key |
| `ClientID` | int(11) | MUL | FK to clients table |
| `Name` | varchar(200) | | Location display name |
| `PasswordID` | int(11) | MUL | FK to passwords table (primary password for this location) |
| `Phone` | varchar(100) | | Location phone |
| `Address1` | varchar(200) | | Street address |
| `City` | varchar(100) | | City |
| `State` | varchar(50) | | State/province |
| `Zip` | varchar(20) | | Postal code |
| `Domain` | varchar(200) | | Primary domain for this location |
| `Router` | varchar(50) | | Router IP |
| `SubnetMask` | varchar(50) | | Subnet mask |

### Key Queries

```sql
-- All locations with client names, for dashboard grid
SELECT l.LocationID, l.Name AS LocationName, c.Name AS ClientName,
       l.PasswordID, l.Domain
FROM locations l
INNER JOIN clients c ON l.ClientID = c.ClientID
ORDER BY c.Name, l.Name;

-- Locations with active managed workstations
SELECT DISTINCT l.LocationID, l.Name, c.Name AS ClientName
FROM locations l
INNER JOIN clients c ON l.ClientID = c.ClientID
INNER JOIN computers comp ON comp.LocationID = l.LocationID
WHERE comp.OS NOT LIKE '%Server%'
AND TIMESTAMPDIFF(HOUR, comp.LastContact, NOW()) < 24
ORDER BY c.Name, l.Name;
```

---

### clients

Top-level client records.

| Column | Type | Description |
|--------|------|-------------|
| `ClientID` | int(11) PK | Primary key |
| `Name` | varchar(200) | Client display name |
| `City` | varchar(100) | City |
| `State` | varchar(50) | State |

---

### computers

All agents/computers managed by CW Automate.

```sql
DESCRIBE computers;
```

| Column | Type | Key | Description |
|--------|------|-----|-------------|
| `ComputerID` | int(11) | PRI | Primary key |
| `ClientID` | int(11) | MUL | FK to clients |
| `LocationID` | int(11) | MUL | FK to locations |
| `Name` | varchar(255) | | Computer/hostname |
| `Domain` | varchar(255) | | Domain name (or workgroup name) |
| `OS` | varchar(255) | | Operating system string |
| `UserAccounts` | LONGTEXT | | Colon-delimited list of local accounts: `:user1::user2:` |
| `LastContact` | datetime | | Last agent check-in time |
| `IPAddress` | varchar(100) | | Primary IP |
| `Username` | varchar(255) | | Currently logged in user |
| `OperatingSystem` | varchar(255) | | Detailed OS string |

### computers.UserAccounts Format

```
:Administrator::TritonTech::Guest::OtherUser:
```

Each username is surrounded by colons. To check if TritonTech exists:
```sql
-- Check single machine
SELECT ComputerID FROM computers
WHERE ComputerID = 12345
AND UserAccounts LIKE '%:TritonTech:%';

-- Find all machines in a location WITHOUT TritonTech (need deployment)
SELECT c.ComputerID, c.Name, c.OS, c.LastContact
FROM computers c
WHERE c.LocationID = 42
AND c.OS NOT LIKE '%Server%'
AND TIMESTAMPDIFF(HOUR, c.LastContact, NOW()) < 1
AND (c.UserAccounts NOT LIKE '%:TritonTech:%' OR c.UserAccounts IS NULL)
ORDER BY c.Name;

-- Find all managed workstations across all locations
SELECT c.ComputerID, c.Name, c.LocationID, l.Name AS LocationName,
       c.UserAccounts, c.LastContact
FROM computers c
INNER JOIN locations l ON c.LocationID = l.LocationID
WHERE c.OS NOT LIKE '%Server%'
AND TIMESTAMPDIFF(HOUR, c.LastContact, NOW()) < 1
ORDER BY l.Name, c.Name;
```

---

### commands

Tracks all commands sent to agents. Created by `SendCommand()`, polled for status.

| Column | Type | Description |
|--------|------|-------------|
| `CmdID` | int(11) PK | Command ID — returned by SendCommand() |
| `ComputerID` | int(11) | Target computer |
| `Command` | int(11) | Command type ID (2=shell, 17=hardware, 123=sysinfo) |
| `Status` | int(11) | 0=Waiting, 1=Sent, 2=Executing, 3=Success, 4=Error |
| `Output` | LONGTEXT | Command output text after execution |
| `Parameters` | text | Command parameters as sent |
| `SendTime` | datetime | When command was queued |
| `CompletionTime` | datetime | When command finished |

### Key Queries

```sql
-- Poll command status
SELECT Status FROM commands WHERE CmdID = 98765;

-- Get command output
SELECT Output FROM commands WHERE CmdID = 98765 AND Status >= 3;

-- Clean up old commands (housekeeping — not plugin responsibility)
SELECT CmdID, Command, Status, Output, SendTime
FROM commands
WHERE ComputerID = 12345
ORDER BY SendTime DESC
LIMIT 10;
```

---

### config

CW Automate server configuration. Read-only for the plugin.

| Column | Description |
|--------|-------------|
| `ServerID` | The CW Automate server's unique ID — used as key by f_CWAESEncrypt |
| `ServerName` | Server hostname |
| `DBVersion` | CW Automate database version |

> **Never write to the config table.** Read-only access only. The `ServerID` field is the root of the f_CWAESEncrypt key derivation.

---

## Plugin Tables (v3 New)

### plugin_triton_msp_settings

```sql
CREATE TABLE `plugin_triton_msp_settings` (
    `SettingID`    int(11)       NOT NULL AUTO_INCREMENT,
    `Setting`      varchar(255)  NOT NULL,
    `Value`        varchar(1000) NOT NULL DEFAULT '',
    PRIMARY KEY (`SettingID`),
    UNIQUE KEY `Setting` (`Setting`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8;

-- Default data inserted on first run:
INSERT INTO plugin_triton_msp_settings (Setting, Value) VALUES
    ('AccountName',     'TritonTech'),
    ('DisplayName',     'Triton Technologies Support'),
    ('Description',     'Managed support account - Triton Technologies'),
    ('HideFromLogin',   '1'),
    ('RotationDays',    '90'),
    ('AutoRotate',      '1'),
    ('PasswordScope',   'Location'),
    ('MinLength',       '12'),
    ('MinUpper',        '2'),
    ('MinLower',        '2'),
    ('MinNumbers',      '2'),
    ('MinSpecial',      '2'),
    ('LegacyAccountName',  'TT_Service'),
    ('MigrationComplete',  '0'),
    ('ConfigFilePath',  '%SystemDrive%\\Triton\\MSPAccounts\\config.xml');
```

---

### plugin_triton_msp_deployments

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

---

## Plugin Tables (v2 Legacy — Reference Only)

These tables are from the original MSP Accounts plugin. They are documented here for migration reference and should be preserved as backups before the v3 upgrade.

### plugin_itsc_msp_accounts_settings (v2)

| Column | Type | Default | Description |
|--------|------|---------|-------------|
| `MSP_Name` | varchar(50) PK | — | MSP name (settings key) |
| `User_Prefix` | varchar(50) | `MSP_` | Prefix for domain accounts |
| `Exclude_Locations` | varchar(2000) | `0,1` | Comma-sep location IDs to skip |
| `Service_Account` | varchar(50) | `None` | Service account username |
| `Min_Password_Length` | tinyint(2) | 14 | Minimum password length |
| `Password_Change_Days` | tinyint(3) | 59 | Rotation interval |
| `Local_Service_Account` | BINARY(1) | 0 | Enable local account management |
| `Local_Service_Account_Exclude` | BINARY(1) | 1 | Exclude flagged locations |
| `Min_Password_Upper` | tinyint(2) | 2 | Min uppercase chars |
| `Min_Password_Lower` | tinyint(2) | 2 | Min lowercase chars |
| `Min_Password_Number` | tinyint(2) | 2 | Min numeric chars |
| `Min_Password_Special` | tinyint(2) | 2 | Min special chars |

### plugin_itsc_msp_accounts_users (v2)

| Column | Type | Description |
|--------|------|-------------|
| `Username` | varchar(50) PK | MSP technician username |
| `Password` | BLOB | `AES_ENCRYPT(pwd, SHA("shinybrowncoat"))` — COMPROMISED KEY |
| `AutoChangePassword` | tinyint(1) | Auto-rotation enabled |
| `AutoChangeDate` | date | Date of last rotation |

> ⚠️ All passwords in this table are encrypted with the publicly known key `"shinybrowncoat"`. Treat as plaintext-equivalent.

### plugin_itsc_msp_accounts_userstatus (v2)

| Column | Type | Description |
|--------|------|-------------|
| `Username` | varchar(50) PK | MSP technician username |
| `PluginUserEmail` | varchar(50) | Email for change log reports |
| `TimeStamp` | DATETIME | Last operation timestamp |
| `ClientDCids` | varchar(5000) | Pipe-delimited DC computerIDs from last bulk change |
| `[domain.name]` | varchar(255) | Dynamic column per client domain: `YYYY-MM-DD HH:MM:SS\|result` |

---

## Key SQL Queries

### Dashboard Queries

```sql
-- Dashboard summary for all managed locations
SELECT
    l.LocationID,
    l.Name AS LocationName,
    c.Name AS ClientName,
    COALESCE(d.Status, 'Not Started') AS DeploymentStatus,
    COALESCE(d.MachineCount, 0) AS TotalMachines,
    COALESCE(d.SuccessCount, 0) AS Deployed,
    COALESCE(d.FailureCount, 0) AS Failed,
    d.LastRotated,
    d.RotationDue,
    CASE
        WHEN d.RotationDue IS NULL THEN 'Unknown'
        WHEN d.RotationDue < NOW() THEN 'Overdue'
        WHEN d.RotationDue < DATE_ADD(NOW(), INTERVAL 7 DAY) THEN 'Due Soon'
        ELSE 'Current'
    END AS RotationStatus
FROM locations l
INNER JOIN clients c ON l.ClientID = c.ClientID
LEFT JOIN plugin_triton_msp_deployments d ON d.LocationID = l.LocationID
WHERE l.LocationID IN (
    -- Only show locations with managed workstations
    SELECT DISTINCT LocationID FROM computers
    WHERE OS NOT LIKE '%Server%'
    AND TIMESTAMPDIFF(HOUR, LastContact, NOW()) < 24
)
ORDER BY c.Name, l.Name;
```

```sql
-- Summary card counts
SELECT
    COUNT(CASE WHEN d.Status = 'Active' THEN 1 END) AS TotalDeployed,
    COUNT(CASE WHEN d.RotationDue < NOW() THEN 1 END) AS OverdueRotations,
    COUNT(CASE WHEN d.FailureCount > 0 THEN 1 END) AS LocationsWithFailures,
    SUM(COALESCE(d.MachineCount, 0)) AS TotalManagedMachines
FROM plugin_triton_msp_deployments d;
```

### Rotation Queries

```sql
-- Find locations due for rotation
SELECT d.LocationID, l.Name, d.LastRotated, d.RotationDue,
       DATEDIFF(NOW(), d.LastRotated) AS DaysSinceRotation,
       s.Value AS RotationDays
FROM plugin_triton_msp_deployments d
INNER JOIN locations l ON l.LocationID = d.LocationID
CROSS JOIN plugin_triton_msp_settings s
WHERE s.Setting = 'RotationDays'
AND d.Status = 'Active'
AND DATEDIFF(NOW(), d.LastRotated) >= CAST(s.Value AS UNSIGNED)
ORDER BY d.RotationDue ASC;
```

```sql
-- Update password after successful rotation
UPDATE passwords p
INNER JOIN locations l ON l.PasswordID = p.PasswordID
SET p.Password   = f_CWAESEncrypt(0, 'NewPassword123!'),
    p.Last_User  = 'TritonMSP_AutoRotate',
    p.Last_Date  = NOW(),
    p.ExpireDate = DATE_ADD(NOW(), INTERVAL 90 DAY)
WHERE l.LocationID = 42;

UPDATE plugin_triton_msp_deployments
SET LastRotated = NOW(),
    RotationDue = DATE_ADD(NOW(), INTERVAL 90 DAY),
    LastLog = 'Rotation completed successfully'
WHERE LocationID = 42;
```

### New Machine Detection Query

```sql
-- Online workstations in managed locations WITHOUT TritonTech
SELECT c.ComputerID, c.Name, c.LocationID, l.Name AS LocationName,
       c.UserAccounts, c.LastContact, c.OS
FROM computers c
INNER JOIN locations l ON c.LocationID = l.LocationID
INNER JOIN plugin_triton_msp_deployments d ON d.LocationID = c.LocationID
WHERE c.OS NOT LIKE '%Server%'
AND TIMESTAMPDIFF(HOUR, c.LastContact, NOW()) < 1
AND (c.UserAccounts NOT LIKE '%:TritonTech:%' OR c.UserAccounts IS NULL)
AND d.Status = 'Active'
ORDER BY c.LocationID, c.Name;
```

### Migration Queries

```sql
-- Find machines with legacy account (e.g., TT_Service)
SELECT c.ComputerID, c.Name, c.LocationID, c.UserAccounts
FROM computers c
WHERE c.OS NOT LIKE '%Server%'
AND c.UserAccounts LIKE '%:TT_Service:%'
ORDER BY c.LocationID, c.Name;

-- Count migration status
SELECT
    COUNT(CASE WHEN UserAccounts LIKE '%:TritonTech:%' THEN 1 END) AS HasTritonTech,
    COUNT(CASE WHEN UserAccounts LIKE '%:TT_Service:%' THEN 1 END) AS HasLegacy,
    COUNT(CASE WHEN UserAccounts LIKE '%:TritonTech:%'
               AND UserAccounts LIKE '%:TT_Service:%' THEN 1 END) AS HasBoth,
    COUNT(*) AS Total
FROM computers
WHERE OS NOT LIKE '%Server%'
AND TIMESTAMPDIFF(HOUR, LastContact, NOW()) < 24;
```

---

## IControlCenter SQL Methods Reference

Quick reference for the SQL methods available via the IControlCenter host object:

| Method | Use Case | Returns |
|--------|----------|---------|
| `GetSQL("SELECT...")` | Single value queries | String (first column, first row) |
| `GetValues("SELECT...")` | Lists for ComboBox/ListView | ArrayList of strings |
| `GetDataSet("SELECT...")` | Multi-row grid data | DataSet |
| `SetSQL("INSERT/UPDATE/DELETE")` | Execute with no return | Nothing |
| `SetSQLWithID("INSERT...")` | Execute INSERT, get new row ID | Integer |

### Important Notes

- All SQL runs against the CW Automate MySQL database directly
- No parameterized query support in the base interface — sanitize ALL user inputs before including in SQL strings
- `GetSQL()` returns `Nothing` if no rows match — always check for `Nothing` or empty string
- `GetDataSet()` returns a DataSet with Tables(0) — check `ds.Tables(0).Rows.Count > 0` before iterating
- There is no transaction support in the plugin SQL interface — design operations to be safe if interrupted
