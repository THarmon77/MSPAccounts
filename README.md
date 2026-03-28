# MSP Accounts — ConnectWise Automate Plugin

> **Fork of [mspgeek/MSPAccounts](https://github.com/mspgeek/MSPAccounts)**  
> Maintained by Triton Technologies (THarmon77) — Private fork for internal use.  
> Original plugin by MrRat / RealTime, LLC — Copyright 2015.  
> This fork: full rebuild as **Triton Account Manager v3** for ConnectWise Automate 2026.

---

## ⚠️ Active Development — v3 Rebuild in Progress

This repository is being **completely rebuilt** as Triton Account Manager v3. The original codebase (v2) is preserved for reference but will be replaced. All v3 design decisions are documented in the `docs/` folder before any code is written.

### v3 Documentation Index

| Document | Description |
|----------|-------------|
| **[docs/DESIGN_SPEC.md](docs/DESIGN_SPEC.md)** | Full v3 plugin design — purpose, architecture, module map, UI design, security decisions, all design choices documented |
| **[docs/SDK_INTERFACES.md](docs/SDK_INTERFACES.md)** | Complete CW Automate Plugin SDK reference — every interface, method, property, and usage pattern sourced from the ConnectWise Developer Portal |
| **[docs/DATABASE.md](docs/DATABASE.md)** | Database reference — f_CWAESEncrypt, all CW Automate native tables used (passwords, locations, computers, commands), new plugin tables, key SQL queries |

### v3 Summary

**What it does:** Deploys and maintains a single shared local administrator account (`TritonTech`) on every managed workstation across all Triton-managed clients. Stores a unique password per location in the CW Automate Passwords tab. Rotates passwords on a 90-day schedule.

**Key design decisions:**
- Password storage: **`f_CWAESEncrypt()`** — CW Automate's own native function. Passwords appear in the CW Passwords tab without any plugin involvement. No master key, no DPAPI, no custom encryption.
- Password scope: **Per-Location** (default) or Per-Client. Global scope removed.
- Target machines: All workstations — standalone, workgroup, domain-joined, Azure AD joined. (`OS NOT LIKE '%Server%'`)
- Password generation: **`RNGCryptoServiceProvider`** — cryptographically secure. Never `System.Random`.
- Error handling: **`Try/Catch/Finally`** throughout. No `On Error GoTo`. All timeouts enforced.
- Logging: **`objHost.LogMessage()`** everywhere. No `MessageBox.Show` in background threads.
- Migration: One-time routine to replace legacy accounts (`TT_Service`) with `TritonTech`.

**CW Automate version:** v25.0.436 Patch 11  
**Interfaces.dll version:** 2024.2.72.0  
**Target framework:** .NET Framework 4.8

---

## Original v2 Plugin Documentation (Legacy Reference)

The sections below document the **original mspgeek/MSPAccounts codebase** as it exists in this repository. This documentation is preserved as reference for understanding the legacy code during the v3 rebuild.

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

**Core capabilities (original v2):**

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
├── docs/                         # v3 design documentation
│   ├── DESIGN_SPEC.md            # Full v3 design specification
│   ├── SDK_INTERFACES.md         # Complete SDK interface reference
│   └── DATABASE.md               # Database schema and query reference
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
| CW Automate version | v25.0.436 Patch 11 (confirmed compatible with Interfaces.dll v2024.2.72.0) |
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

**v3 Resolution:** Eliminated entirely — v3 uses `f_CWAESEncrypt()` (CW Automate native). No plugin-managed encryption key needed.

---

### HIGH — SQL Injection Risk

All SQL queries are built by string concatenation with unsanitized user input. Example from `Reporting.vb`:

```vb
m_host.GetSQL("SELECT `" & tmpColumnName & "` FROM plugin_itsc_msp_accounts_userstatus WHERE `Username` = '" & passedUserName & "'")
```

**v3 Resolution:** Use `IParameterizedQuery` where available in Interfaces.dll; sanitize all inputs before SQL inclusion.

---

### HIGH — Weak Random Number Generator

`PasswordManagement.randomPassword()` uses `System.Random`, which is **not cryptographically secure**.

**v3 Resolution:** `RNGCryptoServiceProvider` throughout `PasswordManager.vb`.

---

### MEDIUM — Weak Encryption Key Derivation

`AES_ENCRYPT(password, SHA(key))` uses MySQL's `SHA()` function = SHA-1 (deprecated).

**v3 Resolution:** Eliminated — `f_CWAESEncrypt()` used instead.

---

### MEDIUM — VB6-Style Error Handling

Several files use `On Error GoTo errorHandler` rather than structured `Try/Catch/Finally`.

**v3 Resolution:** `Try/Catch/Finally` throughout all new modules.

---

### MEDIUM — Blocking Thread Sleep Polling

Command status polled in tight loops with no timeout, no cancellation, no UI feedback.

**v3 Resolution:** All command polling loops have configurable timeout + iteration limit with `TimeoutException`.

---

### LOW — `MessageBox.Show` in Background Threads

`ServiceManagement.vb` calls `Windows.Forms.MessageBox.Show()` inside `ThreadPool` worker threads.

**v3 Resolution:** All logging via `objHost.LogMessage()`. No UI calls from background threads.

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

#### 3. Commit any source changes to a branch before building
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

-- Restore status from backup
TRUNCATE TABLE plugin_itsc_msp_accounts_userstatus;
INSERT INTO plugin_itsc_msp_accounts_userstatus
    SELECT * FROM plugin_itsc_msp_accounts_userstatus_bak_YYYYMMDD;
```

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
```

---

## 2026 Modernization Roadmap

The v3 rebuild proceeds in phases. See [docs/DESIGN_SPEC.md](docs/DESIGN_SPEC.md) for full details.

### Phase 1 — Foundation (current)
- [x] Document full design spec in `docs/DESIGN_SPEC.md`
- [x] Document complete SDK interface reference in `docs/SDK_INTERFACES.md`
- [x] Document database schema in `docs/DATABASE.md`
- [ ] Create `dev` branch
- [ ] Replace `Interfaces.dll` with v2024.2.72.0
- [ ] Create `Config.vb` — reads `%SystemDrive%\Triton\MSPAccounts\config.xml`
- [ ] Create `Logger.vb` — wraps ILogger with levels
- [ ] Rewrite `clsPermissions.vb` — new tables, defaults

### Phase 2 — Core Logic
- [ ] Create `PasswordManager.vb` — RNGCryptoServiceProvider + f_CWAESEncrypt
- [ ] Create `AccountDeployer.vb` — deploy/verify/remove TritonTech via SendCommand
- [ ] Create `MigrationManager.vb` — one-time legacy account replacement
- [ ] Rewrite `iSync.vb` — daily rotation check
- [ ] Rewrite `iSync2.vb` — 6-minute new machine detection

### Phase 3 — UI
- [ ] Build `MainForm.vb` — 4-tab design (Dashboard, Actions, Settings, Log)
- [ ] Remove all old form files

### Phase 4 — Testing & Deployment
- [ ] Test on CW Automate v25.0.436 Patch 11
- [ ] Verify f_CWAESEncrypt integration shows passwords in CW Passwords tab
- [ ] Test migration routine against actual TT_Service accounts
- [ ] Deploy to production

---

## Branching Strategy

```
master          — stable, deployable builds only
dev             — integration branch for in-progress v3 work
feature/<name>  — individual feature or fix branches
backup/<date>   — pre-update snapshots (read-only after creation)
```

---

## Contributing & Change Process

1. Never commit directly to `master`.
2. Create a `feature/` branch for every change, no matter how small.
3. Update `docs/DESIGN_SPEC.md` if your change affects architecture or design decisions.
4. Update `Globals.vb` version number for every build that gets deployed.
5. Follow the backup procedure before deploying.
6. All new methods require:
   - XML doc comment header (`''' <summary>`)
   - Input validation at method entry
   - `Try/Catch` error handling (no `On Error GoTo`)
   - At least one call to `Logger.Log()` on error paths

---

## Attribution & License

**Original author:** MrRat / RealTime, LLC  
**Original repository:** [https://github.com/mspgeek/MSPAccounts](https://github.com/mspgeek/MSPAccounts)  
**Original copyright:** 2015  

This fork is maintained by **Triton Technologies** for internal use. All modifications are private and not for redistribution. The original code is used under the terms of its original license (none explicitly stated in the source repository — used with attribution).

The `Interfaces.dll` file is the ConnectWise Automate plugin SDK and is property of ConnectWise, LLC.
