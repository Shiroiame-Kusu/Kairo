using Kairo.Core;
using Kairo.Core.Models;
using Kairo.Core.Providers;
using Kairo.Cli.Configuration;
using Kairo.Cli.Utils;
using Kairo.Core.Localization;

namespace Kairo.Cli.Services;

/// <summary>
/// API 客户端服务
/// </summary>
public class ApiClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
    private readonly IFrpProvider _provider;
    private bool _isLoggedIn;
    private bool _refreshedThisSession;

    public class LoginResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Username { get; set; }
        public string? FrpToken { get; set; }

        /// <summary>是否因网络问题（无法连接、超时）失败，而非服务端拒绝</summary>
        public bool IsNetworkError { get; set; }
    }

    public ApiClient(IFrpProvider provider)
    {
        Logger.Debug($"创建 ApiClient 实例: provider={provider.Id}");
        _provider = provider;
        ProviderAuth.Apply(_provider);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"Kairo-{AppConstants.Version}");
        Logger.Debug($"设置 User-Agent: Kairo-{AppConstants.Version}");
    }

    public IFrpProvider Provider => _provider;

    public void Dispose()
    {
        Logger.Debug("释放 ApiClient 资源");
        _http.Dispose();
    }

    /// <summary>
    /// 使用 OAuth Code 获取 Refresh Token 并登录
    /// </summary>
    public async Task<LoginResult> ExchangeCodeForRefreshTokenAsync(string code, string codeVerifier = "", string? redirectUri = null)
    {
        Logger.MethodEntry($"code长度={code.Length}");
        try
        {
            redirectUri ??= _provider.Type == FrpProviderType.Lolia ? BuildLoopbackCallbackUri() : string.Empty;
            var tokenResult = await _provider.ExchangeCodeForRefreshTokenAsync(_http, code, redirectUri, codeVerifier);
            if (!tokenResult.Success || string.IsNullOrWhiteSpace(tokenResult.Data))
            {
                Logger.Error($"获取 Refresh Token 失败: code={tokenResult.Code}, message={tokenResult.Message}");
                Logger.MethodExit("失败");
                return new LoginResult { Success = false, Message = tokenResult.Message };
            }

            var result = await LoginWithRefreshTokenAsync(tokenResult.Data);
            Logger.MethodExit(result.Success ? "成功" : "失败");
            return result;
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, "ExchangeCodeForRefreshTokenAsync 发生异常");
            Logger.MethodExit("异常");
            return new LoginResult { Success = false, Message = ex.Message, IsNetworkError = IsNetworkException(ex) };
        }
    }

    private static string BuildLoopbackCallbackUri()
    {
        var port = CliConfigManager.Config.OAuthPort > 0 ? CliConfigManager.Config.OAuthPort : 10000;
        return $"http://127.0.0.1:{port}/oauth/callback";
    }

    /// <summary>
    /// 使用 Refresh Token 登录
    /// </summary>
    public async Task<LoginResult> LoginWithRefreshTokenAsync(string refreshToken)
    {
        Logger.MethodEntry($"refreshToken长度={refreshToken.Length}");
        _refreshedThisSession = true;
        try
        {
            var result = await _provider.LoginWithRefreshTokenAsync(_http, refreshToken);
            if (!result.Success || result.Data == null)
            {
                Logger.Error($"登录失败: code={result.Code}, message={result.Message}");
                Logger.MethodExit("失败");
                return new LoginResult { Success = false, Message = L.T("cli.api.status", result.Code, result.Message) };
            }

            CliConfigManager.Config.ID = result.Data.UserId;
            CliConfigManager.Config.AccessToken = result.Data.AccessToken;
            CliConfigManager.Config.RefreshToken = result.Data.RefreshToken;
            CliConfigManager.Config.Username = result.Data.User.Username;
            CliConfigManager.Config.FrpToken = result.Data.FrpToken;
            ProviderAuth.Save(_provider, save: false);
            CliConfigManager.Save();

            _isLoggedIn = true;
            Logger.Debug($"登录成功: 用户={result.Data.User.Username}");
            Logger.MethodExit("成功");
            return new LoginResult { Success = true, Username = result.Data.User.Username, FrpToken = result.Data.FrpToken };
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, "LoginWithRefreshTokenAsync 发生异常");
            Logger.MethodExit("异常");
            return new LoginResult { Success = false, Message = ex.Message, IsNetworkError = IsNetworkException(ex) };
        }
    }

    /// <summary>
    /// 确保已登录
    /// </summary>
    public async Task<bool> EnsureLoggedInAsync()
    {
        Logger.MethodEntry();

        if (_isLoggedIn)
        {
            Logger.MethodExit("true (已登录)");
            return true;
        }

        if (string.IsNullOrWhiteSpace(CliConfigManager.Config.RefreshToken))
        {
            Logger.Debug("RefreshToken 为空，无法登录");
            Logger.MethodExit("false (无RefreshToken)");
            return false;
        }

        if (string.IsNullOrWhiteSpace(CliConfigManager.Config.AccessToken))
        {
            Logger.Debug("AccessToken 为空，尝试使用 RefreshToken 登录");
            var result = await LoginWithRefreshTokenAsync(CliConfigManager.Config.RefreshToken);
            Logger.MethodExit(result.Success ? "true" : "false");
            return result.Success;
        }

        Logger.Debug("使用现有 AccessToken");
        _http.DefaultRequestHeaders.Remove("Authorization");
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {CliConfigManager.Config.AccessToken}");
        _isLoggedIn = true;
        Logger.MethodExit("true");
        return true;
    }

    /// <summary>
    /// 获取隧道列表（访问令牌过期时自动刷新一次后重试）
    /// </summary>
    public async Task<FrpApiResult<List<Tunnel>>> GetTunnelsAsync()
    {
        Logger.MethodEntry();
        try
        {
            if (!await EnsureLoggedInAsync())
                return FrpApiResult<List<Tunnel>>.Fail(401, L.T("cli.api.notSignedIn"));

            var result = await WithAuthRetryAsync(() => _provider.GetTunnelsAsync(_http, CliConfigManager.Config.ID));
            if (!result.Success)
            {
                Logger.Error($"获取隧道列表失败: code={result.Code}, message={result.Message}");
                return FrpApiResult<List<Tunnel>>.Fail(result.Code, result.Message);
            }

            var tunnels = (result.Data ?? Array.Empty<FrpTunnel>()).Select(ToTunnel).ToList();
            Logger.Debug($"成功获取 {tunnels.Count} 个隧道");
            return FrpApiResult<List<Tunnel>>.Ok(tunnels);
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, "GetTunnelsAsync 发生异常");
            return FrpApiResult<List<Tunnel>>.Fail(0, ex.Message);
        }
        finally
        {
            Logger.MethodExit();
        }
    }

    public async Task<FrpApiResult<FrpcConfigResult>> GetFrpcConfigAsync(Tunnel tunnel)
    {
        if (!await EnsureLoggedInAsync())
            return FrpApiResult<FrpcConfigResult>.Fail(401, L.T("cli.api.notSignedIn"));

        var frpTunnel = new FrpTunnel
        {
            Id = tunnel.Id,
            Name = tunnel.ProxyName,
            Token = tunnel.Token,
            Type = tunnel.ProxyType,
            LocalIp = tunnel.LocalIp,
            LocalPort = tunnel.LocalPort,
            RemotePort = tunnel.RemotePort,
            UseCompression = tunnel.UseCompression,
            UseEncryption = tunnel.UseEncryption,
            Domain = tunnel.Domain,
            SecretKey = tunnel.SecretKey
        };
        try
        {
            return await WithAuthRetryAsync(() => _provider.GetFrpcConfigAsync(_http, frpTunnel));
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, $"获取隧道 {tunnel.Id} 的 frpc 配置失败");
            return FrpApiResult<FrpcConfigResult>.Fail(0, ex.Message);
        }
    }

    /// <summary>
    /// 访问令牌失效（401/403）时使用 Refresh Token 重新登录并重试一次
    /// </summary>
    private async Task<FrpApiResult<T>> WithAuthRetryAsync<T>(Func<Task<FrpApiResult<T>>> call)
    {
        var result = await call();
        if (result.Success || result.Code is not (401 or 403) || _refreshedThisSession)
            return result;

        var refreshToken = CliConfigManager.Config.RefreshToken;
        if (string.IsNullOrWhiteSpace(refreshToken))
            return result;

        ConsoleUi.Info(L.T("cli.api.refreshing"));
        var login = await LoginWithRefreshTokenAsync(refreshToken);
        return login.Success ? await call() : result;
    }

    private static bool IsNetworkException(Exception ex) =>
        ex is HttpRequestException or TaskCanceledException || ex.InnerException is HttpRequestException;

    private static Tunnel ToTunnel(FrpTunnel tunnel) => new()
    {
        Id = tunnel.Id,
        ProxyName = tunnel.Name,
        Token = tunnel.Token,
        ProxyType = tunnel.Type,
        LocalIp = tunnel.LocalIp,
        LocalPort = tunnel.LocalPort,
        RemotePort = tunnel.RemotePort,
        UseCompression = tunnel.UseCompression,
        UseEncryption = tunnel.UseEncryption,
        Domain = tunnel.Domain,
        SecretKey = tunnel.SecretKey,
        NodeInfo = tunnel.Node == null ? null : new TunnelNode
        {
            Id = tunnel.Node.Id,
            Name = tunnel.Node.Name,
            Host = tunnel.Node.Host,
            Ip = tunnel.Node.Ip
        }
    };
}
