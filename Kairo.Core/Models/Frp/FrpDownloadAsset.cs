namespace Kairo.Core.Models;

public sealed class FrpDownloadAsset
{
    public string Name { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public string? Digest { get; init; }
}
