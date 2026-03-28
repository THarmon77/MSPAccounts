# MSP Accounts — ConnectWise Automate Plugin

> **Fork of [mspgeek/MSPAccounts](https://github.com/mspgeek/MSPAccounts)**
> Maintained by Triton Technologies (THarmon77) — Private fork for internal use.
> Original plugin by MrRat / RealTime, LLC — Copyright 2015.
> This fork: modernization and maintenance for ConnectWise Automate 2026.

---

## Table of Contents

1. [What This Plugin Does](#what-this-plugin-does)
2. [Architecture Overview](#architecture-overview)
3. [File Structure](#file-structure)
4. [Prerequisites](#prerequisites)
5. [Building from Source](#building-from-source)
6. [Installation & Deployment](#installation--deployment)
7. [Configuration](#configuration)
8. [How the Plugin Works — Module by Module](#how-the-plugin-works--module-by-module)
9. [Database Tables](#database-tables)
10. [Security Notes & Known Issues](#security-notes--known-issues)
11. [Backup Procedures](#backup-procedures)
12. [Rollback & Revert Procedures](#rollback--revert-procedures)
13. [2026 Modernization Roadmap](#2026-modernization-roadmap)
14. [Branching Strategy](#branching-strategy)
15. [Contributing & Change Process](#contributing--change-process)
16. [Attribution & License](#attribution--license)

---

## What This Plugin Does

MSP Accounts is a **ConnectWise Automate (formerly LabTech) client-side plugin** that gives MSP technicians a centralized interface to manage their own support accounts across all client Active Directory domains — directly from within the CW Automate console.

**Core capabilities:**

| Feature | Description |
|---|---|
| **Domain User Create** | Creates an MSP-prefixed Domain Admin account in an OU across any client AD |
| **Domain User Delete** | Removes the MSP account from a client AD |
| **Password Change (Bulk)** | Changes the MSP user's password simultaneously across every domain where that user exists |
| **Local Service Account** | Adds/removes/rotates a local administrator account on standalone/workgroup machines |
| **Password Vault Integration** | Stores and updates passwords in CW Automate's built-in password vault |
| **Change Log / Reporting** | Emails a per-user change log after every password operation |
| **Auto Password Rotation** | Timer-driven automatic password changes on a configurable schedule |

---

## Architecture Overview

This is a **VB.NET Windows Forms class library** compiled to a `.dll` that CW Automate loads as a plugin at startup. It does **not** run on a server — it runs inside the **CW Automate Control Center client application** on the technician's workstation.

```
CW Automate Control Center (client EXE)
    └── Loads plugin DLL via LabTech.Interfaces
            ├── IPlugin        → PluginMain.vb        (registration)
            ├── IMenu          → clsMenus.vb           (adds menu item)
            ├── IPermissions   → clsPermissions.vb     (creates DB tables)
            ├── ITab           → TabClass.vb            (adds UI tab)
            ├── ISync          → iSync.vb / iSync2.vb  (background timers)
            └── Windows Form   → MSP_Accounts.vb       (main UI)
                    ├── UserManagement.vb
                    ├── PasswordManagement.vb
                    ├── ServiceManagement.vb
                    └── Reporting.vb
```

**Key communication pattern:**  
The plugin talks to agents on client machines by calling `objHost.SendCommand(computerID, commandType, args)` — this queues a command in the CW Automate database, the agent on the remote machine executes it, and the plugin polls `SELECT Status FROM commands WHERE cmdid=X` in a loop until status >= 3 (completed).

---

## File Structure

```
MSPAccounts/
│
├── MSP Accounts.sln              # Visual Studio solution file
├── MSP Accounts.vbproj           # VB.NET project file (.NET Framework)
├── Interfaces.dll                # CW Automate plugin SDK (pre-compiled, do not modify)
│
├── PLUGIN DEFINITION/
│   └── PluginMain.vb             # IPlugin entry point — registers name, version, author
│
├── ADD Menus/
│   └── clsMenus.vb               # IMenu — adds "MSP Accounts" to the CW Automate menu bar
│
├── ADD Tables/
│   └── clsPermissions.vb         # IPermissions — creates/migrates plugin DB tables on startup
│
├── ADD Tabs/
│   └── TabClass.vb               # ITab — adds a tab panel inside CW Automate
│
├── ADD Timers/
│   ├── iSync.vb                  # ISync timer — drives automatic password rotation
│   └── iSync2.vb                 # ISync timer — secondary scheduled task
│
├── Globals.vb                    # Module: shared constants (version, plugin name, author)
│                                 # ⚠️  CONTAINS HARDCODED SQL PASSWORD — see Security Notes
├── Dictionary.vb                 # Placeholder (currently empty)
├── Settings.vb                   # MySettings partial class stub
│
├── UserManagement.vb             # Class: create/delete/verify AD domain accounts
├── PasswordManagement.vb         # Class: generate, validate, and bulk-change passwords
├── ServiceManagement.vb          # Class: manage local service accounts on workgroup machines
├── Reporting.vb                  # Class: build and email password change log reports
│
├── MSP_Accounts.vb               # Main Windows Form (UI logic — large, ~1200 lines)
├── MSP_Accounts.Designer.vb      # Auto-generated WinForms designer code (do not edit manually)
├── MSP_Accounts.resx             # Form resource file (icons, strings)
│
├── My Project/                   # AssemblyInfo, application settings
└── Resources/                    # Embedded images and icons
```

---

## Prerequisites

### Build Environment
| Requirement | Version |
|---|---|
| Visual Studio | 2019 or 2022 (Community or higher) |
| .NET Framework | 4.8 (matches CW Automate client) |
| VB.NET | Included with Visual Studio |
| `Interfaces.dll` | Included in repo root — do not replace unless upgrading CW Automate |

### Runtime Environment
| Requirement | Notes |
|---|---|
| ConnectWise Automate | Self-hosted, Control Center client installed on technician workstation |
| CW Automate version | Originally written for ~v10-11 (2015). Verify interface compatibility before deploying to newer versions. |
| Plugin folder access | Technician must have write access to the CW Automate plugins directory |
| MySQL access | Plugin creates/queries tables in the CW Automate MySQL database |

---

## Building from Source

### Step 1 — Clone the repo

```bash
git clone https://github.com/THarmon77/MSPAccounts.git
cd MSPAccounts
```

### Step 2 — Open in Visual Studio

Open `MSP Accounts.sln` in Visual Studio 2019 or 2022.

### Step 3 — Verify references

In Solution Explorer → References, confirm `Interfaces.dll` resolves correctly. It should point to the `Interfaces.dll` in the repo root. If it shows as missing:
- Right-click References → Add Reference → Browse → select `Interfaces.dll` from the repo root.

### Step 4 — Configure `Globals.vb` before building

**Before building, update these constants in `Globals.vb`:**

```vb
Public Const mVersion As Double = 2.0          ' Update for each release
Public Const mAuthor As String = "Triton Technologies"
Public Const PluginName As String = "MSP Accounts - Triton"
Public Const sqlPassword As String = "CHANGE_THIS_BEFORE_BUILD"   ' ⚠️ See Security Notes
```

### Step 5 — Build

- Build → Build Solution (`Ctrl+Shift+B`)
- Output DLL will be in `bin\Release\` or `bin\Debug\`
- The compiled file will be named `MSP Accounts.dll`

### Step 6 — Run tests (manual for now)

There are no automated unit tests in the original project. Before deploying any change, perform the manual test checklist in the [Testing Checklist](#testing-checklist) section below.

---

## Installation & Deployment

### First-time install

1. **Back up first** — see [Backup Procedures](#backup-procedures) before any install or update.
2. Close the CW Automate Control Center on all technician workstations where the plugin will be used.
3. Copy the compiled `MSP Accounts.dll` to the CW Automate plugins folder:
   - Default path: `C:\Program Files (x86)\LabTech\Plugins\` (or ConnectWise Automate equivalent)
   - Confirm the path in your environment — it may vary by installation version.
4. Reopen the Control Center. CW Automate will detect the new plugin on startup.
5. On first run, `clsPermissions.vb` → `DoInitialSetup()` automatically creates the required database tables if they do not exist. Check the CW Automate log for any SQL errors.
6. Navigate to **Menu → MSP Accounts** to open the plugin UI.
7. Configure the plugin settings (MSP name, user prefix, password policy) — see [Configuration](#configuration).

### Updating an existing install

1. **Back up first** — see [Backup Procedures](#backup-procedures).
2. Close the Control Center on all workstations.
3. Replace the old `MSP Accounts.dll` with the new build.
4. Reopen the Control Center. `DoInitialSetup()` will automatically apply any new database migrations.
5. Verify the version number shown in the About dialog matches your new build.

---

## Configuration

All plugin settings are stored in the `plugin_itsc_msp_accounts_settings` table in the CW Automate MySQL database. They are managed through the plugin's Settings tab in the UI.

| Setting | Description | Default |
|---|---|---|
| `MSP_Name` | Your MSP name — used in account display names, OU names, and password vault titles | `Managed_Service_Provider` |
| `User_Prefix` | Prefix added to all MSP usernames (e.g., `TT_` → creates `TT_JSmith`) | `MSP_` |
| `Exclude_Locations` | Comma-separated CW Automate Location IDs to skip during local service account operations | `0,1` |
| `Service_Account` | Name of the designated service account user | `None` |
| `Min_Password_Length` | Minimum length for generated/validated passwords | `14` |
| `Password_Change_Days` | How many days before automatic password rotation triggers | `59` |
| `Min_Password_Upper` | Minimum uppercase characters required | `2` |
| `Min_Password_Lower` | Minimum lowercase characters required | `2` |
| `Min_Password_Number` | Minimum numeric characters required | `2` |
| `Min_Password_Special` | Minimum special characters required | `2` |
| `Local_Service_Account` | Whether to manage local service accounts (BINARY 0/1) | `0` |
| `Local_Service_Account_Exclude` | Whether to exclude locations from local service account operations | `1` |

---

## How the Plugin Works — Module by Module

### `PluginMain.vb` — Plugin Entry Point
Implements `LabTech.Interfaces.IPlugin`. Provides the plugin name, version, author, and compatibility check to CW Automate at load time. All `IsLicensed()` checks return `True` — no license enforcement.

---

### `clsMenus.vb` — Menu Registration
Implements `LabTech.Interfaces.IMenu`. Registers one menu item: **"MSP Accounts"** in the main CW Automate menu bar. Clicking it opens or brings to front the main `MSP_Accounts_Form`. The form reference is held in memory so only one instance can be open at a time. Form is disposed when the Control Center closes (`Decommision()`).

---

### `clsPermissions.vb` — Database Setup & Migrations
Implements `LabTech.Interfaces.IPermissions`. Called by CW Automate's DB agent (not the UI). Creates three tables on first run and applies schema migrations for older plugin versions:

- Creates `plugin_itsc_msp_accounts_settings` if missing, inserts default row
- Creates `plugin_itsc_msp_accounts_users` if missing
- Creates `plugin_itsc_msp_accounts_userstatus` if missing
- **Migration v2.160531**: widens `Exclude_Locations` column to VARCHAR(2000)
- **Migration v2.171129**: adds four password complexity columns
- **Migration (timestamp fix)**: adds `TimeStamp` column to userstatus table

Also grants `SELECT,INSERT,UPDATE,DELETE` on all three tables to all users via `GetPermissionSet()`.

---

### `TabClass.vb` — UI Tab
Implements `LabTech.Interfaces.ITab`. Adds a tab panel inside the CW Automate Control Center interface.

---

### `iSync.vb` / `iSync2.vb` — Background Timers
Implement `LabTech.Interfaces.ISync`. These are background timer classes that CW Automate calls on a schedule. `iSync.vb` drives automatic password rotation — it checks which users are past their `Password_Change_Days` threshold and triggers `PasswordManagement.LoopedChangePassword()` for each one.

---

### `Globals.vb` — Shared Constants
A VB Module (static/shared scope). Holds:
- `mVersion` — plugin version number
- `mAuthor` — author string
- `PluginName` — display name
- `sqlPassword` — **⚠️ hardcoded encryption key used for AES password storage — see Security Notes**

---

### `UserManagement.vb` — AD Domain Account Management

Three public shared methods:

**`createUserPrep()`** — Prepares the AD environment before creating a user:
1. Queries the DC for the "Domain Admins" group LDAP path via `dsquery`
2. Builds the LDAP base string from the domain name
3. Checks if the MSP OU exists; creates it via `dsadd ou` if not
4. Returns the MSP OU LDAP path and Domain Admins LDAP path to the caller

**`UserCreate()`** — Creates a Domain Admin account:
1. Calls `createUserPrep()` if needed
2. Runs `dsadd user` to create the account in the MSP OU with Domain Admin membership
3. Falls back to `net group "Domain Admins" /add` if `dsadd` partially succeeds
4. Waits 30 seconds, then verifies with `net user`
5. If this is the service account, stores the password in CW Automate's password vault
6. Triggers `SendHardwareInfo` (command 17) and `SendSystemInfo` (command 123) to refresh agent data

**`UserDelete()`** — Deletes a Domain Admin account:
1. Runs `dsrm` to remove the user from AD
2. Falls back to `net user /delete` if `dsrm` fails
3. Verifies deletion, refreshes agent data

**`verifyAccount()`** — Tests credentials by temporarily setting the CW Automate service account to the specified credentials and running `whoami` as that user.

---

### `PasswordManagement.vb` — Password Operations

**`ValidatePassword()`** — Validates a password against complexity requirements:
- Minimum length, uppercase, lowercase, numeric, special character counts
- Blocks shell-injection characters: `| & % ' \` " ; \ whitespace`
- Blocks common weak passwords (12345, PASSWORD, QWERTY, etc.)

**`randomPassword()`** — Generates a cryptographically shuffled random password meeting the configured complexity minimums. Uses `System.Random` (see Security Notes re: upgrading to `RNGCryptoServiceProvider`).

**`LoopedChangePassword()`** — Bulk password change across all domains:
1. Queries all AD PDC Emulators where the user account exists
2. Generates (or accepts a passed) password
3. Updates the password record in `plugin_itsc_msp_accounts_users`
4. For the service account: updates the CW Automate password vault for each domain
5. Spawns one thread per domain, each calling `PasswordChange()`
6. Joins all threads (180-second timeout per thread)
7. Calls `Reporting.log_Reporting()` when all threads complete

**`PasswordChange()`** — Changes the password on a single domain:
1. Runs `dsmod user ... -pwd ... -pwdneverexpires no`
2. Falls back to `net user <name> <password>` if `dsmod` fails
3. Verifies with `dsquery user -u <name> -p <password>`
4. Logs result to `plugin_itsc_msp_accounts_userstatus`

---

### `ServiceManagement.vb` — Local Service Account Management

Manages a local administrator account on standalone/workgroup machines (machines where `Name = SUBSTRING_INDEX(Username,'\\',1)` — i.e., not domain-joined).

**`LocalServiceLoader()`** — Entry point. Queries all eligible workgroup machines, then for each location:
- `Add`: Creates password vault entry, spawns `LocalComputerLoader` for machines without the account
- `Delete`: Removes password vault entries, spawns `LocalComputerLoader` for machines that have the account
- `Change`: Rotates the password, spawns `LocalComputerLoader` for machines that have the account

**`LocalComputerLoader()`** — Per-location worker. Iterates machines in a location and spawns `LocalComputerThread` for each via `ThreadPool.QueueUserWorkItem`.

**`LocalComputerThread()`** — Per-machine worker. Executes:
- `Add`: `net user /add` + `net localgroup Administrators /add` + hides account via registry write (Winlogon SpecialAccounts)
- `Delete`: `net user /delete`
- `Change`: `net user <name> <password>`
- Then sends `SendSystemInfo` (command 123) to refresh agent data

---

### `Reporting.vb` — Change Log & Email

**`log_Reporting()`** — Called after every `LoopedChangePassword()`. Builds an HTML change log:
1. Gets all PDC Emulator computerIDs and their domain names
2. Gets the list of DC computerIDs that were involved in the last password change from `ClientDCids`
3. For each DC, reads the result column from `plugin_itsc_msp_accounts_userstatus`
4. Assembles an HTML report of successes and failures
5. Emails the report to the user's email address via `ICoreFunctionality.SendEmail()`

---

## Database Tables

### `plugin_itsc_msp_accounts_settings`
Plugin configuration. Single row, keyed on `MSP_Name`.

| Column | Type | Description |
|---|---|---|
| `MSP_Name` | varchar(50) PK | Your MSP name |
| `User_Prefix` | varchar(50) | Account name prefix |
| `Exclude_Locations` | varchar(2000) | Comma-sep LocationIDs to skip |
| `Service_Account` | varchar(50) | Service account username |
| `Min_Password_Length` | tinyint(2) | Minimum password length |
| `Password_Change_Days` | tinyint(3) | Rotation interval in days |
| `Local_Service_Account` | BINARY(1) | Enable local account management |
| `Local_Service_Account_Exclude` | BINARY(1) | Exclude flagged locations |
| `Min_Password_Upper` | tinyint(2) | Min uppercase chars |
| `Min_Password_Lower` | tinyint(2) | Min lowercase chars |
| `Min_Password_Number` | tinyint(2) | Min numeric chars |
| `Min_Password_Special` | tinyint(2) | Min special chars |

---

### `plugin_itsc_msp_accounts_users`
One row per MSP user managed by this plugin.

| Column | Type | Description |
|---|---|---|
| `Username` | varchar(50) PK | MSP technician username |
| `Password` | blob | AES-encrypted password (`AES_ENCRYPT(pwd, SHA(key))`) |
| `AutoChangePassword` | tinyint(1) | Whether auto-rotation is enabled |
| `AutoChangeDate` | date | Date of last auto-rotation |

---

### `plugin_itsc_msp_accounts_userstatus`
One row per user. Tracks the last operation result for each domain. Domain names are used as **dynamic column names** (added by migrations as needed).

| Column | Type | Description |
|---|---|---|
| `Username` | varchar(50) PK | MSP technician username |
| `PluginUserEmail` | varchar(50) | Email for change log reports |
| `TimeStamp` | DATETIME | Last operation timestamp |
| `ClientDCids` | varchar(5000) | Pipe-delimited DC computerIDs from last bulk change |
| `[domain.name]` | varchar(255) | One column per client domain — stores `YYYY-MM-DD HH:MM:SS\|result message` |

---

## Security Notes & Known Issues

> These are issues that exist in the **original codebase**. They are documented here so they can be tracked and resolved in this fork's modernization effort. **Do not deploy to production without addressing the critical items.**

### CRITICAL — Hardcoded Encryption Key

**File:** `Globals.vb` line 14, also duplicated inline in `UserManagement.vb` and `PasswordManagement.vb`

```vb
Public Const sqlPassword As String = "shinybrowncoat"
```

This string is the `SHA()` key used in every MySQL `AES_ENCRYPT()` / `AES_DECRYPT()` call that stores passwords in the CW Automate database. It is:
- Committed to version history in the original public repository
- A publicly known value — treat all encrypted passwords as compromised if the DB is exposed
- Duplicated as a local variable in two files, bypassing even the global

**Resolution plan:** Replace with a value read from CW Automate's secure config or encrypted settings at runtime. All existing encrypted passwords must be re-encrypted after any key change.

---

### HIGH — SQL Injection Risk

All SQL queries are built by string concatenation with unsanitized user input. Example from `Reporting.vb`:

```vb
m_host.GetSQL("SELECT `" & tmpColumnName & "` FROM plugin_itsc_msp_accounts_userstatus WHERE `Username` = '" & passedUserName & "'")
```

The CW Automate `IControlCenter.GetSQL()` / `SetSQL()` interface does not appear to support parameterized queries. Mitigation: strict input sanitization on all values before they enter SQL strings. This is tracked in the modernization roadmap.

---

### HIGH — Weak Random Number Generator

`PasswordManagement.randomPassword()` uses `System.Random`, which is **not cryptographically secure**. Passwords generated in rapid succession (e.g., bulk rotation) may be predictable.

**Resolution plan:** Replace `System.Random` with `System.Security.Cryptography.RNGCryptoServiceProvider`.

---

### MEDIUM — Weak Encryption Key Derivation

`AES_ENCRYPT(password, SHA(key))` uses MySQL's `SHA()` function, which is **SHA-1** — a deprecated hashing algorithm. The key is also derived only from `ClientID + 1` as a salt, which is trivially guessable.

**Resolution plan:** Migrate to `SHA2(key, 256)` at minimum, or handle encryption in .NET code rather than MySQL.

---

### MEDIUM — VB6-Style Error Handling

Several files use `On Error GoTo errorHandler` (VB6 pattern) rather than structured `Try/Catch/Finally`. This can mask errors and makes debugging difficult.

**Files affected:** `UserManagement.vb`, `ServiceManagement.vb`, `Reporting.vb`

---

### MEDIUM — Blocking Thread Sleep Polling

Command status is polled in tight loops:
```vb
Do While CInt(subHost.GetSQL("Select Status from commands where cmdid=" & cmdID)) < 3
    Threading.Thread.Sleep(5000)
Loop
```

There is no timeout, no cancellation, and no UI feedback. A hung command will block the thread indefinitely.

**Resolution plan:** Add a maximum iteration count (e.g., 60 iterations = 5 minutes) with a timeout exception.

---

### LOW — `MessageBox.Show` in Background Threads

`ServiceManagement.vb` calls `Windows.Forms.MessageBox.Show()` inside `ThreadPool` worker threads. This will crash or hang in a non-UI thread context.

**Resolution plan:** Replace with proper logging to the CW Automate log via `objHost.LogMessage()`.

---

### LOW — "LabTech" Branding

The `Interfaces.dll` namespace is `LabTech.Interfaces` (the pre-rebranding name). The plugin About text also says "LabTech". These are cosmetic but should be updated where possible to reflect "ConnectWise Automate".

---

### LOW — `System.Random` Not Seeded Per-Instance

Two separate `New System.Random` instances are created in `randomPassword()` with no explicit seed, which can produce identical sequences if called in rapid succession on the same thread.

---

## Testing Checklist

Before deploying any build to production, manually verify:

- [ ] Plugin loads without errors in CW Automate (check CW Automate log)
- [ ] "MSP Accounts" appears in the menu bar
- [ ] Settings tab loads and displays current configuration
- [ ] Password validation rejects weak passwords and shell-injection characters
- [ ] Password generator produces passwords that pass validation
- [ ] User creation flow completes and account appears in target AD
- [ ] User deletion flow removes the account
- [ ] Bulk password change completes and email report is received
- [ ] Auto-change timer fires on schedule (check `AutoChangeDate` in DB)
- [ ] Local service account add/delete/change operates correctly on a test workgroup machine
- [ ] DB tables exist and have correct schema after first run
- [ ] DB migrations run cleanly when upgrading from previous version

---

## Backup Procedures

### Before any install, update, or configuration change:

#### 1. Back up the DLL
```
Copy the existing MSP Accounts.dll from the CW Automate Plugins folder to a dated backup folder.
Example: C:\Backups\CWA-Plugins\MSP_Accounts_2026-01-15.dll
```

#### 2. Back up the plugin database tables
Connect to the CW Automate MySQL database and run:

```sql
-- Settings backup
CREATE TABLE plugin_itsc_msp_accounts_settings_bak_YYYYMMDD
    AS SELECT * FROM plugin_itsc_msp_accounts_settings;

-- Users backup
CREATE TABLE plugin_itsc_msp_accounts_users_bak_YYYYMMDD
    AS SELECT * FROM plugin_itsc_msp_accounts_users;

-- Status backup
CREATE TABLE plugin_itsc_msp_accounts_userstatus_bak_YYYYMMDD
    AS SELECT * FROM plugin_itsc_msp_accounts_userstatus;
```

Replace `YYYYMMDD` with today's date (e.g., `20260115`).

#### 3. Export backup tables to files (optional but recommended)
```sql
SELECT * FROM plugin_itsc_msp_accounts_settings_bak_YYYYMMDD
INTO OUTFILE '/tmp/msp_accounts_settings_backup_YYYYMMDD.csv'
FIELDS TERMINATED BY ',' ENCLOSED BY '"' LINES TERMINATED BY '\n';
```

#### 4. Commit any source changes to a branch before building
```bash
git checkout -b backup/before-update-YYYY-MM-DD
git add .
git commit -m "Backup: pre-update snapshot YYYY-MM-DD"
git push origin backup/before-update-YYYY-MM-DD
```

---

## Rollback & Revert Procedures

### Rollback the DLL (same-day, low risk)

1. Close the CW Automate Control Center on all workstations.
2. Delete or rename the current `MSP Accounts.dll` in the Plugins folder.
3. Copy the backed-up `.dll` back to the Plugins folder.
4. Reopen the Control Center. The previous version will load.
5. Verify the version number in the About dialog matches the expected previous version.

**Note:** If the database schema was migrated (new columns added), rolling back the DLL will still work — the old DLL will simply ignore new columns. The schema migration is additive-only by design.

---

### Rollback the database tables

If data was corrupted or incorrectly modified:

```sql
-- Restore settings from backup
TRUNCATE TABLE plugin_itsc_msp_accounts_settings;
INSERT INTO plugin_itsc_msp_accounts_settings
    SELECT * FROM plugin_itsc_msp_accounts_settings_bak_YYYYMMDD;

-- Restore users from backup
TRUNCATE TABLE plugin_itsc_msp_accounts_users;
INSERT INTO plugin_itsc_msp_accounts_users
    SELECT * FROM plugin_itsc_msp_accounts_users_bak_YYYYMMDD;

-- Restore status from backup (note: dynamic columns may differ between versions)
TRUNCATE TABLE plugin_itsc_msp_accounts_userstatus;
INSERT INTO plugin_itsc_msp_accounts_userstatus
    SELECT * FROM plugin_itsc_msp_accounts_userstatus_bak_YYYYMMDD;
```

**Caution:** The `plugin_itsc_msp_accounts_userstatus` table uses dynamic columns (one per client domain). If the backup was from a different schema version, column mismatches may occur. Restore column-by-column if needed.

---

### Revert to a previous source version (Git)

```bash
# View recent commits
git log --oneline

# Create a branch from a specific commit to test it
git checkout -b revert/test-YYYY-MM-DD <commit-hash>

# Build and test from that branch
# If confirmed good, tag it
git tag v2.x-reverted-YYYY-MM-DD

# To make it the new master (coordinate with team first)
git checkout master
git reset --hard <commit-hash>
git push origin master --force
# ⚠️ Only do this if you are certain and no other work depends on the reverted commits
```

---

## 2026 Modernization Roadmap

Work items tracked as individual branches. Each branch = one focused change. Merge to `master` only after testing.

### Phase 1 — Critical Security (do first)
- [ ] **Remove hardcoded `sqlPassword`** — load from encrypted config or CW Automate secure store
- [ ] **Replace `System.Random` with `RNGCryptoServiceProvider`** in `PasswordManagement.randomPassword()`
- [ ] **Upgrade AES key derivation** from `SHA()` (SHA-1) to `SHA2(key, 256)` in all SQL encryption calls
- [ ] **Input sanitization layer** — create a `Sanitize.vb` module with shared methods for all SQL string inputs

### Phase 2 — Code Quality & Reliability
- [ ] **Replace all `On Error GoTo`** with `Try/Catch/Finally` blocks (`UserManagement.vb`, `ServiceManagement.vb`, `Reporting.vb`)
- [ ] **Add command polling timeouts** — max iterations with `TimeoutException` thrown after configurable limit
- [ ] **Replace `MessageBox.Show` in threads** with `objHost.LogMessage()` throughout `ServiceManagement.vb`
- [ ] **Add a `Logger.vb` module** — centralized logging with severity levels (Info, Warning, Error), writing to CW Automate log and optionally to a plugin log table

### Phase 3 — Modularization
- [ ] **Break up `MSP_Accounts.vb`** — separate UI event handlers from business logic; move data access calls to a `DataAccess.vb` module
- [ ] **Extract SQL strings** — move all SQL into a `Queries.vb` module (named, documented constants)
- [ ] **Extract AD command strings** — move all `dsadd/dsmod/dsrm/net user` command templates to a `Commands.vb` module
- [ ] **Break `LoopedChangePassword()`** into smaller, single-responsibility methods

### Phase 4 — CW Automate 2026 Compatibility
- [ ] **Verify `Interfaces.dll` compatibility** with current CW Automate version — obtain updated SDK from ConnectWise if needed
- [ ] **Update "LabTech" references** in plugin About text and comments
- [ ] **Update branding** in `Globals.vb` (author, plugin name)
- [ ] **Review command IDs** (e.g., `SendCommand(..., 17, ...)`, `SendCommand(..., 123, ...)`) against current CW Automate command type documentation

### Phase 5 — Documentation & Testing
- [ ] **Add XML doc comments** to all public methods (`''' <summary>`)
- [ ] **Add unit tests** — extract pure logic (password gen/validation) to testable static methods in a separate project
- [ ] **Add `CHANGELOG.md`** — version history going forward

---

## Branching Strategy

```
master          — stable, deployable builds only
dev             — integration branch for in-progress work
feature/<name>  — individual feature or fix branches
backup/<date>   — pre-update snapshots (read-only after creation)
```

**Branch naming examples:**
- `feature/remove-hardcoded-password`
- `feature/add-logging-module`
- `feature/replace-system-random`
- `backup/before-update-2026-01-15`

---

## Contributing & Change Process

1. Never commit directly to `master`.
2. Create a `feature/` branch for every change, no matter how small.
3. Update this README if your change affects architecture, security, or configuration.
4. Update `Globals.vb` version number for every build that gets deployed.
5. Follow the backup procedure before deploying.
6. Document any new SQL queries in the Database Tables section.
7. All new methods require:
   - XML doc comment header (`''' <summary>`)
   - Input validation at method entry
   - `Try/Catch` error handling (no `On Error GoTo`)
   - At least one call to `objHost.LogMessage()` on error paths

---

## Attribution & License

**Original author:** MrRat / RealTime, LLC  
**Original repository:** [https://github.com/mspgeek/MSPAccounts](https://github.com/mspgeek/MSPAccounts)  
**Original copyright:** 2015  

This fork is maintained by **Triton Technologies** for internal use. All modifications are private and not for redistribution. The original code is used under the terms of its original license (none explicitly stated in the source repository — used with attribution).

The `Interfaces.dll` file is the ConnectWise Automate plugin SDK and is property of ConnectWise, LLC.
