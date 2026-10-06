using Kairo.Core.Localization;
using Kairo.Core.Providers;
using Kairo.Cli.Configuration;
using Kairo.Cli.Services;
using Kairo.Cli.Utils;

namespace Kairo.Cli;

/// <summary>
/// 服务商查看与切换（切换结果会写入配置，与 GUI 登录页的选择保持一致）
/// </summary>
internal static class CliProviderSwitcher
{
    public static IFrpProvider Current => FrpProviderRegistry.Get(CliConfigManager.Config.ProviderId);

    public static bool IsLoggedIn(IFrpProvider provider) =>
        !string.IsNullOrWhiteSpace(ProviderAuth.Peek(provider).RefreshToken);

    /// <summary>是否所有服务商都没有保存登录状态（首次使用）</summary>
    public static bool HasNoAccounts => FrpProviderRegistry.All.All(p => !IsLoggedIn(p));

    public static bool TryResolve(string name, out IFrpProvider provider)
    {
        if (FrpProviderRegistry.TryGet(name, out provider))
            return true;

        ConsoleUi.Error(L.T("cli.provider.unknown", name));
        ConsoleUi.Hint(L.T("cli.provider.available", string.Join(", ", FrpProviderRegistry.All.Select(p => $"{p.Id} ({p.DisplayName})"))));
        return false;
    }

    /// <summary>
    /// 切换到指定服务商：保存当前服务商的凭据，再载入目标服务商的凭据
    /// </summary>
    public static void SwitchTo(IFrpProvider provider)
    {
        var current = Current;
        if (string.Equals(current.Id, provider.Id, StringComparison.OrdinalIgnoreCase))
        {
            ConsoleUi.Info(L.T("cli.provider.alreadyUsing", provider.DisplayName));
            return;
        }

        ProviderAuth.SaveCurrent(save: false);
        CliConfigManager.Config.ProviderId = provider.Id;
        ProviderAuth.ApplyCurrent();
        CliConfigManager.Save();
        Logger.Debug($"服务商已切换: {current.Id} -> {provider.Id}");

        var state = ProviderAuth.Peek(provider);
        ConsoleUi.Success(IsLoggedIn(provider)
            ? L.T("cli.provider.switchedSignedIn", provider.DisplayName, state.Username)
            : L.T("cli.provider.switchedSignedOut", provider.DisplayName));
    }

    public static void PrintProviders()
    {
        ConsoleUi.Section(L.T("cli.provider.title"));
        var current = Current;
        var nameWidth = FrpProviderRegistry.All.Max(p => ConsoleUi.DisplayWidth(p.DisplayName)) + 2;
        foreach (var provider in FrpProviderRegistry.All)
        {
            var isCurrent = provider.Id == current.Id;
            var state = ProviderAuth.Peek(provider);
            Console.Write("  ");
            ConsoleUi.Write(isCurrent ? "● " : "○ ", isCurrent ? ConsoleColor.Green : ConsoleColor.DarkGray);
            ConsoleUi.Write(ConsoleUi.PadRight(provider.DisplayName, nameWidth), isCurrent ? ConsoleColor.White : ConsoleColor.Gray);
            ConsoleUi.Write(ConsoleUi.PadRight(provider.Id, 8), ConsoleColor.DarkGray);
            if (IsLoggedIn(provider))
                ConsoleUi.Write(L.T("cli.provider.signedInAs", state.Username), ConsoleColor.Green);
            else
                ConsoleUi.Write(L.T("cli.provider.notSignedIn"), ConsoleColor.DarkGray);
            if (isCurrent)
                ConsoleUi.Write("  " + L.T("cli.provider.current"), ConsoleColor.DarkGray);
            Console.WriteLine();
        }
        Console.WriteLine();
    }

    /// <summary>交互式选择服务商；取消时返回 null</summary>
    public static IFrpProvider? Pick(string title)
    {
        var providers = FrpProviderRegistry.All;
        var current = Current;
        var labels = providers
            .Select(p => IsLoggedIn(p)
                ? L.T("cli.provider.optionSignedIn", p.DisplayName, ProviderAuth.Peek(p).Username)
                : p.DisplayName)
            .ToList();
        var defaultIndex = Math.Max(0, providers.ToList().FindIndex(p => p.Id == current.Id));
        var index = ConsoleUi.Choose(title, labels, defaultIndex);
        return index.HasValue ? providers[index.Value] : null;
    }
}
