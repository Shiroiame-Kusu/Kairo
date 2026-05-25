namespace Kairo.Core.Models;

public sealed class FrpDownloadRelease
{
    public string Version { get; init; } = string.Empty;
    public string ReleaseName { get; init; } = string.Empty;
    public string TagName { get; init; } = string.Empty;
    public IReadOnlyList<FrpDownloadAsset> Assets { get; init; } = Array.Empty<FrpDownloadAsset>();
}
