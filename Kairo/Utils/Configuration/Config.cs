using System.Collections.Generic;
using Kairo.Components;

namespace Kairo.Utils.Configuration
{
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
        public bool DebugMode = false;
        public bool AutoStartUp = false;
        public int AppliedTheme = 0;
        public bool UsingDownloadMirror = true;
        public bool FollowSystemTheme = true;
        public bool DarkTheme = false;

        // Preferred update branch: "Stable", "Beta", or "Alpha" (case-insensitive). Empty = follow current branch.
        public string UpdateBranch = "";

        public List<Proxy> AutoLaunch = new();
    }
}