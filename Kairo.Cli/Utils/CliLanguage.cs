using System.Runtime.CompilerServices;
using Kairo.Core.Localization;

namespace Kairo.Cli.Utils;

/// <summary>
/// kairo-cli 的界面语言：依次读取 --lang 参数、KAIRO_LANG 环境变量、配置中的 language（与 GUI 共用），默认简体中文
/// </summary>
internal static class CliLanguage
{
    public const string EnvironmentVariable = "KAIRO_LANG";

    [ModuleInitializer]
    internal static void RegisterLanguages() => Localizer.Register(typeof(CliLanguage).Assembly, "Kairo.Cli.Lang.");

    /// <summary>在正式解析参数之前找出 --lang / --language 的值</summary>
    public static string? FindArgument(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            foreach (var name in new[] { "--lang", "--language" })
            {
                if (arg.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return i + 1 < args.Length ? args[i + 1] : null;
                if (arg.StartsWith(name + "=", StringComparison.OrdinalIgnoreCase))
                    return arg[(name.Length + 1)..];
            }
        }
        return null;
    }

    /// <summary>应用界面语言，返回无法识别的输入值（用于提示），全部有效时返回 null</summary>
    public static string? Apply(string? argument, string? configured)
    {
        string? invalid = null;
        foreach (var candidate in new[] { argument, Environment.GetEnvironmentVariable(EnvironmentVariable), configured })
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            if (Localizer.IsSupported(candidate))
            {
                Localizer.SetLanguage(candidate);
                return invalid;
            }
            invalid ??= candidate;
        }

        Localizer.SetLanguage(Localizer.DefaultLanguage);
        return invalid;
    }
}
