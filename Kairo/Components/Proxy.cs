using System.Text.Json.Serialization;
using System;

namespace Kairo.Components
{
    public class Proxy
    {
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

        // v3: node is object
        [JsonPropertyName("node")]
        public ProxyNode? NodeInfo { get; set; }

        // Compatibility helpers
        [JsonIgnore]
        public int Node => NodeInfo?.Id ?? 0;

        [JsonPropertyName("secret_key")]
        public string? SecretKey { get; set; }
    }

    public class ProxyNode
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("host")] public string? Host { get; set; }
        [JsonPropertyName("ip")] public string? Ip { get; set; }
    }
}