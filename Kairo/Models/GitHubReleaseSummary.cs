using System.Text.Json.Serialization;

namespace Kairo.Models;

public sealed class GitHubReleaseSummary
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; init; } = string.Empty;
}
