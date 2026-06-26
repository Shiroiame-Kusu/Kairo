namespace Kairo.Core.Models;

public sealed class GitHubReleaseData
{
    public string TagName { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public List<GitHubReleaseAssetData> Assets { get; init; } = new();
}
