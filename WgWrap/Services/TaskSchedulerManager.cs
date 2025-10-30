using System.Diagnostics;

namespace WgWrap.Services;

/// <summary>
/// Manages Windows Task Scheduler tasks for network monitoring
/// </summary>
internal class TaskSchedulerManager
{
    private const string TaskName = "WireGuardTray_NetworkMonitor";
    private const string TaskDescription = "Monitors network changes and manages WireGuard VPN connection based on trusted networks";

    /// <summary>
    /// Checks if the network monitor task is installed
    /// </summary>
    public bool IsTaskInstalled()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Query /TN \"{TaskName}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                process.WaitForExit();
                return process.ExitCode == 0;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the path to the file where task last run time is stored
    /// </summary>
    private string GetLastRunFilePath()
    {
        var appDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
        var dataDir = Path.Combine(appDir, "data");
        Directory.CreateDirectory(dataDir); // Ensure data directory exists
        return Path.Combine(dataDir, "task_last_run.flag");
    }

    /// <summary>
    /// Records the current time as the task last run time
    /// </summary>
    public void RecordTaskRun()
    {
        try
        {
            var filePath = GetLastRunFilePath();
            var timestamp = DateTime.Now.ToString("o"); // ISO 8601 format
            File.WriteAllText(filePath, timestamp);
        }
        catch
        {
            // Silently fail - this is not critical
        }
    }

    /// <summary>
    /// Gets the last run time of the network monitor task from the timestamp file
    /// </summary>
    /// <returns>DateTime of last run, or null if task has never run</returns>
    public DateTime? GetTaskLastRunTime()
    {
        try
        {
            var filePath = GetLastRunFilePath();
            if (!File.Exists(filePath))
            {
                return null;
            }

            var content = File.ReadAllText(filePath).Trim();
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            // Parse the ISO 8601 timestamp
            if (DateTime.TryParse(content, out var lastRun))
            {
                return lastRun;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }



    /// <summary>
    /// Installs the network monitor task
    /// </summary>
    public (bool success, string? message) InstallTask()
    {
        try
        {
            var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;

            // For .NET Core/5+, the location might be a .dll, so we need to get the .exe
            if (exePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                exePath = exePath.Substring(0, exePath.Length - 4) + ".exe";
            }

            if (!File.Exists(exePath))
            {
                return (false, $"Executable not found at:\n{exePath}");
            }

            // Create XML for the task with network change event trigger
            var taskXml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.4"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>{TaskDescription}</Description>
    <URI>\{TaskName}</URI>
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

            // Save XML to temp file
            var tempXmlPath = Path.Combine(Path.GetTempPath(), $"{TaskName}.xml");
            File.WriteAllText(tempXmlPath, taskXml);

            try
            {
                // Use schtasks to create the task
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Create /TN \"{TaskName}\" /XML \"{tempXmlPath}\" /F",
                    UseShellExecute = true,
                    Verb = "runas", // Request elevation
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                var process = Process.Start(psi);
                if (process != null)
                {
                    process.WaitForExit();

                    if (process.ExitCode == 0)
                    {
                        return (true, "Network Monitor Task installed successfully!\n\n" +
                            "The task will automatically run when your network connection changes, " +
                            "checking if the VPN should be started or stopped based on your current network.\n\n" +
                            "⚠ IMPORTANT: This task runs independently at the Windows system level. " +
                            "Closing this application will only stop the in-app monitoring and tray icon, " +
                            "but the Network Monitor Task will continue to manage your VPN. " +
                            "To stop automatic VPN management, you must uninstall the task.");
                    }
                    else
                    {
                        return (false, $"Failed to install task. Exit code: {process.ExitCode}\n\n" +
                            "Please check Windows Event Viewer for more details.");
                    }
                }
                return (false, "Failed to start installation process");
            }
            finally
            {
                // Clean up temp file
                try
                {
                    if (File.Exists(tempXmlPath))
                        File.Delete(tempXmlPath);
                }
                catch { }
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // User cancelled UAC prompt (error code 1223)
            if (ex.NativeErrorCode == 1223)
            {
                return (false, null); // Null message means user cancelled
            }
            return (false, $"Failed to install task: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to install task: {ex.Message}");
        }
    }

    /// <summary>
    /// Uninstalls the network monitor task
    /// </summary>
    public (bool success, string? message) UninstallTask()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = $"/Delete /TN \"{TaskName}\" /F",
                UseShellExecute = true,
                Verb = "runas", // Request elevation
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            var process = Process.Start(psi);
            if (process != null)
            {
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    return (true, "Network Monitor Task uninstalled successfully!");
                }
                else
                {
                    return (false, $"Failed to uninstall task. Exit code: {process.ExitCode}");
                }
            }
            return (false, "Failed to start uninstallation process");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // User cancelled UAC prompt (error code 1223)
            if (ex.NativeErrorCode == 1223)
            {
                return (false, null); // Null message means user cancelled
            }
            return (false, $"Failed to uninstall task: {ex.Message}");
        }
        catch (Exception ex)
        {
            return (false, $"Failed to uninstall task: {ex.Message}");
        }
    }
}
