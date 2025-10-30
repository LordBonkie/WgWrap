# WgWrap

**Version: 0.3.0 beta**

WireGuard auto-management tray application.

## Table of Contents

- [Features](#features)
  - [Core Features](#core-features)
  - [Intelligent Menu System](#intelligent-menu-system)
  - [Network Monitor Task (Fast Response)](#network-monitor-task-fast-response)
- [Tray Icon Colors](#tray-icon-colors)
  - [Icon Color Meanings](#icon-color-meanings)
  - [Priority System](#priority-system)
  - [Quick Visual Guide](#quick-visual-guide)
- [Logging](#logging)
- [Configuration](#configuration)
  - [Configuration Options](#configuration-options)
  - [How WgWrap Manages Your Config File](#how-wgwrap-manages-your-config-file)
    - [Internal File Management](#internal-file-management)
    - [Config Editing Workflow](#config-editing-workflow)
    - [Important Behaviors](#important-behaviors)
    - [Why This Design?](#why-this-design)
    - [Quick Reference](#quick-reference)
- [Settings Window](#settings-window)
  - [Configuration Lock](#configuration-lock)
- [Elevation & UAC](#elevation--uac)
  - [Daily Operations (No UAC)](#daily-operations-no-uac)
  - [Service Management (UAC Required - One Time Only)](#service-management-uac-required---one-time-only)
  - [Typical Workflow](#typical-workflow)
- [Intelligent Menu System](#intelligent-menu-system-1)
  - [Menu Behavior](#menu-behavior)
  - [Automatic Updates](#automatic-updates)
- [Manual Disable Tracking](#manual-disable-tracking)
  - [Visual Indicators](#visual-indicators)
  - [How It Works](#how-it-works-1)
  - [Use Cases](#use-cases)
  - [Persistence](#persistence)
- [Network Monitor Task](#network-monitor-task)
  - [Why Use It?](#why-use-it)
  - [Quick Start](#quick-start)
  - [How It Works](#how-it-works-2)
  - [Technical Details](#technical-details)
  - [Benefits](#benefits)
  - [Example Scenarios](#example-scenarios)
  - [Troubleshooting](#troubleshooting)
  - [Menu Indicators](#menu-indicators)
  - [Compatibility](#compatibility)
  - [Recommendation](#recommendation)
  - [Tips](#tips)
- [Architecture](#architecture)
- [Build & Run (Windows, cmd.exe)](#build--run-windows-cmdexe)
- [Future Improvements](#future-improvements)

## Features

### Core Features
- Auto start/stop WireGuard tunnel based on current WiFi SSID and IP ranges
- **Manual disable tracking**: Remember when you manually stop the VPN and prevent auto-restart
- **Auto-start with Windows**: Menu-based toggle to automatically start WgWrap when Windows boots
- **File-based logging**: Comprehensive logging with daily rotation and 30-day retention
- **Config change detection**: Automatic detection of external modifications to your original config file
- **Real-time statistics**: Live VPN statistics display (transfer data, handshake time, connection duration) in Status window
- Trusted SSID and IP range lists for automatic VPN management
- Periodic check and flag file update (configurable timer interval)
- Single instance using global mutex
- JSON configuration file for easy customization
- Comprehensive Status window with menu bar for all controls
- Clear separation: Start/Stop for daily use, Install/Uninstall for service management

### Intelligent Menu System
- System tray icon with context menu for quick actions
- Status window with full menu bar (VPN, System Setup, Configuration, Start with Windows, Shutdown)
- Menu items automatically enable/disable based on VPN service status
- Visual indicators showing current auto-start state
- **Edit Config**: Built-in configuration editor with syntax highlighting and real-time validation
- Service submenu for one-time install/uninstall operations

### Network Monitor Task (Fast Response)
- **Instant network change detection**: Responds to network changes in ~5 seconds (vs. 30-second timer)
- **Event-driven**: Uses Windows Event Log triggers for network connections/disconnections
- **Battery efficient**: Only runs when network changes occur
- **One-click setup**: Easy installation via tray menu
- **Runs alongside timer**: Works together with periodic checks for maximum reliability

## Tray Icon Colors

WgWrap uses **color-coded tray icons** with a distinctive **white "W" branding** to provide instant visual feedback about your VPN status at a glance. The icon color changes based on the current state, with the most critical status always displayed. The "W" logo makes WgWrap easily identifiable in your system tray among other icons.

### Icon Color Meanings

| Color | Status | Description |
|-------|--------|-------------|
| 🔴 **Red** | No Service Installed | The WireGuard VPN service is not installed. Use "System Setup > Install VPN Service" to get started. |
| 🟠 **Orange** | Manually Disabled | Auto-start is disabled because you manually stopped the VPN. It won't restart automatically until you manually start it again. |
| 🔵 **Blue** | Trusted Network | You're on a trusted WiFi network or IP range. VPN is disabled as configured in your trusted networks list. |
| 🟢 **Green** | VPN Connected | VPN is active and protecting your connection. You're securely connected through WireGuard. |
| 🟡 **Yellow** | Transitional State | VPN is disconnected, starting, stopping, or in another transitional state. |

### Priority System

When multiple conditions apply, the tray icon displays the highest priority status:

1. **Highest Priority**: 🔴 Red (No Service) - You need to install the service first
2. **High Priority**: 🟠 Orange (Manual Disable) - You've explicitly disabled auto-start
3. **Medium Priority**: 🔵 Blue (Trusted Network) - You're on a safe network
4. **Normal**: 🟢 Green (Connected) - VPN is working normally
5. **Default**: 🟡 Yellow (Other) - Transitional or disconnected states

This ensures the most important information is always visible. For example, if you're on a trusted network but have also manually disabled the VPN, the orange icon will show (manual disable takes priority).

### Quick Visual Guide

**What you should see in different scenarios:**

- **At home on trusted WiFi**: 🔵 Blue icon (trusted network)
- **At coffee shop with VPN on**: 🟢 Green icon (connected and protected)
- **After manually stopping VPN**: 🟠 Orange icon (manual disable active)
- **Fresh install before setup**: 🔴 Red icon (service not installed)
- **VPN disconnected on untrusted network**: 🟡 Yellow icon (needs attention)

The icon provides instant feedback without needing to hover or click - just glance at your system tray to know your VPN status!

## Logging

WgWrap includes comprehensive file-based logging to help troubleshoot issues and track VPN state changes:

- **Daily rotation**: New log file created each day (`wgwrap-YYYY-MM-DD.log`)
- **30-day retention**: Automatic cleanup of logs older than 30 days
- **Async buffering**: Minimal performance impact using background writer
- **Log levels**: INFO, WARN, ERROR, DEBUG
- **Location**: `logs/` directory in the application folder
- **Exception details**: Full stack traces for debugging

Example log entries:
```
[2025-10-29 14:30:15.123] [INFO] Application starting.
[2025-10-29 14:30:15.456] [INFO] Configuration loaded.
[2025-10-29 14:30:16.789] [INFO] Detected WiFi SSID: 'HomeNetwork'.
[2025-10-29 14:30:17.012] [INFO] Trusted network detected; stopping VPN (auto).
```

Logs are invaluable for:
- Diagnosing VPN connection issues
- Tracking automatic network switching behavior
- Understanding why auto-start was disabled
- Troubleshooting Task Scheduler integration

## Configuration
The application uses `appsettings.json` for configuration. Create or modify this file in the same directory as the executable:

> **⚠️ WARNING**: Manually editing `appsettings.json` can be dangerous! Invalid JSON syntax (missing commas, quotes, brackets) will prevent the application from loading your configuration, and it may fall back to default values or fail to start. **Always use the Settings window** (accessible from the tray icon) to modify configuration safely. The Settings window validates your input and ensures proper JSON formatting. Only edit `appsettings.json` manually if you are comfortable with JSON syntax and understand the risks.

> **⚠️ CONFIGURATION CONFLICT WARNING**: **Do NOT use a WireGuard configuration file (.conf) that is already loaded in the official WireGuard application!** WgWrap manages tunnels as Windows services independently from the WireGuard GUI application. If the same tunnel is active in both applications simultaneously, they will conflict and cause connection issues, service failures, or unpredictable behavior. Either use WgWrap **OR** the WireGuard GUI app for a given tunnel, never both at the same time. If you need to switch, make sure to completely stop/remove the tunnel from one application before using it in the other.

```json
{
  "WireGuard": {
    "ConfigPath": "C:/wg/my-wireguard-config.conf",
    "WgExe": "C:/Program Files/WireGuard/wireguard.exe",
    "TrustedSsids": [
      "HomeNetwork",
      "OfficeWiFi"
    ],
    "TrustedIpRanges": [
      "192.168.1.0/24",
      "10.0.0.0/8"
    ],
    "ExcludedNetworkAdapters": [
      "OpenVPN",
      "WireGuard"
    ],
    "TimerEnabled": true,
    "TimerIntervalSeconds": 30,
    "AutoStartWithWindows": false,
    "ShowShutdownWarning": true
  }
}
```

### Configuration Options:
- **ConfigPath**: Full path to your WireGuard configuration file (the tunnel name is automatically derived from the filename)
- **WgExe**: Full path to the WireGuard executable (wireguard.exe)
- **TrustedSsids**: Array of WiFi network names where VPN should be disabled
- **TrustedIpRanges**: Array of IP ranges in CIDR notation (e.g., "192.168.1.0/24") where VPN should be disabled - useful for Ethernet connections on trusted networks. Note: Only physical network adapters are checked for IP matching, excluding VPN tunnels and any adapters with names containing the strings specified in ExcludedNetworkAdapters (default excludes 'OpenVPN' and 'WireGuard').
- **ExcludedNetworkAdapters**: Array of strings to match against network adapter names. Adapters containing any of these strings are excluded from IP range checks. This uses **fuzzy matching** (case-insensitive substring search) rather than exact matching. For example, "OpenVPN" will match "OpenVPN TAP-Windows6", "My OpenVPN Adapter", etc.
- **TimerEnabled**: Enable/disable automatic network checks (default: true)
- **TimerIntervalSeconds**: Interval between automatic checks in seconds, 10-3600 (default: 30)
- **AutoStartWithWindows**: Automatically start WgWrap when Windows boots (default: false) - managed via menu bar
- **ShowShutdownWarning**: Show warning when closing application with Network Monitor Task installed (default: true)

If `appsettings.json` is missing or cannot be loaded, the application will use default values.

### How WgWrap Manages Your Config File

**Important**: WgWrap creates an internal copy of your WireGuard configuration file to manage the VPN service. Understanding this is crucial for proper usage.

#### Internal File Management

When you install the VPN service, WgWrap:

1. **Creates an internal copy**: Your original `.conf` file is copied to `wg_wrap_tunnel.conf` in the WgWrap application directory
2. **Uses the internal copy**: The WireGuard service is installed using this internal copy, NOT your original file
3. **Keeps your original untouched**: Your original config file at the `ConfigPath` location remains unchanged

> **🔒 SECURITY NOTE**: Your WireGuard configuration file (which contains private keys and sensitive connection details) is copied to the WgWrap application directory as `wg_wrap_tunnel.conf`. This file contains the same sensitive information as your original config. Ensure the WgWrap application directory has appropriate file permissions and is not accessible to unauthorized users. The internal copy is necessary for the Windows service to function properly.

**File locations:**
- **Original config**: The file path you specify in `ConfigPath` (e.g., `C:/wg/my-wireguard-config.conf`)
- **Internal config**: `data/wg_wrap_tunnel.conf` (in the WgWrap application directory)
- **Modified config**: `data/modified_config.conf` (created when you edit via Configuration menu)

#### Config Editing Workflow

When you use the **"Edit Config"** button in the Status window:

1. **First edit**: Creates a `modified_config.conf` file with your changes
2. **Your original remains safe**: The file at `ConfigPath` is never modified
3. **Service uses modified version**: Future service installations use the modified config
4. **Indicator shown**: Status window shows "⚠ Using modified config (original untouched)"

#### Important Behaviors

> **⚠️ CRITICAL**: When you **uninstall the VPN service**, the `modified_config.conf` file is **permanently deleted**. Make sure to back up any manual edits before uninstalling!

> **ℹ️ NOTE**: If you edit your original config file manually (outside WgWrap), you must **reinstall the service** for changes to take effect. WgWrap only syncs the internal copy during service installation.

> **💡 TIP**: To revert to your original config, use the "Revert to Original" button in the config editor. This deletes the modified version and the next service install will use your original file.

#### Why This Design?

This approach provides several benefits:

1. **Safety**: Your original config file is never accidentally modified or deleted
2. **Flexibility**: You can make temporary edits without affecting your master config
3. **Consistency**: The service always uses a known filename (`wg_wrap_tunnel.conf`)
4. **Isolation**: Changes made in WgWrap don't affect configs used by other WireGuard tools

#### Quick Reference

| Scenario | What Happens |
|----------|--------------|
| **Install Service** | Copies original → `wg_wrap_tunnel.conf`, uses that for service |
| **Edit Config** | Creates/updates `modified_config.conf`, shows warning indicator |
| **Reinstall Service** | Uses `modified_config.conf` if exists, otherwise uses original |
| **Uninstall Service** | **Deletes** `modified_config.conf` and `wg_wrap_tunnel.conf` |
| **Edit Original Manually** | No effect until you reinstall the service |
| **Revert to Original** | Deletes `modified_config.conf`, next install uses original |

#### Automatic Change Detection

WgWrap automatically detects when your original configuration file has been modified externally:

- **When detected**: Opening or activating the Status window checks if the original config file has been modified since last sync
- **User notification**: A dialog appears showing the modification times and asking if you want to apply the changes
- **Modified config warning**: If you have a modified config active, a **strong warning** is shown that it will be permanently deleted
- **Automatic reinstall**: If you choose to apply changes and the service is installed, WgWrap offers to automatically reinstall the service with the updated config
- **Timestamp tracking**: After applying or ignoring changes, the timestamp is updated so you won't be prompted again for the same modification

This ensures you're always aware when external changes have been made to your configuration and can choose whether to apply them.

## Status Window

The application includes a comprehensive Status window accessible from the tray icon, providing detailed VPN status, configuration, and management options. The window features:

### Menu Bar Options

**VPN Menu:**
- **Activate** - Manually start the VPN service
- **Deactivate** - Manually stop the VPN service

**System Setup Menu:**
- **Install VPN Service** - One-time installation of the WireGuard service (requires UAC)
- **Uninstall VPN Service** - Complete removal of the VPN service (requires UAC)
- **Install Network Monitor Task** - Set up instant network change detection
- **Uninstall Network Monitor Task** - Remove network monitoring task

**Configuration Menu:**
- **Edit Config** - Open the WireGuard configuration editor with syntax highlighting and real-time validation

**Start with Windows Menu:**
- **Activate** - Enable automatic startup when Windows boots
- **Deactivate** - Disable automatic startup

**Shutdown Application** - Exit WgWrap (shows warning if Network Monitor Task is installed)

### Status Tab Features

- **Visual Status Orb**: Large colored orb with "W" branding showing current VPN state (matches tray icon colors)
- **Real-time Statistics**: Transfer data, handshake time, and connection duration (refreshes every 2 seconds when window is active)
- **Network Information**: Current WiFi SSID, trusted network detection, IP address
- **Service Status**: VPN service state and auto-start indicator
- **Configuration Details**: Display of active WireGuard settings (interface and peer configuration)
- **Modified Config Indicator**: Warning shown when using a modified configuration

### Settings Tab Features

> **⚠️ WARNING**: Do NOT select a WireGuard configuration file (.conf) that is already active in the official WireGuard application! This will cause conflicts. Use the configuration in either WgWrap OR the WireGuard GUI app, never both.

- Browse and select the WireGuard configuration file (tunnel name is auto-derived from filename)
- Browse and select the WireGuard executable
- Manage the list of trusted SSIDs (one per line) - for WiFi networks
- Manage the list of trusted IP ranges (one per line in CIDR format) - for Ethernet/wired networks
- Configure automatic network check timer and interval

### Configuration Lock

**Important**: When the VPN service is installed, the following settings are locked and cannot be changed:
- Tunnel Name
- Config Path (determines the tunnel name)

This prevents configuration mismatches that could break service controls. To change these settings:
1. Use "Service > Uninstall VPN Service" to remove the service
2. Modify the settings as needed
3. Use "Service > Install VPN Service" to reinstall with the new configuration

**Trusted SSIDs can always be modified**, even when the service is installed, as they don't affect the service itself.
**Trusted SSIDs and IP Ranges can always be modified**, even when the service is installed, as they don't affect the service itself.
Changes made through the Settings window are saved directly to `appsettings.json`.

## Elevation & UAC

The application runs **without administrator privileges** by default. When operations require elevation, Windows UAC will prompt for permission:

### Daily Operations (No UAC):
- **Start VPN**: Starts an already-installed service - no elevation needed
- **Stop VPN**: Stops the running service - no elevation needed
- **Show Status**: Check VPN and WiFi status - no elevation needed
- **Settings**: Modify configuration - no elevation needed

### Service Management (UAC Required - One Time Only):
- **Service > Install VPN Service**: One-time installation - triggers UAC prompt
- **Service > Uninstall VPN Service**: Complete removal - triggers UAC prompt

> **⚠️ NOTE:** If you have a modified configuration (edited via the Settings window), uninstalling the VPN service will permanently delete your modified config file. Make sure to back up any changes you wish to keep before uninstalling the service.

### Typical Workflow:
1. **First time setup**: Use "Service > Install VPN Service" (requires UAC) to install the WireGuard tunnel
   - During installation, the app automatically configures service permissions
   - This allows any authenticated user to start/stop the service without UAC
2. **Daily use**: Use "Start VPN" and "Stop VPN" **without any UAC prompts** ✨
3. **Auto-management**: The application automatically starts/stops based on WiFi network
4. **Removal**: Use "Service > Uninstall VPN Service" (requires UAC) to completely remove the tunnel

## Intelligent Menu System

The tray icon context menu intelligently enables and disables options based on the current VPN service status. This prevents invalid operations and provides clear visual feedback about what actions are available.

### Menu Behavior:

**When the VPN service is NOT installed:**
- ✅ **Install VPN Service** - Enabled (you can install the service)
- ❌ **Uninstall VPN Service** - Disabled (nothing to uninstall)
- ❌ **Start VPN** - Disabled (service must be installed first)
- ❌ **Stop VPN** - Disabled (service must be installed first)

**When the VPN service is installed but STOPPED:**
- ✅ **Start VPN** - Enabled (you can start the service)
- ❌ **Stop VPN** - Disabled (already stopped)
- ❌ **Install VPN Service** - Disabled (already installed)
- ✅ **Uninstall VPN Service** - Enabled (you can uninstall if needed)

**When the VPN service is installed and RUNNING:**
- ❌ **Start VPN** - Disabled (already running)
- ✅ **Stop VPN** - Enabled (you can stop the VPN service)
- ❌ **Install VPN Service** - Disabled (already installed)
- ✅ **Uninstall VPN Service** - Enabled (you can uninstall if needed)

### Automatic Updates:

The menu state automatically updates:
- Every 30 seconds (via periodic check)
- Immediately after Start/Stop/Install/Uninstall operations
- When the VPN status changes (detected via flag file monitoring)

This ensures the menu always reflects the current state of your VPN service, preventing user errors and providing a smoother experience.

## Manual Disable Tracking

The application intelligently tracks when you manually stop the VPN and prevents automatic restart until you manually start it again. **The tray menu always shows the current auto-start status** with a clear visual indicator.

### Visual Indicators:

The tray menu displays one of two indicators at the top:

**When auto-start is ENABLED (normal mode):**
```
▶ Auto-start Enabled  (in green)
```

**When auto-start is DISABLED (manually stopped):**
```
⏸ Auto-start Disabled  (in red)
```

This makes it immediately obvious whether the VPN will automatically start when you leave trusted networks.

### How It Works:

**When you manually click "Stop VPN":**
- The VPN service stops
- A manual disable flag is set and persisted to disk (`wg_manual_disable.flag`)
- The tray menu shows **"⏸ Auto-start Disabled"** in red at the top
- Auto-management is suppressed - the VPN will **not** automatically restart even when you leave trusted networks

**When you manually click "Start VPN":**
- The VPN service starts
- The manual disable flag is cleared
- The indicator changes to **"▶ Auto-start Enabled"** in green
- Auto-management resumes - the VPN will automatically start/stop based on WiFi network

**When you install the VPN service:**
- The manual disable flag is automatically cleared
- The indicator changes to **"▶ Auto-start Enabled"** in green
- Auto-management is enabled - the VPN will automatically start/stop based on network
- This ensures a clean start whenever the service is (re)installed

**When auto-management stops the VPN (on trusted networks):**
- The VPN service stops
- The manual disable flag is **NOT** set
- The indicator remains **"▶ Auto-start Enabled"** in green
- Auto-management continues normally
- The VPN will automatically restart when you leave the trusted network

### Use Cases:

1. **Temporary VPN pause**: Stop the VPN for a specific reason (testing, troubleshooting) and prevent it from auto-restarting
2. **Work from home**: Stop the VPN when working from home and prevent restarts while on your home network
3. **Battery saving**: Manually disable VPN to save battery and keep it off until you explicitly re-enable it
4. **Selective usage**: Keep the service installed but control when it runs without uninstalling

### Persistence:

The manual disable state is saved to a file (`wg_manual_disable.flag`) in the application directory, so it persists across:
- Application restarts
- System reboots
- Task Scheduler runs

The state remains until you manually click "Start VPN" again.

## Network Monitor Task

The Network Monitor Task provides **instant response to network changes** by using Windows Task Scheduler integration with Event Log triggers. Instead of waiting for the 30-second timer, the application can detect and respond to network changes in approximately 5 seconds.

### Why Use It?

| Feature | 30-Second Timer Only | With Network Monitor Task |
|---------|---------------------|--------------------------|
| Response Time | Up to 30 seconds | ~5 seconds |
| Resource Usage | Continuous polling | On-demand only |
| Battery Impact | Moderate | Very low |
| Setup | Automatic | One-time manual |
| Reliability | Good | Excellent (dual approach) |

### Quick Start

#### Installation
1. **Right-click** the WgWrap tray icon
2. Go to **Auto-Start Task** → **Install Network Monitor Task**
3. Click **Yes** on the UAC prompt (one-time setup)
4. See success confirmation

#### Verification
You can verify the task was created:
- Press `Win + R`, type `taskschd.msc`, press Enter
- Look for **WireGuardTray_NetworkMonitor** in the task list

#### Uninstallation
1. **Right-click** the WgWrap tray icon
2. Go to **Auto-Start Task** → **Uninstall Network Monitor Task**
3. Confirm and accept UAC prompt

### How It Works

When a network change occurs (WiFi connect/disconnect, Ethernet plug/unplug, etc.):

```
Network Change Event
    ↓
Windows Event Log (Event 10000/10001)
    ↓
Task Scheduler Triggers (5-second delay)
    ↓
Launches WgWrap.exe in background
    ↓
Runs AutoManageVpn() only
    ↓
Exits immediately (no tray icon)
```

### Technical Details

**Event Triggers:**
- **Event Source**: `Microsoft-Windows-NetworkProfile/Operational`
- **Event IDs**: 
  - `10000` - Network connected
  - `10001` - Network disconnected
- **Delay**: 5 seconds after event (allows network to stabilize)

**Task Configuration:**
- **Name**: `WireGuardTray_NetworkMonitor`
- **Runs as**: Current user (no elevation required for execution)
- **Multiple Instances**: Ignores new instances if already running
- **Battery**: Runs even on battery power
- **Execution Limit**: 1 minute timeout
- **Priority**: Normal (7)

**Duplicate Prevention:**
- Uses existing application mutex to prevent multiple instances
- If tray app is running, task execution completes quickly
- Rapid network changes don't cause duplicate processes

### Benefits

1. **Real-Time Response**: Network changes detected immediately instead of waiting up to 30 seconds
2. **Battery Friendly**: Application only runs when needed, not continuously
3. **Windows Native**: Uses built-in Task Scheduler (reliable and well-tested)
4. **User-Level**: No system-level permissions needed to run the task (only for install/uninstall)
5. **Dual Approach**: Works alongside 30-second timer for maximum reliability

### Example Scenarios

**Scenario 1: Arriving at Coffee Shop (Untrusted WiFi)**
- Without Task: Connect to WiFi → Wait up to 30 seconds → VPN starts
- With Task: Connect to WiFi → 5 seconds → VPN starts automatically ✨

**Scenario 2: Arriving Home (Trusted WiFi)**
- Without Task: Connect to home WiFi → Wait up to 30 seconds → VPN stops
- With Task: Connect to home WiFi → 5 seconds → VPN stops automatically ✨

**Scenario 3: Unplugging Ethernet**
- Without Task: Disconnect ethernet → Wait up to 30 seconds → VPN adjusts
- With Task: Disconnect ethernet → 5 seconds → VPN responds instantly ✨

### Troubleshooting

#### Task Not Triggering?
1. **Check Event Viewer** for network events:
   - Open Event Viewer (`eventvwr.msc`)
   - Navigate to: Applications and Services Logs → Microsoft → Windows → NetworkProfile → Operational
   - Look for Event IDs 10000 and 10001
2. **Verify task exists** in Task Scheduler (`taskschd.msc`)
3. **Check task history** in Task Scheduler to see execution logs
4. **Manually run task** to test: Right-click task → Run

#### Task Installed but VPN Not Responding?
1. Ensure VPN service is installed via "Service > Install VPN Service"
2. Check trusted SSIDs are configured in Settings
3. Verify the task is enabled in Task Scheduler
4. Test manual VPN start/stop to confirm basic functionality

#### Want to Test Manually?
1. Open Task Scheduler (`taskschd.msc`)
2. Find **WireGuardTray_NetworkMonitor**
3. Right-click → **Run**
4. Watch VPN status change based on current network

#### Performance Issues?
1. Check task execution history in Task Scheduler
2. Verify execution time is typically under 1 second
3. Look for errors in Event Viewer
4. Ensure mutex is preventing duplicate instances

### Menu Indicators

The Auto-Start Task menu items are automatically enabled/disabled:
- **Install Network Monitor Task**: Enabled only when task is NOT installed
- **Uninstall Network Monitor Task**: Enabled only when task IS installed

This prevents installing the task twice or uninstalling when it doesn't exist.

### Compatibility

- **Windows Version**: Windows 7 and later (Task Scheduler 2.0)
- **.NET Version**: Compatible with .NET 9.0
- **Privileges**: UAC elevation required only for install/uninstall, not execution
- **User Context**: Runs in user's session (can access user's VPN service permissions)

### Recommendation

**Use both the timer and the task together!**

- The **30-second timer** ensures regular checks even if events are missed
- The **Network Monitor Task** provides instant response to network changes
- Together, they provide the best user experience and reliability

The timer acts as a backup in case:
- The task fails to trigger
- Event logs are disabled or not generating events
- Network changes don't generate the expected event IDs
- Task Scheduler service is not running

This dual approach ensures your VPN is always managed correctly!

### Tips

1. **First-time setup**: Install the task once and forget about it
2. **Moving the app**: If you move WgWrap.exe to a new location, reinstall the task
3. **Upgrading**: When upgrading WgWrap, the task automatically uses the new version
4. **Battery life**: The task has minimal impact on battery life compared to continuous polling
5. **Network roaming**: Perfect for laptop users who frequently switch between networks
6. **Corporate networks**: Great for automatically enabling VPN when leaving trusted office networks

## Shutdown Warning

When you close the WgWrap application while the Network Monitor Task is installed, a warning dialog appears to remind you that:

- The application UI will close, but the Network Monitor Task continues running
- The VPN will still be automatically managed based on network changes
- To completely stop automatic VPN management, you must uninstall the Network Monitor Task

### Don't Show Again Option

The shutdown warning includes a "Don't show this warning again" checkbox:

- **First warning**: Check the box and click "Yes" to remember your preference
- **Subsequent closes**: No warning will be shown
- **After service reinstall**: The warning preference is reset, so you'll see the warning again (this ensures you're always informed after making system changes)
- **Configuration**: The preference is stored in `appsettings.json` as `ShowShutdownWarning`

This design ensures you're always aware of the task's continued operation while allowing experienced users to skip the warning.

## Architecture

WgWrap follows Clean Architecture / Layered Approach principles with clear separation of concerns:

```
WgWrap/
├── Core/                           # Infrastructure & Foundation
│   ├── Configuration/
│   │   └── ConfigurationManager.cs    # Settings management
│   ├── Logging/
│   │   └── Logger.cs                   # File-based logging
│   ├── Models/
│   │   └── WireGuardConfigInfo.cs      # Data models
│   └── Settings/                       # (Removed - consolidated into Configuration)
│
├── Services/                       # Business Logic
│   ├── VpnServiceManager.cs           # VPN operations
│   ├── VpnAutoManager.cs              # Auto-management
│   ├── NetworkManager.cs              # Network detection
│   ├── TaskSchedulerManager.cs        # Task Scheduler
│   ├── StartupManager.cs              # Windows startup
│   └── ManualDisableTracker.cs        # State tracking
│
├── UI/                             # User Interface
│   ├── Forms/
│   │   ├── StatusForm.cs              # Status window with menu bar
│   │   └── ConfigEditorForm.cs        # Config editor
│   ├── TrayIcon/
│   │   └── TrayIconManager.cs         # System tray
│   └── IconGenerator.cs               # Branded icon generation
│
└── Program.cs                      # Application entry point
```

**Key Design Patterns:**
- **Centralized Icon Generation**: `IconGenerator` class provides consistent branded "W" icons with dynamic colors for all UI components
- **Centralized Configuration**: `ConfigurationManager` uses a single `UpdateSettings()` method for all config changes
- **Color Logic Consolidation**: `IconGenerator.GetIconColor()` method ensures consistent status colors across tray icon and status form
- **Menu-based Controls**: Auto-start and other settings accessible via menu bar for better UX

**Namespaces:**
- `WgWrap.Core.Configuration` - Configuration management
- `WgWrap.Core.Logging` - Logging infrastructure
- `WgWrap.Core.Models` - Data models
- `WgWrap.Services` - All business logic services
- `WgWrap.UI.Forms` - Windows Forms
- `WgWrap.UI.TrayIcon` - Tray icon management
- `WgWrap.UI` - Icon generation utilities
- `WgWrap` - Application entry point

## Build & Run (Windows, cmd.exe)

### Manual Build
```cmd
cd WgWrap
dotnet build
bin\Debug\net9.0-windows\WgWrap.exe
```

### Portable Release Build
To create a portable ZIP package for distribution:

1. **Run the build script**:
   ```powershell
   .\build-and-package.ps1
   ```

2. **Script features**:
   - Builds the application in Release mode
   - Publishes as self-contained Windows x64 application
   - Creates `WgWrap-Portable.zip` (~47MB)
   - **Excludes**: `appsettings.json` and `data/` directory contents

3. **Distribution**:
   - Extract the ZIP anywhere on Windows
   - Run `WgWrap.exe` directly (no installation required)
   - Users must create their own `appsettings.json` configuration file

The application will run without administrator privileges and request elevation (UAC) only when needed.

## Future Improvements
- Dark mode theme support
- Multi-tunnel support (manage multiple VPN configurations)
- Enhanced error reporting in the UI

## Licensing & Commercial Use

This project is licensed under the GNU Affero General Public License v3.0 (AGPL-3.0). You are free to use, modify, and distribute this software under the terms of the AGPL. 

**Commercial licensing is available:** If you wish to use this software in a way not permitted by the AGPL (for example, in a closed-source or commercial product), please contact the author to discuss commercial licensing terms.

**Author:** Tom Bongers  
**Email:** tcjbongers@[the google thing]

**WireGuard Disclaimer:**
This project is not affiliated with, endorsed by, or sponsored by the WireGuard project. WireGuard® is a registered trademark of Jason A. Donenfeld. This software does not include or distribute WireGuard itself, and all rights to WireGuard remain with their respective owners.

See the LICENSE file for full terms and contact information.
