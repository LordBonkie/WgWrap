using Microsoft.Win32;

namespace WgWrap.Services;

/// <summary>
/// Manages Windows startup registry entries for auto-start functionality
/// </summary>
internal class StartupManager
{
    private const string StartupRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "WgWrap";

    /// <summary>
    /// Checks if the application is set to auto-start with Windows
    /// </summary>
    public bool IsAutoStartEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, false);
            if (key == null) return false;

            var value = key.GetValue(AppName);
            if (value == null) return false;

            // Verify the path matches the current executable
            var exePath = GetExecutablePath();
            return value.ToString()?.Equals(exePath, StringComparison.OrdinalIgnoreCase) ?? false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Enables auto-start with Windows by adding a registry entry
    /// </summary>
    public (bool success, string? message) EnableAutoStart()
    {
        try
        {
            var exePath = GetExecutablePath();
            
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true);
            if (key == null)
            {
                return (false, "Failed to open registry key for startup configuration.");
            }

            key.SetValue(AppName, exePath);
            return (true, "Auto-start with Windows has been enabled.");
        }
        catch (UnauthorizedAccessException)
        {
            return (false, "Access denied. Unable to modify registry startup settings.");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to enable auto-start: {ex.Message}");
        }
    }

    /// <summary>
    /// Disables auto-start with Windows by removing the registry entry
    /// </summary>
    public (bool success, string? message) DisableAutoStart()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true);
            if (key == null)
            {
                return (false, "Failed to open registry key for startup configuration.");
            }

            // Check if the value exists before trying to delete
            var value = key.GetValue(AppName);
            if (value != null)
            {
                key.DeleteValue(AppName, false);
            }

            return (true, "Auto-start with Windows has been disabled.");
        }
        catch (UnauthorizedAccessException)
        {
            return (false, "Access denied. Unable to modify registry startup settings.");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to disable auto-start: {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the path to the current executable
    /// </summary>
    private string GetExecutablePath()
    {
        var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;

        // For .NET Core/5+, the location might be a .dll, so we need to get the .exe
        if (exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            exePath = exePath.Substring(0, exePath.Length - 4) + ".exe";
        }

        return exePath;
    }
}
