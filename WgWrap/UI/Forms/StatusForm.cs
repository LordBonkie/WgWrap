using WgWrap.Core.Configuration;
using WgWrap.Services;

namespace WgWrap.UI.Forms;

/// <summary>
/// Status window displaying VPN state with orb indicator and action buttons
/// </summary>
internal class StatusForm : Form
{
    private readonly ConfigurationManager _config;
    private readonly VpnServiceManager _vpnManager;
    private readonly NetworkManager _networkManager;
    private readonly TaskSchedulerManager _taskManager;
    private readonly ManualDisableTracker _disableTracker;
    private readonly VpnAutoManager _autoManager;
    private readonly StartupManager _startupManager;
    
    private readonly Action _onStartVpn;
    private readonly Action _onStopVpn;
    private readonly Action _onInstallService;
    private readonly Action _onUninstallService;
    private readonly Action _onInstallTask;
    private readonly Action _onUninstallTask;
    private readonly Action _onShowSettings;
    private readonly Action _onExit;

    private Panel? _orbPanel;
    private Label? _statusLabel;
    private Label? _ssidLabel;
    private Label? _autoStartLabel;
    private Label? _trustedNetworkLabel;
    private Label? _nextCheckLabelText;
    private Label? _nextCheckLabel;
    private Label? _taskStatusLabel;
    private Label? _bandwidthReceivedLabel;
    private Label? _bandwidthSentLabel;
    private Label? _configStatusLabel;
    
    // Interface configuration labels
    private Label? _interfacePrivateKeyLabel;
    private Label? _interfaceAddressLabel;
    private Label? _interfaceDnsLabel;
    private Label? _interfaceMtuLabel;
    
    // Peer configuration labels
    private Label? _peerPublicKeyLabel;
    private Label? _peerPresharedKeyLabel;
    private Label? _peerAllowedIpsLabel;
    private Label? _peerEndpointLabel;
    private Label? _peerKeepaliveLabel;
    
    // Menu items
    private ToolStripMenuItem? _startMenuItem;
    private ToolStripMenuItem? _stopMenuItem;
    private ToolStripMenuItem? _installServiceMenuItem;
    private ToolStripMenuItem? _uninstallServiceMenuItem;
    private ToolStripMenuItem? _installTaskMenuItem;
    private ToolStripMenuItem? _uninstallTaskMenuItem;
    private ToolStripMenuItem? _activateAutoStartMenuItem;
    private ToolStripMenuItem? _deactivateAutoStartMenuItem;
    
    // Settings tab controls
    private TextBox? _txtConfigPath;
    private TextBox? _txtWgExe;
    private TextBox? _txtTrustedSsids;
    private TextBox? _txtTrustedIpRanges;
    private CheckBox? _chkTimerEnabled;
    private TextBox? _txtTimerInterval;
    private Button? _btnBrowseConfig;
    private Button? _btnBrowseWgExe;
    private Button? _btnSaveSettings;
    private Label? _warningLabel;
    private System.Windows.Forms.Timer? _countdownTimer;
    private System.Windows.Forms.Timer? _statsRefreshTimer;

    public StatusForm(
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
        _startupManager = new StartupManager();
        _onStartVpn = onStartVpn;
        _onStopVpn = onStopVpn;
        _onInstallService = onInstallService;
        _onUninstallService = onUninstallService;
        _onInstallTask = onInstallTask;
        _onUninstallTask = onUninstallTask;
        _onShowSettings = onShowSettings;
        _onExit = onExit;

        InitializeComponents();
        RefreshStatus();
        
        // Wire up form events for stats refresh timer management
        this.Activated += StatusForm_Activated;
        this.Deactivate += StatusForm_Deactivate;
        this.Shown += StatusForm_Shown;
    }

    private void InitializeComponents()
    {
        Text = "WireGuard";
        Size = new Size(600, 540);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        BackColor = Color.White;

        // Create menu bar
        var menuStrip = new MenuStrip();
        
        // VPN Control menu
        var vpnMenu = new ToolStripMenuItem("VPN");
        _startMenuItem = new ToolStripMenuItem("Activate", null, (s, e) => { _onStartVpn(); RefreshStatus(); });
        _stopMenuItem = new ToolStripMenuItem("Deactivate", null, (s, e) => { _onStopVpn(); RefreshStatus(); });
        vpnMenu.DropDownItems.Add(_startMenuItem);
        vpnMenu.DropDownItems.Add(_stopMenuItem);
        menuStrip.Items.Add(vpnMenu);
        
        // System Setup menu
        var systemSetupMenu = new ToolStripMenuItem("System Setup");
        
        // Service submenu items
        _installServiceMenuItem = new ToolStripMenuItem("Install VPN Service", null, (s, e) => { _onInstallService(); RefreshStatus(); });
        _uninstallServiceMenuItem = new ToolStripMenuItem("Uninstall VPN Service", null, (s, e) => { _onUninstallService(); RefreshStatus(); });
        systemSetupMenu.DropDownItems.Add(_installServiceMenuItem);
        systemSetupMenu.DropDownItems.Add(_uninstallServiceMenuItem);
        
        // Separator
        systemSetupMenu.DropDownItems.Add(new ToolStripSeparator());
        
        // Task submenu items
        _installTaskMenuItem = new ToolStripMenuItem("Install Network Monitor Task", null, (s, e) => { _onInstallTask(); RefreshStatus(); });
        _uninstallTaskMenuItem = new ToolStripMenuItem("Uninstall Network Monitor Task", null, (s, e) => { _onUninstallTask(); RefreshStatus(); });
        systemSetupMenu.DropDownItems.Add(_installTaskMenuItem);
        systemSetupMenu.DropDownItems.Add(_uninstallTaskMenuItem);
        
        menuStrip.Items.Add(systemSetupMenu);
        
        // Configuration menu
        var configMenu = new ToolStripMenuItem("Configuration");
        var editConfigMenuItem = new ToolStripMenuItem("Edit Config", null, (s, e) => OpenConfigEditor());
        configMenu.DropDownItems.Add(editConfigMenuItem);
        menuStrip.Items.Add(configMenu);
        
        // Start with Windows menu
        var startWithWindowsMenu = new ToolStripMenuItem("Start with Windows");
        _activateAutoStartMenuItem = new ToolStripMenuItem("Activate", null, (s, e) => { EnableAutoStart(); RefreshStatus(); });
        _deactivateAutoStartMenuItem = new ToolStripMenuItem("Deactivate", null, (s, e) => { DisableAutoStart(); RefreshStatus(); });
        startWithWindowsMenu.DropDownItems.Add(_activateAutoStartMenuItem);
        startWithWindowsMenu.DropDownItems.Add(_deactivateAutoStartMenuItem);
        menuStrip.Items.Add(startWithWindowsMenu);
        
        // Exit button (right-aligned)
        var exitButton = new ToolStripButton("Shutdown application")
        {
            Alignment = ToolStripItemAlignment.Right
        };
        exitButton.Click += (s, e) => _onExit();
        menuStrip.Items.Add(exitButton);
        
        // Version label (right-aligned, non-clickable)
        var versionLabel = new ToolStripLabel($"v{Program.Version}")
        {
            Alignment = ToolStripItemAlignment.Right,
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 9, FontStyle.Italic)
        };
        menuStrip.Items.Add(versionLabel);

        Controls.Add(menuStrip);

        // Create tab control
        var tabControl = new TabControl
        {
            Location = new Point(0, menuStrip.Height),
            Size = new Size(600, 540 - menuStrip.Height),
            Appearance = TabAppearance.FlatButtons,
            SizeMode = TabSizeMode.Fixed,
            ItemSize = new Size(100, 30)
        };

        var statusTab = new TabPage("Status") { BackColor = Color.White };
        var settingsTab = new TabPage("Settings") { BackColor = Color.White };
        tabControl.TabPages.Add(statusTab);
        tabControl.TabPages.Add(settingsTab);
        Controls.Add(tabControl);

        // ===== STATUS TAB =====
        var statusPanel = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(580, 430),
            AutoScroll = true,
            BackColor = Color.White,
            Padding = new Padding(20, 20, 20, 60)
        };
        statusTab.Controls.Add(statusPanel);

        // Tunnel name label
        // var tunnelLabel = new Label
        // {
        //     Text = _config.TunnelName,
        //     Location = new Point(20, 15),
        //     Size = new Size(200, 25),
        //     Font = new Font(Font.FontFamily, 11, FontStyle.Bold),
        //     ForeColor = Color.FromArgb(64, 64, 64)
        // };
        // statusPanel.Controls.Add(tunnelLabel);

        // Status indicator with orb (aligned to left)
        _orbPanel = new Panel
        {
            Location = new Point(20, 12),
            Size = new Size(30, 30)
        };
        _orbPanel.Paint += OrbPanel_Paint;
        statusPanel.Controls.Add(_orbPanel);

        _statusLabel = new Label
        {
            Location = new Point(55, 15),
            Size = new Size(150, 25),
            Font = new Font(Font.FontFamily, 10, FontStyle.Bold),
            Text = "Checking..."
        };
        statusPanel.Controls.Add(_statusLabel);

        // Auto-start indicator
        _autoStartLabel = new Label
        {
            Location = new Point(210, 15),
            Size = new Size(150, 25),
            Font = new Font(Font.FontFamily, 9, FontStyle.Regular),
            ForeColor = Color.Gray,
            Text = "Auto-start: Unknown"
        };
        statusPanel.Controls.Add(_autoStartLabel);


        _configStatusLabel = new Label
        {
            Location = new Point(20, 45),
            Size = new Size(440, 20),
            Font = new Font(Font.FontFamily, 8, FontStyle.Regular),
            ForeColor = Color.Gray,
            Text = ""
        };
        statusPanel.Controls.Add(_configStatusLabel);

        // Update config status indicator
        UpdateConfigStatusIndicator();

        // Details section with grid-like layout
        int yPos = 75;
        
        AddDetailRow(statusPanel, "SSID:", ref _ssidLabel, yPos);
        yPos += 35;

        // Trusted network status
        var trustedLabel = new Label
        {
            Text = "Trusted Network:",
            Location = new Point(20, yPos),
            Size = new Size(120, 25),
            Font = new Font(Font.FontFamily, 9, FontStyle.Regular),
            ForeColor = Color.FromArgb(100, 100, 100),
            TextAlign = ContentAlignment.MiddleLeft
        };
        statusPanel.Controls.Add(trustedLabel);

        _trustedNetworkLabel = new Label
        {
            Location = new Point(145, yPos),
            Size = new Size(415, 25),
            Font = new Font(Font.FontFamily, 9, FontStyle.Regular),
            ForeColor = Color.FromArgb(64, 64, 64),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "Checking..."
        };
        statusPanel.Controls.Add(_trustedNetworkLabel);
        yPos += 35;

        // Next check countdown (always create, but visibility controlled dynamically)
        bool initialTimerState = Program.IsTimerEnabled();
        
        _nextCheckLabelText = new Label
        {
            Text = "Next Check:",
            Location = new Point(20, yPos),
            Size = new Size(120, 25),
            Font = new Font(Font.FontFamily, 9, FontStyle.Regular),
            ForeColor = Color.FromArgb(100, 100, 100),
            TextAlign = ContentAlignment.MiddleLeft,
            Visible = initialTimerState
        };
        statusPanel.Controls.Add(_nextCheckLabelText);

        _nextCheckLabel = new Label
        {
            Location = new Point(145, yPos),
            Size = new Size(415, 25),
            Font = new Font(Font.FontFamily, 9, FontStyle.Regular),
            ForeColor = Color.FromArgb(64, 64, 64),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "Calculating...",
            Visible = initialTimerState
        };
        statusPanel.Controls.Add(_nextCheckLabel);
        yPos += 35;

        // Task status
        var taskStatusLabelText = new Label
        {
            Text = "Monitor Task:",
            Location = new Point(20, yPos),
            Size = new Size(120, 25),
            Font = new Font(Font.FontFamily, 9, FontStyle.Regular),
            ForeColor = Color.FromArgb(100, 100, 100),
            TextAlign = ContentAlignment.MiddleLeft
        };
        statusPanel.Controls.Add(taskStatusLabelText);

        _taskStatusLabel = new Label
        {
            Location = new Point(145, yPos),
            Size = new Size(415, 25),
            Font = new Font(Font.FontFamily, 9, FontStyle.Regular),
            ForeColor = Color.FromArgb(64, 64, 64),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "Checking..."
        };
        statusPanel.Controls.Add(_taskStatusLabel);
        yPos += 35;

        // Bandwidth statistics
        var bandwidthHeaderLabel = new Label
        {
            Text = "Transfer Statistics:",
            Location = new Point(20, yPos),
            Size = new Size(540, 20),
            Font = new Font(Font.FontFamily, 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(80, 80, 80)
        };
        statusPanel.Controls.Add(bandwidthHeaderLabel);
        yPos += 25;

        // Received bandwidth
        var bandwidthReceivedLabelText = new Label
        {
            Text = "Downloaded:",
            Location = new Point(35, yPos),
            Size = new Size(105, 25),
            Font = new Font(Font.FontFamily, 9, FontStyle.Regular),
            ForeColor = Color.FromArgb(100, 100, 100),
            TextAlign = ContentAlignment.MiddleLeft
        };
        statusPanel.Controls.Add(bandwidthReceivedLabelText);

        _bandwidthReceivedLabel = new Label
        {
            Location = new Point(145, yPos),
            Size = new Size(415, 25),
            Font = new Font(Font.FontFamily, 9, FontStyle.Regular),
            ForeColor = Color.FromArgb(64, 64, 64),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "N/A"
        };
        statusPanel.Controls.Add(_bandwidthReceivedLabel);
        yPos += 25;

        // Sent bandwidth
        var bandwidthSentLabelText = new Label
        {
            Text = "Uploaded:",
            Location = new Point(35, yPos),
            Size = new Size(105, 25),
            Font = new Font(Font.FontFamily, 9, FontStyle.Regular),
            ForeColor = Color.FromArgb(100, 100, 100),
            TextAlign = ContentAlignment.MiddleLeft
        };
        statusPanel.Controls.Add(bandwidthSentLabelText);

        _bandwidthSentLabel = new Label
        {
            Location = new Point(145, yPos),
            Size = new Size(415, 25),
            Font = new Font(Font.FontFamily, 9, FontStyle.Regular),
            ForeColor = Color.FromArgb(64, 64, 64),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = "N/A"
        };
        statusPanel.Controls.Add(_bandwidthSentLabel);
        yPos += 35;

        // Start countdown timer to update the display
        _countdownTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _countdownTimer.Tick += (_, _) => UpdateCountdown();
        if (initialTimerState)
        {
            _countdownTimer.Start();
        }
        
        // Initialize stats refresh timer (updates every 3 seconds)
        _statsRefreshTimer = new System.Windows.Forms.Timer { Interval = 3000 };
        _statsRefreshTimer.Tick += (_, _) => RefreshStatistics();
        
        // [Interface] section
        var interfaceLabel = new Label
        {
            Text = "[Interface]",
            Location = new Point(20, yPos),
            Size = new Size(540, 22),
            Font = new Font(Font.FontFamily, 10, FontStyle.Bold),
            ForeColor = Color.FromArgb(64, 64, 64)
        };
        statusPanel.Controls.Add(interfaceLabel);
        yPos += 28;

        // Interface details in proper order
        AddConfigRow(statusPanel, "PrivateKey", ref _interfacePrivateKeyLabel, yPos);
        yPos += 22;
        AddConfigRow(statusPanel, "Address", ref _interfaceAddressLabel, yPos);
        yPos += 22;
        AddConfigRow(statusPanel, "DNS", ref _interfaceDnsLabel, yPos);
        yPos += 22;
        AddConfigRow(statusPanel, "MTU", ref _interfaceMtuLabel, yPos);
        yPos += 35;

        // [Peer] section
        var peerLabel = new Label
        {
            Text = "[Peer]",
            Location = new Point(20, yPos),
            Size = new Size(540, 22),
            Font = new Font(Font.FontFamily, 10, FontStyle.Bold),
            ForeColor = Color.FromArgb(64, 64, 64)
        };
        statusPanel.Controls.Add(peerLabel);
        yPos += 28;

        // Peer details in proper order
        AddConfigRow(statusPanel, "PublicKey", ref _peerPublicKeyLabel, yPos);
        yPos += 22;
        AddConfigRow(statusPanel, "PresharedKey", ref _peerPresharedKeyLabel, yPos);
        yPos += 22;
        
        // AllowedIPs - use taller label for multi-line content
        var allowedIpsKeyLabel = new Label
        {
            Text = "AllowedIPs",
            Location = new Point(35, yPos),
            Size = new Size(110, 20),
            Font = new Font("Consolas", 9, FontStyle.Regular),
            ForeColor = Color.FromArgb(100, 100, 100),
            TextAlign = ContentAlignment.TopLeft
        };
        statusPanel.Controls.Add(allowedIpsKeyLabel);
        
        var allowedIpsEquals = new Label
        {
            Text = "=",
            Location = new Point(145, yPos),
            Size = new Size(15, 20),
            Font = new Font("Consolas", 9, FontStyle.Regular),
            ForeColor = Color.FromArgb(100, 100, 100),
            TextAlign = ContentAlignment.TopLeft
        };
        statusPanel.Controls.Add(allowedIpsEquals);
        
        _peerAllowedIpsLabel = new Label
        {
            Location = new Point(160, yPos),
            Size = new Size(400, 60),
            Font = new Font("Consolas", 9, FontStyle.Regular),
            ForeColor = Color.FromArgb(64, 64, 64),
            TextAlign = ContentAlignment.TopLeft,
            AutoSize = false
        };
        statusPanel.Controls.Add(_peerAllowedIpsLabel);
        yPos += 65;
        
        AddConfigRow(statusPanel, "Endpoint", ref _peerEndpointLabel, yPos);
        yPos += 22;
        AddConfigRow(statusPanel, "PersistentKeepalive", ref _peerKeepaliveLabel, yPos);
        yPos += 35;


        // ===== SETTINGS TAB =====
        var settingsPanel = new Panel
        {
            Location = new Point(0, 0),
            Size = new Size(580, 430),
            AutoScroll = true,
            BackColor = Color.White,
            Padding = new Padding(20, 20, 20, 60)
        };
        settingsTab.Controls.Add(settingsPanel);

        int settY = 20;
        bool serviceInstalled = _vpnManager.GetVpnStatus() != "Not Installed";

        // Warning label about configuration conflicts with WireGuard GUI
        var conflictWarningLabel = new Label
        {
            Text = "⚠ WARNING: Do NOT use a config file already active in the WireGuard application!\nThis will cause conflicts. Use the config in WgWrap OR WireGuard GUI, never both.",
            Location = new Point(20, settY),
            Size = new Size(540, 40),
            ForeColor = Color.FromArgb(200, 0, 0),
            Font = new Font(Font.FontFamily, 9, FontStyle.Bold),
            BackColor = Color.FromArgb(255, 245, 245)
        };
        settingsPanel.Controls.Add(conflictWarningLabel);
        settY += 50;

        // Warning label if service is installed
        if (serviceInstalled)
        {
            _warningLabel = new Label
            {
                Text = "⚠ Configuration is locked while VPN service is installed.\nUninstall the service first to modify tunnel settings.",
                Location = new Point(20, settY),
                Size = new Size(540, 40),
                ForeColor = Color.FromArgb(220, 100, 0),
                Font = new Font(Font.FontFamily, 9, FontStyle.Bold)
            };
            settingsPanel.Controls.Add(_warningLabel);
            settY += 50;
        }

        // Config Path (tunnel name will be derived from filename)
        AddSettingsLabel(settingsPanel, "Config Path:", settY);
        
        _txtConfigPath = new TextBox
        {
            Location = new Point(150, settY),
            Size = new Size(330, 23),
            Text = _config.OriginalConfigPath,
            Enabled = !serviceInstalled
        };
        settingsPanel.Controls.Add(_txtConfigPath);
        
        _btnBrowseConfig = new Button
        {
            Text = "Browse",
            Location = new Point(490, settY - 2),
            Size = new Size(70, 27),
            FlatStyle = FlatStyle.System,
            Enabled = !serviceInstalled
        };
        _btnBrowseConfig.Click += BtnBrowseConfig_Click;
        settingsPanel.Controls.Add(_btnBrowseConfig);
        settY += 35;

        // WireGuard Exe
        AddSettingsLabel(settingsPanel, "WireGuard Exe:", settY);
        _txtWgExe = new TextBox
        {
            Location = new Point(150, settY),
            Size = new Size(330, 23),
            Text = _config.WgExe,
            Enabled = !serviceInstalled
        };
        settingsPanel.Controls.Add(_txtWgExe);
        
        _btnBrowseWgExe = new Button
        {
            Text = "Browse",
            Location = new Point(490, settY - 2),
            Size = new Size(70, 27),
            FlatStyle = FlatStyle.System,
            Enabled = !serviceInstalled
        };
        _btnBrowseWgExe.Click += BtnBrowseWgExe_Click;
        settingsPanel.Controls.Add(_btnBrowseWgExe);
        settY += 35;

        // Trusted SSIDs
        AddSettingsLabel(settingsPanel, "Trusted SSIDs:", settY);
        _txtTrustedSsids = new TextBox
        {
            Location = new Point(150, settY),
            Size = new Size(410, 80),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Text = string.Join(Environment.NewLine, _config.TrustedSsids)
        };
        settingsPanel.Controls.Add(_txtTrustedSsids);
        settY += 90;

        // Help text for SSIDs
        var helpSsidLabel = new Label
        {
            Text = "Enter one SSID per line (WiFi networks where VPN should be disabled)",
            Location = new Point(150, settY),
            Size = new Size(410, 20),
            ForeColor = Color.Gray,
            Font = new Font(Font.FontFamily, 8)
        };
        settingsPanel.Controls.Add(helpSsidLabel);
        settY += 30;

        // Trusted IP Ranges
        AddSettingsLabel(settingsPanel, "Trusted IP Ranges:", settY);
        _txtTrustedIpRanges = new TextBox
        {
            Location = new Point(150, settY),
            Size = new Size(410, 80),
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            Text = string.Join(Environment.NewLine, _config.TrustedIpRanges)
        };
        settingsPanel.Controls.Add(_txtTrustedIpRanges);
        settY += 90;

        // Help text for IP ranges
        var helpIpLabel = new Label
        {
            Text = "Enter one IP/mask per line in CIDR format (e.g., 192.168.1.0/24 for Ethernet)",
            Location = new Point(150, settY),
            Size = new Size(410, 20),
            ForeColor = Color.Gray,
            Font = new Font(Font.FontFamily, 8)
        };
        settingsPanel.Controls.Add(helpIpLabel);
        settY += 30;

        // Timer enabled checkbox
        _chkTimerEnabled = new CheckBox
        {
            Text = "Enable automatic network checks",
            Location = new Point(150, settY),
            Size = new Size(410, 25),
            Checked = _config.TimerEnabled
        };
        _chkTimerEnabled.CheckedChanged += (_, _) =>
        {
            if (_txtTimerInterval != null)
                _txtTimerInterval.Enabled = _chkTimerEnabled.Checked;
        };
        settingsPanel.Controls.Add(_chkTimerEnabled);
        settY += 35;

        // Timer interval
        AddSettingsLabel(settingsPanel, "Check Interval (sec):", settY);
        _txtTimerInterval = new TextBox
        {
            Location = new Point(150, settY),
            Size = new Size(100, 23),
            Text = _config.TimerIntervalSeconds.ToString(),
            Enabled = _config.TimerEnabled
        };
        settingsPanel.Controls.Add(_txtTimerInterval);

        var intervalHelp = new Label
        {
            Text = "(10-3600 seconds, default: 30)",
            Location = new Point(260, settY + 3),
            Size = new Size(300, 20),
            ForeColor = Color.Gray,
            Font = new Font(Font.FontFamily, 8)
        };
        settingsPanel.Controls.Add(intervalHelp);
        settY += 40;


        // Save button
        _btnSaveSettings = new Button
        {
            Text = "Save Settings",
            Location = new Point(470, settY),
            Size = new Size(90, 30),
            FlatStyle = FlatStyle.System
        };
        _btnSaveSettings.Click += BtnSaveSettings_Click;
        settingsPanel.Controls.Add(_btnSaveSettings);
    }

    private void AddSettingsLabel(Panel panel, string text, int y)
    {
        var label = new Label
        {
            Text = text,
            Location = new Point(20, y + 3),
            Size = new Size(120, 20),
            Font = new Font(Font.FontFamily, 9),
            ForeColor = Color.FromArgb(64, 64, 64),
            TextAlign = ContentAlignment.MiddleLeft
        };
        panel.Controls.Add(label);
    }

    private void BtnBrowseConfig_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "WireGuard Config Files (*.conf)|*.conf|All Files (*.*)|*.*",
            Title = "Select WireGuard Configuration File"
        };

        if (_txtConfigPath != null && !string.IsNullOrEmpty(_txtConfigPath.Text))
        {
            try
            {
                dialog.InitialDirectory = Path.GetDirectoryName(_txtConfigPath.Text);
            }
            catch { }
        }

        if (dialog.ShowDialog() == DialogResult.OK && _txtConfigPath != null)
        {
            _txtConfigPath.Text = dialog.FileName;
        }
    }

    private void BtnBrowseWgExe_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Executable Files (*.exe)|*.exe|All Files (*.*)|*.*",
            Title = "Select WireGuard Executable"
        };

        if (_txtWgExe != null && !string.IsNullOrEmpty(_txtWgExe.Text))
        {
            try
            {
                dialog.InitialDirectory = Path.GetDirectoryName(_txtWgExe.Text);
            }
            catch { }
        }

        if (dialog.ShowDialog() == DialogResult.OK && _txtWgExe != null)
        {
            _txtWgExe.Text = dialog.FileName;
        }
    }

    private void BtnSaveSettings_Click(object? sender, EventArgs e)
    {
        bool serviceInstalled = _vpnManager.GetVpnStatus() != "Not Installed";

        var updatedSsids = (_txtTrustedSsids?.Text ?? "")
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();

        var updatedIpRanges = (_txtTrustedIpRanges?.Text ?? "")
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToArray();

        // Validate timer interval
        bool timerEnabled = _chkTimerEnabled?.Checked ?? true;
        int timerInterval = 30;
        
        if (_txtTimerInterval != null && !string.IsNullOrWhiteSpace(_txtTimerInterval.Text))
        {
            if (!int.TryParse(_txtTimerInterval.Text, out timerInterval) || timerInterval < 10 || timerInterval > 3600)
            {
                MessageBox.Show("Timer interval must be between 10 and 3600 seconds.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        // If service is installed, only allow saving trusted SSIDs, IP ranges, and timer settings
        if (serviceInstalled)
        {
            try
            {
                SaveSettingsToFile(_config.OriginalConfigPath, _config.WgExe, 
                    updatedSsids, updatedIpRanges, timerEnabled, timerInterval, _config.AutoStartWithWindows);
                
                // Reload configuration and restart timer with new settings
                Program.ReloadConfiguration();
                
                // Trigger immediate auto-management check to apply new network settings
                _autoManager.AutoManageVpn();
                
                // Update the UI
                UpdateCountdownVisibility();
                RefreshStatus();
                
                MessageBox.Show("Settings saved and applied successfully!", 
                    "Settings Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save settings: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return;
        }

        // Full validation when service is not installed
        if (_txtConfigPath == null || string.IsNullOrWhiteSpace(_txtConfigPath.Text))
        {
            MessageBox.Show("Config path cannot be empty.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_txtWgExe == null || string.IsNullOrWhiteSpace(_txtWgExe.Text))
        {
            MessageBox.Show("WireGuard executable path cannot be empty.", "Validation Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            SaveSettingsToFile(_txtConfigPath.Text.Trim(), 
                _txtWgExe.Text.Trim(), updatedSsids, updatedIpRanges, timerEnabled, timerInterval, _config.AutoStartWithWindows);
            
            // Reload configuration and restart timer with new settings
            Program.ReloadConfiguration();
            
            // Trigger immediate auto-management check to apply new settings
            _autoManager.AutoManageVpn();
            
            // Update the UI
            UpdateCountdownVisibility();
            RefreshStatus();
            
            MessageBox.Show("Settings saved and applied successfully!", 
                "Settings Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save settings: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void SaveSettingsToFile(string configPath, string wgExe, 
        string[] trustedSsids, string[] trustedIpRanges, bool timerEnabled, int timerIntervalSeconds, bool autoStartWithWindows)
    {
        string appPath = AppDomain.CurrentDomain.BaseDirectory;
        string settingsPath = Path.Combine(appPath, "appsettings.json");

        var settings = new
        {
            WireGuard = new
            {
                ConfigPath = configPath,
                WgExe = wgExe,
                TrustedSsids = trustedSsids,
                TrustedIpRanges = trustedIpRanges,
                TimerEnabled = timerEnabled,
                TimerIntervalSeconds = timerIntervalSeconds,
                AutoStartWithWindows = autoStartWithWindows
            }
        };

        string json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(settingsPath, json);
    }

    private void EnableAutoStart()
    {
        var result = _startupManager.EnableAutoStart();
        if (result.success)
        {
            // Update configuration
            _config.SetAutoStartWithWindows(true);
            MessageBox.Show("Auto-start with Windows has been enabled.", "Auto-Start Enabled", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else if (result.message != null)
        {
            MessageBox.Show($"Failed to enable auto-start: {result.message}", "Auto-Start Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
    
    private void DisableAutoStart()
    {
        var result = _startupManager.DisableAutoStart();
        if (result.success)
        {
            // Update configuration
            _config.SetAutoStartWithWindows(false);
            MessageBox.Show("Auto-start with Windows has been disabled.", "Auto-Start Disabled", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else if (result.message != null)
        {
            MessageBox.Show($"Failed to disable auto-start: {result.message}", "Auto-Start Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ApplyAutoStartSetting(bool enable)
    {
        if (enable)
        {
            var result = _startupManager.EnableAutoStart();
            if (!result.success && result.message != null)
            {
                MessageBox.Show($"Warning: {result.message}", "Auto-Start Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        else
        {
            var result = _startupManager.DisableAutoStart();
            if (!result.success && result.message != null)
            {
                MessageBox.Show($"Warning: {result.message}", "Auto-Start Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }

    private void AddSectionLabel(Panel panel, string text, int y)
    {
        var label = new Label
        {
            Text = text,
            Location = new Point(20, y),
            Size = new Size(540, 20),
            Font = new Font(Font.FontFamily, 10, FontStyle.Bold),
            ForeColor = Color.FromArgb(64, 64, 64)
        };
        panel.Controls.Add(label);
    }

    private void AddDetailRow(Panel panel, string labelText, ref Label? valueLabel, int y)
    {
        var label = new Label
        {
            Text = labelText,
            Location = new Point(20, y),
            Size = new Size(120, 25),
            Font = new Font(Font.FontFamily, 9, FontStyle.Regular),
            ForeColor = Color.FromArgb(100, 100, 100),
            TextAlign = ContentAlignment.MiddleLeft
        };
        panel.Controls.Add(label);

        valueLabel = new Label
        {
            Location = new Point(145, y),
            Size = new Size(415, 25),
            Font = new Font(Font.FontFamily, 9, FontStyle.Regular),
            ForeColor = Color.FromArgb(64, 64, 64),
            TextAlign = ContentAlignment.MiddleLeft
        };
        panel.Controls.Add(valueLabel);
    }

    private void AddConfigRow(Panel panel, string key, ref Label? valueLabel, int y)
    {
        // Key label (indented, monospace)
        var keyLabel = new Label
        {
            Text = key,
            Location = new Point(35, y),
            Size = new Size(110, 20),
            Font = new Font("Consolas", 9, FontStyle.Regular),
            ForeColor = Color.FromArgb(100, 100, 100),
            TextAlign = ContentAlignment.TopLeft
        };
        panel.Controls.Add(keyLabel);

        // Equals sign
        var equalsLabel = new Label
        {
            Text = "=",
            Location = new Point(145, y),
            Size = new Size(15, 20),
            Font = new Font("Consolas", 9, FontStyle.Regular),
            ForeColor = Color.FromArgb(100, 100, 100),
            TextAlign = ContentAlignment.TopLeft
        };
        panel.Controls.Add(equalsLabel);

        // Value label (monospace)
        valueLabel = new Label
        {
            Location = new Point(160, y),
            Size = new Size(400, 20),
            Font = new Font("Consolas", 9, FontStyle.Regular),
            ForeColor = Color.FromArgb(64, 64, 64),
            TextAlign = ContentAlignment.TopLeft
        };
        panel.Controls.Add(valueLabel);
    }


    private void OrbPanel_Paint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var status = _vpnManager?.GetVpnStatus() ?? "Unknown";
        
        // Determine if on trusted network (same logic as tray icon)
        var ssid = _networkManager?.GetSsid() ?? "";
        bool isTrustedSsid = _config.TrustedSsids.Any(t => t.Equals(ssid, StringComparison.OrdinalIgnoreCase));
        bool isTrustedIp = _networkManager?.IsOnTrustedIpNetwork(_config.TrustedIpRanges) ?? false;
        bool isOnTrustedNetwork = isTrustedSsid || isTrustedIp;
        
        // Get orb color using centralized logic
        var orbColor = IconGenerator.GetIconColor(status, _disableTracker.IsManuallyDisabled, isOnTrustedNetwork);

        // Use the IconGenerator to create a consistent branded icon
        // Size 30x30 to match the panel size
        using (var bitmap = IconGenerator.CreateColoredBitmap(orbColor, 30))
        {
            g.DrawImage(bitmap, 0, 0);
        }
    }

    public void RefreshStatus()
    {
        if (_vpnManager == null || _networkManager == null || 
            _statusLabel == null || _ssidLabel == null || _autoStartLabel == null ||
            _trustedNetworkLabel == null || _orbPanel == null) return;

        var ssid = _networkManager.GetSsid();
        var status = _vpnManager.GetVpnStatus();
        var serviceName = $"WireGuardTunnel${_config.TunnelName}";
        var wgServices = _vpnManager.GetWireGuardServices();

        // Check which trusted network rules match
        bool isTrustedSsid = _config.TrustedSsids.Contains(ssid, StringComparer.OrdinalIgnoreCase);
        bool isTrustedIp = _networkManager.IsOnTrustedIpNetwork(_config.TrustedIpRanges);

        _statusLabel.Text = status;
        _statusLabel.ForeColor = status == "Connected" ? Color.FromArgb(0, 180, 0) : Color.FromArgb(200, 0, 0);
        
        _ssidLabel.Text = ssid;

        if (isTrustedSsid && isTrustedIp)
        {
            _trustedNetworkLabel.Text = "Yes (SSID + IP range matched)";
            _trustedNetworkLabel.ForeColor = Color.FromArgb(0, 150, 0);
        }
        else if (isTrustedSsid)
        {
            _trustedNetworkLabel.Text = "Yes (SSID matched)";
            _trustedNetworkLabel.ForeColor = Color.FromArgb(0, 150, 0);
        }
        else if (isTrustedIp)
        {
            _trustedNetworkLabel.Text = "Yes (IP range matched)";
            _trustedNetworkLabel.ForeColor = Color.FromArgb(0, 150, 0);
        }
        else
        {
            _trustedNetworkLabel.Text = "No (untrusted network)";
            _trustedNetworkLabel.ForeColor = Color.FromArgb(200, 0, 0);
        }
        
        if (_disableTracker.IsManuallyDisabled)
        {
            _autoStartLabel.Text = "Auto-start: Disabled";
            _autoStartLabel.ForeColor = Color.FromArgb(200, 0, 0);
        }
        else
        {
            _autoStartLabel.Text = "Auto-start: Enabled";
            _autoStartLabel.ForeColor = Color.FromArgb(0, 150, 0);
        }

        // Update countdown timer display based on current timer state
        UpdateCountdownVisibility();


        // Get detailed configuration info
        var configInfo = _vpnManager.GetConfigInfo();
        
        // Update Interface labels
        if (!string.IsNullOrEmpty(configInfo.InterfaceAddress))
        {
            if (_interfacePrivateKeyLabel != null)
                _interfacePrivateKeyLabel.Text = !string.IsNullOrEmpty(configInfo.InterfacePublicKey) 
                    ? TruncateKey(configInfo.InterfacePublicKey) 
                    : "(none)";
            
            if (_interfaceAddressLabel != null)
                _interfaceAddressLabel.Text = configInfo.InterfaceAddress;
            
            if (_interfaceDnsLabel != null)
                _interfaceDnsLabel.Text = !string.IsNullOrEmpty(configInfo.DNS) 
                    ? configInfo.DNS 
                    : "(none)";
            
            if (_interfaceMtuLabel != null)
                _interfaceMtuLabel.Text = !string.IsNullOrEmpty(configInfo.MTU) 
                    ? configInfo.MTU 
                    : "(none)";
        }
        else
        {
            // No interface info available
            if (_interfacePrivateKeyLabel != null)
                _interfacePrivateKeyLabel.Text = "(not available)";
            if (_interfaceAddressLabel != null)
                _interfaceAddressLabel.Text = "(not available)";
            if (_interfaceDnsLabel != null)
                _interfaceDnsLabel.Text = "(not available)";
            if (_interfaceMtuLabel != null)
                _interfaceMtuLabel.Text = "(not available)";
        }
        
        // Update Peer labels
        if (!string.IsNullOrEmpty(configInfo.PeerPublicKey))
        {
            if (_peerPublicKeyLabel != null)
                _peerPublicKeyLabel.Text = TruncateKey(configInfo.PeerPublicKey);
            
            if (_peerPresharedKeyLabel != null)
                _peerPresharedKeyLabel.Text = !string.IsNullOrEmpty(configInfo.PresharedKey) 
                    ? configInfo.PresharedKey 
                    : "(none)";
            
            if (_peerAllowedIpsLabel != null)
                _peerAllowedIpsLabel.Text = !string.IsNullOrEmpty(configInfo.AllowedIPs) 
                    ? configInfo.AllowedIPs 
                    : "(none)";
            
            if (_peerEndpointLabel != null)
                _peerEndpointLabel.Text = !string.IsNullOrEmpty(configInfo.Endpoint) 
                    ? configInfo.Endpoint 
                    : "(none)";
            
            if (_peerKeepaliveLabel != null)
                _peerKeepaliveLabel.Text = !string.IsNullOrEmpty(configInfo.PersistentKeepalive) 
                    ? configInfo.PersistentKeepalive 
                    : "(none)";
        }
        else
        {
            // No peer info available
            if (_peerPublicKeyLabel != null)
                _peerPublicKeyLabel.Text = "(not available)";
            if (_peerPresharedKeyLabel != null)
                _peerPresharedKeyLabel.Text = "(not available)";
            if (_peerAllowedIpsLabel != null)
                _peerAllowedIpsLabel.Text = "(not available)";
            if (_peerEndpointLabel != null)
                _peerEndpointLabel.Text = "(not available)";
            if (_peerKeepaliveLabel != null)
                _peerKeepaliveLabel.Text = "(not available)";
        }

        // Update menu item states
        if (_startMenuItem == null || _stopMenuItem == null || 
            _installServiceMenuItem == null || _uninstallServiceMenuItem == null ||
            _installTaskMenuItem == null || _uninstallTaskMenuItem == null ||
            _activateAutoStartMenuItem == null || _deactivateAutoStartMenuItem == null) return;

        bool serviceInstalled = status != "Not Installed";
        bool vpnRunning = status == "Connected";
        bool unknownState = status.StartsWith("Unknown") || status.StartsWith("Status:");

        _startMenuItem.Enabled = serviceInstalled && (!vpnRunning || unknownState);
        _stopMenuItem.Enabled = serviceInstalled && (vpnRunning || unknownState);
        _installServiceMenuItem.Enabled = !serviceInstalled;
        _uninstallServiceMenuItem.Enabled = serviceInstalled;
        if (_btnBrowseConfig != null) _btnBrowseConfig.Enabled = !serviceInstalled;
        if (_txtConfigPath != null) _txtConfigPath.Enabled = !serviceInstalled;
        if (_btnBrowseWgExe != null) _btnBrowseWgExe.Enabled = !serviceInstalled;
        if (_txtWgExe != null) _txtWgExe.Enabled = !serviceInstalled;

        // Update auto-start menu items
        bool autoStartEnabled = _startupManager.IsAutoStartEnabled();
        _activateAutoStartMenuItem.Enabled = !autoStartEnabled;
        _deactivateAutoStartMenuItem.Enabled = autoStartEnabled;

        bool taskInstalled = _taskManager.IsTaskInstalled();
        _installTaskMenuItem.Enabled = serviceInstalled && !taskInstalled; // Only allow task install when service is installed
        _uninstallTaskMenuItem.Enabled = taskInstalled;

        // Update task status and last run time labels
        if (_taskStatusLabel != null)
        {
            if (taskInstalled)
            {
                _taskStatusLabel.Text = "Installed";
                _taskStatusLabel.ForeColor = Color.FromArgb(0, 150, 0);

                // Get and display last run time
                var lastRun = _taskManager.GetTaskLastRunTime();
                if (lastRun.HasValue)
                {
                    _taskStatusLabel.Text += " Triggered: ";
                    var elapsed = DateTime.Now - lastRun.Value;
                    if (elapsed.TotalMinutes < 1)
                    {
                        _taskStatusLabel.Text += "Just now";
                    }
                    else if (elapsed.TotalHours < 1)
                    {
                        _taskStatusLabel.Text += $"{(int)elapsed.TotalMinutes} minute{((int)elapsed.TotalMinutes != 1 ? "s" : "")} ago";
                    }
                    else if (elapsed.TotalDays < 1)
                    {
                        _taskStatusLabel.Text += $"{(int)elapsed.TotalHours} hour{((int)elapsed.TotalHours != 1 ? "s" : "")} ago";
                    }
                    else
                    {
                        _taskStatusLabel.Text += lastRun.Value.ToString("g"); // Short date/time format
                    }
                }
            }
            else
            {
                _taskStatusLabel.Text = "Not Installed";
                _taskStatusLabel.ForeColor = Color.FromArgb(200, 0, 0);
            }
        }

        // Update bandwidth statistics
        if (_bandwidthReceivedLabel != null && _bandwidthSentLabel != null)
        {
            var stats = _vpnManager.GetBandwidthStats();
            if (stats.HasValue && status == "Connected")
            {
                _bandwidthReceivedLabel.Text = VpnServiceManager.FormatBytes(stats.Value.received);
                _bandwidthSentLabel.Text = VpnServiceManager.FormatBytes(stats.Value.sent);
                _bandwidthReceivedLabel.ForeColor = Color.FromArgb(0, 120, 0);
                _bandwidthSentLabel.ForeColor = Color.FromArgb(0, 120, 0);
            }
            else
            {
                _bandwidthReceivedLabel.Text = status == "Connected" ? "Checking..." : "N/A";
                _bandwidthSentLabel.Text = status == "Connected" ? "Checking..." : "N/A";
                _bandwidthReceivedLabel.ForeColor = Color.Gray;
                _bandwidthSentLabel.ForeColor = Color.Gray;
            }
        }

        // Redraw orb
        _orbPanel.Invalidate();
    }

    private void UpdateCountdownVisibility()
    {
        if (_nextCheckLabel == null || _nextCheckLabelText == null) return;

        // Check if timer is currently enabled
        bool timerEnabled = Program.IsTimerEnabled();

        if (timerEnabled)
        {
            // Show countdown labels and restart timer if needed
            _nextCheckLabelText.Visible = true;
            _nextCheckLabel.Visible = true;
            if (_countdownTimer != null && !_countdownTimer.Enabled)
            {
                _countdownTimer.Start();
            }
        }
        else
        {
            // Hide countdown labels and stop timer
            _nextCheckLabelText.Visible = false;
            _nextCheckLabel.Visible = false;
            if (_countdownTimer != null && _countdownTimer.Enabled)
            {
                _countdownTimer.Stop();
            }
        }
    }

    private void UpdateCountdown()
    {
        if (_nextCheckLabel == null) return;

        var remaining = Program.GetTimerRemainingSeconds();
        if (remaining > 0)
        {
            _nextCheckLabel.Text = $"in {remaining} second{(remaining != 1 ? "s" : "")}";
            _nextCheckLabel.ForeColor = Color.FromArgb(64, 64, 64);
        }
        else
        {
            _nextCheckLabel.Text = "checking now...";
            _nextCheckLabel.ForeColor = Color.FromArgb(0, 150, 0);
        }
    }

    /// <summary>
    /// Refreshes settings UI controls to reflect current configuration
    /// </summary>
    public void RefreshSettingsUI()
    {
        // Reload configuration first
        _config.LoadConfiguration();
        
        // Update timer checkbox
        if (_chkTimerEnabled != null)
        {
            _chkTimerEnabled.Checked = _config.TimerEnabled;
        }
        
        // Update timer interval textbox
        if (_txtTimerInterval != null)
        {
            _txtTimerInterval.Text = _config.TimerIntervalSeconds.ToString();
            _txtTimerInterval.Enabled = _config.TimerEnabled;
        }
        
        // Update countdown visibility
        UpdateCountdownVisibility();
        
        // Refresh the status display
        RefreshStatus();
    }

    /// <summary>
    /// Opens the config editor form
    /// </summary>
    private void OpenConfigEditor()
    {
        try
        {
            using var editorForm = new ConfigEditorForm(
                _config,
                new Core.Logging.Logger(),
                () => {
                    // Callback when config is saved
                    UpdateConfigStatusIndicator();
                    RefreshStatus();
                },
                _vpnManager);
            
            editorForm.ShowDialog(this);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open config editor:\n{ex.Message}",
                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Updates the config status indicator label
    /// </summary>
    private void UpdateConfigStatusIndicator()
    {
        if (_configStatusLabel == null) return;

        if (_config.IsUsingModifiedConfig())
        {
            _configStatusLabel.Text = "⚠ Using modified config (original untouched)";
            _configStatusLabel.ForeColor = Color.FromArgb(200, 100, 0);
        }
        else
        {
            _configStatusLabel.Text = "";
        }
    }

    private string TruncateKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Length <= 20)
            return key;
        
        // Show first 8 and last 8 characters with ... in between
        return $"{key.Substring(0, 8)}...{key.Substring(key.Length - 8)}";
    }

    private void StatusForm_Shown(object? sender, EventArgs e)
    {
        // Start the stats refresh timer when the form is first shown
        _statsRefreshTimer?.Start();
        
        // Check if original config file has changed
        CheckForOriginalConfigChanges();
    }

    private void StatusForm_Activated(object? sender, EventArgs e)
    {
        // Restart the stats refresh timer when the form becomes active
        if (_statsRefreshTimer != null && !_statsRefreshTimer.Enabled)
        {
            _statsRefreshTimer.Start();
        }
        
        // Check if original config file has changed
        CheckForOriginalConfigChanges();
    }

    private void StatusForm_Deactivate(object? sender, EventArgs e)
    {
        // Stop the stats refresh timer when the form is deactivated to save resources
        _statsRefreshTimer?.Stop();
    }

    /// <summary>
    /// Checks if the original config file has been modified and prompts user to apply changes
    /// </summary>
    private void CheckForOriginalConfigChanges()
    {
        if (_config == null || _vpnManager == null) return;
        
        // Check if original config has changed
        if (!_config.HasOriginalConfigChanged())
            return;
        
        var currentModified = _config.GetCurrentOriginalConfigModifiedDate();
        var trackedModified = _config.OriginalConfigLastModified;
        
        // Build warning message
        string message;
        bool hasModifiedConfig = _config.IsUsingModifiedConfig();
        
        if (hasModifiedConfig)
        {
            message = 
                "⚠️ IMPORTANT: The original WireGuard configuration file has been modified externally.\n\n" +
                $"Original File: {Path.GetFileName(_config.OriginalConfigPath)}\n" +
                $"Last Known Modified: {trackedModified:yyyy-MM-dd HH:mm:ss}\n" +
                $"Current Modified: {currentModified:yyyy-MM-dd HH:mm:ss}\n\n" +
                "⚠️ WARNING: You have an active MODIFIED configuration!\n\n" +
                "If you apply the updated original file:\n" +
                "• Your current modified config will be PERMANENTLY DELETED\n" +
                "• All manual edits made through the config editor will be LOST\n" +
                "• The service will use the externally updated original file\n\n" +
                "Do you want to apply the updated original config file?\n\n" +
                "Choose 'Yes' to apply the updated original (DELETES modified config)\n" +
                "Choose 'No' to ignore this change and keep using your modified config";
        }
        else
        {
            message = 
                "The original WireGuard configuration file has been modified externally.\n\n" +
                $"Original File: {Path.GetFileName(_config.OriginalConfigPath)}\n" +
                $"Last Known Modified: {trackedModified:yyyy-MM-dd HH:mm:ss}\n" +
                $"Current Modified: {currentModified:yyyy-MM-dd HH:mm:ss}\n\n" +
                "Do you want to apply the updated configuration?\n\n" +
                "If you choose 'Yes':\n" +
                "• The updated file will be synced to the internal config\n" +
                "• You may need to reinstall the VPN service for changes to take effect\n\n" +
                "Choose 'No' to ignore this change and continue with the current config";
        }
        
        var result = MessageBox.Show(
            message,
            hasModifiedConfig ? "Original Config Changed - Modified Config Will Be Lost!" : "Original Config File Changed",
            MessageBoxButtons.YesNo,
            hasModifiedConfig ? MessageBoxIcon.Warning : MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2 // Default to No for safety
        );
        
        if (result == DialogResult.Yes)
        {
            try
            {
                // Apply the original config (deletes modified config if exists, syncs internal)
                _config.ApplyOriginalConfig();
                
                // Check if service is installed and auto-reinstall if available
                bool serviceWasInstalled = _vpnManager.GetVpnStatus() != "Not Installed";
                
                if (serviceWasInstalled)
                {
                    var reinstallResult = MessageBox.Show(
                        "Configuration updated successfully!\n\n" +
                        (hasModifiedConfig ? "Modified config has been deleted.\n" : "") +
                        "Internal config has been synced with the updated original file.\n\n" +
                        "The VPN service needs to be reinstalled for changes to take effect.\n\n" +
                        "Would you like to reinstall the VPN service now?",
                        "Reinstall VPN Service?",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question
                    );
                    
                    if (reinstallResult == DialogResult.Yes)
                    {
                        // Uninstall the old service
                        bool uninstallSuccess = false;
                        string? uninstallError = null;
                        
                        _vpnManager.UninstallVpn(
                            () => uninstallSuccess = true,
                            (error) => uninstallError = error
                        );
                        
                        if (!uninstallSuccess && uninstallError != null)
                        {
                            MessageBox.Show(
                                $"Failed to uninstall service:\n{uninstallError}\n\n" +
                                "Please manually uninstall and reinstall the VPN service.",
                                "Service Reinstall Failed",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning
                            );
                        }
                        else
                        {
                            // Wait a moment for uninstall to complete
                            System.Threading.Thread.Sleep(500);
                            
                            // Reinstall with the new config
                            bool installSuccess = false;
                            string? installError = null;
                            
                            _vpnManager.InstallVpn(
                                () => installSuccess = true,
                                (error) => installError = error
                            );
                            
                            if (!installSuccess && installError != null)
                            {
                                MessageBox.Show(
                                    $"Failed to reinstall service:\n{installError}\n\n" +
                                    "Please manually reinstall the VPN service.",
                                    "Service Reinstall Failed",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Warning
                                );
                            }
                            else
                            {
                                MessageBox.Show(
                                    "Service reinstalled successfully with updated config!\n\n" +
                                    (hasModifiedConfig ? "Modified config has been deleted.\n" : "") +
                                    "The updated original config is now active.",
                                    "Reinstall Complete",
                                    MessageBoxButtons.OK,
                                    MessageBoxIcon.Information
                                );
                            }
                        }
                    }
                    else
                    {
                        MessageBox.Show(
                            "Configuration updated but service not reinstalled.\n\n" +
                            (hasModifiedConfig ? "Modified config has been deleted.\n" : "") +
                            "Please manually reinstall the VPN service for changes to take effect.",
                            "Manual Reinstall Required",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information
                        );
                    }
                }
                else
                {
                    // Service not installed, just show normal update message
                    MessageBox.Show(
                        "Configuration updated successfully!\n\n" +
                        (hasModifiedConfig ? "Modified config has been deleted.\n" : "") +
                        "Internal config has been synced with the updated original file.\n\n" +
                        "Note: You need to install the VPN service for changes to take effect.",
                        "Configuration Applied",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                }
                
                // Refresh the status display
                UpdateConfigStatusIndicator();
                RefreshStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to apply updated configuration:\n{ex.Message}",
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }
        else
        {
            // User chose not to apply - update the tracked date to stop asking
            _config.UpdateOriginalConfigModifiedDate();
            
            MessageBox.Show(
                "Original config change ignored.\n\n" +
                (hasModifiedConfig 
                    ? "You will continue using your modified config.\n" 
                    : "You will continue using the current config.\n") +
                "You won't be prompted about this change again.",
                "Change Ignored",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        }
    }

    /// <summary>
    /// Refreshes only the statistics (bandwidth, task status) without full UI refresh
    /// </summary>
    private void RefreshStatistics()
    {
        if (_vpnManager == null) return;

        var status = _vpnManager.GetVpnStatus();

        // Update bandwidth statistics
        if (_bandwidthReceivedLabel != null && _bandwidthSentLabel != null)
        {
            var stats = _vpnManager.GetBandwidthStats();
            if (stats.HasValue && status == "Connected")
            {
                _bandwidthReceivedLabel.Text = VpnServiceManager.FormatBytes(stats.Value.received);
                _bandwidthSentLabel.Text = VpnServiceManager.FormatBytes(stats.Value.sent);
                _bandwidthReceivedLabel.ForeColor = Color.FromArgb(0, 120, 0);
                _bandwidthSentLabel.ForeColor = Color.FromArgb(0, 120, 0);
            }
            else
            {
                _bandwidthReceivedLabel.Text = status == "Connected" ? "Checking..." : "N/A";
                _bandwidthSentLabel.Text = status == "Connected" ? "Checking..." : "N/A";
                _bandwidthReceivedLabel.ForeColor = Color.Gray;
                _bandwidthSentLabel.ForeColor = Color.Gray;
            }
        }

        // Update task status and last run time labels
        if (_taskStatusLabel != null)
        {
            bool taskInstalled = _taskManager.IsTaskInstalled();
            if (taskInstalled)
            {
                _taskStatusLabel.Text = "Installed";
                _taskStatusLabel.ForeColor = Color.FromArgb(0, 150, 0);

                // Get and display last run time
                var lastRun = _taskManager.GetTaskLastRunTime();
                if (lastRun.HasValue)
                {
                    _taskStatusLabel.Text += " Triggered: ";
                    var elapsed = DateTime.Now - lastRun.Value;
                    if (elapsed.TotalMinutes < 1)
                    {
                        _taskStatusLabel.Text += "Just now";
                    }
                    else if (elapsed.TotalHours < 1)
                    {
                        _taskStatusLabel.Text += $"{(int)elapsed.TotalMinutes} minute{((int)elapsed.TotalMinutes != 1 ? "s" : "")} ago";
                    }
                    else if (elapsed.TotalDays < 1)
                    {
                        _taskStatusLabel.Text += $"{(int)elapsed.TotalHours} hour{((int)elapsed.TotalHours != 1 ? "s" : "")} ago";
                    }
                    else
                    {
                        _taskStatusLabel.Text += lastRun.Value.ToString("g"); // Short date/time format
                    }
                }
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _countdownTimer?.Stop();
            _countdownTimer?.Dispose();
            _statsRefreshTimer?.Stop();
            _statsRefreshTimer?.Dispose();
        }
        base.Dispose(disposing);
    }
}

