using Kairo.Core.Configuration;
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
            ConsoleUi.Hint("运行 kairo-cli --help 查看所有命令与参数");
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
            var picked = CliProviderSwitcher.Pick("请选择要使用的服务商:");
            if (picked == null)
            {
                ConsoleUi.Info("已取消");
                return 1;
            }
            if (picked.Id == CurrentProvider.Id)
                ConsoleUi.Info($"将使用 {picked.DisplayName}");
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
            ConsoleUi.Error("未获取到 FRP Token");
            ConsoleUi.Hint("运行 kairo-cli login 重新登录，或使用 --frp-token 指定");
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
            var picked = CliProviderSwitcher.Pick("切换到:");
            if (picked == null)
            {
                ConsoleUi.Info("已取消");
                return providerOnly ? 0 : 1;
            }
            CliProviderSwitcher.SwitchTo(picked);
            if (providerOnly) ShowNextSteps(picked);
        }
        else if (providerOnly)
        {
            ConsoleUi.Command("kairo-cli provider <名称>", "# 切换服务商");
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
            ConsoleUi.Command("kairo-cli list", "# 查看隧道");
            ConsoleUi.Command("kairo-cli start", "# 选择并启动隧道");
        }
        else
        {
            ConsoleUi.Command("kairo-cli login", $"# 登录 {provider.DisplayName}");
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
        ConsoleUi.Error($"尚未登录 {provider.DisplayName}");
        ConsoleUi.Hint("在交互式终端中运行以下命令完成登录:");
        ConsoleUi.Command("kairo-cli login");
        if (provider.Type != FrpProviderType.Lolia)
        {
            ConsoleUi.Dim("       或者先获取授权链接，再使用授权码登录:");
            ConsoleUi.Command("kairo-cli --oauth");
            ConsoleUi.Command("kairo-cli login --code <授权码>");
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
                ConsoleUi.Info($"当前已登录为 {CliConfigManager.Config.Username}，将重新授权");
            success = await _oauthFlow.InteractiveLoginAsync(_apiClient!);
        }
        else
        {
            ConsoleUi.Error("登录需要交互式终端，或通过 --code / --refresh-token 提供凭据");
            if (CurrentProvider.Type != FrpProviderType.Lolia)
                ConsoleUi.Command("kairo-cli --oauth", "# 获取授权链接");
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
                ConsoleUi.Error($"{CurrentProvider.DisplayName} 的授权使用 PKCE，无法单独使用授权码登录");
                ConsoleUi.Hint("请在交互式终端中运行 kairo-cli login");
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
            ConsoleUi.Info($"{provider.DisplayName} 当前未登录");
            return 0;
        }

        var username = CliConfigManager.Config.Username;
        if (options.InteractiveMode && !ConsoleUi.Confirm($"确定要退出 {provider.DisplayName} 账号 {username} 吗?"))
        {
            ConsoleUi.Info("已取消");
            return 0;
        }

        ProviderAuth.ClearCurrent(save: false);
        CliConfigManager.Config.AccessToken = string.Empty;
        CliConfigManager.Config.RefreshToken = string.Empty;
        CliConfigManager.Config.Username = string.Empty;
        CliConfigManager.Config.ID = 0;
        CliConfigManager.Config.FrpToken = string.Empty;
        CliConfigManager.Save();
        ConsoleUi.Success($"已退出 {provider.DisplayName} 账号 {username}");
        return 0;
    }

    private static void ShowStatus()
    {
        var provider = CurrentProvider;
        var config = CliConfigManager.Config;
        var frpcPath = ProviderFrpcPath.Get(provider);
        var frpcInstalled = !string.IsNullOrWhiteSpace(frpcPath) && File.Exists(frpcPath);

        ConsoleUi.Section("当前状态");
        ConsoleUi.KeyValue("服务商", $"{provider.DisplayName} ({provider.Id})");
        if (HasSavedLogin())
            ConsoleUi.KeyValue("账号", config.ID > 0 ? $"{config.Username} (UID {config.ID})" : config.Username, valueColor: ConsoleColor.Green);
        else
            ConsoleUi.KeyValue("账号", "未登录", valueColor: ConsoleColor.Yellow);
        ConsoleUi.KeyValue("FRP Token", provider.Type == FrpProviderType.Lolia
            ? "启动时按隧道获取"
            : string.IsNullOrWhiteSpace(config.FrpToken) ? "无" : SecretMasker.Mask(config.FrpToken));
        ConsoleUi.KeyValue("frpc", frpcInstalled ? frpcPath : "未安装（启动隧道时会自动下载）",
            valueColor: frpcInstalled ? null : ConsoleColor.DarkGray);
        ConsoleUi.KeyValue("下载源", config.UsingDownloadMirror ? "国内镜像优先" : "GitHub");
        ConsoleUi.KeyValue("配置文件", ConfigHelper.GetSettingsFilePath());
        if (Logger.LogFilePath != null)
            ConsoleUi.KeyValue("日志目录", Path.GetDirectoryName(Logger.LogFilePath) ?? Logger.LogFilePath);

        CliProviderSwitcher.PrintProviders();
        if (!HasSavedLogin())
            ConsoleUi.Command("kairo-cli login", $"# 登录 {provider.DisplayName}");
        ConsoleUi.Command("kairo-cli provider <名称>", "# 切换服务商");
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
        ConsoleUi.Command("kairo-cli start <ID,...>", "# 启动指定隧道");
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
            ConsoleUi.Error($"指定的 frpc 不存在: {options.FrpcPath}");
            return null;
        }

        var frpcPath = ProviderFrpcPath.Get(CurrentProvider);
        if (!string.IsNullOrWhiteSpace(frpcPath) && File.Exists(frpcPath))
            return frpcPath;

        ConsoleUi.Info($"未找到 {CurrentProvider.DisplayName} frpc，正在自动下载...");
        using var downloader = new FrpcDownloader(CurrentProvider) { ForceGitHub = options.ForceGitHub };
        var downloadResult = await downloader.DownloadAsync(_cts.Token);
        if (!downloadResult.Success)
        {
            ConsoleUi.Error($"下载 frpc 失败: {downloadResult.Message}");
            ConsoleUi.Hint(options.ForceGitHub
                ? "可以使用 --frpc-path 指定本地已有的 frpc"
                : "可以加上 --github 改用 GitHub 源重试，或使用 --frpc-path 指定本地已有的 frpc");
            return null;
        }

        ConsoleUi.Dim($"       frpc 位置: {downloadResult.FrpcPath}");
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
            ConsoleUi.Info("未指定隧道 ID，将启动全部隧道");
            options.ProxyIds.AddRange(tunnels.Select(t => t.Id));
        }

        return new TunnelResolutionResult(tunnels, null);
    }

    private static void ReportTunnelFetchFailure(string message)
    {
        ConsoleUi.Error($"获取隧道列表失败: {message}");
        if (!HasSavedLogin())
            ConsoleUi.Hint("请先运行 kairo-cli login 登录");
        else
            ConsoleUi.Hint("请检查网络连接；如果登录已失效，请运行 kairo-cli login 重新登录");
    }

    private static void ShowNoTunnels()
    {
        ConsoleUi.Info($"{CurrentProvider.DisplayName} 账号下还没有隧道");
        ConsoleUi.Hint($"前往 {CurrentProvider.DashboardUrl} 创建隧道后再试");
    }

    private sealed record TunnelResolutionResult(List<Tunnel>? Items, int? ExitCode);
}
