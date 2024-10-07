using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LoCyanFrpDesktop.Utils.Components
{
    public class Proxy
    {
        public int Id { get; set; }

        [JsonProperty("proxy_name")]
        public string ProxyName { get; set; }

        [JsonProperty("proxy_type")]
        public string ProxyType { get; set; }

        [JsonProperty("local_ip")]
        public string LocalIp { get; set; }

        [JsonProperty("local_port")]
        public int LocalPort { get; set; }

        [JsonProperty("remote_port")]
        public string RemotePort { get; set; }

        [JsonProperty("use_compression")]
        public string UseCompression { get; set; }

        [JsonProperty("use_encryption")]
        public string UseEncryption { get; set; }
        [JsonProperty("domain")]
        public string Domain { get; set; }
        public int Node { get; set; }

        [JsonProperty("icp")]
        public object Icp { get; set; } // icp 字段可能为 null，使用 object 类型表示
    }
}
