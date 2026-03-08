using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kairo.Core;
using Kairo.Core.Models;
using Kairo.Cli.Configuration;
using Kairo.Cli.Utils;

namespace Kairo.Cli.Services;

/// <summary>
/// API 客户端服务
/// </summary>
public class ApiClient : IDisposable
{
    private readonly HttpClient _http = new();
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
    public async Task<LoginResult> ExchangeCodeForRefreshTokenAsync(string code)
    {
        Logger.MethodEntry($"code长度={code.Length}");
        var sw = Stopwatch.StartNew();
        
        try
        {
            using var formContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("code", code)
            });

            Logger.HttpRequest("POST", ApiEndpoints.GetRefreshToken);
            var response = await _http.PostAsync(ApiEndpoints.GetRefreshToken, formContent);
            sw.Stop();
            Logger.HttpResponse("POST", ApiEndpoints.GetRefreshToken, (int)response.StatusCode, sw.ElapsedMilliseconds);
            
            var body = await response.Content.ReadAsStringAsync();
            Logger.Debug($"响应内容长度: {body.Length} 字节");
            
            var json = JsonNode.Parse(body);
            var status = json?["status"]?.GetValue<int>() ?? 0;
            Logger.Debug($"API 状态码: {status}");
            
            if (status != 200)
            {
                var message = json?["message"]?.GetValue<string>() ?? "未知错误";
                Logger.Error($"获取 Refresh Token 失败: status={status}, message={message}");
                Logger.MethodExit("失败");
                return new LoginResult { Success = false, Message = $"获取 Refresh Token 失败: {message}" };
            }

            var refreshToken = json?["data"]?["refresh_token"]?.GetValue<string>();
            if (string.IsNullOrEmpty(refreshToken))
            {
                Logger.Error("API 返回的 Refresh Token 为空");
                Logger.MethodExit("失败 (空token)");
                return new LoginResult { Success = false, Message = "API 返回的 Refresh Token 为空" };
            }

            Logger.Info($"成功获取 Refresh Token (长度: {refreshToken.Length})");
            Logger.Debug("继续使用 Refresh Token 登录");
            
            // 使用获取到的 refresh token 继续登录
            var result = await LoginWithRefreshTokenAsync(refreshToken);
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

    /// <summary>
    /// 使用 Refresh Token 登录
    /// </summary>
    public async Task<LoginResult> LoginWithRefreshTokenAsync(string refreshToken)
    {
        Logger.MethodEntry($"refreshToken长度={refreshToken.Length}");
        var sw = Stopwatch.StartNew();
        
        try
        {
            using var formContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("app_id", AppConstants.APPID.ToString()),
                new KeyValuePair<string, string>("refresh_token", refreshToken)
            });

            Logger.HttpRequest("POST", ApiEndpoints.GetAccessToken);
            var response = await _http.PostAsync(ApiEndpoints.GetAccessToken, formContent);
            sw.Stop();
            Logger.HttpResponse("POST", ApiEndpoints.GetAccessToken, (int)response.StatusCode, sw.ElapsedMilliseconds);
            
            var accessBody = await response.Content.ReadAsStringAsync();
            Logger.Debug($"响应内容长度: {accessBody.Length} 字节");
            
            var json = JsonNode.Parse(accessBody);
            var accessStatus = json?["status"]?.GetValue<int>() ?? 0;
            Logger.Debug($"API 状态码: {accessStatus}");
            
            if (accessStatus != 200)
            {
                var message = json?["message"]?.GetValue<string>() ?? "未知错误";
                Logger.Error($"获取 Access Token 失败: status={accessStatus}, message={message}");
                Logger.MethodExit("失败");
                return new LoginResult { Success = false, Message = $"API状态: {accessStatus} {message}" };
            }

            var dataNode = json?["data"];
            CliConfigManager.Config.ID = dataNode?["user_id"]?.GetValue<int>() ?? 0;
            CliConfigManager.Config.AccessToken = dataNode?["access_token"]?.GetValue<string>() ?? "";
            CliConfigManager.Config.RefreshToken = refreshToken;
            
            Logger.Info($"成功获取 Access Token, 用户ID: {CliConfigManager.Config.ID}");
            Logger.Config("更新", $"ID={CliConfigManager.Config.ID}, AccessToken长度={CliConfigManager.Config.AccessToken.Length}");

            // 获取用户信息
            _http.DefaultRequestHeaders.Remove("Authorization");
            _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {CliConfigManager.Config.AccessToken}");
            Logger.Debug("设置 Authorization 头");

            var userUrl = $"{ApiEndpoints.GetUserInfo}?user_id={CliConfigManager.Config.ID}";
            sw.Restart();
            Logger.HttpRequest("GET", userUrl);
            var userResp = await _http.GetAsync(userUrl);
            sw.Stop();
            Logger.HttpResponse("GET", userUrl, (int)userResp.StatusCode, sw.ElapsedMilliseconds);
            
            var userBody = await userResp.Content.ReadAsStringAsync();
            Logger.Debug($"用户信息响应长度: {userBody.Length} 字节");
            
            var userJson = JsonNode.Parse(userBody);
            var userNode = userJson?["data"];

            var username = userNode?["username"]?.GetValue<string>() ?? "未知用户";
            CliConfigManager.Config.Username = username;
            Logger.Info($"获取用户名: {username}");

            // 获取 FRP Token
            var frpUrl = $"{ApiEndpoints.GetFrpToken}?user_id={CliConfigManager.Config.ID}";
            sw.Restart();
            Logger.HttpRequest("GET", frpUrl);
            var frpResp = await _http.GetAsync(frpUrl);
            sw.Stop();
            Logger.HttpResponse("GET", frpUrl, (int)frpResp.StatusCode, sw.ElapsedMilliseconds);
            
            var frpBody = await frpResp.Content.ReadAsStringAsync();
            Logger.Debug($"FRP Token 响应长度: {frpBody.Length} 字节");
            
            var frpJson = JsonNode.Parse(frpBody);
            var frpToken = frpJson?["data"]?["token"]?.GetValue<string>() ?? "";

            CliConfigManager.Config.FrpToken = frpToken;
            Logger.Info($"获取 FRP Token (长度: {frpToken.Length})");
            
            Logger.Config("保存", "写入配置文件");
            CliConfigManager.Save();

            _isLoggedIn = true;
            Logger.Info($"登录成功: 用户={username}");
            Logger.MethodExit("成功");
            return new LoginResult { Success = true, Username = username, FrpToken = frpToken };
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
        var sw = Stopwatch.StartNew();
        
        try
        {
            if (!await EnsureLoggedInAsync())
            {
                Logger.Error("未登录，无法获取隧道列表");
                Console.WriteLine("[错误] 未登录，无法获取隧道列表");
                Logger.MethodExit("null (未登录)");
                return null;
            }

            var url = $"{ApiEndpoints.GetAllProxy}{CliConfigManager.Config.ID}";
            Logger.HttpRequest("GET", url);
            var response = await _http.GetAsync(url);
            sw.Stop();
            Logger.HttpResponse("GET", url, (int)response.StatusCode, sw.ElapsedMilliseconds);
            
            var body = await response.Content.ReadAsStringAsync();
            Logger.Debug($"响应内容长度: {body.Length} 字节");
            
            var json = JsonNode.Parse(body);

            var status = json?["status"]?.GetValue<int>() ?? 0;
            Logger.Debug($"API 状态码: {status}");
            
            if (status != 200)
            {
                var message = json?["message"]?.GetValue<string>() ?? "未知错误";
                Logger.Error($"获取隧道列表失败: status={status}, message={message}");
                Console.WriteLine($"[错误] 获取隧道列表失败: {message}");
                Logger.MethodExit("null (API错误)");
                return null;
            }

            // API 返回格式: { "data": { "list": [...] } }
            var dataArray = json?["data"]?["list"] as JsonArray;
            if (dataArray == null)
            {
                Logger.Debug("API 返回的隧道列表为空");
                Logger.MethodExit("空列表");
                return new List<Tunnel>();
            }

            Logger.Debug($"API 返回 {dataArray.Count} 个隧道");
            
            var tunnels = new List<Tunnel>();
            var parseErrors = 0;
            foreach (var item in dataArray)
            {
                if (item == null) continue;
                try
                {
                    var tunnel = JsonSerializer.Deserialize(item.ToJsonString(), TunnelJsonContext.Default.Tunnel);
                    if (tunnel != null)
                    {
                        tunnels.Add(tunnel);
                        Logger.Debug($"  解析隧道: id={tunnel.Id}, name={tunnel.ProxyName}, type={tunnel.ProxyType}");
                    }
                }
                catch (Exception ex)
                {
                    parseErrors++;
                    Logger.Warning($"解析隧道失败: {ex.Message}");
                }
            }

            if (parseErrors > 0)
            {
                Logger.Warning($"有 {parseErrors} 个隧道解析失败");
            }

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
}