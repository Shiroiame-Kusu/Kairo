using Kairo.Core;

namespace Kairo.Cli;

internal static class CliHelpWriter
{
    public static void ShowBanner()
    {
        var versionLine = $"Ver {AppConstants.Version} \"{AppConstants.VersionName}\" {AppConstants.Branch.ToDisplayName()} {AppConstants.Revision}";
        var padding = (61 - versionLine.Length) / 2;
        var centeredVersion = versionLine.PadLeft(padding + versionLine.Length).PadRight(61);
        Console.WriteLine();
        Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║                       Kairo CLI Mode                          ║");
        Console.WriteLine($"║ {centeredVersion} ║");
        Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
    }

    public static void ShowHelp()
    {
        Console.WriteLine("用法: kairo-cli [选项]");
        Console.WriteLine();
        Console.WriteLine("选项:");
        Console.WriteLine("  --oauth, --get-oauth-url  显示 OAuth 授权 URL");
        Console.WriteLine("  --code <code>             使用 OAuth 授权码登录");
        Console.WriteLine("  --refresh-token, -r <token>");
        Console.WriteLine("                            使用 Refresh Token 登录（高级）");
        Console.WriteLine("  --frp-token, -t <token>   指定 FRP Token");
        Console.WriteLine("  --frpc-path, -f <path>    指定 frpc 可执行文件路径");
        Console.WriteLine("  --proxy, -p <id1,id2,...> 指定要启动的隧道 ID");
        Console.WriteLine("  --list, -l                列出所有可用隧道");
        Console.WriteLine("  --no-interactive          禁用交互模式");
        Console.WriteLine("  --debug, -d               启用调试日志模式");
        Console.WriteLine("  --log-file                将日志写入文件");
        Console.WriteLine("  --quiet, -q               安静模式（只显示警告和错误）");
        Console.WriteLine("  --github, --no-mirror     强制使用 GitHub 下载源");
        Console.WriteLine("  --version, -v             显示版本信息");
        Console.WriteLine("  --help, -h                显示此帮助信息");
        Console.WriteLine();
        Console.WriteLine("使用说明:");
        Console.WriteLine("  直接运行 'kairo-cli' 将进入交互式向导模式:");
        Console.WriteLine("    - 未登录时会自动引导完成 OAuth 授权");
        Console.WriteLine("    - 登录后会显示隧道列表并让您选择要启动的隧道");
        Console.WriteLine();
        Console.WriteLine("  也可以使用命令行参数完成各步骤:");
        Console.WriteLine("    kairo-cli --oauth          # 仅显示授权 URL");
        Console.WriteLine("    kairo-cli --code <code>    # 使用授权码登录");
        Console.WriteLine("    kairo-cli --list           # 列出所有隧道");
        Console.WriteLine("    kairo-cli --proxy 1,2,3    # 启动指定隧道");
        Console.WriteLine();
        Console.WriteLine("  调试模式:");
        Console.WriteLine("    kairo-cli --debug          # 显示详细日志");
        Console.WriteLine("    kairo-cli --debug --log-file  # 详细日志并写入文件");
    }

    public static void ShowVersion()
    {
        Console.WriteLine($"Kairo CLI {AppConstants.Version} ({AppConstants.Branch.ToDisplayName()})");
        Console.WriteLine($"Version Name: {AppConstants.VersionName}");
        Console.WriteLine($"Revision: {AppConstants.Revision}");
        Console.WriteLine($"Developer: {AppConstants.Developer}");
        Console.WriteLine(AppConstants.Copyright);
    }
}
