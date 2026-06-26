namespace Kairo.Core.Models;

public sealed class FrpStartOptions
{
    public int TunnelId { get; init; }
    public string TunnelName { get; init; } = string.Empty;
    public string FrpToken { get; init; } = string.Empty;
    public string ApiBaseUrl { get; init; } = string.Empty;
}
