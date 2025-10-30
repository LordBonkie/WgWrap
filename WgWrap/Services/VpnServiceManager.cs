using System.Diagnostics;
using System.ServiceProcess;
using WgWrap.Core.Configuration;
using WgWrap.Core.Logging;
using WgWrap.Core.Models;

namespace WgWrap.Services;

/// <summary>
/// Manages VPN service operations (install, uninstall, start, stop, status)
/// </summary>
internal class VpnServiceManager
{
    private readonly ConfigurationManager _config;
    private readonly Func<bool> _isLaunchedByTask;
    private readonly Logger _logger;

    public VpnServiceManager(ConfigurationManager config, Func<bool> isLaunchedByTask, Logger logger)
    {
        _config = config;
        _isLaunchedByTask = isLaunchedByTask;
        _logger = logger;
    }

    /// <summary>
    /// Checks if any WireGuard service is installed on the system
    /// </summary>
    private bool IsAnyWireGuardServiceInstalled()
    {
        try
        {
            var services = ServiceController.GetServices();
            return services.Any(s => s.ServiceName.StartsWith("WireGuardTunnel$", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the current VPN service status
    /// </summary>
    public string GetVpnStatus()
    {
        var serviceName = $"WireGuardTunnel${_config.TunnelName}";
        try
        {
            using var service = new ServiceController(serviceName);
            service.Refresh();
            var status = service.Status;
            string readable = status switch
            {
                ServiceControllerStatus.Running => "Connected",
                ServiceControllerStatus.Stopped => "Disconnected",
                ServiceControllerStatus.StartPending => "Starting...",
                ServiceControllerStatus.StopPending => "Stopping...",
                _ => $"Status: {status}"
            };
            return readable;
        }
        catch (InvalidOperationException)
        {
            // If our expected service doesn't exist, check if any WireGuard service exists
            // This handles backward compatibility and prevents config lock issues
            if (IsAnyWireGuardServiceInstalled())
            {
                return "Service Exists (Different Name)";
            }
            return "Not Installed";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return "Unknown (Access Error)";
        }
        catch
        {
            return "Unknown";
        }
    }

    /// <summary>
    /// Gets all WireGuard services installed on the system
    /// </summary>
    public string[] GetWireGuardServices()
    {
        try
        {
            var services = ServiceController.GetServices();
            return services
                .Where(s => s.ServiceName.StartsWith("WireGuardTunnel$", StringComparison.OrdinalIgnoreCase))
                .Select(s => $"{s.ServiceName} ({s.Status})")
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Gets detailed configuration information from the WireGuard config file
    /// </summary>
    public WireGuardConfigInfo GetConfigInfo()
    {
        var info = new WireGuardConfigInfo();
        
        try
        {
            // Use the internal config path (always wg_wrap_tunnel.conf)
            var configPath = _config.InternalConfigPath;
            
            if (string.IsNullOrWhiteSpace(configPath) || !File.Exists(configPath))
            {
                _logger.Warn($"Internal config file not found: {configPath}");
                return info;
            }

            var lines = File.ReadAllLines(configPath);
            string currentSection = "";
            
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                    continue;

                if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                {
                    currentSection = trimmed.Trim('[', ']');
                    continue;
                }

                var parts = trimmed.Split('=', 2);
                if (parts.Length != 2) continue;

                var key = parts[0].Trim();
                var value = parts[1].Trim();

                if (currentSection.Equals("Interface", StringComparison.OrdinalIgnoreCase))
                {
                    switch (key.ToLowerInvariant())
                    {
                        case "privatekey":
                            info.InterfacePublicKey = value; // Store the actual key for truncated display
                            break;
                        case "address":
                            info.InterfaceAddress = value;
                            break;
                        case "listenport":
                            info.ListenPort = value;
                            break;
                        case "dns":
                            info.DNS = value;
                            break;
                        case "mtu":
                            info.MTU = value;
                            break;
                    }
                }
                else if (currentSection.Equals("Peer", StringComparison.OrdinalIgnoreCase))
                {
                    switch (key.ToLowerInvariant())
                    {
                        case "publickey":
                            info.PeerPublicKey = value;
                            break;
                        case "presharedkey":
                            info.PresharedKey = "enabled";
                            break;
                        case "allowedips":
                            info.AllowedIPs = value;
                            break;
                        case "endpoint":
                            info.Endpoint = value;
                            break;
                        case "persistentkeepalive":
                            info.PersistentKeepalive = value;
                            break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Error reading WireGuard config file.", ex);
        }

        return info;
    }

    /// <summary>
    /// Starts the VPN service
    /// </summary>
    public void StartVpn(Action<string, string> showNotification)
    {
        _logger.Info("Request to start VPN service received (manual action). Clearing manual disable flag.");
        StartVpnInternal(clearManualDisableFlag: true, showNotification);
    }

    /// <summary>
    /// Internal method to start VPN with optional manual disable flag clearing
    /// </summary>
    public void StartVpnInternal(bool clearManualDisableFlag, Action<string, string> showNotification, Action? onFlagCleared = null)
    {
        var serviceName = $"WireGuardTunnel${_config.TunnelName}";
        _logger.Debug($"StartVpnInternal invoked. Service={serviceName}, clearManualDisableFlag={clearManualDisableFlag}");

        // Clear manual disable flag if requested
        if (clearManualDisableFlag && onFlagCleared != null)
        {
            _logger.Debug("Clearing manual disable flag via callback.");
            onFlagCleared();
        }

        try
        {
            using var service = new ServiceController(serviceName);
            service.Refresh();

            if (service.Status == ServiceControllerStatus.Running)
            {
                _logger.Info("VPN service already running; skipping start.");
                if (!_isLaunchedByTask())
                {
                    showNotification("VPN Already Running", $"VPN service '{_config.TunnelName}' is already running.");
                }
                return;
            }

            try
            {
                if (service.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending)
                {
                    _logger.Debug($"Attempting to start service '{serviceName}'. Current status: {service.Status}");
                    service.Start();
                    service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10));
                    _logger.Info("Service start sequence completed (non-elevated path).");

                    if (!_isLaunchedByTask())
                    {
                        showNotification("VPN Started", $"VPN service '{_config.TunnelName}' started successfully.");
                    }
                    return;
                }
                else
                {
                    _logger.Warn($"Service '{serviceName}' in unexpected state {service.Status} when attempting start.");
                }
            }
            catch (InvalidOperationException ex)
            {
                _logger.Warn($"Non-elevated start failed for '{serviceName}', attempting elevation. Error: {ex.Message}");
                StartVpnWithElevation(showNotification);
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.Error($"StartVpnInternal: Service '{serviceName}' not installed. {ex.Message}", ex);
            if (!_isLaunchedByTask())
            {
                throw new InvalidOperationException($"VPN service '{_config.TunnelName}' is not installed.\n\nPlease use 'Service > Install VPN Service' first.");
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _logger.Warn($"Access error starting service '{serviceName}'. Attempting elevation. Win32: {ex.Message}");
            StartVpnWithElevation(showNotification);
        }
    }

    private void StartVpnWithElevation(Action<string, string> showNotification)
    {
        var serviceName = $"WireGuardTunnel${_config.TunnelName}";
        _logger.Debug($"StartVpnWithElevation invoked for service '{serviceName}'.");
        try
        {
            if (!File.Exists(_config.WgExe))
            {
                _logger.Error($"WireGuard executable missing at '{_config.WgExe}'. Cannot elevate start.");
                if (!_isLaunchedByTask())
                {
                    throw new FileNotFoundException($"WireGuard executable not found at:\n{_config.WgExe}");
                }
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"start {serviceName}",
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true
            };
            var process = Process.Start(psi);

            if (process != null)
            {
                _logger.Debug("Waiting for elevated start process to exit.");
                process.WaitForExit();
                Thread.Sleep(1000);

                var status = GetVpnStatus();
                _logger.Info($"Post-elevation start status: {status}");

                if (!_isLaunchedByTask())
                {
                    if (status == "Connected")
                    {
                        showNotification("VPN Started", $"VPN service '{_config.TunnelName}' started successfully.");
                    }
                    else
                    {
                        showNotification("VPN Start Command Executed", $"Current status: {status}");
                    }
                }
            }
            else
            {
                _logger.Warn("Failed to start elevated process for service start.");
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            if (ex.NativeErrorCode == 1223)
            {
                _logger.Warn("User cancelled UAC elevation for start.");
            }
            else if (!_isLaunchedByTask())
            {
                _logger.Error($"Failed to start VPN with elevation: {ex.Message}");
                throw new InvalidOperationException($"Failed to start VPN with elevation: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Stops the VPN service
    /// </summary>
    public void StopVpn(Action<string, string> showNotification)
    {
        _logger.Info("Request to stop VPN service received (manual action). Setting manual disable flag.");
        StopVpnInternal(setManualDisableFlag: true, showNotification);
    }

    /// <summary>
    /// Internal method to stop VPN with optional manual disable flag setting
    /// </summary>
    public void StopVpnInternal(bool setManualDisableFlag, Action<string, string> showNotification, Action? onFlagSet = null)
    {
        var serviceName = $"WireGuardTunnel${_config.TunnelName}";
        _logger.Debug($"StopVpnInternal invoked. Service={serviceName}, setManualDisableFlag={setManualDisableFlag}");

        // Set manual disable flag if requested
        if (setManualDisableFlag && onFlagSet != null)
        {
            _logger.Debug("Setting manual disable flag via callback.");
            onFlagSet();
        }

        try
        {
            using var service = new ServiceController(serviceName);
            service.Refresh();

            if (service.Status is ServiceControllerStatus.Stopped or ServiceControllerStatus.StopPending)
            {
                _logger.Info("VPN service already stopped; skipping stop.");
                if (!_isLaunchedByTask())
                {
                    showNotification("VPN Already Stopped", $"VPN service '{_config.TunnelName}' is already stopped.");
                }
                return;
            }

            try
            {
                if (service.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending)
                {
                    _logger.Debug($"Attempting to stop service '{serviceName}'. Current status: {service.Status}");
                    service.Stop();
                    service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                    _logger.Info("Service stop sequence completed (non-elevated path).");

                    if (!_isLaunchedByTask())
                    {
                        showNotification("VPN Stopped", $"VPN service '{_config.TunnelName}' stopped successfully.");
                    }
                    return;
                }
                else
                {
                    _logger.Warn($"Service '{serviceName}' in unexpected state {service.Status} when attempting stop.");
                }
            }
            catch (InvalidOperationException ex)
            {
                _logger.Warn($"Non-elevated stop failed for '{serviceName}', attempting elevation. Error: {ex.Message}");
                StopVpnWithElevation(showNotification);
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.Error($"StopVpnInternal: Service '{serviceName}' not found. {ex.Message}");
            if (!_isLaunchedByTask())
            {
                throw new InvalidOperationException($"VPN service not found.\n\nService name: {serviceName}\nTunnel name: {_config.TunnelName}\n\nError: {ex.Message}\n\nTry checking the service name in Windows Services.");
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            _logger.Warn($"Access error stopping service '{serviceName}'. Attempting elevation. Win32: {ex.Message}");
            StopVpnWithElevation(showNotification);
        }
    }

    private void StopVpnWithElevation(Action<string, string> showNotification)
    {
        var serviceName = $"WireGuardTunnel${_config.TunnelName}";
        _logger.Debug($"StopVpnWithElevation invoked for service '{serviceName}'.");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"stop {serviceName}",
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true
            };
            var process = Process.Start(psi);

            if (process != null)
            {
                _logger.Debug("Waiting for elevated stop process to exit.");
                process.WaitForExit();
                Thread.Sleep(1000);

                var status = GetVpnStatus();
                _logger.Info($"Post-elevation stop status: {status}");

                if (!_isLaunchedByTask())
                {
                    if (status == "Disconnected")
                    {
                        showNotification("VPN Stopped", $"VPN service '{_config.TunnelName}' stopped successfully.");
                    }
                    else
                    {
                        showNotification("VPN Stop Command Executed", $"Current status: {status}");
                    }
                }
            }
            else
            {
                _logger.Warn("Failed to start elevated process for service stop.");
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            if (ex.NativeErrorCode == 1223)
            {
                _logger.Warn("User cancelled UAC elevation for stop.");
            }
            else if (!_isLaunchedByTask())
            {
                _logger.Error($"Failed to stop VPN with elevation: {ex.Message}");
                throw new InvalidOperationException($"Failed to stop VPN with elevation: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Installs the VPN service (single UAC prompt using PowerShell script)
    /// </summary>
    public void InstallVpn(Action onSuccess, Action<string> onError)
    {
        _logger.Info("Initiating VPN service installation.");
        try
        {
            if (!File.Exists(_config.WgExe))
            {
                var msg = $"WireGuard executable not found at:\n{_config.WgExe}";
                _logger.Error(msg);
                onError(msg);
                return;
            }

            // Sync the internal config before installing
            try
            {
                _config.SyncInternalConfig();
                _logger.Info($"Synced internal config to: {_config.InternalConfigPath}");
            }
            catch (Exception ex)
            {
                var msg = $"Failed to sync internal config:\n{ex.Message}";
                _logger.Error(msg);
                onError(msg);
                return;
            }

            // Use the internal config path (always wg_wrap_tunnel.conf)
            var configPath = _config.InternalConfigPath;
            
            if (!File.Exists(configPath))
            {
                var msg = $"Internal config file not found at:\n{configPath}";
                _logger.Error(msg);
                onError(msg);
                return;
            }

            InstallVpnService(configPath, onSuccess, onError);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            if (ex.NativeErrorCode == 1223)
            {
                _logger.Warn("User cancelled UAC during installation.");
            }
            else
            {
                _logger.Error($"Failed to install VPN: {ex.Message}");
                onError($"Failed to install VPN: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to install VPN.", ex);
            onError($"Failed to install VPN: {ex.Message}");
        }
    }

    /// <summary>
    /// Uninstalls the VPN service
    /// </summary>
    public void UninstallVpn(Action onSuccess, Action<string> onError)
    {
        _logger.Info("Initiating VPN service uninstallation.");
        try
        {
            if (!File.Exists(_config.WgExe))
            {
                var msg = $"WireGuard executable not found at:\n{_config.WgExe}";
                _logger.Error(msg);
                onError(msg);
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = _config.WgExe,
                Arguments = $"/uninstalltunnelservice {_config.TunnelName}",
                UseShellExecute = true,
                Verb = "runas"
            };
            var process = Process.Start(psi);

            if (process != null)
            {
                _logger.Debug("Waiting for uninstall process to exit.");
                process.WaitForExit();
                WaitForServiceStatusChange(expectInstalled: false, maxWaitSeconds: 5);
                _logger.Info("VPN service uninstallation completed.");
                onSuccess();
            }
            else
            {
                _logger.Warn("Failed to start uninstallation process.");
                onError("Failed to launch uninstallation process.");
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            if (ex.NativeErrorCode == 1223)
            {
                _logger.Warn("User cancelled UAC during uninstallation.");
            }
            else
            {
                _logger.Error($"Failed to uninstall VPN: {ex.Message}");
                onError($"Failed to uninstall VPN: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to uninstall VPN.", ex);
            onError($"Failed to uninstall VPN: {ex.Message}");
        }
    }

    private void WaitForServiceStatusChange(bool expectInstalled, int maxWaitSeconds = 10)
    {
        var serviceName = $"WireGuardTunnel${_config.TunnelName}";
        _logger.Debug($"Waiting for service status change. Service={serviceName}, expectInstalled={expectInstalled}, timeout={maxWaitSeconds}s");
        var startTime = DateTime.Now;
        int successfulChecks = 0;
        const int requiredSuccessfulChecks = 2;

        while ((DateTime.Now - startTime).TotalSeconds < maxWaitSeconds)
        {
            Thread.Sleep(500);

            try
            {
                using var service = new ServiceController(serviceName);
                service.Refresh();
                var _ = service.Status;

                if (expectInstalled)
                {
                    successfulChecks++;
                    if (successfulChecks >= requiredSuccessfulChecks)
                    {
                        _logger.Debug("Service appears installed after consecutive checks.");
                        return;
                    }
                }
            }
            catch (InvalidOperationException)
            {
                if (!expectInstalled)
                {
                    successfulChecks++;
                    if (successfulChecks >= requiredSuccessfulChecks)
                    {
                        _logger.Debug("Service appears uninstalled after consecutive checks.");
                        return;
                    }
                }
                else
                {
                    successfulChecks = 0; // reset
                }
            }
            catch
            {
                successfulChecks = 0; // reset on transient errors
            }
        }
        _logger.Warn("Timeout waiting for service status change.");
    }

    private void SetServicePermissions()
    {
        var serviceName = $"WireGuardTunnel${_config.TunnelName}";
        _logger.Debug($"Setting service permissions for '{serviceName}'.");
        try
        {
            var getSDProcess = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"sdshow {serviceName}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            string currentSD = string.Empty;
            using (var proc = Process.Start(getSDProcess))
            {
                if (proc != null)
                {
                    currentSD = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit();
                }
            }

            if (string.IsNullOrEmpty(currentSD))
            {
                _logger.Warn("Failed to retrieve current security descriptor.");
                return;
            }

            string newPermission = "(A;;RPWPLCRC;;;AU)";

            if (!currentSD.Contains("AU"))
            {
                string newSD = currentSD.Replace("D:", $"D:{newPermission}");

                var setSDProcess = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = $"sdset {serviceName} \"{newSD}\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    CreateNoWindow = true
                };

                using var setProc = Process.Start(setSDProcess);
                setProc?.WaitForExit();
                _logger.Info("Service permissions updated to allow Authenticated Users control.");
            }
            else
            {
                _logger.Debug("Service descriptor already contains AU permissions; skipping modification.");
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to set service permissions: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets bandwidth transfer statistics for the VPN tunnel
    /// </summary>
    /// <returns>Tuple with (received bytes, sent bytes) or null if unavailable</returns>
    public (long received, long sent)? GetBandwidthStats()
    {
        try
        {
            // Use .NET NetworkInterface to get statistics without requiring elevation
            // WireGuard creates a network interface with the tunnel name
            var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
            
            foreach (var iface in interfaces)
            {
                // WireGuard interfaces typically contain the tunnel name in their description
                // or have a specific name pattern
                if (iface.Description.Contains(_config.TunnelName, StringComparison.OrdinalIgnoreCase) ||
                    iface.Name.Contains(_config.TunnelName, StringComparison.OrdinalIgnoreCase) ||
                    iface.Description.Contains("WireGuard", StringComparison.OrdinalIgnoreCase))
                {
                    // Check if this is likely the correct interface by verifying it's up
                    if (iface.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up)
                    {
                        var stats = iface.GetIPv4Statistics();
                        return (stats.BytesReceived, stats.BytesSent);
                    }
                }
            }

            _logger.Debug($"WireGuard interface '{_config.TunnelName}' not found or not operational.");
            return null;
        }
        catch (Exception ex)
        {
            _logger.Debug($"Error getting bandwidth stats: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Formats bytes into human-readable format (B, KB, MB, GB, TB)
    /// </summary>
    public static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }

        return $"{len:0.##} {sizes[order]}";
    }

    private void InstallVpnService(string configPath, Action onSuccess, Action<string> onError)
    {
        
        try
        {
            var installPsi = new ProcessStartInfo
            {
                FileName = _config.WgExe,
                Arguments = $"/installtunnelservice \"{configPath}\"",
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            
            var installProcess = Process.Start(installPsi);
            if (installProcess == null)
            {
                onError("Failed to start service installation process.");
                return;
            }
            
            installProcess.WaitForExit();
            if (installProcess.ExitCode != 0)
            {
                onError($"Service installation failed with exit code {installProcess.ExitCode}");
                return;
            }
            
            _logger.Info("Service installed successfully. Waiting for service registration...");
            WaitForServiceStatusChange(expectInstalled: true, maxWaitSeconds: 5);
            
            // Step 2: Set service permissions (requires UAC)
            SetServicePermissions();
            
            onSuccess();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            if (ex.NativeErrorCode == 1223)
            {
                _logger.Warn("User cancelled UAC during fallback installation.");
                onError("Installation cancelled by user.");
            }
            else
            {
                _logger.Error($"Failed to install VPN using fallback method: {ex.Message}");
                onError($"Failed to install VPN: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to install VPN using fallback method.", ex);
            onError($"Failed to install VPN: {ex.Message}");
        }
    }
}
