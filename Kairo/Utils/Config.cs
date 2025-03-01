﻿using Newtonsoft.Json.Serialization;
using Newtonsoft.Json;
using System.Collections.Generic;
using Kairo.Utils.Components;

namespace Kairo.Utils
{
    
    [JsonObject(NamingStrategyType = typeof(CamelCaseNamingStrategy))]
    internal class Config
    {
        public string AccessToken = "";
        public string RefreshToken = "";
        public string Username = "";
        public int ID = 0;
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
