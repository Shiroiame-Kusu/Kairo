using Kairo.Core.Localization;

namespace Kairo.Core;

/// <summary>
/// 应用程序常量和 API 定义（共享）
/// </summary>
public static class AppConstants
{
    public const string Version = "3.5.0";
    public const string VersionName = "Asteria";
    public const ReleaseChannel Branch = ReleaseChannel.Alpha;
    public const int Revision = 1;
    public const string Developer = "Shiroiame-Kusu & Daiyangcheng";
    public const string Copyright = "Copyright © Shiroiame-Kusu All Rights Reserved";

    // API v3 base
    public const string UpdateCheckerAPI = "https://kairo.nyat.icu/api";
    public const string GithubMirror = "https://hub.locyancs.cn";

    /// <summary>加载界面随机显示的提示（当前语言，见语言文件中的 core.tips）</summary>
    public static IReadOnlyList<string> Tips => L.List("core.tips");
}