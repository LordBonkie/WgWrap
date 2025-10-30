using WgWrap.Core.Configuration;
using WgWrap.Core.Logging;
using WgWrap.Services;

namespace WgWrap.UI.Forms;

/// <summary>
/// Form for editing WireGuard configuration files
/// </summary>
internal class ConfigEditorForm : Form
{
    private readonly string _originalConfigPath;
    private readonly string _modifiedConfigPath;
    private readonly Logger _logger;
    private readonly Action _onConfigSaved;
    private readonly VpnServiceManager? _vpnManager;
    private readonly ConfigurationManager? _config;
    
    private TextBox? _txtConfigContent;
    private Button? _btnSave;
    private Button? _btnCancel;
    private Button? _btnRevertToOriginal;
    private Label? _statusLabel;

    public ConfigEditorForm(
        ConfigurationManager config,
        Logger logger, 
        Action onConfigSaved,
        VpnServiceManager? vpnManager = null)
    {
        _originalConfigPath = config.OriginalConfigPath;
        _modifiedConfigPath = config.ModifiedConfigPath;
        _logger = logger;
        _onConfigSaved = onConfigSaved;
        _vpnManager = vpnManager;
        _config = config;

        InitializeComponents();
        LoadConfig();
    }

    private void InitializeComponents()
    {
        Text = "WireGuard Config Editor";
        Size = new Size(1200, 600);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(900, 400);

        // Status label at top
        _statusLabel = new Label
        {
            Location = new Point(10, 10),
            Size = new Size(1160, 30),
            Font = new Font(Font.FontFamily, 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(200, 100, 0),
            Text = "⚠ Editing a modified copy - Original config file remains untouched",
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };
        Controls.Add(_statusLabel);

        // Left side - Original Config (Read-only)
        var lblOriginal = new Label
        {
            Text = "Original Config (Read-only):",
            Location = new Point(10, 45),
            Size = new Size(200, 20),
            Font = new Font(Font.FontFamily, 9, FontStyle.Bold)
        };
        Controls.Add(lblOriginal);

        var txtOriginalConfig = new TextBox
        {
            Location = new Point(10, 70),
            Size = new Size(575, 440),
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            Font = new Font("Consolas", 10),
            WordWrap = false,
            ReadOnly = true,
            BackColor = Color.FromArgb(245, 245, 245),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
        };
        Controls.Add(txtOriginalConfig);

        // Load original config into read-only view
        try
        {
            if (File.Exists(_originalConfigPath))
            {
                // Read as lines and rejoin with Environment.NewLine to ensure proper line endings
                var lines = File.ReadAllLines(_originalConfigPath);
                txtOriginalConfig.Text = string.Join(Environment.NewLine, lines);
            }
        }
        catch
        {
            txtOriginalConfig.Text = "Error loading original config";
        }

        // Right side - Modified Config (Editable)
        var lblModified = new Label
        {
            Text = "Modified Config (Editable):",
            Location = new Point(605, 45),
            Size = new Size(200, 20),
            Font = new Font(Font.FontFamily, 9, FontStyle.Bold),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        Controls.Add(lblModified);

        _txtConfigContent = new TextBox
        {
            Location = new Point(605, 70),
            Size = new Size(575, 440),
            Multiline = true,
            ScrollBars = ScrollBars.Both,
            Font = new Font("Consolas", 10),
            WordWrap = false,
            AcceptsReturn = true,
            AcceptsTab = true,
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
        };
        Controls.Add(_txtConfigContent);

        // Buttons at bottom
        _btnSave = new Button
        {
            Text = "Save Modified Config",
            Location = new Point(10, 520),
            Size = new Size(150, 30),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        _btnSave.Click += BtnSave_Click;
        Controls.Add(_btnSave);

        _btnRevertToOriginal = new Button
        {
            Text = "Revert to Original",
            Location = new Point(170, 520),
            Size = new Size(150, 30),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left
        };
        _btnRevertToOriginal.Click += BtnRevertToOriginal_Click;
        Controls.Add(_btnRevertToOriginal);

        _btnCancel = new Button
        {
            Text = "Cancel",
            Location = new Point(1070, 520),
            Size = new Size(100, 30),
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right
        };
        _btnCancel.Click += (s, e) => Close();
        Controls.Add(_btnCancel);
    }

    private void LoadConfig()
    {
        try
        {
            // Load from modified config if it exists, otherwise from original
            string configPath = File.Exists(_modifiedConfigPath) ? _modifiedConfigPath : _originalConfigPath;
            
            if (File.Exists(configPath))
            {
                if (_txtConfigContent != null)
                {
                    // Read as lines and rejoin with Environment.NewLine to ensure proper line endings
                    var lines = File.ReadAllLines(configPath);
                    _txtConfigContent.Text = string.Join(Environment.NewLine, lines);
                }
                
                UpdateStatusLabel();
            }
            else
            {
                MessageBox.Show($"Config file not found at:\n{configPath}", 
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Close();
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to load config file.", ex);
            MessageBox.Show($"Failed to load config file:\n{ex.Message}", 
                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        try
        {
            if (_txtConfigContent == null) return;

            // Check if service is installed - if so, warn user about reinstall
            bool serviceIsInstalled = _vpnManager != null && _vpnManager.GetVpnStatus() != "Not Installed";
            
            if (serviceIsInstalled)
            {
                var result = MessageBox.Show(
                    "⚠ WARNING: The VPN service will be automatically reinstalled with the new configuration.\n\n" +
                    "This means:\n" +
                    "• The VPN service will be stopped\n" +
                    "• The service will be uninstalled\n" +
                    "• The service will be reinstalled with your modified config\n" +
                    "• This may interrupt your current VPN connection\n\n" +
                    "Do you want to continue?",
                    "Confirm Save and Reinstall",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);

                if (result != DialogResult.Yes)
                {
                    return; // User cancelled
                }
            }

            // Ensure the directory exists
            var dir = Path.GetDirectoryName(_modifiedConfigPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Save to modified config path
            File.WriteAllText(_modifiedConfigPath, _txtConfigContent.Text);
            
            _logger.Info($"Modified config saved to: {_modifiedConfigPath}");

            // Sync the internal config file with the modified config
            if (_config != null)
            {
                try
                {
                    _config.SyncInternalConfig();
                    _logger.Info("Internal config synced with modified config");
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to sync internal config: {ex.Message}");
                    MessageBox.Show($"Config saved but failed to sync internal config:\n{ex.Message}",
                        "Warning", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            // Check if service is installed and auto-reinstall if available
            bool serviceWasInstalled = _vpnManager != null && _vpnManager.GetVpnStatus() != "Not Installed";
            
            if (serviceWasInstalled && _vpnManager != null)
            {
                _logger.Info("Service is installed, attempting silent reinstall with new config...");
                
                // Show a brief status message
                _statusLabel!.Text = "⏳ Reinstalling service with new config...";
                _statusLabel.ForeColor = Color.FromArgb(0, 100, 200);
                Application.DoEvents(); // Update UI
                
                // ...existing code...
                // Uninstall the old service
                bool uninstallSuccess = false;
                string? uninstallError = null;
                
                _vpnManager.UninstallVpn(
                    () => uninstallSuccess = true,
                    (error) => uninstallError = error
                );
                
                if (!uninstallSuccess && uninstallError != null)
                {
                    _logger.Warn($"Failed to uninstall service during silent reinstall: {uninstallError}");
                    MessageBox.Show($"Configuration saved but failed to reinstall service:\n{uninstallError}\n\n" +
                        "Please manually uninstall and reinstall the VPN service.",
                        "Service Reinstall Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                        _logger.Warn($"Failed to reinstall service: {installError}");
                        MessageBox.Show($"Configuration saved but failed to reinstall service:\n{installError}\n\n" +
                            "Please manually reinstall the VPN service.",
                            "Service Reinstall Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    }
                    else
                    {
                        _logger.Info("Service reinstalled successfully with new config.");
                        MessageBox.Show("Configuration saved and service reinstalled successfully!\n\n" +
                            "The modified config is now active.\n" +
                            "Original config file remains untouched.",
                            "Config Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }
            }
            else
            {
                // Service not installed, just show normal save message
                MessageBox.Show("Configuration saved successfully!\n\n" +
                    "The modified config will be used for VPN connections.\n" +
                    "Original config file remains untouched.\n\n" +
                    "Note: You need to install the VPN service for changes to take effect.",
                    "Config Saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            
            UpdateStatusLabel();
            _onConfigSaved();
            Close();
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to save config file.", ex);
            MessageBox.Show($"Failed to save config file:\n{ex.Message}", 
                "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BtnRevertToOriginal_Click(object? sender, EventArgs e)
    {
        var result = MessageBox.Show(
            "Are you sure you want to revert to the original config?\n\n" +
            "This will delete the modified config and use the original file.\n" +
            "The VPN service will be reinstalled with the original config.",
            "Revert to Original",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result == DialogResult.Yes)
        {
            try
            {
                if (_config != null)
                {
                    _config.RevertToOriginal();
                    _logger.Info("Reverted to original config and synced internal config");
                }
                else if (File.Exists(_modifiedConfigPath))
                {
                    File.Delete(_modifiedConfigPath);
                    _logger.Info($"Deleted modified config: {_modifiedConfigPath}");
                }

                // Check if service is installed and reinstall if needed
                bool serviceIsInstalled = _vpnManager != null && _vpnManager.GetVpnStatus() != "Not Installed";
                
                if (serviceIsInstalled && _vpnManager != null)
                {
                    // Reinstall service with original config
                    _vpnManager.UninstallVpn(
                        () => {
                            System.Threading.Thread.Sleep(500);
                            _vpnManager.InstallVpn(
                                () => {
                                    MessageBox.Show("Reverted to original config and service reinstalled successfully!",
                                        "Reverted", MessageBoxButtons.OK, MessageBoxIcon.Information);
                                },
                                (error) => {
                                    MessageBox.Show($"Reverted but failed to reinstall service:\n{error}",
                                        "Partial Success", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                                }
                            );
                        },
                        (error) => {
                            MessageBox.Show($"Reverted but failed to uninstall service:\n{error}\n\n" +
                                "Please manually reinstall the VPN service.",
                                "Partial Success", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }
                    );
                }
                else
                {
                    MessageBox.Show("Reverted to original config successfully!",
                        "Reverted", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }

                _onConfigSaved();
                Close();
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to revert to original config.", ex);
                MessageBox.Show($"Failed to revert to original config:\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void UpdateStatusLabel()
    {
        if (_statusLabel == null) return;

        if (File.Exists(_modifiedConfigPath))
        {
            _statusLabel.Text = "⚠ Using MODIFIED config - Original file remains untouched";
            _statusLabel.ForeColor = Color.FromArgb(200, 100, 0);
        }
        else
        {
            _statusLabel.Text = "✓ Using ORIGINAL config file";
            _statusLabel.ForeColor = Color.FromArgb(0, 150, 0);
        }
    }
}

