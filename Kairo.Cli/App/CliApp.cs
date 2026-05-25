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

    private IFrpProvider CurrentProvider => FrpProviderRegistry.Get(CliConfigManager.Config.ProviderId);

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
        CliHelpWriter.ShowBanner();
        var options = CliArgumentParser.Parse(_args);

        if (options.ShowHelp)
        {
            CliHelpWriter.ShowHelp();
            return 0;
        }

        if (options.ShowVersion)
        {
            CliHelpWriter.ShowVersion();
            return 0;
        }

        if (options.GetOAuthUrl)
        {
            _oauthFlow.ShowOAuthUrl();
            return 0;
        }

        _apiClient = new ApiClient();
        if (!await HandleLoginAsync(options))
            return 1;

        var frpToken = ResolveFrpToken(options);
        if (string.IsNullOrWhiteSpace(frpToken) && CurrentProvider.Type != FrpProviderType.Lolia)
        {
            var loginResult = await HandleMissingFrpTokenAsync(options);
            if (!loginResult.Success) return loginResult.ExitCode;
            frpToken = loginResult.FrpToken;
        }

        var frpcPath = await ResolveFrpcPathAsync(options);
        if (string.IsNullOrWhiteSpace(frpcPath))
            return 1;

        var tunnels = await ResolveTunnelsAsync(options);
        if (tunnels.ExitCode.HasValue)
            return tunnels.ExitCode.Value;

        Logger.Info($"开始启动隧道，数量: {options.ProxyIds.Count}");
        return await _processRunner.StartAsync(frpcPath, frpToken ?? string.Empty, options.ProxyIds, tunnels.Items, _apiClient);
    }

    private async Task<bool> HandleLoginAsync(CliOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.OAuthCode))
        {
            if (CurrentProvider.Type == FrpProviderType.Lolia)
            {
                Console.WriteLine("[错误] LoliaFRP OAuth 使用 PKCE，请使用交互式登录以保持 code_verifier");
                return false;
            }
            return await _oauthFlow.PerformLoginWithCodeAsync(_apiClient!, options.OAuthCode);
        }

        if (!string.IsNullOrWhiteSpace(options.RefreshToken))
            return await _oauthFlow.PerformLoginWithRefreshTokenAsync(_apiClient!, options.RefreshToken);

        return true;
    }

    private static string ResolveFrpToken(CliOptions options) => !string.IsNullOrWhiteSpace(options.FrpToken)
        ? options.FrpToken
        : CliConfigManager.Config.FrpToken;

    private async Task<LoginRequirementResult> HandleMissingFrpTokenAsync(CliOptions options)
    {
        if (options.InteractiveMode)
        {
            if (!await _oauthFlow.InteractiveLoginAsync(_apiClient!))
                return new LoginRequirementResult(false, 1, string.Empty);
            return new LoginRequirementResult(true, 0, CliConfigManager.Config.FrpToken);
        }

        Console.WriteLine("[警告] 未找到 FRP Token，请先通过 OAuth 登录");
        Console.WriteLine();
        _oauthFlow.ShowOAuthUrl();
        Console.WriteLine();
        Console.WriteLine("获取授权码后，使用以下命令登录:");
        Console.WriteLine("  kairo-cli --code <your_code>");
        Console.WriteLine();
        return new LoginRequirementResult(false, 1, string.Empty);
    }

    private async Task<string?> ResolveFrpcPathAsync(CliOptions options)
    {
        var frpcPath = options.FrpcPath ?? ProviderFrpcPath.Get(CurrentProvider);
        if (!string.IsNullOrWhiteSpace(frpcPath) && File.Exists(frpcPath))
            return frpcPath;

        Console.WriteLine("[信息] 未找到 frpc 可执行文件，正在下载...");
        using var downloader = new FrpcDownloader { ForceGitHub = options.ForceGitHub };
        var downloadResult = await downloader.DownloadAsync(_cts.Token);
        if (!downloadResult.Success)
        {
            Console.WriteLine($"[错误] 下载 frpc 失败: {downloadResult.Message}");
            return null;
        }

        Console.WriteLine($"[成功] frpc 已下载到: {downloadResult.FrpcPath}");
        Console.WriteLine();
        return downloadResult.FrpcPath;
    }

    private async Task<TunnelResolutionResult> ResolveTunnelsAsync(CliOptions options)
    {
        List<Tunnel>? tunnels = null;
        if (options.ListProxies || options.ProxyIds.Count == 0 || CurrentProvider.Type == FrpProviderType.Lolia)
        {
            tunnels = await FetchTunnelsAsync();
            if (tunnels == null || tunnels.Count == 0)
            {
                Console.WriteLine("[信息] 没有可用的隧道");
                return new TunnelResolutionResult(null, 0);
            }

            CliTunnelSelector.ShowTunnelList(tunnels);
            if (options.ListProxies)
            {
                Console.WriteLine("使用以下命令启动隧道:");
                Console.WriteLine("  kairo-cli --proxy <id1,id2,...>");
                return new TunnelResolutionResult(tunnels, 0);
            }

            if (options.InteractiveMode)
            {
                var selectedIds = CliTunnelSelector.InteractiveSelectTunnels(tunnels);
                if (selectedIds == null || selectedIds.Count == 0)
                {
                    Console.WriteLine("[错误] 未选择有效的隧道");
                    return new TunnelResolutionResult(tunnels, 1);
                }
                foreach (var id in selectedIds)
                    options.ProxyIds.Add(id);
            }
            else
            {
                Console.WriteLine("[信息] 未指定隧道 ID，将启动全部隧道");
                foreach (var tunnel in tunnels)
                    options.ProxyIds.Add(tunnel.Id);
            }
        }

        return new TunnelResolutionResult(tunnels, null);
    }

    private async Task<List<Tunnel>?> FetchTunnelsAsync()
    {
        await _apiClient!.EnsureLoggedInAsync();
        return await _apiClient.GetTunnelsAsync();
    }

    private sealed record LoginRequirementResult(bool Success, int ExitCode, string FrpToken);
    private sealed record TunnelResolutionResult(List<Tunnel>? Items, int? ExitCode);
}
