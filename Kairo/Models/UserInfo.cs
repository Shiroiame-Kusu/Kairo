using System.Text.Json.Serialization;
using ExtendedNumerics;

namespace Kairo.Models
{
    public class UserInfo
    {
        [JsonPropertyName("qq")] public long QQ { get; set; }
        [JsonPropertyName("qq_social_id")] public string? QQSocialID { get; set; }
        [JsonPropertyName("reg_time")] public string? RegTime { get; set; }
        [JsonPropertyName("id")] public int ID { get; set; }
        [JsonPropertyName("inbound")] public int Inbound { get; set; }
        [JsonPropertyName("outbound")] public int Outbound { get; set; }
        [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
        [JsonPropertyName("traffic")] public BigDecimal Traffic { get; set; }
        [JsonPropertyName("avatar")] public string? Avatar { get; set; }
        [JsonPropertyName("username")] public string Username { get; set; } = string.Empty;
        [JsonPropertyName("status")] public int Status { get; set; }
        [JsonPropertyName("frp_token")] public string? FrpToken { get; set; }
        [JsonPropertyName("limit")] public LimitInfo? Limit { get; set; }
        public string? Token { get; set; }

        public class LimitInfo
        {
            [JsonPropertyName("inbound")] public int Inbound { get; set; }
            [JsonPropertyName("outbound")] public int Outbound { get; set; }
            [JsonPropertyName("tunnel")] public int? Tunnel { get; set; }
        }
    }
}
