using Kairo.Core;
using Kairo.Core.Providers;
using Kairo.Cli.Utils;

namespace Kairo.Cli;

internal static class CliHelpWriter
{
    private static string ProviderIds => string.Join(" / ", FrpProviderRegistry.All.Select(p => p.Id));

    public static void ShowBanner()
    {
        Console.WriteLine();
        ConsoleUi.Write("  Kairo CLI ", ConsoleColor.Cyan);
        ConsoleUi.Write($"{AppConstants.Version} \"{AppConstants.VersionName}\"", ConsoleColor.White);
        ConsoleUi.WriteLine($"  {AppConstants.Branch.ToDisplayName()} {AppConstants.Revision}", ConsoleColor.DarkGray);
    }

    public static void ShowHelp()
    {
        Section("用法");
        Console.WriteLine("  kairo-cli [命令] [选项]");

        Section("命令");
        Item("(无)", "交互式向导：登录并选择要启动的隧道");
        Item("start [ID...]", "启动隧道，例如 start 1,2,3；未指定 ID 时交互选择");
        Item("list, ls", "列出当前账号的所有隧道");
        Item("login", "登录（或重新登录）当前服务商");
        Item("logout", "退出当前服务商的登录");
        Item("status, whoami", "查看服务商、登录状态与 frpc 信息");
        Item("provider [名称]", $"查看或切换服务商 ({ProviderIds})");
        Item("help, version", "显示帮助 / 版本信息");

        Section("选项");
        Item("--provider [名称]", "切换服务商并记住选择；省略名称时交互选择");
        Item("-p, --proxy <ID,...>", "指定要启动的隧道 ID");
        Item("-l, --list", "列出隧道（同 list）");
        Item("--oauth", "仅显示 OAuth 授权链接");
        Item("--code <授权码>", "使用 OAuth 授权码登录");
        Item("-r, --refresh-token <令牌>", "使用 Refresh Token 登录（高级）");
        Item("-t, --frp-token <令牌>", "指定 FRP Token");
        Item("-f, --frpc-path <路径>", "指定 frpc 可执行文件路径");
        Item("--github, --no-mirror", "下载 frpc 时强制使用 GitHub 源");
        Item("--no-interactive", "禁用所有交互提示（适合脚本与服务）");
        Item("--no-color", "禁用彩色输出（也可设置 NO_COLOR 环境变量）");
        Item("-d, --debug", "输出调试日志");
        Item("--log-file", "将日志写入文件");
        Item("-q, --quiet", "安静模式（只显示警告和错误）");
        Item("-v, --version", "显示版本信息");
        Item("-h, --help", "显示此帮助信息");

        Section("示例");
        ConsoleUi.Command("kairo-cli", "# 首次使用：选择服务商并登录");
        ConsoleUi.Command("kairo-cli provider lolia", "# 切换到 LoliaFRP");
        ConsoleUi.Command("kairo-cli --provider locyan list", "# 切换到 LoCyanFrp 并列出隧道");
        ConsoleUi.Command("kairo-cli start 1,2", "# 启动 ID 为 1 和 2 的隧道");
        ConsoleUi.Command("kairo-cli start --no-interactive", "# 不询问，直接启动全部隧道");

        Section("环境变量");
        Item("KAIRO_CONFIG_DIR", "自定义配置目录");
        Item("NO_COLOR", "禁用彩色输出");
        Console.WriteLine();
    }

    public static void ShowVersion()
    {
        var version = AppVersion.FromComponents(AppConstants.Version, AppConstants.Branch, AppConstants.Revision);
        Console.WriteLine($"Kairo CLI {AppConstants.Version} ({AppConstants.Branch.ToDisplayName()})");
        Console.WriteLine($"Version Name: {AppConstants.VersionName}");
        Console.WriteLine($"Revision: {AppConstants.Revision}");
        Console.WriteLine($"Tag: {version.ToTagString()}");
        Console.WriteLine($"Developer: {AppConstants.Developer}");
        Console.WriteLine(AppConstants.Copyright);
    }

    private static void Section(string title)
    {
        Console.WriteLine();
        ConsoleUi.WriteLine(title + ":", ConsoleColor.Yellow);
    }

    private static void Item(string name, string description)
    {
        Console.Write("  ");
        ConsoleUi.Write(ConsoleUi.PadRight(name, 28), ConsoleColor.Green);
        Console.WriteLine(" " + description);
    }
}
