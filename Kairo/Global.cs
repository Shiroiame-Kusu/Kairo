using Kairo.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Documents;

namespace Kairo
{
    internal static class Global
    {
        public static readonly string PATH = Path.GetDirectoryName(Environment.ProcessPath);
        public static readonly DateTime StartTime = DateTime.Now;
        public static bool LoginedByConsole = false;
        public const string Version = "3.0.0";
        public const string VersionName = "Haruhikage";
        public const string Branch = "Alpha";
        public const int Revision = 1;
        public static readonly BuildInfo BuildInfo = new();
        public const string Developer = "Shiroiame-Kusu & Daiyangcheng";
        public const string Copyright = "Copyright © Shiroiame-Kusu All Rights Reserved";
        public static Config Config = new();
        public static bool isDarkThemeEnabled;
        public static SecureString Password = new();
        public static List<string> Tips = new() {
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
        public const string API = "https://api.locyanfrp.cn/v3";
        //public const string UpdateCheckerAPI = "http://localhost:5043/api";
        public const string UpdateCheckerAPI = "https://kairo.nyat.icu/api";
        public const string GithubMirror = "https://proxy-gh.1l1.icu/";
        public class APIList
        {   
            public const string GetUserInfo = $"{API}/user/info";
            public const string AGodDamnSwitch = Branch == "Alpha" ? "preview" : "dashboard";
            public const string GetTheFUCKINGRefreshToken = $"https://{AGodDamnSwitch}.locyanfrp.cn/auth/oauth/authorize?user_id=";
            public const string GetAccessToken = $"{API}/auth/oauth/access-token";
            public const string GetFrpToken = $"{API}/user/frp/token";
            public const string GetSign = $"{API}/sign?user_id=";
            public const string GetNotice = $"{API}/notice";
            public const string GetAllProxy = $"{API}/proxy/all?user_id=";
            public const string DeleteProxy = $"{API}/proxy?user_id=";
        }
        public static int OAuthPort = 10000;
        public const int APPID = 9;

    }
}
