namespace Kairo.Core.Models;

public sealed class LocyanTunnelListData
{
    public List<LocyanTunnelData> List { get; init; } = new();
}

public sealed class LocyanTunnelData
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string LocalIp { get; init; } = string.Empty;
    public int LocalPort { get; init; }
    public int RemotePort { get; init; }
    public bool UseCompression { get; init; }
    public bool UseEncryption { get; init; }
    public string Domain { get; init; } = string.Empty;
    public string CustomDomain { get; init; } = string.Empty;
    public string SecretKey { get; init; } = string.Empty;
    public LocyanNodeData? Node { get; init; }
}

public sealed class LocyanCreateTunnelData
{
    public int TunnelId { get; init; }
}
