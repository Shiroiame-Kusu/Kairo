namespace Kairo.Core.Models;

public sealed class GitHubReleaseAssetData
{
    public string Name { get; init; } = string.Empty;
    public string BrowserDownloadUrl { get; init; } = string.Empty;
    public string? Digest { get; init; }
}
