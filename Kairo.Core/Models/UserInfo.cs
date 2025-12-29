using System.Text.Json.Serialization;

namespace Kairo.Core.Models;

/// <summary>
/// 用户信息
/// </summary>
public class UserInfo
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("traffic")]
    public long Traffic { get; set; }

    [JsonPropertyName("group_id")]
    public int GroupId { get; set; }

    [JsonPropertyName("group_name")]
    public string? GroupName { get; set; }

    [JsonPropertyName("outbound")]
    public long Outbound { get; set; }

    [JsonPropertyName("inbound")]
    public long Inbound { get; set; }
}
