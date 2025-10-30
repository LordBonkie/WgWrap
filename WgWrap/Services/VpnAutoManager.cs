using WgWrap.Core.Configuration;
using WgWrap.Core.Logging;

namespace WgWrap.Services;

/// <summary>
/// Manages automatic VPN start/stop based on network conditions
/// </summary>
internal class VpnAutoManager
{
    private readonly ConfigurationManager _config;
    private readonly VpnServiceManager _vpnManager;
    private readonly NetworkManager _networkManager;
    private readonly ManualDisableTracker _disableTracker;
    private readonly string _flagFile;
    private readonly Logger _logger;

    public VpnAutoManager(
        ConfigurationManager config,
        VpnServiceManager vpnManager,
        NetworkManager networkManager,
        ManualDisableTracker disableTracker,
        Logger logger)
    {
        _config = config;
        _vpnManager = vpnManager;
        _networkManager = networkManager;
        _disableTracker = disableTracker;
        _logger = logger;
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var dataDir = Path.Combine(appDir, "data");
        Directory.CreateDirectory(dataDir); // Ensure data directory exists
        _flagFile = Path.Combine(dataDir, "wg_status.flag");
    }

    /// <summary>
    /// Automatically manages VPN based on current network
    /// </summary>
    public void AutoManageVpn()
    {
        var ssid = _networkManager.GetSsid();
        var status = _vpnManager.GetVpnStatus();
        _logger.Debug($"AutoManageVpn invoked. Current SSID='{ssid}', VPN Status='{status}', ManualDisabled={_disableTracker.IsManuallyDisabled}");

        // Only auto-manage if the service is installed
        if (status != "Not Installed")
        {
            bool isTrustedSsid = _config.TrustedSsids.Contains(ssid, StringComparer.OrdinalIgnoreCase);
            bool isTrustedIp = _networkManager.IsOnTrustedIpNetwork(_config.TrustedIpRanges, _config.ExcludedNetworkAdapters);
            bool isTrusted = isTrustedSsid || isTrustedIp;
            
            _logger.Debug($"IsTrustedSsid={isTrustedSsid}, IsTrustedIp={isTrustedIp}, IsTrusted={isTrusted}. " +
                         $"Trusted SSID count={_config.TrustedSsids.Length}, Trusted IP ranges count={_config.TrustedIpRanges.Length}");

            if (isTrusted)
            {
                if (status == "Connected")
                {
                    _logger.Info($"Trusted network detected (SSID={isTrustedSsid}, IP={isTrustedIp}); stopping VPN (auto).");
                    _vpnManager.StopVpnInternal(setManualDisableFlag: false, (_, _) => { });
                }
                else
                {
                    _logger.Debug("Trusted network detected; VPN already not connected or in non-running state.");
                }
            }
            else
            {
                if (status == "Disconnected" && !_disableTracker.IsManuallyDisabled)
                {
                    _logger.Info("Untrusted network detected; starting VPN (auto).");
                    _vpnManager.StartVpnInternal(clearManualDisableFlag: false, (_, _) => { });
                }
                else if (status == "Disconnected" && _disableTracker.IsManuallyDisabled)
                {
                    _logger.Debug("VPN is disconnected and manual disable flag is set; skipping auto-start.");
                }
                else
                {
                    _logger.Debug("Untrusted network detected; VPN already connected or in transition.");
                }
            }
        }
        else
        {
            _logger.Warn("AutoManageVpn: Service not installed; skipping auto-management.");
        }

        // Update flag file
        UpdateFlagFile(ssid);
    }

    private void UpdateFlagFile(string ssid)
    {
        try
        {
            var currentStatus = _vpnManager.GetVpnStatus();
            var content = $"SSID: {ssid}\nVPN: {currentStatus}";
            if (_disableTracker.IsManuallyDisabled)
            {
                content += "\nAuto-start: Disabled (manually)";
            }
            File.WriteAllText(_flagFile, content);
            _logger.Debug("Flag file updated with current status.");
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to update flag file: {ex.Message}");
        }
    }

    /// <summary>
    /// Ensures the flag file exists
    /// </summary>
    public void EnsureFlagFileExists()
    {
        try
        {
            var dir = Path.GetDirectoryName(_flagFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
                _logger.Debug("Created directory for flag file.");
            }
            if (!File.Exists(_flagFile))
            {
                File.WriteAllText(_flagFile, string.Empty);
                _logger.Debug("Created initial empty flag file.");
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to ensure flag file existence: {ex.Message}");
        }
    }

    public string GetFlagFilePath() => _flagFile;
}
