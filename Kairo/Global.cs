using System;
using System.Collections.Generic;
using Kairo.Components;
using Kairo.Core;
using Kairo.Utils.Configuration;
using Kairo.Utils;

namespace Kairo
{
    internal static class Global
    {
        public static readonly DateTime StartTime = DateTime.Now;
        public static bool LoginedByConsole = false;
        public const string Version = AppConstants.Version;
        public const string VersionName = AppConstants.VersionName;
        public const string Branch = AppConstants.Branch;
        public const int Revision = AppConstants.Revision;
        public static readonly BuildInfo BuildInfo = new();
        public const string Developer = AppConstants.Developer;
        public const string Copyright = AppConstants.Copyright;
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
            "Tips:他们说下载的时候把电脑抱起来摇匀, 下载速度会更快哦",
            "Tips:LocyanFrp永远不会跑路, 就像你家楼下清仓甩卖的店一样",
            "Tips:有的时候其实都不算是bug, 其实是我们特意写的特性 (确信",
            "Tips:你说的对,但是LocyanFrp是由Daiyangcheng女士主导的一款...",
            "Tips:如果你遇到了连不上API的情况, 那这边建议先换台新电脑呢亲",
            "Tips:你需要客服? 你是指LCF做开发的这几个暴躁老姐吗",
            "Tips:你染上LCF了? 你给哥们说实话, 你真的染上LCF了?",
            "Tips:我们要组一辈子LocyanFrp!",
            "Tips:不是, 哥们, 你确定真的要启动吗",
            "Tips:再急, 再急就给你Crash了",
            "Tips:那我问你, 那我问你",
            "Sayings:只要是我能做的，我什么都愿意做!",
            "Sayings:你是抱着多大的觉悟说出这种话的?",
            "Sayings:你这个人，满脑子都只想着自己呢。"
        };

        // API v3 base
        public const string API = AppConstants.API;
        public const string Dashboard = AppConstants.Dashboard;
        //public const string UpdateCheckerAPI = "http://localhost:5043/api";
        public const string UpdateCheckerAPI = AppConstants.UpdateCheckerAPI;
        public const string GithubMirror = AppConstants.GithubMirror;

        public class APIList
        {
            // User & OAuth
            public const string GetUserInfo = $"{API}/user";
            public const string GetTheFUCKINGRefreshToken = $"{Dashboard}/auth/oauth/authorize";
            public const string GetAccessToken = $"{API}/auth/oauth/access-token";

            public const string GetFrpToken = $"{API}/user/frp/token";

            // Site & sign
            public const string GetSign = $"{API}/sign"; // GET for status with ?user_id=, POST with form user_id for sign

            public const string GetNotice = $"{API}/site/notice";

            // Tunnels & nodes
            public const string GetAllProxy = $"{API}/tunnels?user_id=";
            public const string DeleteProxy = $"{API}/tunnel?user_id="; // append user_id and use &tunnel_id=
            public const string Tunnel = $"{API}/tunnel"; // base for PUT(create)/PATCH(update)
            public const string GetAllNodes = $"{API}/nodes?user_id=";
            // Random port for a node
            public const string GetRandomPort = $"{API}/node/port"; // GET with ?user_id=&node_id=
        }

        public static int OAuthPort = 10000;
        public const int APPID = 1;

        public static void RefreshRuntimeFlags()
        {
            DebugMode = Config?.DebugMode ?? false;
            DebugConsoleManager.Sync(DebugMode);
        }
    }
}