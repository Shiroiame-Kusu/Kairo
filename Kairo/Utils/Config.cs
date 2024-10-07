using Newtonsoft.Json.Serialization;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Kairo.Utils.Components;

namespace Kairo.Utils
{
    
    [JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    internal class Config
    {
        public string Token = "";
        public string LoginToken = "";
        public string Username = "";
        //public string Password = "";
        public string FrpToken = "";
        public string FrpcPath = "";
        public bool DebugMode = true;
        public bool AutoStartUp = false;
        public int AppliedTheme = 0;
        public bool UsingDownloadMirror = true;
        public List<Proxy> AutoLaunch = new();
    }
}
