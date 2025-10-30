namespace WgWrap.Services;

/// <summary>
/// Tracks manual disable state for VPN auto-management
/// </summary>
internal class ManualDisableTracker
{
    private readonly string _manualDisableFile;
    private bool _manuallyDisabled;

    public bool IsManuallyDisabled => _manuallyDisabled;

    public ManualDisableTracker()
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var dataDir = Path.Combine(appDir, "data");
        Directory.CreateDirectory(dataDir); // Ensure data directory exists
        _manualDisableFile = Path.Combine(dataDir, "wg_manual_disable.flag");
        LoadState();
    }

    /// <summary>
    /// Loads the manual disable state from disk
    /// </summary>
    public void LoadState()
    {
        try
        {
            _manuallyDisabled = File.Exists(_manualDisableFile);
        }
        catch
        {
            _manuallyDisabled = false;
        }
    }

    /// <summary>
    /// Sets the manual disable state
    /// </summary>
    public void SetDisabled(bool disabled)
    {
        _manuallyDisabled = disabled;
        SaveState();
    }

    /// <summary>
    /// Saves the manual disable state to disk
    /// </summary>
    private void SaveState()
    {
        try
        {
            if (_manuallyDisabled)
            {
                File.WriteAllText(_manualDisableFile, DateTime.Now.ToString("o"));
            }
            else
            {
                if (File.Exists(_manualDisableFile))
                {
                    File.Delete(_manualDisableFile);
                }
            }
        }
        catch
        {
            // Ignore save errors
        }
    }
}
