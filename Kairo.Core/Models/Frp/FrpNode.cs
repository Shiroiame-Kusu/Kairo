namespace Kairo.Core.Models;

public sealed class FrpNode
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public string Ip { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<string> PortRanges { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SupportedProtocols { get; init; } = Array.Empty<string>();
}
