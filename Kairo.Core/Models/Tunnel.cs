using System.Text.Json.Serialization;

namespace Kairo.Core.Models;

/// <summary>
/// 隧道/代理信息（共享模型）
/// </summary>
public class Tunnel
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string ProxyName { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string ProxyType { get; set; } = string.Empty;

    [JsonPropertyName("local_ip")]
    public string LocalIp { get; set; } = string.Empty;

    [JsonPropertyName("local_port")]
    public int LocalPort { get; set; }

    [JsonPropertyName("remote_port")]
    public int? RemotePort { get; set; }

    [JsonPropertyName("use_compression")]
    public bool UseCompression { get; set; }

    [JsonPropertyName("use_encryption")]
    public bool UseEncryption { get; set; }

    [JsonPropertyName("domain")]
    public string? Domain { get; set; }

    [JsonPropertyName("node")]
    public TunnelNode? NodeInfo { get; set; }

    [JsonPropertyName("secret_key")]
    public string? SecretKey { get; set; }

    // 兼容性
    [JsonIgnore]
    public int Node => NodeInfo?.Id ?? 0;
}

/// <summary>
/// 节点信息
/// </summary>
public class TunnelNode
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("host")]
    public string? Host { get; set; }

    [JsonPropertyName("ip")]
    public string? Ip { get; set; }
}
