namespace Kairo.Core.Models;

public sealed class LocyanNodeListData
{
    public List<LocyanNodeData> List { get; init; } = new();
}

public sealed class LocyanNodeData
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public string Ip { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public List<string> PortRange { get; init; } = new();
    public LocyanNodeAdditionalData? Additional { get; init; }
}

public sealed class LocyanNodeAdditionalData
{
    public string Description { get; init; } = string.Empty;
}
