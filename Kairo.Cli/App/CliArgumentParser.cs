using Kairo.Cli.Utils;

namespace Kairo.Cli;

internal static class CliArgumentParser
{
    public static CliOptions Parse(string[] args)
    {
        Logger.MethodEntry();
        var options = new CliOptions();
        var noInteractive = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            Logger.Debug($"解析参数[{i}]: {arg}");
            switch (arg.ToLowerInvariant())
            {
                case "--help" or "-h":
                    options.ShowHelp = true;
                    break;
                case "--version" or "-v":
                    options.ShowVersion = true;
                    break;
                case "--oauth" or "--get-oauth-url":
                    options.GetOAuthUrl = true;
                    break;
                case "--code":
                    ReadValue(args, ref i, value => options.OAuthCode = value, "--code");
                    break;
                case "--refresh-token" or "-r":
                    ReadValue(args, ref i, value => options.RefreshToken = value, "--refresh-token");
                    break;
                case "--frp-token" or "-t":
                    ReadValue(args, ref i, value => options.FrpToken = value, "--frp-token");
                    break;
                case "--frpc-path" or "-f":
                    ReadValue(args, ref i, value => options.FrpcPath = value, "--frpc-path");
                    break;
                case "--proxy" or "-p":
                    ReadValue(args, ref i, value => ParseProxyIds(value, options.ProxyIds), "--proxy");
                    break;
                case "--list" or "-l":
                    options.ListProxies = true;
                    break;
                case "--no-interactive":
                    noInteractive = true;
                    break;
                case "--github" or "--no-mirror":
                    options.ForceGitHub = true;
                    break;
                case "--debug" or "-d" or "--log-file" or "--quiet" or "-q":
                    break;
                default:
                    Logger.Debug($"未知参数: {arg}");
                    break;
            }
        }

        options.InteractiveMode = !noInteractive && !Console.IsInputRedirected;
        Logger.MethodExit();
        return options;
    }

    private static void ReadValue(string[] args, ref int index, Action<string> assign, string optionName)
    {
        if (index + 1 < args.Length)
        {
            assign(args[++index]);
            return;
        }
        Logger.Warning($"{optionName} 缺少参数值");
    }

    private static void ParseProxyIds(string value, List<int> proxyIds)
    {
        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part.Trim(), out var id))
                proxyIds.Add(id);
            else
                Logger.Warning($"无效的隧道 ID: {part}");
        }
    }
}
