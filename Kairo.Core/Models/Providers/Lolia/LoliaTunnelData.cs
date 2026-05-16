namespace Kairo.Core.Models;

public sealed class LoliaCreateTunnelRequest
{
    public int NodeId { get; init; }
    public string Type { get; init; } = string.Empty;
    public string LocalIp { get; init; } = string.Empty;
    public int LocalPort { get; init; }
    public int RemotePort { get; init; }
    public string CustomDomain { get; init; } = string.Empty;
    public string Remark { get; init; } = string.Empty;
}

public sealed class LoliaTunnelListData
{
    public List<LoliaTunnelData> List { get; init; } = new();
}

public sealed class LoliaTunnelData
{
    public int BandwidthLimit { get; init; }
    public string ClientVersion { get; init; } = string.Empty;
    public string CreatedAt { get; init; } = string.Empty;
    public string CustomDomain { get; init; } = string.Empty;
    public int Id { get; init; }
    public string LocalIp { get; init; } = string.Empty;
    public int LocalPort { get; init; }
    public string Name { get; init; } = string.Empty;
    public string NodeAddress { get; init; } = string.Empty;
    public int NodeId { get; init; }
    public string NodeName { get; init; } = string.Empty;
    public string Remark { get; init; } = string.Empty;
    public int? RemotePort { get; init; }
    public string Status { get; init; } = string.Empty;
    public string TunnelToken { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public bool AutoTls { get; init; }
}

public sealed class LoliaCreateTunnelData
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
}
