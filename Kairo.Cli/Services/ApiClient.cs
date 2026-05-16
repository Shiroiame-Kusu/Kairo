using System.Diagnostics;
using Kairo.Core;
using Kairo.Core.Models;
using Kairo.Core.Providers;
using Kairo.Cli.Configuration;
using Kairo.Cli.Utils;

namespace Kairo.Cli.Services;

/// <summary>
/// API 客户端服务
/// </summary>
public class ApiClient : IDisposable
{
    private readonly HttpClient _http = new();
    private readonly IFrpProvider _provider;
    private bool _isLoggedIn;

    public class LoginResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Username { get; set; }
        public string? FrpToken { get; set; }
    }

    public ApiClient()
    {
        Logger.Debug($"创建 ApiClient 实例");
        _provider = FrpProviderRegistry.Get(CliConfigManager.Config.ProviderId);
        ProviderAuth.Apply(_provider);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"Kairo-{AppConstants.Version}");
        Logger.Debug($"设置 User-Agent: Kairo-{AppConstants.Version}");
    }

    public void Dispose()
    {
        Logger.Debug("释放 ApiClient 资源");
        _http.Dispose();
    }

    /// <summary>
    /// 使用 OAuth Code 获取 Refresh Token
    /// </summary>
    public async Task<LoginResult> ExchangeCodeForRefreshTokenAsync(string code, string codeVerifier = "")
    {
        Logger.MethodEntry($"code长度={code.Length}");
        try
        {
            var redirectUri = _provider.Type == FrpProviderType.Lolia ? BuildLoopbackCallbackUri() : string.Empty;
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
            return new LoginResult { Success = false, Message = ex.Message };
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
        try
        {
            var result = await _provider.LoginWithRefreshTokenAsync(_http, refreshToken);
            if (!result.Success || result.Data == null)
            {
                Logger.Error($"登录失败: code={result.Code}, message={result.Message}");
                Logger.MethodExit("失败");
                return new LoginResult { Success = false, Message = $"API状态: {result.Code} {result.Message}" };
            }

            CliConfigManager.Config.ID = result.Data.UserId;
            CliConfigManager.Config.AccessToken = result.Data.AccessToken;
            CliConfigManager.Config.RefreshToken = result.Data.RefreshToken;
            CliConfigManager.Config.Username = result.Data.User.Username;
            CliConfigManager.Config.FrpToken = result.Data.FrpToken;
            ProviderAuth.Save(_provider, save: false);
            CliConfigManager.Save();

            _isLoggedIn = true;
            Logger.Info($"登录成功: 用户={result.Data.User.Username}");
            Logger.MethodExit("成功");
            return new LoginResult { Success = true, Username = result.Data.User.Username, FrpToken = result.Data.FrpToken };
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, "LoginWithRefreshTokenAsync 发生异常");
            Logger.MethodExit("异常");
            return new LoginResult { Success = false, Message = ex.Message };
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
            Logger.Debug("已登录（缓存）");
            Logger.MethodExit("true (已登录)");
            return true;
        }

        if (string.IsNullOrWhiteSpace(CliConfigManager.Config.RefreshToken))
        {
            Logger.Warning("RefreshToken 为空，无法登录");
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
    /// 获取隧道列表
    /// </summary>
    public async Task<List<Tunnel>?> GetTunnelsAsync()
    {
        Logger.MethodEntry();
        try
        {
            if (!await EnsureLoggedInAsync())
            {
                Logger.Error("未登录，无法获取隧道列表");
                Console.WriteLine("[错误] 未登录，无法获取隧道列表");
                Logger.MethodExit("null (未登录)");
                return null;
            }

            var result = await _provider.GetTunnelsAsync(_http, CliConfigManager.Config.ID);
            if (!result.Success)
            {
                Logger.Error($"获取隧道列表失败: code={result.Code}, message={result.Message}");
                Console.WriteLine($"[错误] 获取隧道列表失败: {result.Message}");
                Logger.MethodExit("null (API错误)");
                return null;
            }

            var tunnels = (result.Data ?? Array.Empty<FrpTunnel>()).Select(ToTunnel).ToList();
            Logger.Info($"成功获取 {tunnels.Count} 个隧道");
            Logger.MethodExit($"{tunnels.Count} 个隧道");
            return tunnels;
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, "GetTunnelsAsync 发生异常");
            Console.WriteLine($"[错误] 获取隧道列表异常: {ex.Message}");
            Logger.MethodExit("null (异常)");
            return null;
        }
    }

    public async Task<FrpApiResult<FrpcConfigResult>> GetFrpcConfigAsync(Tunnel tunnel)
    {
        if (!await EnsureLoggedInAsync())
            return FrpApiResult<FrpcConfigResult>.Fail(0, "未登录");

        return await _provider.GetFrpcConfigAsync(_http, new FrpTunnel
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
        });
    }

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