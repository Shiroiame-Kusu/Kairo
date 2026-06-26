namespace Kairo.Core.Models;

public sealed class FrpAssetSelection
{
    public string Version { get; init; } = string.Empty;
    public string Platform { get; init; } = string.Empty;
    public string Architecture { get; init; } = string.Empty;
    public FrpDownloadAsset Asset { get; init; } = new();
}
