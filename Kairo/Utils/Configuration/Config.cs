using Newtonsoft.Json.Serialization;
using Newtonsoft.Json;
using System.Collections.Generic;
using Kairo.Components;

namespace Kairo.Utils.Configuration
{
    
    [JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    internal class Config
    {
        public string AccessToken = "";
        public string RefreshToken = "";
        public string Username = "";
        public int ID = 0;
        public int OAuthPort = 10000;
        //public string Password = "";
        public string FrpToken = "";
        public string FrpcPath = "";
        public bool DebugMode = true;
        public bool AutoStartUp = false;
        public int AppliedTheme = 0;
        public bool UsingDownloadMirror = true;
        public bool FollowSystemTheme = true;
        public bool DarkTheme = false;

        public List<Proxy> AutoLaunch = new();
    }
}