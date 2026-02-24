using System;
using System.Collections.Generic;
using Kairo.Components;
using Kairo.Utils.Configuration;
using Kairo.Utils;

namespace Kairo
{
    internal static class Global
    {
        public static readonly DateTime StartTime = DateTime.Now;
        public static bool LoginedByConsole = false;
        public const string Version = "3.2.1";
        public const string VersionName = "Sonetto";
        public const string Branch = "LoliaFrp";
        public const int Revision = 1;
        public static readonly BuildInfo BuildInfo = new();
        public const string Developer = "Shiroiame-Kusu & Daiyangcheng";
        public const string Copyright = "Copyright © Shiroiame-Kusu All Rights Reserved";
        public static Config Config = new();
        public static bool isDarkThemeEnabled;
        public static bool DebugMode { get; private set; }
        public static bool DebugModeEnabled => DebugMode;

        public static void SetDebugMode(bool enabled, bool persist)
        {
            DebugMode = enabled;
            Config.DebugMode = enabled;
            DebugConsoleManager.Sync(enabled);
            if (persist)
                ConfigManager.Save();
        }

        public static List<string> Tips = new()
        {
            "Tips:他们说下载的时候把电脑抱起来摇匀, 下载速度会更快哦"
        };

        // Lolia-Center v1 API
        public const string LoliaApi = "https://api.lolia.link/api/v1";
        public const string Dashboard = "https://dash.lolia.link";
        // public const string UpdateCheckerAPI = "https://kairo.nyat.icu/api";
        public const string GithubMirror = "https://hub.locyancs.cn/";

        public const int OAuthPort = 10000;
        public const string ClientId = "4qav2seu7hooz62f";
        public const string ClientSecret = "c56txzleytmkh8ochralqa65oyo8dnh7";

        public static void RefreshRuntimeFlags()
        {
            DebugMode = Config?.DebugMode ?? false;
            DebugConsoleManager.Sync(DebugMode);
        }
    }
}