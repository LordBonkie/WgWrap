using Microsoft.Extensions.Configuration;

namespace WgWrap.Core.Configuration;

/// <summary>
/// Manages application configuration from appsettings.json
/// </summary>
internal class ConfigurationManager
{
    // Constant tunnel name used internally by WgWrap
    public const string InternalTunnelName = "wg_wrap_tunnel";
    
    public string TunnelName => InternalTunnelName; // Always use the constant tunnel name
    public string OriginalConfigPath { get; private set; } = "";
    public string InternalConfigPath { get; private set; } = "";
    public string ModifiedConfigPath { get; private set; } = "";
    public string WgExe { get; private set; } = "";
    public string[] TrustedSsids { get; private set; } = Array.Empty<string>();
    public string[] TrustedIpRanges { get; private set; } = Array.Empty<string>();
    public string[] ExcludedNetworkAdapters { get; private set; } = Array.Empty<string>();
    public bool TimerEnabled { get; private set; } = true;
    public int TimerIntervalSeconds { get; private set; } = 30;
    public bool AutoStartWithWindows { get; private set; } = false;
    public bool ShowShutdownWarning { get; private set; } = true;
    public DateTime OriginalConfigLastModified { get; private set; } = DateTime.MinValue;

    public void LoadConfiguration()
    {
        try
        {
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var settingsPath = Path.Combine(appDir, "appsettings.json");
            var exampleSettingsPath = Path.Combine(appDir, "appsettings.example.json");

            // If appsettings.json doesn't exist, copy from example
            if (!File.Exists(settingsPath) && File.Exists(exampleSettingsPath))
            {
                File.Copy(exampleSettingsPath, settingsPath, overwrite: false);
            }

            // If appsettings.json exists, check for missing settings and add defaults
            if (File.Exists(settingsPath))
            {
                EnsureCompleteConfiguration(settingsPath, exampleSettingsPath);
            }

            var builder = new ConfigurationBuilder()
                .SetBasePath(appDir)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

            var configuration = builder.Build();

            // Load original ConfigPath from settings
            OriginalConfigPath = configuration["WireGuard:ConfigPath"] ?? OriginalConfigPath;
            
            // Record the original config file's last modified date
            if (!string.IsNullOrEmpty(OriginalConfigPath) && File.Exists(OriginalConfigPath))
            {
                OriginalConfigLastModified = File.GetLastWriteTime(OriginalConfigPath);
            }
            
            // Set up internal config paths
            var dataDir = Path.Combine(appDir, "data");
            Directory.CreateDirectory(dataDir); // Ensure data directory exists
            InternalConfigPath = Path.Combine(dataDir, $"{InternalTunnelName}.conf");
            ModifiedConfigPath = Path.Combine(dataDir, "modified_config.conf");
            
            WgExe = configuration["WireGuard:WgExe"] ?? WgExe;

            // ...existing code for loading SSIDs, IP ranges, timer settings, auto-start...
            var ssids = configuration.GetSection("WireGuard:TrustedSsids").Get<string[]>();
            if (ssids != null && ssids.Length > 0)
            {
                TrustedSsids = ssids;
            }

            var ipRanges = configuration.GetSection("WireGuard:TrustedIpRanges").Get<string[]>();
            if (ipRanges != null && ipRanges.Length > 0)
            {
                TrustedIpRanges = ipRanges;
            }

            var excludedAdapters = configuration.GetSection("WireGuard:ExcludedNetworkAdapters").Get<string[]>();
            if (excludedAdapters != null && excludedAdapters.Length > 0)
            {
                ExcludedNetworkAdapters = excludedAdapters;
            }

            // Load timer settings
            if (bool.TryParse(configuration["WireGuard:TimerEnabled"], out var timerEnabled))
            {
                TimerEnabled = timerEnabled;
            }

            if (int.TryParse(configuration["WireGuard:TimerIntervalSeconds"], out var intervalSeconds))
            {
                if (intervalSeconds >= 10 && intervalSeconds <= 3600)
                {
                    TimerIntervalSeconds = intervalSeconds;
                }
            }

            // Load auto-start setting
            if (bool.TryParse(configuration["WireGuard:AutoStartWithWindows"], out var autoStart))
            {
                AutoStartWithWindows = autoStart;
            }
            
            // Load shutdown warning setting
            if (bool.TryParse(configuration["WireGuard:ShowShutdownWarning"], out var showWarning))
            {
                ShowShutdownWarning = showWarning;
            }
        }
        catch
        {
            // If configuration loading fails, use the default values already set
        }
    }

    /// <summary>
    /// Syncs the internal config file with either modified or original config
    /// </summary>
    public void SyncInternalConfig()
    {
        try
        {
            // Determine source: modified config if exists, otherwise original
            string sourceConfigPath = File.Exists(ModifiedConfigPath) ? ModifiedConfigPath : OriginalConfigPath;
            
            if (!File.Exists(sourceConfigPath))
            {
                throw new FileNotFoundException($"Config file not found: {sourceConfigPath}");
            }

            // Copy content to internal config file
            File.Copy(sourceConfigPath, InternalConfigPath, overwrite: true);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to sync internal config: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Checks if a modified config is currently in use
    /// </summary>
    public bool IsUsingModifiedConfig()
    {
        return File.Exists(ModifiedConfigPath);
    }

    /// <summary>
    /// Deletes the modified config, reverting to original
    /// </summary>
    public void RevertToOriginal()
    {
        if (File.Exists(ModifiedConfigPath))
        {
            File.Delete(ModifiedConfigPath);
        }
        SyncInternalConfig(); // Re-sync with original
    }
    
    /// <summary>
    /// Central method to update settings in appsettings.json
    /// Updates both the file and in-memory values
    /// </summary>
    private void UpdateSettings(Action<ConfigUpdate> updateAction)
    {
        try
        {
            var update = new ConfigUpdate(this);
            updateAction(update);
            
            var appDir = AppDomain.CurrentDomain.BaseDirectory;
            var settingsPath = Path.Combine(appDir, "appsettings.json");
            
            var settings = new
            {
                WireGuard = new
                {
                    ConfigPath = update.ConfigPath ?? OriginalConfigPath,
                    WgExe = update.WgExe ?? WgExe,
                    TrustedSsids = update.TrustedSsids ?? TrustedSsids,
                    TrustedIpRanges = update.TrustedIpRanges ?? TrustedIpRanges,
                    ExcludedNetworkAdapters = update.ExcludedNetworkAdapters ?? ExcludedNetworkAdapters,
                    TimerEnabled = update.TimerEnabled ?? TimerEnabled,
                    TimerIntervalSeconds = update.TimerIntervalSeconds ?? TimerIntervalSeconds,
                    AutoStartWithWindows = update.AutoStartWithWindows ?? AutoStartWithWindows,
                    ShowShutdownWarning = update.ShowShutdownWarning ?? ShowShutdownWarning
                }
            };
            
            var json = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            File.WriteAllText(settingsPath, json);
            
            // Update in-memory values
            if (update.ConfigPath != null) OriginalConfigPath = update.ConfigPath;
            if (update.WgExe != null) WgExe = update.WgExe;
            if (update.TrustedSsids != null) TrustedSsids = update.TrustedSsids;
            if (update.TrustedIpRanges != null) TrustedIpRanges = update.TrustedIpRanges;
            if (update.ExcludedNetworkAdapters != null) ExcludedNetworkAdapters = update.ExcludedNetworkAdapters;
            if (update.TimerEnabled != null) TimerEnabled = update.TimerEnabled.Value;
            if (update.TimerIntervalSeconds != null) TimerIntervalSeconds = update.TimerIntervalSeconds.Value;
            if (update.AutoStartWithWindows != null) AutoStartWithWindows = update.AutoStartWithWindows.Value;
            if (update.ShowShutdownWarning != null) ShowShutdownWarning = update.ShowShutdownWarning.Value;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to update settings: {ex.Message}", ex);
        }
    }
    
    /// <summary>
    /// Updates the ShowShutdownWarning setting
    /// </summary>
    public void SetShowShutdownWarning(bool value)
    {
        UpdateSettings(update => update.ShowShutdownWarning = value);
    }
    
    /// <summary>
    /// Resets the shutdown warning to show again (called after service installation)
    /// </summary>
    public void ResetShutdownWarning()
    {
        SetShowShutdownWarning(true);
    }
    
    /// <summary>
    /// Updates the timer enabled setting
    /// </summary>
    public void SetTimerEnabled(bool value)
    {
        UpdateSettings(update => update.TimerEnabled = value);
    }
    
    /// <summary>
    /// Updates the auto-start with Windows setting
    /// </summary>
    public void SetAutoStartWithWindows(bool value)
    {
        UpdateSettings(update => update.AutoStartWithWindows = value);
    }
    
    /// <summary>
    /// Checks if the original config file has been modified since last check
    /// </summary>
    public bool HasOriginalConfigChanged()
    {
        if (string.IsNullOrEmpty(OriginalConfigPath) || !File.Exists(OriginalConfigPath))
            return false;
            
        var currentModified = File.GetLastWriteTime(OriginalConfigPath);
        return currentModified > OriginalConfigLastModified && OriginalConfigLastModified != DateTime.MinValue;
    }
    
    /// <summary>
    /// Updates the tracked modification date of the original config file
    /// </summary>
    public void UpdateOriginalConfigModifiedDate()
    {
        if (!string.IsNullOrEmpty(OriginalConfigPath) && File.Exists(OriginalConfigPath))
        {
            OriginalConfigLastModified = File.GetLastWriteTime(OriginalConfigPath);
        }
    }
    
    /// <summary>
    /// Gets the current last modified date of the original config file
    /// </summary>
    public DateTime GetCurrentOriginalConfigModifiedDate()
    {
        if (string.IsNullOrEmpty(OriginalConfigPath) || !File.Exists(OriginalConfigPath))
            return DateTime.MinValue;
            
        return File.GetLastWriteTime(OriginalConfigPath);
    }
    
    /// <summary>
    /// Applies the original config by deleting modified config and resyncing
    /// </summary>
    public void ApplyOriginalConfig()
    {
        // Delete modified config if it exists
        if (File.Exists(ModifiedConfigPath))
        {
            File.Delete(ModifiedConfigPath);
        }
        
        // Sync internal config with original
        SyncInternalConfig();
        
        // Update the tracked modification date
        UpdateOriginalConfigModifiedDate();
    }
    
    /// <summary>
    /// Ensures the configuration file has all required settings, adding defaults from example if missing
    /// </summary>
    private void EnsureCompleteConfiguration(string settingsPath, string exampleSettingsPath)
    {
        try
        {
            if (!File.Exists(exampleSettingsPath))
                return;

            // Load both configurations
            var settingsJson = File.ReadAllText(settingsPath);
            var exampleJson = File.ReadAllText(exampleSettingsPath);

            var settingsDoc = System.Text.Json.JsonDocument.Parse(settingsJson);
            var exampleDoc = System.Text.Json.JsonDocument.Parse(exampleJson);

            // Check if WireGuard section exists
            if (!settingsDoc.RootElement.TryGetProperty("WireGuard", out var settingsWireGuard))
            {
                // If no WireGuard section, copy the entire example
                File.Copy(exampleSettingsPath, settingsPath, overwrite: true);
                return;
            }

            var exampleWireGuard = exampleDoc.RootElement.GetProperty("WireGuard");

            // Check each setting and add if missing
            bool hasChanges = false;
            var updatedSettings = settingsDoc.RootElement.Clone();

            // Helper function to add missing property
            void AddMissingProperty(string propertyName, System.Text.Json.JsonElement defaultValue)
            {
                if (!settingsWireGuard.TryGetProperty(propertyName, out _))
                {
                    // Property is missing, add it
                    var newWireGuard = AddPropertyToObject(settingsWireGuard, propertyName, defaultValue);
                    updatedSettings = ReplaceProperty(updatedSettings, "WireGuard", newWireGuard);
                    hasChanges = true;
                }
            }

            // Check each required property
            if (exampleWireGuard.TryGetProperty("ConfigPath", out var configPath))
                AddMissingProperty("ConfigPath", configPath);
            if (exampleWireGuard.TryGetProperty("WgExe", out var wgExe))
                AddMissingProperty("WgExe", wgExe);
            if (exampleWireGuard.TryGetProperty("TrustedSsids", out var trustedSsids))
                AddMissingProperty("TrustedSsids", trustedSsids);
            if (exampleWireGuard.TryGetProperty("TrustedIpRanges", out var trustedIpRanges))
                AddMissingProperty("TrustedIpRanges", trustedIpRanges);
            if (exampleWireGuard.TryGetProperty("ExcludedNetworkAdapters", out var excludedAdapters))
                AddMissingProperty("ExcludedNetworkAdapters", excludedAdapters);
            if (exampleWireGuard.TryGetProperty("TimerEnabled", out var timerEnabled))
                AddMissingProperty("TimerEnabled", timerEnabled);
            if (exampleWireGuard.TryGetProperty("TimerIntervalSeconds", out var timerInterval))
                AddMissingProperty("TimerIntervalSeconds", timerInterval);
            if (exampleWireGuard.TryGetProperty("AutoStartWithWindows", out var autoStart))
                AddMissingProperty("AutoStartWithWindows", autoStart);
            if (exampleWireGuard.TryGetProperty("ShowShutdownWarning", out var showWarning))
                AddMissingProperty("ShowShutdownWarning", showWarning);

            // Save updated configuration if changes were made
            if (hasChanges)
            {
                var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                var updatedJson = System.Text.Json.JsonSerializer.Serialize(updatedSettings, options);
                File.WriteAllText(settingsPath, updatedJson);
            }
        }
        catch
        {
            // If anything fails, just continue with existing configuration
        }
    }

    /// <summary>
    /// Adds a property to a JSON object
    /// </summary>
    private System.Text.Json.JsonElement AddPropertyToObject(System.Text.Json.JsonElement obj, string propertyName, System.Text.Json.JsonElement value)
    {
        var dict = new Dictionary<string, System.Text.Json.JsonElement>();
        foreach (var property in obj.EnumerateObject())
        {
            dict[property.Name] = property.Value;
        }
        dict[propertyName] = value;

        var json = System.Text.Json.JsonSerializer.Serialize(dict);
        return System.Text.Json.JsonDocument.Parse(json).RootElement;
    }

    /// <summary>
    /// Replaces a property in a JSON object
    /// </summary>
    private System.Text.Json.JsonElement ReplaceProperty(System.Text.Json.JsonElement obj, string propertyName, System.Text.Json.JsonElement value)
    {
        var dict = new Dictionary<string, System.Text.Json.JsonElement>();
        foreach (var property in obj.EnumerateObject())
        {
            dict[property.Name] = property.Name == propertyName ? value : property.Value;
        }

        var json = System.Text.Json.JsonSerializer.Serialize(dict);
        return System.Text.Json.JsonDocument.Parse(json).RootElement;
    }
    
    /// <summary>
    /// Helper class for building configuration updates
    /// </summary>
    private class ConfigUpdate
    {
        public string? ConfigPath { get; set; }
        public string? WgExe { get; set; }
        public string[]? TrustedSsids { get; set; }
        public string[]? TrustedIpRanges { get; set; }
        public string[]? ExcludedNetworkAdapters { get; set; }
        public bool? TimerEnabled { get; set; }
        public int? TimerIntervalSeconds { get; set; }
        public bool? AutoStartWithWindows { get; set; }
        public bool? ShowShutdownWarning { get; set; }
        
        public ConfigUpdate(ConfigurationManager config)
        {
            // Initialize with null - only changed values will be set
        }
    }
}
