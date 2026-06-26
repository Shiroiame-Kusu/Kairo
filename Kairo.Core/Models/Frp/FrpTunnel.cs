namespace Kairo.Core.Models;

public sealed class FrpTunnel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Token { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string LocalIp { get; init; } = string.Empty;
    public int LocalPort { get; init; }
    public int? RemotePort { get; init; }
    public bool UseCompression { get; init; }
    public bool UseEncryption { get; init; }
    public string? Domain { get; init; }
    public string? SecretKey { get; init; }
    public FrpNode? Node { get; init; }
}
