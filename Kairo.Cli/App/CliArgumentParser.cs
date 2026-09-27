using Kairo.Cli.Utils;
using Kairo.Core.Localization;

namespace Kairo.Cli;

internal static class CliArgumentParser
{
    private static readonly Dictionary<string, CliCommand> Commands = new(StringComparer.OrdinalIgnoreCase)
    {
        ["run"] = CliCommand.Run,
        ["start"] = CliCommand.Start,
        ["list"] = CliCommand.List,
        ["ls"] = CliCommand.List,
        ["login"] = CliCommand.Login,
        ["logout"] = CliCommand.Logout,
        ["status"] = CliCommand.Status,
        ["whoami"] = CliCommand.Status,
        ["provider"] = CliCommand.Provider,
        ["providers"] = CliCommand.Provider,
        ["help"] = CliCommand.Help,
        ["version"] = CliCommand.Version
    };

    /// <summary>所有已知的选项（用于拼写建议）</summary>
    private static readonly string[] KnownOptions =
    {
        "--help", "--version", "--oauth", "--get-oauth-url", "--code", "--refresh-token", "--frp-token",
        "--frpc-path", "--proxy", "--list", "--provider", "--no-interactive", "--github", "--no-mirror",
        "--debug", "--log-file", "--quiet", "--no-color", "--lang", "--language"
    };

    public static IReadOnlyCollection<string> CommandNames => Commands.Keys;

    public static CliOptions Parse(string[] args)
    {
        Logger.MethodEntry();
        var options = new CliOptions();
        var noInteractive = false;
        var wantsHelp = false;
        var wantsVersion = false;
        var wantsList = false;
        CliCommand? explicitCommand = null;

        for (var i = 0; i < args.Length; i++)
        {
            var raw = args[i];
            Logger.Debug($"解析参数[{i}]: {raw}");

            // 支持 --option=value 写法
            string? inlineValue = null;
            var arg = raw;
            if (raw.StartsWith("--", StringComparison.Ordinal) && raw.Contains('='))
            {
                var eq = raw.IndexOf('=');
                arg = raw[..eq];
                inlineValue = raw[(eq + 1)..];
            }

            switch (arg.ToLowerInvariant())
            {
                case "--help" or "-h" or "-?":
                    wantsHelp = true;
                    break;
                case "--version" or "-v":
                    wantsVersion = true;
                    break;
                case "--oauth" or "--get-oauth-url":
                    options.GetOAuthUrl = true;
                    break;
                case "--code":
                    options.OAuthCode = ReadValue(args, ref i, inlineValue, "--code", L.T("cli.args.value.code"), options);
                    break;
                case "--refresh-token" or "-r":
                    options.RefreshToken = ReadValue(args, ref i, inlineValue, "--refresh-token", L.T("cli.args.value.refreshToken"), options);
                    break;
                case "--frp-token" or "-t":
                    options.FrpToken = ReadValue(args, ref i, inlineValue, "--frp-token", L.T("cli.args.value.frpToken"), options);
                    break;
                case "--frpc-path" or "-f":
                    options.FrpcPath = ReadValue(args, ref i, inlineValue, "--frpc-path", L.T("cli.args.value.frpcPath"), options);
                    break;
                case "--proxy" or "-p":
                    var ids = ReadValue(args, ref i, inlineValue, "--proxy", L.T("cli.args.value.tunnelIds"), options);
                    if (ids != null) ParseProxyIds(ids, options);
                    break;
                case "--list" or "-l":
                    wantsList = true;
                    break;
                case "--lang" or "--language":
                    // 界面语言在 Program 中已提前应用，这里只消费参数值
                    ReadValue(args, ref i, inlineValue, arg, L.T("cli.args.value.language"), options);
                    break;
                case "--provider":
                    options.ProviderRequested = true;
                    options.ProviderName = inlineValue ?? ReadOptionalValue(args, ref i);
                    break;
                case "--no-interactive":
                    noInteractive = true;
                    break;
                case "--github" or "--no-mirror":
                    options.ForceGitHub = true;
                    break;
                case "--no-color":
                    options.NoColor = true;
                    break;
                case "--debug" or "-d" or "--log-file" or "--quiet" or "-q":
                    // 日志相关参数已在 Program 中处理
                    break;
                default:
                    if (raw.StartsWith('-') && raw.Length > 1 && !int.TryParse(raw, out _))
                    {
                        options.Errors.Add(BuildUnknownOptionMessage(arg));
                        // 跳过紧随其后的值，避免把它再报告成未知命令
                        if (inlineValue == null && i + 1 < args.Length && !IsOption(args[i + 1]) && !Commands.ContainsKey(args[i + 1]))
                            i++;
                        break;
                    }
                    HandlePositional(raw, ref explicitCommand, options);
                    break;
            }
        }

        options.Command = wantsHelp ? CliCommand.Help
            : wantsVersion ? CliCommand.Version
            : explicitCommand
              ?? (wantsList ? CliCommand.List
                  : options.ProxyIds.Count > 0 ? CliCommand.Start
                  : CliCommand.Run);

        options.InteractiveMode = !noInteractive && !Console.IsInputRedirected;
        Logger.Debug($"命令: {options.Command}, 交互模式: {options.InteractiveMode}");
        Logger.MethodExit();
        return options;
    }

    private static void HandlePositional(string value, ref CliCommand? explicitCommand, CliOptions options)
    {
        if (explicitCommand == null)
        {
            if (Commands.TryGetValue(value, out var command))
            {
                explicitCommand = command;
                return;
            }

            // 直接写隧道 ID（如 kairo-cli 1,2）视为 start
            if (value.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries).All(part => int.TryParse(part.Trim(), out _)))
            {
                explicitCommand = CliCommand.Start;
                ParseProxyIds(value, options);
                return;
            }

            var suggestion = Suggest(value, Commands.Keys);
            options.Errors.Add(suggestion != null
                ? L.T("cli.args.unknownCommandSuggest", value, suggestion)
                : L.T("cli.args.unknownCommand", value));
            return;
        }

        switch (explicitCommand)
        {
            case CliCommand.Start or CliCommand.Run:
                ParseProxyIds(value, options);
                break;
            case CliCommand.Provider when options.ProviderName == null:
                options.ProviderRequested = true;
                options.ProviderName = value;
                break;
            case CliCommand.Help:
                break;
            default:
                options.Errors.Add(L.T("cli.args.extraArgument", value));
                break;
        }
    }

    private static string? ReadValue(string[] args, ref int index, string? inlineValue, string optionName, string valueName, CliOptions options)
    {
        if (inlineValue != null)
        {
            if (inlineValue.Length > 0) return inlineValue;
            options.Errors.Add(L.T("cli.args.missingValue", optionName, valueName));
            return null;
        }

        if (index + 1 < args.Length && !IsOption(args[index + 1]))
            return args[++index];

        options.Errors.Add(L.T("cli.args.missingValueExample", optionName, valueName));
        Logger.Warning($"{optionName} 缺少参数值");
        return null;
    }

    /// <summary>读取可选的参数值：下一个参数不是选项且不是子命令时才视为值</summary>
    private static string? ReadOptionalValue(string[] args, ref int index)
    {
        if (index + 1 >= args.Length) return null;
        var next = args[index + 1];
        if (IsOption(next) || Commands.ContainsKey(next)) return null;
        index++;
        return next;
    }

    private static bool IsOption(string value) => value.StartsWith('-') && value.Length > 1 && !int.TryParse(value, out _);

    private static void ParseProxyIds(string value, CliOptions options)
    {
        foreach (var part in value.Split(new[] { ',', ' ', '，' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part.Trim(), out var id) && id > 0)
            {
                if (!options.ProxyIds.Contains(id))
                    options.ProxyIds.Add(id);
            }
            else
            {
                options.Errors.Add(L.T("cli.args.invalidTunnelId", part.Trim()));
            }
        }
    }

    private static string BuildUnknownOptionMessage(string option)
    {
        var suggestion = Suggest(option, KnownOptions);
        return suggestion != null
            ? L.T("cli.args.unknownOptionSuggest", option, suggestion)
            : L.T("cli.args.unknownOption", option);
    }

    private static string? Suggest(string input, IEnumerable<string> candidates)
    {
        string? best = null;
        var bestDistance = int.MaxValue;
        foreach (var candidate in candidates)
        {
            var distance = Distance(input.ToLowerInvariant(), candidate);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }
        return bestDistance <= Math.Max(2, input.Length / 3) ? best : null;
    }

    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }
}
