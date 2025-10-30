using WgWrap.Core.Configuration;
using WgWrap.Services;
using WgWrap.UI.Forms;

namespace WgWrap.UI.TrayIcon;

/// <summary>
/// Manages the system tray icon and context menu
/// </summary>
internal class TrayIconManager : IDisposable
{
    private NotifyIcon? _icon;
    private ToolStripMenuItem? _startVpnItem;
    private ToolStripMenuItem? _stopVpnItem;
    private StatusForm? _statusForm;

    private readonly ConfigurationManager _config;
    private readonly VpnServiceManager _vpnManager;
    private readonly NetworkManager _networkManager;
    private readonly TaskSchedulerManager _taskManager;
    private readonly ManualDisableTracker _disableTracker;
    private readonly VpnAutoManager _autoManager;
    
    private readonly Action _onStartVpn;
    private readonly Action _onStopVpn;
    private readonly Action _onInstallService;
    private readonly Action _onUninstallService;
    private readonly Action _onInstallTask;
    private readonly Action _onUninstallTask;
    private readonly Action _onShowSettings;
    private readonly Action _onExit;

    public TrayIconManager(
        ConfigurationManager config,
        VpnServiceManager vpnManager,
        NetworkManager networkManager,
        TaskSchedulerManager taskManager,
        ManualDisableTracker disableTracker,
        VpnAutoManager autoManager,
        Action onStartVpn,
        Action onStopVpn,
        Action onInstallService,
        Action onUninstallService,
        Action onInstallTask,
        Action onUninstallTask,
        Action onShowSettings,
        Action onExit)
    {
        _config = config;
        _vpnManager = vpnManager;
        _networkManager = networkManager;
        _taskManager = taskManager;
        _disableTracker = disableTracker;
        _autoManager = autoManager;
        _onStartVpn = onStartVpn;
        _onStopVpn = onStopVpn;
        _onInstallService = onInstallService;
        _onUninstallService = onUninstallService;
        _onInstallTask = onInstallTask;
        _onUninstallTask = onUninstallTask;
        _onShowSettings = onShowSettings;
        _onExit = onExit;
    }

    public void Initialize()
    {
        _icon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            Text = "WireGuard Tray"
        };
        
        _icon.DoubleClick += (_, _) => ShowStatus();
        
        BuildMenu();
        UpdateIcon();
        UpdateMenuItems();
    }

    public void RebuildMenu()
    {
        if (_icon == null) return;
        
        var oldMenu = _icon.ContextMenuStrip;
        BuildMenu();
        UpdateMenuItems();
        oldMenu?.Dispose();
    }

    private void BuildMenu()
    {
        var menu = new ContextMenuStrip();

        // Auto-start status indicator
        if (_disableTracker.IsManuallyDisabled)
        {
            var disabledIndicator = new ToolStripMenuItem("⏸ Auto-start Disabled")
            {
                Enabled = false,
                ForeColor = System.Drawing.Color.Red
            };
            menu.Items.Add(disabledIndicator);
        }
        else
        {
            var enabledIndicator = new ToolStripMenuItem("▶ Auto-start Enabled")
            {
                Enabled = false,
                ForeColor = System.Drawing.Color.Green
            };
            menu.Items.Add(enabledIndicator);
        }
        menu.Items.Add(new ToolStripSeparator());

        // VPN Control
        _startVpnItem = new ToolStripMenuItem("Start VPN");
        _startVpnItem.Click += (_, _) => _onStartVpn();
        menu.Items.Add(_startVpnItem);

        _stopVpnItem = new ToolStripMenuItem("Stop VPN");
        _stopVpnItem.Click += (_, _) => _onStopVpn();
        menu.Items.Add(_stopVpnItem);

        menu.Items.Add(new ToolStripSeparator());


        // Status
        var statusItem = menu.Items.Add("Show Status");
        statusItem.Click += (_, _) => ShowStatus();


        if (_icon != null)
        {
            _icon.ContextMenuStrip = menu;
        }
    }

    public void ShowStatus()
    {
        if (_statusForm == null || _statusForm.IsDisposed)
        {
            _statusForm = new StatusForm(
                _config,
                _vpnManager,
                _networkManager,
                _taskManager,
                _disableTracker,
                _autoManager,
                _onStartVpn,
                _onStopVpn,
                _onInstallService,
                _onUninstallService,
                _onInstallTask,
                _onUninstallTask,
                _onShowSettings,
                _onExit
            );
        }
        
        _statusForm.RefreshStatus();
        _statusForm.Show();
        _statusForm.BringToFront();
        _statusForm.Activate();
    }

    public void UpdateIcon()
    {
        if (_icon == null) return;
        
        var ssid = _networkManager.GetSsid();
        var status = _vpnManager.GetVpnStatus();
        bool isTrustedSsid = _config.TrustedSsids.Any(t => t.Equals(ssid, StringComparison.OrdinalIgnoreCase));
        bool isTrustedIp = _networkManager.IsOnTrustedIpNetwork(_config.TrustedIpRanges);
        bool isOnTrustedNetwork = isTrustedSsid || isTrustedIp;
        
        string tooltip = $"SSID: {ssid}\nVPN: {status}";
        
        if (isOnTrustedNetwork)
        {
            tooltip += "\nTrusted network (VPN disabled)";
        }

        // Get icon color using centralized logic
        var iconColor = IconGenerator.GetIconColor(status, _disableTracker.IsManuallyDisabled, isOnTrustedNetwork);

        _icon.Icon = IconGenerator.CreateColoredIcon(iconColor);
        _icon.Text = tooltip.Length > 63 ? tooltip.Substring(0, 63) : tooltip;
        
        // Update status form if open
        if (_statusForm != null && !_statusForm.IsDisposed && _statusForm.Visible)
        {
            _statusForm.RefreshStatus();
        }
    }


    public void UpdateMenuItems()
    {
        if (_startVpnItem == null || _stopVpnItem == null)
            return;

        var status = _vpnManager.GetVpnStatus();

        bool serviceInstalled = status != "Not Installed";
        bool vpnRunning = status == "Connected";
        bool unknownState = status.StartsWith("Unknown") || status.StartsWith("Status:");

        _startVpnItem.Enabled = serviceInstalled && (!vpnRunning || unknownState);
        _stopVpnItem.Enabled = serviceInstalled && (vpnRunning || unknownState);
        
        // Update status form if open
        if (_statusForm != null && !_statusForm.IsDisposed && _statusForm.Visible)
        {
            _statusForm.RefreshStatus();
        }
    }

    /// <summary>
    /// Refreshes the settings UI in the status form if it's open
    /// </summary>
    public void RefreshStatusFormSettings()
    {
        if (_statusForm != null && !_statusForm.IsDisposed && _statusForm.Visible)
        {
            _statusForm.RefreshSettingsUI();
        }
    }

    public void ShowNotification(string title, string message)
    {
        if (_icon == null) return;

        try
        {
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = message;
            _icon.BalloonTipIcon = title.Contains("Start") || title.Contains("success")
                ? ToolTipIcon.Info
                : title.Contains("Stop") || title.Contains("Already")
                    ? ToolTipIcon.Warning
                    : ToolTipIcon.Info;

            _icon.ShowBalloonTip(3000);
        }
        catch
        {
            // Silently ignore notification failures
        }
    }

    public void Hide()
    {
        if (_icon != null)
        {
            _icon.Visible = false;
        }
    }

    public void Dispose()
    {
        if (_icon != null)
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
        
        if (_statusForm != null && !_statusForm.IsDisposed)
        {
            _statusForm.Close();
            _statusForm.Dispose();
        }
    }
}

