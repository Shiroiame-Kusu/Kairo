namespace Kairo.Core.Models;

public sealed class CreateFrpTunnelRequest
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string LocalIp { get; init; } = string.Empty;
    public int LocalPort { get; init; }
    public int NodeId { get; init; }
    public int? RemotePort { get; init; }
    public bool UseEncryption { get; init; }
    public bool UseCompression { get; init; }
    public string SecretKey { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
}
