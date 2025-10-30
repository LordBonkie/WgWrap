// WgWrap - WireGuard auto-management tray application
// Author: Tom Bongers
// Email: tcjbongers@[the google thing]
// License: AGPL v3, see LICENSE file for details

using System.Diagnostics;
using System.Security.Principal;
using WgWrap.Core.Configuration;
using WgWrap.Core.Logging;
using WgWrap.Services;
using WgWrap.UI.TrayIcon;

namespace WgWrap;

internal static class Program
{
    // Managers
    private static ConfigurationManager? _configManager;
    private static VpnServiceManager? _vpnManager;
    private static NetworkManager? _networkManager;
    private static TaskSchedulerManager? _taskManager;
    private static ManualDisableTracker? _disableTracker;
    private static VpnAutoManager? _autoManager;
    private static TrayIconManager? _trayManager;
    private static Logger? _logger;
    
    // Application resources
    private static Mutex? _mutex;
    private static FileSystemWatcher? _watcher;
    private static System.Windows.Forms.Timer? _timer;
    private static DateTime _lastTimerTick = DateTime.MinValue;
    
    // Launch mode tracking
    private static bool _launchedByTask;

    // Public accessors for status window
    public static int GetTimerRemainingSeconds()
    {
        if (_timer == null || !_timer.Enabled || _configManager == null)
            return 0;

        if (_lastTimerTick == DateTime.MinValue)
            return _configManager.TimerIntervalSeconds;

        var elapsed = (DateTime.Now - _lastTimerTick).TotalSeconds;
        var remaining = _configManager.TimerIntervalSeconds - elapsed;
        return remaining > 0 ? (int)Math.Ceiling(remaining) : 0;
    }

    public static bool IsTimerEnabled() => _timer != null && _timer.Enabled && _configManager != null && _configManager.TimerEnabled;

    /// <summary>
    /// Reloads configuration and restarts timer with new settings
    /// </summary>
    public static void ReloadConfiguration()
    {
        if (_configManager == null)
        {
            _logger?.Error("ReloadConfiguration called but _configManager is null.");
            return;
        }

        _logger?.Info("Reloading configuration and applying changes...");
        
        // Reload configuration from file
        _configManager.LoadConfiguration();
        
        // Restart timer with new settings
        if (_timer != null)
        {
            _timer.Stop();
            _timer.Dispose();
            _timer = null;
            _logger?.Debug("Stopped and disposed old timer.");
        }
        
        SetupTimer();
        
        // Update tray components
        _trayManager?.UpdateIcon();
        _trayManager?.UpdateMenuItems();
        
        _logger?.Info("Configuration reloaded and timer restarted successfully.");
    }

    [STAThread]
    private static void Main(string[] args)
    {
        _logger = new Logger();
        _logger.Info("Application starting.");
        _launchedByTask = IsLaunchedByTaskScheduler(args);
        _logger.Debug($"Launch mode determined: LaunchedByTask={_launchedByTask}");
        
        try
        {
            _configManager = new ConfigurationManager();
            _configManager.LoadConfiguration();
            _logger.Info("Configuration loaded.");
            
            // Sync the internal config file on startup
            try
            {
                _configManager.SyncInternalConfig();
                _logger.Info($"Internal config synced. Using: {(_configManager.IsUsingModifiedConfig() ? "modified" : "original")} config");
            }
            catch (Exception ex)
            {
                _logger.Error($"Failed to sync internal config on startup: {ex.Message}", ex);
                // Don't fail app startup, just log the error
            }
            
            _networkManager = new NetworkManager(_logger);
            _disableTracker = new ManualDisableTracker();
            _vpnManager = new VpnServiceManager(_configManager, () => _launchedByTask, _logger);
            _taskManager = new TaskSchedulerManager();
            _autoManager = new VpnAutoManager(_configManager, _vpnManager, _networkManager, _disableTracker, _logger);
        }
        catch (Exception ex)
        {
            _logger.Error("Fatal error during initialization.", ex);
            MessageBox.Show($"Failed to initialize application: {ex.Message}", "Initialization Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        
        if (_launchedByTask)
        {
            _logger.Info("Running in scheduled task mode. Performing auto-management and exiting.");
            
            // Record the task run time
            _taskManager?.RecordTaskRun();
            
            try
            {
                _autoManager?.AutoManageVpn();
            }
            catch (Exception ex)
            {
                _logger.Error("Error during auto-management in task mode.", ex);
            }
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        bool createdNew;
        _mutex = new Mutex(false, "Global/WgWrap_SingleInstance", out createdNew);
        if (!createdNew)
        {
            _logger.Warn("Another instance is already running. Exiting.");
            MessageBox.Show("WireGuardTray is already active.", "WireGuard Tray", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _logger.Debug("Performing initial auto-management check.");
        _autoManager?.AutoManageVpn();

        SetupTray();
        _autoManager?.EnsureFlagFileExists();
        SetupWatcher();
        SetupTimer();
        _trayManager?.UpdateIcon();

        _logger.Info("Initialization complete. Entering message loop.");
        try
        {
            Application.Run();
        }
        finally
        {
            _logger.Info("Application exiting. Performing cleanup.");
            Cleanup();
        }
    }

    private static bool IsLaunchedByTaskScheduler(string[] args)
    {
        if (args != null && args.Length > 0)
        {
            foreach (var arg in args)
            {
                if (string.Equals(arg, "--task", StringComparison.OrdinalIgnoreCase))
                {
                    _logger?.Debug("Detected --task argument.");
                    return true;
                }
            }
        }
        
        try
        {
            string user = Environment.UserName;
            if (string.Equals(user, "SYSTEM", StringComparison.OrdinalIgnoreCase))
            {
                _logger?.Debug("Running as SYSTEM; assuming task scheduler launch.");
                return true;
            }
            var parent = GetParentProcess();
            if (parent != null && parent.ProcessName.Contains("taskeng", StringComparison.OrdinalIgnoreCase))
            {
                _logger?.Debug("Detected parent process 'taskeng'; assuming task scheduler launch.");
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger?.Warn($"Task launch detection fallback failed: {ex.Message}");
        }
        return false;
    }

    private static bool IsRunningAsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex)
        {
            _logger?.Warn($"Failed to check administrator status: {ex.Message}");
            return false;
        }
    }

    private static Process? GetParentProcess()
    {
        try
        {
            using var current = Process.GetCurrentProcess();
            // Implementation omitted; returns null.
        }
        catch (Exception ex)
        {
            _logger?.Debug($"GetParentProcess failed: {ex.Message}");
        }
        return null;
    }

    private static void SetupTray()
    {
        if (_configManager == null || _vpnManager == null || _networkManager == null || 
            _taskManager == null || _disableTracker == null || _autoManager == null) 
        {
            _logger?.Error("SetupTray called before managers are initialized.");
            return;
        }

        _trayManager = new TrayIconManager(
            _configManager,
            _vpnManager,
            _networkManager,
            _taskManager,
            _disableTracker,
            _autoManager,
            onStartVpn: OnStartVpn,
            onStopVpn: OnStopVpn,
            onInstallService: OnInstallService,
            onUninstallService: OnUninstallService,
            onInstallTask: OnInstallTask,
            onUninstallTask: OnUninstallTask,
            onShowSettings: ShowSettingsWindow,
            onExit: ExitApplication
        );
        
        _logger?.Debug("Initializing tray manager.");
        _trayManager.Initialize();
    }

    private static void OnStartVpn()
    {
        if (_vpnManager == null || _trayManager == null || _disableTracker == null)
        {
            _logger?.Error("OnStartVpn invoked but required components are null.");
            return;
        }
        
        try
        {
            _logger?.Info("Manual start VPN action initiated.");
            _disableTracker.SetDisabled(false);
            _trayManager.RebuildMenu();
            _vpnManager.StartVpn(_trayManager.ShowNotification);
            _trayManager.UpdateIcon();
            _trayManager.UpdateMenuItems();
        }
        catch (Exception ex)
        {
            _logger?.Error("Error during manual VPN start.", ex);
            if (!_launchedByTask)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private static void OnStopVpn()
    {
        if (_vpnManager == null || _trayManager == null || _disableTracker == null)
        {
            _logger?.Error("OnStopVpn invoked but required components are null.");
            return;
        }
        
        try
        {
            _logger?.Info("Manual stop VPN action initiated.");
            _disableTracker.SetDisabled(true);
            _trayManager.RebuildMenu();
            _vpnManager.StopVpn(_trayManager.ShowNotification);
            _trayManager.UpdateIcon();
            _trayManager.UpdateMenuItems();
        }
        catch (Exception ex)
        {
            _logger?.Error("Error during manual VPN stop.", ex);
            if (!_launchedByTask)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private static void OnInstallService()
    {
        if (_vpnManager == null || _trayManager == null || _disableTracker == null || _taskManager == null)
        {
            _logger?.Error("OnInstallService invoked but required components are null.");
            return;
        }
        
        _logger?.Info("Manual install service action initiated.");
        
        // Service-only installation
        _vpnManager.InstallVpn(
            onSuccess: () =>
            {
                _logger?.Info("Service installation callback success. Resetting manual disable flag and shutdown warning.");
                _disableTracker.SetDisabled(false);
                _configManager?.ResetShutdownWarning();
                _trayManager.RebuildMenu();
                
                if (!_launchedByTask)
                {
                    // Show success message and prompt for task installation
                    var result = MessageBox.Show(
                        "VPN service installation completed.\n\n" +
                        "You can now start/stop the VPN without administrator privileges.\n\n" +
                        "Would you like to install the Network Monitor Task?\n" +
                        "This will automatically manage your VPN based on network changes.",
                        "Installation Complete",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);
                    
                    if (result == DialogResult.Yes)
                    {
                        _logger?.Info("User chose to install network monitor task.");
                        OnInstallTask();
                    }
                    else
                    {
                        _logger?.Debug("User declined task installation.");
                    }
                }
                
                _trayManager.UpdateIcon();
                _trayManager.UpdateMenuItems();
            },
            onError: (message) =>
            {
                _logger?.Error($"Service installation failed: {message}");
                if (!_launchedByTask)
                {
                    MessageBox.Show(message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        );
    }

    /// <summary>
    /// Installs both VPN service and Network Monitor Task with a single UAC prompt
    /// </summary>
    private static void InstallServiceAndTask()
    {
        if (_vpnManager == null || _trayManager == null || _disableTracker == null || 
            _taskManager == null || _configManager == null)
        {
            _logger?.Error("InstallServiceAndTask invoked but required components are null.");
            return;
        }

        _logger?.Info("Starting combined service and task installation.");

        try
        {
            // Get paths
            var appDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
            var scriptPath = Path.Combine(appDir, "Scripts", "InstallServiceAndTask.ps1");

            if (!File.Exists(scriptPath))
            {
                _logger?.Warn($"Combined installation script not found at {scriptPath}, falling back to separate installations.");
                MessageBox.Show(
                    "Combined installation script not found.\n\n" +
                    "The service will be installed separately (2 UAC prompts).",
                    "Script Not Found",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                OnInstallService(); // Fall back to normal flow
                return;
            }

            // Sync config
            _configManager.SyncInternalConfig();
            var configPath = _configManager.InternalConfigPath;

            if (!File.Exists(configPath))
            {
                MessageBox.Show($"Internal config file not found at:\n{configPath}", "Error", 
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Create task XML
            var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            if (exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                exePath = exePath.Substring(0, exePath.Length - 4) + ".exe";
            }

            var taskXml = CreateTaskXml(exePath);
            var tempXmlPath = Path.Combine(Path.GetTempPath(), "WireGuardTray_NetworkMonitor.xml");
            File.WriteAllText(tempXmlPath, taskXml);

            var serviceName = $"WireGuardTunnel${_configManager.TunnelName}";

            // Run combined PowerShell script
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" " +
                           $"-WgExePath \"{_configManager.WgExe}\" " +
                           $"-ConfigPath \"{configPath}\" " +
                           $"-ServiceName \"{serviceName}\" " +
                           $"-TaskXmlPath \"{tempXmlPath}\"",
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            var process = Process.Start(psi);

            if (process != null)
            {
                _logger?.Debug("Waiting for combined installation script to complete.");
                process.WaitForExit();

                // Clean up temp file
                try { if (File.Exists(tempXmlPath)) File.Delete(tempXmlPath); } catch { }

                var exitCode = process.ExitCode;
                _logger?.Debug($"Combined installation script exited with code: {exitCode}");

                if (exitCode == 0)
                {
                    _logger?.Info("Combined installation completed successfully.");
                    _disableTracker.SetDisabled(false);
                    _configManager.ResetShutdownWarning();
                    _trayManager.RebuildMenu();

                    // Check if timer is enabled and offer to disable it
                    if (_configManager.TimerEnabled)
                    {
                        var timerResult = MessageBox.Show(
                            "✅ Installation completed successfully!\n\n" +
                            "Both the VPN Service and Network Monitor Task have been installed.\n\n" +
                            "Since the Network Monitor Task will automatically check network changes, " +
                            "you can disable the periodic timer in this application to reduce resource usage.\n\n" +
                            "Would you like to disable the timer now?",
                            "Installation Complete - Disable Timer?",
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Question);

                        if (timerResult == DialogResult.Yes)
                        {
                            _logger?.Info("User chose to disable timer after combined installation.");
                            try
                            {
                                DisableTimerInSettings();
                                ReloadConfiguration();
                                _trayManager?.RefreshStatusFormSettings();
                                _logger?.Info("Timer disabled successfully.");
                                MessageBox.Show(
                                    "Timer has been disabled. The Network Monitor Task will handle network changes.",
                                    "Timer Disabled",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Information);
                            }
                            catch (Exception ex)
                            {
                                _logger?.Error($"Failed to disable timer: {ex.Message}");
                                MessageBox.Show($"Failed to disable timer: {ex.Message}", "Error",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                            }
                        }
                    }
                    else
                    {
                        MessageBox.Show(
                            "✅ Installation completed successfully!\n\n" +
                            "Both the VPN Service and Network Monitor Task have been installed.",
                            "Installation Complete",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }

                    _trayManager?.UpdateIcon();
                    _trayManager?.UpdateMenuItems();
                }
                else
                {
                    var errorMsg = exitCode switch
                    {
                        1 => "WireGuard executable not found",
                        2 => "Configuration file not found",
                        3 => "Service installation failed",
                        4 => "Failed to set service permissions",
                        5 => "Failed to install Network Monitor Task",
                        _ => $"Installation failed with exit code {exitCode}"
                    };
                    _logger?.Error($"Combined installation failed: {errorMsg}");
                    MessageBox.Show($"Installation failed: {errorMsg}", "Error",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            else
            {
                _logger?.Warn("Failed to start combined installation process.");
                MessageBox.Show("Failed to launch installation process.", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            if (ex.NativeErrorCode == 1223)
            {
                _logger?.Warn("User cancelled UAC during combined installation.");
            }
            else
            {
                _logger?.Error($"Failed to perform combined installation: {ex.Message}");
                MessageBox.Show($"Failed to perform combined installation: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            _logger?.Error("Failed to perform combined installation.", ex);
            MessageBox.Show($"Failed to perform combined installation: {ex.Message}", "Error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Creates the XML definition for the Network Monitor Task
    /// </summary>
    private static string CreateTaskXml(string exePath)
    {
        const string taskName = "WireGuardTray_NetworkMonitor";
        const string taskDescription = "Monitors network changes and manages WireGuard VPN connection based on trusted networks";

        return $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.4"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>{taskDescription}</Description>
    <URI>\{taskName}</URI>
  </RegistrationInfo>
  <Triggers>
    <EventTrigger>
      <Enabled>true</Enabled>
      <Subscription>&lt;QueryList&gt;&lt;Query Id=""0"" Path=""Microsoft-Windows-NetworkProfile/Operational""&gt;&lt;Select Path=""Microsoft-Windows-NetworkProfile/Operational""&gt;*[System[Provider[@Name='Microsoft-Windows-NetworkProfile'] and (EventID=10000 or EventID=10001)]]&lt;/Select&gt;&lt;/Query&gt;&lt;/QueryList&gt;</Subscription>
      <Delay>PT1S</Delay>
    </EventTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>{Environment.UserDomainName}\{Environment.UserName}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <DisallowStartOnRemoteAppSession>false</DisallowStartOnRemoteAppSession>
    <UseUnifiedSchedulingEngine>true</UseUnifiedSchedulingEngine>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT1M</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{exePath}</Command>
      <Arguments>--task</Arguments>
    </Exec>
  </Actions>
</Task>";
    }

    private static void OnUninstallService()
    {
        if (_vpnManager == null || _trayManager == null || _taskManager == null || _configManager == null)
        {
            _logger?.Error("OnUninstallService invoked but required components are null.");
            return;
        }

        // Warn the user about losing the modified config if it exists
        bool hasModifiedConfig = _configManager.IsUsingModifiedConfig();
        if (!_launchedByTask && hasModifiedConfig)
        {
            var result = MessageBox.Show(
                "⚠ WARNING: You have a MODIFIED configuration file.\n\n" +
                "Uninstalling the VPN service will permanently delete your modified config.\n" +
                "This action cannot be undone.\n\n" +
                "Do you want to continue?",
                "Modified Config Will Be Lost",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (result != DialogResult.Yes)
            {
                _logger?.Info("User cancelled service uninstallation due to modified config warning.");
                return;
            }
        }

        _logger?.Info("Manual uninstall service action initiated.");
        _vpnManager.UninstallVpn(
            onSuccess: () =>
            {
                _logger?.Info("Service uninstallation callback success.");

                // Clean up internal config file since service is being removed
                try
                {
                    if (_configManager != null)
                    {
                        var internalConfigPath = _configManager.InternalConfigPath;
                        if (!string.IsNullOrEmpty(internalConfigPath) && File.Exists(internalConfigPath))
                        {
                            File.Delete(internalConfigPath);
                            _logger?.Info($"Deleted internal config file: {internalConfigPath}");
                        }
                        // Remove the modified config file as well
                        var modifiedConfigPath = _configManager.ModifiedConfigPath;
                        if (!string.IsNullOrEmpty(modifiedConfigPath) && File.Exists(modifiedConfigPath))
                        {
                            File.Delete(modifiedConfigPath);
                            _logger?.Info($"Deleted modified config file: {modifiedConfigPath}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger?.Warn($"Failed to delete internal or modified config file: {ex.Message}");
                }
                
                // Also uninstall the network monitor task
                bool taskWasInstalled = _taskManager.IsTaskInstalled();
                if (taskWasInstalled)
                {
                    _logger?.Info("Network monitor task detected, uninstalling it as well.");
                    var (taskSuccess, taskMessage) = _taskManager.UninstallTask();
                    
                    if (taskSuccess)
                    {
                        _logger?.Info("Network monitor task uninstalled successfully.");
                        
                        // Re-enable timer if it was disabled
                        bool timerDisabled = _configManager is { TimerEnabled: false };
                        if (timerDisabled)
                        {
                            _logger?.Info("Timer is disabled, re-enabling it since task is being uninstalled.");
                            try
                            {
                                EnableTimerInSettings();
                                ReloadConfiguration();
                                _trayManager?.RefreshStatusFormSettings();
                                _logger?.Info("Timer re-enabled successfully.");
                            }
                            catch (Exception ex)
                            {
                                _logger?.Error($"Failed to re-enable timer: {ex.Message}");
                            }
                        }
                        
                        if (!_launchedByTask)
                        {
                            string msg = "VPN service and Network Monitor Task uninstallation completed.";
                            if (timerDisabled)
                            {
                                msg += "\n\nThe periodic timer has been re-enabled to continue monitoring network changes.";
                            }
                            MessageBox.Show(msg, "Uninstallation Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        }
                    }
                    else
                    {
                        _logger?.Warn($"Network monitor task uninstallation failed: {taskMessage}");
                        if (!_launchedByTask && taskMessage != null)
                        {
                            MessageBox.Show($"VPN service uninstalled, but task uninstallation failed:\n{taskMessage}",
                                "Partial Uninstallation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    }
                }
                else
                {
                    _logger?.Info("No network monitor task to uninstall.");
                    if (!_launchedByTask)
                    {
                        MessageBox.Show("VPN service uninstallation completed.",
                            "Uninstallation Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
                
                _trayManager?.UpdateIcon();
                _trayManager?.UpdateMenuItems();
            },
            onError: (message) =>
            {
                _logger?.Error($"Service uninstallation failed: {message}");
                if (!_launchedByTask)
                {
                    MessageBox.Show(message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        );
    }

    private static void OnInstallTask()
    {
        if (_taskManager == null || _trayManager == null || _configManager == null)
        {
            _logger?.Error("OnInstallTask invoked but required components are null.");
            return;
        }
        
        _logger?.Info("Manual install task action initiated.");
        var (success, message) = _taskManager.InstallTask();
        
        if (success && message != null)
        {
            _logger?.Info($"Task installation succeeded: {message}");
            
            // Check if timer is currently enabled
            bool timerEnabled = _configManager.TimerEnabled;
            
            if (timerEnabled)
            {
                // Prompt user about disabling the timer
                var result = MessageBox.Show(
                    message + "\n\n" +
                    "Since the Network Monitor Task will automatically check network changes, " +
                    "you can disable the periodic timer in this application to reduce resource usage.\n\n" +
                    "Would you like to disable the timer now?",
                    "Task Installed - Disable Timer?",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);
                
                if (result == DialogResult.Yes)
                {
                    _logger?.Info("User chose to disable timer after task installation.");
                    
                    // Disable timer in configuration
                    try
                    {
                        DisableTimerInSettings();
                        
                        // Reload configuration and update timer
                        ReloadConfiguration();
                        
                        // Refresh settings UI if status form is open
                        _trayManager?.RefreshStatusFormSettings();
                        
                        _logger?.Info("Timer disabled successfully.");
                        MessageBox.Show("Timer has been disabled. The Network Monitor Task will handle network changes.",
                            "Timer Disabled", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    catch (Exception ex)
                    {
                        _logger?.Error($"Failed to disable timer: {ex.Message}");
                        MessageBox.Show($"Failed to disable timer: {ex.Message}", "Error", 
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                }
                else
                {
                    _logger?.Debug("User chose to keep timer enabled.");
                }
            }
            else
            {
                // Timer already disabled, just show success message
                MessageBox.Show(message, "Task Installed", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        else if (message != null)
        {
            _logger?.Info($"Task installation failed: {message}");
            MessageBox.Show(message, "Installation Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        
        _trayManager?.UpdateMenuItems();
    }

    /// <summary>
    /// Disables the timer in appsettings.json
    /// </summary>
    private static void DisableTimerInSettings()
    {
        if (_configManager == null)
        {
            throw new InvalidOperationException("Configuration manager is not initialized.");
        }

        _configManager.SetTimerEnabled(false);
        _logger?.Debug("Timer disabled in appsettings.json");
    }

    /// <summary>
    /// Enables the timer in appsettings.json
    /// </summary>
    private static void EnableTimerInSettings()
    {
        if (_configManager == null)
        {
            throw new InvalidOperationException("Configuration manager is not initialized.");
        }

        _configManager.SetTimerEnabled(true);
        _logger?.Debug("Timer enabled in appsettings.json");
    }

    private static void OnUninstallTask()
    {
        if (_taskManager == null || _trayManager == null || _configManager == null)
        {
            _logger?.Error("OnUninstallTask invoked but required components are null.");
            return;
        }
        
        _logger?.Info("Manual uninstall task action initiated.");
        if (!_taskManager.IsTaskInstalled())
        {
            _logger?.Warn("Uninstall task requested but task not installed.");
            MessageBox.Show("Network Monitor Task is not installed.",
                "Task Not Found", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var result = MessageBox.Show(
            "Are you sure you want to uninstall the Network Monitor Task?\n\n" +
            "After uninstalling, automatic VPN management on network changes will be disabled.",
            "Confirm Uninstall",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result != DialogResult.Yes)
        {
            _logger?.Debug("User cancelled task uninstallation dialog.");
            return;
        }

        var (success, message) = _taskManager.UninstallTask();
        
        if (success)
        {
            _logger?.Info($"Task uninstallation succeeded: {message}");
            
            // Check if timer is currently disabled
            bool timerDisabled = !_configManager.TimerEnabled;
            
            if (timerDisabled)
            {
                // Re-enable the timer since task is being removed
                _logger?.Info("Timer is disabled, re-enabling it since task is being uninstalled.");
                
                try
                {
                    EnableTimerInSettings();
                    
                    // Reload configuration and restart timer
                    ReloadConfiguration();
                    
                    // Refresh settings UI if status form is open
                    _trayManager?.RefreshStatusFormSettings();
                    
                    _logger?.Info("Timer re-enabled successfully.");
                    
                    MessageBox.Show(
                        (message ?? "Task uninstalled successfully.") + "\n\n" +
                        "The periodic timer has been re-enabled to continue monitoring network changes.",
                        "Task Uninstalled - Timer Enabled",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    _logger?.Error($"Failed to re-enable timer: {ex.Message}");
                    MessageBox.Show(
                        (message ?? "Task uninstalled successfully.") + "\n\n" +
                        $"Warning: Failed to re-enable timer: {ex.Message}",
                        "Task Uninstalled",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
            else
            {
                // Timer already enabled, just show success
                if (message != null)
                {
                    MessageBox.Show(message, "Task Uninstalled", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }
        else if (message != null)
        {
            _logger?.Info($"Task uninstallation failed: {message}");
            MessageBox.Show(message, "Uninstallation Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        
        _trayManager?.UpdateMenuItems();
    }

    private static void ShowSettingsWindow()
    {
        try
        {
            if (_trayManager == null)
            {
                _logger?.Error("ShowSettingsWindow invoked but _trayManager is null.");
                return;
            }
            
            _logger?.Debug("Opening status window (settings are integrated in Settings tab).");
            // Settings are now integrated into the StatusForm, so just show the status window
            _trayManager.ShowStatus();
        }
        catch (Exception ex)
        {
            _logger?.Error("Error opening status window.", ex);
            MessageBox.Show($"Error opening status window: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void ExitApplication()
    {
        _logger?.Info("Exit application requested.");
        
        // Check if the Network Monitor Task is installed AND if we should show the warning
        if (_taskManager != null && _taskManager.IsTaskInstalled() && 
            _configManager != null && _configManager.ShowShutdownWarning)
        {
            // Create custom dialog with checkbox
            var warningForm = new Form
            {
                Text = "Network Monitor Task Still Active",
                Width = 500,
                Height = 350,
                StartPosition = FormStartPosition.CenterScreen,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false,
                MinimizeBox = false,
                Icon = SystemIcons.Warning
            };
            
            var messageLabel = new Label
            {
                Text = "⚠ WARNING: The Network Monitor Task is installed and will continue running.\n\n" +
                       "Closing this application will only stop:\n" +
                       "• The in-app periodic timer (if enabled)\n" +
                       "• The tray icon and status display\n\n" +
                       "The Network Monitor Task will continue to automatically manage your VPN " +
                       "based on network changes at the Windows system level.\n\n" +
                       "To stop automatic VPN management, you must uninstall the Network Monitor Task " +
                       "from the menu before closing.\n\n" +
                       "Do you want to close the application anyway?",
                Left = 20,
                Top = 20,
                Width = 440,
                Height = 220,
                AutoSize = false
            };
            warningForm.Controls.Add(messageLabel);
            
            var dontShowCheckbox = new CheckBox
            {
                Text = "Don't show this warning again",
                Left = 20,
                Top = 250,
                Width = 250,
                Checked = false
            };
            warningForm.Controls.Add(dontShowCheckbox);
            
            var yesButton = new Button
            {
                Text = "Yes",
                DialogResult = DialogResult.Yes,
                Left = 280,
                Top = 275,
                Width = 80
            };
            warningForm.Controls.Add(yesButton);
            
            var noButton = new Button
            {
                Text = "No",
                DialogResult = DialogResult.No,
                Left = 370,
                Top = 275,
                Width = 80
            };
            warningForm.Controls.Add(noButton);
            
            warningForm.AcceptButton = noButton; // Default to No
            warningForm.CancelButton = noButton;
            
            var result = warningForm.ShowDialog();
            
            // Save preference if checkbox was checked
            if (dontShowCheckbox.Checked && _configManager != null)
            {
                try
                {
                    _configManager.SetShowShutdownWarning(false);
                    _logger?.Info("User chose to not show shutdown warning again.");
                }
                catch (Exception ex)
                {
                    _logger?.Warn($"Failed to save shutdown warning preference: {ex.Message}");
                }
            }
            
            if (result != DialogResult.Yes)
            {
                _logger?.Info("User cancelled application exit.");
                return;
            }
        }
        
        _logger?.Info("Proceeding with application exit.");
        _trayManager?.Hide();
        _timer?.Stop();
        if (_watcher != null) _watcher.EnableRaisingEvents = false;
        try { _mutex?.ReleaseMutex(); } catch { }
        Cleanup();
        Application.Exit();
    }

    private static void Cleanup()
    {
        _logger?.Debug("Performing resource cleanup.");
        _trayManager?.Dispose();
        _watcher?.Dispose();
        _timer?.Dispose();
        _mutex?.Dispose();
        (_logger as IDisposable)?.Dispose();
    }

    private static void SetupWatcher()
    {
        if (_autoManager == null)
        {
            _logger?.Error("SetupWatcher called but _autoManager is null.");
            return;
        }
        
        try
        {
            var flagFile = _autoManager.GetFlagFilePath();
            var dir = Path.GetDirectoryName(flagFile);
            if (string.IsNullOrEmpty(dir)) return;
            _watcher = new FileSystemWatcher(dir, Path.GetFileName(flagFile))
            {
                EnableRaisingEvents = true
            };
            _watcher.Changed += (_, _) => 
            { 
                _logger?.Debug("Flag file change detected; updating tray UI.");
                _trayManager?.UpdateIcon(); 
                _trayManager?.UpdateMenuItems(); 
            };
            _logger?.Debug("File system watcher initialized for flag file.");
        }
        catch (Exception ex)
        {
            _logger?.Warn($"Failed to setup file watcher: {ex.Message}");
        }
    }

    private static void SetupTimer()
    {
        if (_configManager == null)
        {
            _logger?.Error("SetupTimer called but _configManager is null.");
            return;
        }

        if (!_configManager.TimerEnabled)
        {
            _logger?.Info("Timer is disabled in configuration. Skipping timer setup.");
            return;
        }

        _timer = new System.Windows.Forms.Timer
        {
            Interval = _configManager.TimerIntervalSeconds * 1000
        };
        _timer.Tick += (_, _) => 
        { 
            _lastTimerTick = DateTime.Now;
            _logger?.Debug("Timer tick: performing auto-management update.");
            _autoManager?.AutoManageVpn(); 
            _trayManager?.UpdateIcon(); 
            _trayManager?.UpdateMenuItems(); 
        };
        _lastTimerTick = DateTime.Now;
        _timer.Start();
        _logger?.Info($"Periodic timer started ({_configManager.TimerIntervalSeconds}s interval).");
    }

    public static string Version
    {
        get
        {
            try
            {
                // Read version from assembly (works both during development and after publishing)
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                return version?.ToString() ?? "x.x.x";
            }
            catch
            {
                // Fallback version
                return "x.x.x";
            }
        }
    }
}
