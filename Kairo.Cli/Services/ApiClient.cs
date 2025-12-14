using System.Text.Json;
using System.Text.Json.Nodes;
using Kairo.Core;
using Kairo.Core.Models;
using Kairo.Cli.Configuration;

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
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"Kairo-CLI/{AppConstants.Version}");
    }

    public void Dispose() => _http.Dispose();

    /// <summary>
    /// 使用 OAuth Code 获取 Refresh Token
    /// </summary>
    public async Task<LoginResult> ExchangeCodeForRefreshTokenAsync(string code)
    {
        try
        {
            var formContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("code", code)
            });

            var response = await _http.PostAsync(ApiEndpoints.GetRefreshToken, formContent);
            var body = await response.Content.ReadAsStringAsync();
            var json = JsonNode.Parse(body);

            var status = json?["status"]?.GetValue<int>() ?? 0;
            if (status != 200)
            {
                var message = json?["message"]?.GetValue<string>() ?? "未知错误";
                return new LoginResult { Success = false, Message = $"获取 Refresh Token 失败: {message}" };
            }

            var refreshToken = json?["data"]?["refresh_token"]?.GetValue<string>();
            if (string.IsNullOrEmpty(refreshToken))
            {
                return new LoginResult { Success = false, Message = "API 返回的 Refresh Token 为空" };
            }

            // 使用获取到的 refresh token 继续登录
            return await LoginWithRefreshTokenAsync(refreshToken);
        }
        catch (Exception ex)
        {
            return new LoginResult { Success = false, Message = ex.Message };
        }
    }

    /// <summary>
    /// 使用 Refresh Token 登录
    /// </summary>
    public async Task<LoginResult> LoginWithRefreshTokenAsync(string refreshToken)
    {
        try
        {
            var formContent = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("app_id", AppConstants.APPID.ToString()),
                new KeyValuePair<string, string>("refresh_token", refreshToken)
            });

            var response = await _http.PostAsync(ApiEndpoints.GetAccessToken, formContent);
            var accessBody = await response.Content.ReadAsStringAsync();
            var json = JsonNode.Parse(accessBody);

            var accessStatus = json?["status"]?.GetValue<int>() ?? 0;
            if (accessStatus != 200)
            {
                var message = json?["message"]?.GetValue<string>() ?? "未知错误";
                return new LoginResult { Success = false, Message = $"API状态: {accessStatus} {message}" };
            }

            var dataNode = json?["data"];
            CliConfigManager.Config.ID = dataNode?["user_id"]?.GetValue<int>() ?? 0;
            CliConfigManager.Config.AccessToken = dataNode?["access_token"]?.GetValue<string>() ?? "";
            CliConfigManager.Config.RefreshToken = refreshToken;

            // 获取用户信息
            _http.DefaultRequestHeaders.Remove("Authorization");
            _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {CliConfigManager.Config.AccessToken}");

            var userUrl = $"{ApiEndpoints.GetUserInfo}?user_id={CliConfigManager.Config.ID}";
            var userResp = await _http.GetAsync(userUrl);
            var userBody = await userResp.Content.ReadAsStringAsync();
            var userJson = JsonNode.Parse(userBody);
            var userNode = userJson?["data"];

            var username = userNode?["username"]?.GetValue<string>() ?? "未知用户";
            CliConfigManager.Config.Username = username;

            // 获取 FRP Token
            var frpUrl = $"{ApiEndpoints.GetFrpToken}?user_id={CliConfigManager.Config.ID}";
            var frpResp = await _http.GetAsync(frpUrl);
            var frpBody = await frpResp.Content.ReadAsStringAsync();
            var frpJson = JsonNode.Parse(frpBody);
            var frpToken = frpJson?["data"]?["token"]?.GetValue<string>() ?? "";

            CliConfigManager.Config.FrpToken = frpToken;
            CliConfigManager.Save();

            _isLoggedIn = true;
            return new LoginResult { Success = true, Username = username, FrpToken = frpToken };
        }
        catch (Exception ex)
        {
            return new LoginResult { Success = false, Message = ex.Message };
        }
    }

    /// <summary>
    /// 确保已登录
    /// </summary>
    public async Task<bool> EnsureLoggedInAsync()
    {
        if (_isLoggedIn) return true;

        if (string.IsNullOrWhiteSpace(CliConfigManager.Config.RefreshToken))
            return false;

        if (string.IsNullOrWhiteSpace(CliConfigManager.Config.AccessToken))
        {
            var result = await LoginWithRefreshTokenAsync(CliConfigManager.Config.RefreshToken);
            return result.Success;
        }

        _http.DefaultRequestHeaders.Remove("Authorization");
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {CliConfigManager.Config.AccessToken}");
        _isLoggedIn = true;
        return true;
    }

    /// <summary>
    /// 获取隧道列表
    /// </summary>
    public async Task<List<Tunnel>?> GetTunnelsAsync()
    {
        try
        {
            if (!await EnsureLoggedInAsync())
            {
                Console.WriteLine("[错误] 未登录，无法获取隧道列表");
                return null;
            }

            var url = $"{ApiEndpoints.GetAllProxy}{CliConfigManager.Config.ID}";
            var response = await _http.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();
            var json = JsonNode.Parse(body);

            var status = json?["status"]?.GetValue<int>() ?? 0;
            if (status != 200)
            {
                var message = json?["message"]?.GetValue<string>() ?? "未知错误";
                Console.WriteLine($"[错误] 获取隧道列表失败: {message}");
                return null;
            }

            // API 返回格式: { "data": { "list": [...] } }
            var dataArray = json?["data"]?["list"] as JsonArray;
            if (dataArray == null) return new List<Tunnel>();

            var tunnels = new List<Tunnel>();
            foreach (var item in dataArray)
            {
                if (item == null) continue;
                try
                {
                    var tunnel = JsonSerializer.Deserialize(item.ToJsonString(), TunnelJsonContext.Default.Tunnel);
                    if (tunnel != null) tunnels.Add(tunnel);
                }
                catch { }
            }

            return tunnels;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[错误] 获取隧道列表异常: {ex.Message}");
            return null;
        }
    }
}
