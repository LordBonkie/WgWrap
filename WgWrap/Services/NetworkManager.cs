using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using WgWrap.Core.Logging;

namespace WgWrap.Services;

/// <summary>
/// Manages network detection and SSID retrieval
/// </summary>
internal class NetworkManager
{
    private readonly Logger _logger;

    public NetworkManager(Logger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets the current WiFi SSID or returns "Ethernet/Unknown" if not on WiFi
    /// </summary>
    public string GetSsid()
    {
        _logger.Debug("Starting SSID retrieval using netsh.");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = "wlan show interfaces",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc == null)
            {
                _logger.Warn("Failed to start netsh process for SSID retrieval.");
                return "Unknown";
            }
            string output = proc.StandardOutput.ReadToEnd();
            proc.WaitForExit(2000);
            _logger.Debug($"netsh output length: {output.Length} characters.");
            
            var match = Regex.Match(output, @"^\s*SSID\s*:\s*(.+)$", RegexOptions.Multiline);
            if (match.Success)
            {
                var ssid = match.Groups[1].Value.Trim();
                _logger.Info($"Detected WiFi SSID: '{ssid}'.");
                return ssid;
            }
            _logger.Info("No WiFi SSID found; assuming Ethernet or unknown network.");
            return "Ethernet/Unknown";
        }
        catch (Exception ex)
        {
            _logger.Error("Exception during SSID retrieval.", ex);
            return "Unknown";
        }
    }

    /// <summary>
    /// Gets all local IPv4 addresses (excluding loopback)
    /// </summary>
    public string[] GetLocalIpAddresses()
    {
        _logger.Debug("Retrieving local IP addresses.");
        try
        {
            var addresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up)
                .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                .Where(addr => addr.Address.AddressFamily == AddressFamily.InterNetwork 
                            && !IPAddress.IsLoopback(addr.Address))
                .Select(addr => addr.Address.ToString())
                .ToArray();

            _logger.Info($"Found {addresses.Length} local IP address(es): {string.Join(", ", addresses)}");
            return addresses;
        }
        catch (Exception ex)
        {
            _logger.Error("Exception during IP address retrieval.", ex);
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Checks if any local IP address matches the trusted IP ranges
    /// </summary>
    public bool IsOnTrustedIpNetwork(string[] trustedIpRanges)
    {
        if (trustedIpRanges == null || trustedIpRanges.Length == 0)
        {
            return false;
        }

        var localIps = GetLocalIpAddresses();
        if (localIps.Length == 0)
        {
            _logger.Debug("No local IP addresses found; not on trusted IP network.");
            return false;
        }

        foreach (var localIp in localIps)
        {
            foreach (var trustedRange in trustedIpRanges)
            {
                if (IsIpInRange(localIp, trustedRange))
                {
                    _logger.Info($"Local IP '{localIp}' matches trusted range '{trustedRange}'.");
                    return true;
                }
            }
        }

        _logger.Debug("No local IP addresses match trusted ranges.");
        return false;
    }

    /// <summary>
    /// Checks if an IP address is within a CIDR range (e.g., 192.168.1.0/24)
    /// </summary>
    private bool IsIpInRange(string ipAddress, string cidrRange)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(cidrRange))
                return false;

            var parts = cidrRange.Split('/');
            if (parts.Length != 2)
            {
                _logger.Warn($"Invalid CIDR format: '{cidrRange}'. Expected format: IP/mask (e.g., 192.168.1.0/24)");
                return false;
            }

            if (!IPAddress.TryParse(parts[0], out var networkAddress))
            {
                _logger.Warn($"Invalid network address in CIDR: '{parts[0]}'");
                return false;
            }

            if (!int.TryParse(parts[1], out var prefixLength) || prefixLength < 0 || prefixLength > 32)
            {
                _logger.Warn($"Invalid prefix length in CIDR: '{parts[1]}'. Must be 0-32.");
                return false;
            }

            if (!IPAddress.TryParse(ipAddress, out var ip))
            {
                _logger.Warn($"Invalid IP address: '{ipAddress}'");
                return false;
            }

            var networkBytes = networkAddress.GetAddressBytes();
            var ipBytes = ip.GetAddressBytes();

            if (networkBytes.Length != ipBytes.Length)
                return false;

            int maskBits = prefixLength;
            for (int i = 0; i < networkBytes.Length; i++)
            {
                if (maskBits >= 8)
                {
                    if (networkBytes[i] != ipBytes[i])
                        return false;
                    maskBits -= 8;
                }
                else if (maskBits > 0)
                {
                    int mask = (byte)(0xFF << (8 - maskBits));
                    if ((networkBytes[i] & mask) != (ipBytes[i] & mask))
                        return false;
                    maskBits = 0;
                }
                else
                {
                    break;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Error checking IP range '{ipAddress}' against '{cidrRange}'.", ex);
            return false;
        }
    }
}
