using Newtonsoft.Json;
using System;

namespace Kairo.Components
{
    public class Proxy
    {
        public int Id { get; set; }

        [JsonProperty("name")]
        public string ProxyName { get; set; } = string.Empty;

        [JsonProperty("type")]
        public string ProxyType { get; set; } = string.Empty;

        [JsonProperty("local_ip")]
        public string LocalIp { get; set; } = string.Empty;

        [JsonProperty("local_port")]
        public int LocalPort { get; set; }

        [JsonProperty("remote_port")]
        public int? RemotePort { get; set; }

        [JsonProperty("use_compression")]
        public bool UseCompression { get; set; }

        [JsonProperty("use_encryption")]
        public bool UseEncryption { get; set; }

        [JsonProperty("domain")]
        public string? Domain { get; set; }

        // v3: node is object
        [JsonProperty("node")]
        public ProxyNode? NodeInfo { get; set; }

        // Compatibility helpers
        [JsonIgnore]
        public int Node => NodeInfo?.Id ?? 0;

        [JsonProperty("secret_key")]
        public string? SecretKey { get; set; }
    }

    public class ProxyNode
    {
        [JsonProperty("id")] public int Id { get; set; }
        [JsonProperty("name")] public string? Name { get; set; }
        [JsonProperty("host")] public string? Host { get; set; }
        [JsonProperty("ip")] public string? Ip { get; set; }
    }
}