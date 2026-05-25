using System;
using System.Collections.Generic;
using Kairo.Components;
using Kairo.Core;
using Kairo.Core.Providers;
using Kairo.Utils.Configuration;
using Kairo.Utils;

namespace Kairo
{
    internal static class Global
    {
        public static readonly DateTime StartTime = DateTime.Now;
        public const string Version = AppConstants.Version;
        public const string VersionName = AppConstants.VersionName;
        public const ReleaseChannel Branch = AppConstants.Branch;
        public const int Revision = AppConstants.Revision;
        public static readonly BuildInfo BuildInfo = new();
        public const string Developer = AppConstants.Developer;
        public const string Copyright = AppConstants.Copyright;
        public static Config Config = new();
        public static IFrpProvider CurrentProvider => FrpProviderRegistry.Get(Config?.ProviderId);
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

        public static readonly List<string> Tips = AppConstants.Tips;

        //public const string UpdateCheckerAPI = "http://localhost:5043/api";
        public const string UpdateCheckerAPI = AppConstants.UpdateCheckerAPI;

        public static int OAuthPort = 10000;

        public static void RefreshRuntimeFlags()
        {
            DebugMode = Config?.DebugMode ?? false;
            DebugConsoleManager.Sync(DebugMode);
        }
    }
}