using Kairo.Core.Configuration;
using Kairo.Core.Localization;
using Kairo.Core.Logging;
using Kairo.Core.Models;
using Kairo.Core.Providers;
using Kairo.Cli.Configuration;
using Kairo.Cli.Services;
using Kairo.Cli.Utils;

namespace Kairo.Cli;

public class CliApp : IDisposable
{
    private readonly string[] _args;
    private readonly CancellationTokenSource _cts = new();
    private readonly CliOAuthFlow _oauthFlow;
    private readonly CliFrpcProcessRunner _processRunner;
    private ApiClient? _apiClient;
    private bool _disposed;

    private static IFrpProvider CurrentProvider => CliProviderSwitcher.Current;

    public CliApp(string[] args)
    {
        _args = args;
        _oauthFlow = new CliOAuthFlow(() => CurrentProvider);
        _processRunner = new CliFrpcProcessRunner(() => CurrentProvider, _cts);
        Logger.Debug($"CliApp 实例创建，参数数量: {args.Length}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _processRunner.Dispose();
        _apiClient?.Dispose();
        _cts.Dispose();
    }

    public async Task<int> RunAsync()
    {
        var options = CliArgumentParser.Parse(_args);

        if (options.Errors.Count > 0)
        {
            foreach (var error in options.Errors)
                ConsoleUi.Error(error);
            ConsoleUi.Hint(L.T("cli.app.seeHelp"));
            return 2;
        }

        switch (options.Command)
        {
            case CliCommand.Help:
                CliHelpWriter.ShowBanner();
                CliHelpWriter.ShowHelp();
                return 0;
            case CliCommand.Version:
                CliHelpWriter.ShowVersion();
                return 0;
        }

        if (!Console.IsOutputRedirected && !ConsoleUi.Quiet)
            CliHelpWriter.ShowBanner();

        if (options.ProviderRequested || options.Command == CliCommand.Provider)
        {
            var providerResult = HandleProviderSelection(options);
            if (providerResult.HasValue)
                return providerResult.Value;
        }

        if (options.GetOAuthUrl)
        {
            _oauthFlow.ShowOAuthUrl();
            return 0;
        }

        switch (options.Command)
        {
            case CliCommand.Status:
                ShowStatus();
                return 0;
            case CliCommand.Logout:
                return Logout(options);
        }

        if (ShouldAskForProvider(options))
        {
            var picked = CliProviderSwitcher.Pick(L.T("cli.app.chooseProvider"));
            if (picked == null)
            {
                ConsoleUi.Info(L.T("cli.cancelled"));
                return 1;
            }
            if (picked.Id == CurrentProvider.Id)
                ConsoleUi.Info(L.T("cli.app.usingProvider", picked.DisplayName));
            else
                CliProviderSwitcher.SwitchTo(picked);
        }

        _apiClient = new ApiClient(CurrentProvider);

        if (options.Command == CliCommand.Login)
            return await LoginCommandAsync(options);

        if (!await HandleLoginArgumentsAsync(options))
            return 1;

        if (NeedsLogin(options) && !HasSavedLogin())
        {
            if (!options.InteractiveMode)
            {
                ShowNotLoggedInGuidance();
                return 1;
            }
            if (!await _oauthFlow.InteractiveLoginAsync(_apiClient))
                return 1;
        }

        if (options.Command == CliCommand.List)
            return await ListAsync();

        var frpToken = ResolveFrpToken(options);
        if (string.IsNullOrWhiteSpace(frpToken) && CurrentProvider.Type != FrpProviderType.Lolia)
        {
            ConsoleUi.Error(L.T("cli.app.noFrpToken"));
            ConsoleUi.Hint(L.T("cli.app.noFrpTokenHint"));
            return 1;
        }

        var tunnels = await ResolveTunnelsAsync(options);
        if (tunnels.ExitCode.HasValue)
            return tunnels.ExitCode.Value;

        var frpcPath = await ResolveFrpcPathAsync(options);
        if (string.IsNullOrWhiteSpace(frpcPath))
            return 1;

        Logger.Info($"开始启动隧道，数量: {options.ProxyIds.Count}");
        return await _processRunner.StartAsync(frpcPath, frpToken ?? string.Empty, options.ProxyIds, tunnels.Items, _apiClient);
    }

    // ── 服务商 ───────────────────────────────────────────────

    /// <summary>
    /// 处理 --provider / provider 子命令；返回值不为 null 时直接以该退出码结束
    /// </summary>
    private static int? HandleProviderSelection(CliOptions options)
    {
        var providerOnly = options.Command == CliCommand.Provider;

        if (!string.IsNullOrWhiteSpace(options.ProviderName))
        {
            if (!CliProviderSwitcher.TryResolve(options.ProviderName, out var provider))
                return 2;
            CliProviderSwitcher.SwitchTo(provider);
            if (!providerOnly) return null;
            ShowNextSteps(provider);
            return 0;
        }

        CliProviderSwitcher.PrintProviders();
        if (options.InteractiveMode)
        {
            var picked = CliProviderSwitcher.Pick(L.T("cli.app.switchTo"));
            if (picked == null)
            {
                ConsoleUi.Info(L.T("cli.cancelled"));
                return providerOnly ? 0 : 1;
            }
            CliProviderSwitcher.SwitchTo(picked);
            if (providerOnly) ShowNextSteps(picked);
        }
        else if (providerOnly)
        {
            ConsoleUi.Command(L.T("cli.app.providerCommand"), L.T("cli.app.comment.switchProvider"));
        }

        return providerOnly ? 0 : null;
    }

    /// <summary>首次使用（所有服务商都未登录）时先询问要使用哪个服务商</summary>
    private static bool ShouldAskForProvider(CliOptions options) =>
        options.InteractiveMode
        && !options.ProviderRequested
        && options.Command is CliCommand.Run or CliCommand.Start or CliCommand.Login
        && string.IsNullOrWhiteSpace(options.OAuthCode)
        && string.IsNullOrWhiteSpace(options.RefreshToken)
        && string.IsNullOrWhiteSpace(options.FrpToken)
        && FrpProviderRegistry.All.Count > 1
        && CliProviderSwitcher.HasNoAccounts;

    private static void ShowNextSteps(IFrpProvider provider)
    {
        Console.WriteLine();
        if (CliProviderSwitcher.IsLoggedIn(provider))
        {
            ConsoleUi.Command("kairo-cli list", L.T("cli.app.comment.list"));
            ConsoleUi.Command("kairo-cli start", L.T("cli.app.comment.start"));
        }
        else
        {
            ConsoleUi.Command("kairo-cli login", L.T("cli.app.comment.login", provider.DisplayName));
        }
    }

    // ── 账号 ─────────────────────────────────────────────────

    private static bool HasSavedLogin() => !string.IsNullOrWhiteSpace(CliConfigManager.Config.RefreshToken);

    /// <summary>
    /// 是否需要登录：列出隧道、LoliaFRP 以及未指定隧道时都需要调用 API；
    /// LoCyanFrp 在指定隧道 ID 且已有 FRP Token 时可直接启动
    /// </summary>
    private static bool NeedsLogin(CliOptions options)
    {
        if (options.Command == CliCommand.List) return true;
        if (CurrentProvider.Type == FrpProviderType.Lolia) return true;
        if (options.ProxyIds.Count == 0) return true;
        return string.IsNullOrWhiteSpace(ResolveFrpToken(options));
    }

    private void ShowNotLoggedInGuidance()
    {
        var provider = CurrentProvider;
        ConsoleUi.Error(L.T("cli.app.notSignedIn", provider.DisplayName));
        ConsoleUi.Hint(L.T("cli.app.signInHint"));
        ConsoleUi.Command("kairo-cli login");
        if (provider.Type != FrpProviderType.Lolia)
        {
            ConsoleUi.Dim(L.T("cli.app.orUseCode"));
            ConsoleUi.Command("kairo-cli --oauth");
            ConsoleUi.Command(L.T("cli.app.loginWithCodeCommand"));
        }
    }

    private async Task<int> LoginCommandAsync(CliOptions options)
    {
        bool success;
        if (!string.IsNullOrWhiteSpace(options.OAuthCode) || !string.IsNullOrWhiteSpace(options.RefreshToken))
        {
            success = await HandleLoginArgumentsAsync(options);
        }
        else if (options.InteractiveMode)
        {
            if (HasSavedLogin())
                ConsoleUi.Info(L.T("cli.app.reauthorizing", CliConfigManager.Config.Username));
            success = await _oauthFlow.InteractiveLoginAsync(_apiClient!);
        }
        else
        {
            ConsoleUi.Error(L.T("cli.app.loginNeedsTerminal"));
            if (CurrentProvider.Type != FrpProviderType.Lolia)
                ConsoleUi.Command("kairo-cli --oauth", L.T("cli.app.comment.getAuthLink"));
            return 1;
        }

        if (success)
            ShowNextSteps(CurrentProvider);
        return success ? 0 : 1;
    }

    private async Task<bool> HandleLoginArgumentsAsync(CliOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.OAuthCode))
        {
            if (CurrentProvider.Type == FrpProviderType.Lolia)
            {
                ConsoleUi.Error(L.T("cli.app.pkceOnly", CurrentProvider.DisplayName));
                ConsoleUi.Hint(L.T("cli.app.pkceHint"));
                return false;
            }
            return await _oauthFlow.PerformLoginWithCodeAsync(_apiClient!, options.OAuthCode);
        }

        if (!string.IsNullOrWhiteSpace(options.RefreshToken))
            return await _oauthFlow.PerformLoginWithRefreshTokenAsync(_apiClient!, options.RefreshToken);

        return true;
    }

    private static int Logout(CliOptions options)
    {
        var provider = CurrentProvider;
        if (!HasSavedLogin())
        {
            ConsoleUi.Info(L.T("cli.app.alreadySignedOut", provider.DisplayName));
            return 0;
        }

        var username = CliConfigManager.Config.Username;
        if (options.InteractiveMode && !ConsoleUi.Confirm(L.T("cli.app.logoutConfirm", provider.DisplayName, username)))
        {
            ConsoleUi.Info(L.T("cli.cancelled"));
            return 0;
        }

        ProviderAuth.ClearCurrent(save: false);
        CliConfigManager.Config.AccessToken = string.Empty;
        CliConfigManager.Config.RefreshToken = string.Empty;
        CliConfigManager.Config.Username = string.Empty;
        CliConfigManager.Config.ID = 0;
        CliConfigManager.Config.FrpToken = string.Empty;
        CliConfigManager.Save();
        ConsoleUi.Success(L.T("cli.app.loggedOut", provider.DisplayName, username));
        return 0;
    }

    private static void ShowStatus()
    {
        var provider = CurrentProvider;
        var config = CliConfigManager.Config;
        var frpcPath = ProviderFrpcPath.Get(provider);
        var frpcInstalled = !string.IsNullOrWhiteSpace(frpcPath) && File.Exists(frpcPath);

        var rows = new List<(string Key, string Value, ConsoleColor? Color)>
        {
            (L.T("cli.status.provider"), $"{provider.DisplayName} ({provider.Id})", null),
            HasSavedLogin()
                ? (L.T("cli.status.account"), config.ID > 0 ? $"{config.Username} (UID {config.ID})" : config.Username, ConsoleColor.Green)
                : (L.T("cli.status.account"), L.T("cli.status.notSignedIn"), ConsoleColor.Yellow),
            (L.T("cli.status.frpToken"), provider.Type == FrpProviderType.Lolia
                ? L.T("cli.status.perTunnelToken")
                : string.IsNullOrWhiteSpace(config.FrpToken) ? L.T("cli.status.none") : SecretMasker.Mask(config.FrpToken), null),
            ("frpc", frpcInstalled ? frpcPath : L.T("cli.status.frpcMissing"), frpcInstalled ? null : ConsoleColor.DarkGray),
            (L.T("cli.status.source"), config.UsingDownloadMirror ? L.T("cli.status.mirrorFirst") : "GitHub", null),
            (L.T("cli.status.language"), CurrentLanguageDisplay(), null),
            (L.T("cli.status.configFile"), ConfigHelper.GetSettingsFilePath(), null)
        };
        if (Logger.LogFilePath != null)
            rows.Add((L.T("cli.status.logDir"), Path.GetDirectoryName(Logger.LogFilePath) ?? Logger.LogFilePath, null));

        ConsoleUi.Section(L.T("cli.status.title"));
        // 键名随语言变化，按最长的键对齐
        var keyWidth = rows.Max(row => ConsoleUi.DisplayWidth(row.Key)) + 1;
        foreach (var row in rows)
            ConsoleUi.KeyValue(row.Key, row.Value, keyWidth, row.Color);

        CliProviderSwitcher.PrintProviders();
        if (!HasSavedLogin())
            ConsoleUi.Command("kairo-cli login", L.T("cli.app.comment.login", provider.DisplayName));
        ConsoleUi.Command(L.T("cli.app.providerCommand"), L.T("cli.app.comment.switchProvider"));
        Console.WriteLine();
    }

    // ── 隧道 ─────────────────────────────────────────────────

    private async Task<int> ListAsync()
    {
        var result = await _apiClient!.GetTunnelsAsync();
        if (!result.Success)
        {
            ReportTunnelFetchFailure(result.Message);
            return 1;
        }

        var tunnels = result.Data ?? new List<Tunnel>();
        if (tunnels.Count == 0)
        {
            ShowNoTunnels();
            return 0;
        }

        CliTunnelSelector.ShowTunnelList(tunnels, CurrentProvider);
        ConsoleUi.Command("kairo-cli start <ID,...>", L.T("cli.app.comment.startIds"));
        Console.WriteLine();
        return 0;
    }

    private static string ResolveFrpToken(CliOptions options) => !string.IsNullOrWhiteSpace(options.FrpToken)
        ? options.FrpToken
        : CliConfigManager.Config.FrpToken;

    private async Task<string?> ResolveFrpcPathAsync(CliOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.FrpcPath))
        {
            if (File.Exists(options.FrpcPath))
                return options.FrpcPath;
            ConsoleUi.Error(L.T("cli.frpc.pathMissing", options.FrpcPath));
            return null;
        }

        var frpcPath = ProviderFrpcPath.Get(CurrentProvider);
        if (!string.IsNullOrWhiteSpace(frpcPath) && File.Exists(frpcPath))
            return frpcPath;

        ConsoleUi.Info(L.T("cli.frpc.downloadingAuto", CurrentProvider.DisplayName));
        using var downloader = new FrpcDownloader(CurrentProvider) { ForceGitHub = options.ForceGitHub };
        var downloadResult = await downloader.DownloadAsync(_cts.Token);
        if (!downloadResult.Success)
        {
            ConsoleUi.Error(L.T("cli.frpc.downloadFailed", downloadResult.Message));
            ConsoleUi.Hint(L.T(options.ForceGitHub ? "cli.frpc.useLocalHint" : "cli.frpc.useGithubHint"));
            return null;
        }

        ConsoleUi.Dim(L.T("cli.frpc.location", downloadResult.FrpcPath));
        return downloadResult.FrpcPath;
    }

    private async Task<TunnelResolutionResult> ResolveTunnelsAsync(CliOptions options)
    {
        // LoCyanFrp 指定了隧道 ID 时无需获取列表，可在 API 不可用时直接启动
        if (options.ProxyIds.Count > 0 && CurrentProvider.Type != FrpProviderType.Lolia)
            return new TunnelResolutionResult(null, null);

        var result = await _apiClient!.GetTunnelsAsync();
        if (!result.Success)
        {
            ReportTunnelFetchFailure(result.Message);
            return new TunnelResolutionResult(null, 1);
        }

        var tunnels = result.Data ?? new List<Tunnel>();
        if (tunnels.Count == 0)
        {
            ShowNoTunnels();
            return new TunnelResolutionResult(tunnels, 0);
        }

        if (options.ProxyIds.Count > 0)
            return new TunnelResolutionResult(tunnels, null);

        CliTunnelSelector.ShowTunnelList(tunnels, CurrentProvider);
        if (options.InteractiveMode)
        {
            var selectedIds = CliTunnelSelector.InteractiveSelectTunnels(tunnels);
            if (selectedIds == null || selectedIds.Count == 0)
                return new TunnelResolutionResult(tunnels, 0);
            options.ProxyIds.AddRange(selectedIds);
        }
        else
        {
            ConsoleUi.Info(L.T("cli.tunnels.startingAll"));
            options.ProxyIds.AddRange(tunnels.Select(t => t.Id));
        }

        return new TunnelResolutionResult(tunnels, null);
    }

    private static void ReportTunnelFetchFailure(string message)
    {
        ConsoleUi.Error(L.T("cli.tunnels.fetchFailed", message));
        ConsoleUi.Hint(L.T(HasSavedLogin() ? "cli.tunnels.checkNetwork" : "cli.tunnels.signInFirst"));
    }

    private static void ShowNoTunnels()
    {
        ConsoleUi.Info(L.T("cli.tunnels.none", CurrentProvider.DisplayName));
        ConsoleUi.Hint(L.T("cli.tunnels.createHint", CurrentProvider.DashboardUrl));
    }

    private static string CurrentLanguageDisplay()
    {
        var code = Localizer.CurrentLanguage;
        var name = Localizer.Languages.FirstOrDefault(language => language.Code == code)?.NativeName ?? code;
        return $"{name} ({code})";
    }

    private sealed record TunnelResolutionResult(List<Tunnel>? Items, int? ExitCode);
}
