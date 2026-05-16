namespace Kairo.Core.Models;

public sealed class LoliaNodeListRequest
{
    public int Page { get; init; }
    public int Limit { get; init; }
}

public sealed class LoliaNodeListData
{
    public List<LoliaNodeData> Nodes { get; init; } = new();
}

public sealed class LoliaNodeData
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public string Ip { get; init; } = string.Empty;
    public string Address { get; init; } = string.Empty;
    public string NodeAddress { get; init; } = string.Empty;
    public string ServerAddress { get; init; } = string.Empty;
    public string Remark { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public List<string> PortRanges { get; init; } = new();
    public List<string> SupportedProtocols { get; init; } = new();
}
