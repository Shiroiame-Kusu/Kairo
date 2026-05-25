namespace Kairo.Core.Models;

public sealed class CreateFrpTunnelResult
{
    public int TunnelId { get; init; }
    public string TunnelName { get; init; } = string.Empty;
}
