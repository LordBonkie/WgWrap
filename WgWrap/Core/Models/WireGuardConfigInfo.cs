namespace WgWrap.Core.Models;

/// <summary>
/// Contains WireGuard configuration information parsed from config file
/// </summary>
internal class WireGuardConfigInfo
{
    public string InterfacePublicKey { get; set; } = "";
    public string InterfaceAddress { get; set; } = "";
    public string ListenPort { get; set; } = "";
    public string DNS { get; set; } = "";
    public string MTU { get; set; } = "";
    public string PeerPublicKey { get; set; } = "";
    public string PresharedKey { get; set; } = "";
    public string AllowedIPs { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string PersistentKeepalive { get; set; } = "";
}

