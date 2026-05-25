using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Kairo.Core;
using Kairo.Core.Logging;
using Kairo.Core.Models;
using Kairo.Core.Providers;

namespace Kairo.Utils
{
    /// <summary>
    /// 统一的 API 客户端，自动处理 Authorization header 和 token 管理
    /// </summary>
    public sealed class ApiClient : IDisposable
    {
        private static readonly Lazy<ApiClient> _instance = new(() => new ApiClient());
        
        /// <summary>
        /// 获取全局单例实例
        /// </summary>
        public static ApiClient Instance => _instance.Value;

        private readonly HttpClient _http;
        private readonly HttpClient _httpExternal;
        private bool _disposed;

        private const string BrowserUserAgent = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/144.0.0.0 Safari/537.36 Edg/144.0.0.0";

        /// <summary>
        /// 创建新的 ApiClient 实例
        /// </summary>
        public ApiClient()
        {
            // For our own API: use Kairo/{version}
            _http = new HttpClient();
            _http.DefaultRequestHeaders.UserAgent.ParseAdd($"Kairo/{Global.Version}");
            
            // For external APIs (GitHub): use browser User-Agent
            _httpExternal = new HttpClient();
            _httpExternal.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",BrowserUserAgent);
        }

        /// <summary>
        /// 检查是否已登录（有有效的 AccessToken 和用户 ID）
        /// </summary>
        public static bool IsLoggedIn => 
            !string.IsNullOrWhiteSpace(Global.Config.AccessToken) && Global.Config.ID != 0;

        /// <summary>
        /// 确保已登录，否则抛出异常
        /// </summary>
        /// <exception cref="InvalidOperationException">未登录时抛出</exception>
        public static void EnsureLoggedIn()
        {
            if (!IsLoggedIn)
                throw new InvalidOperationException("未登录或令牌缺失");
        }

        /// <summary>
        /// 尝试确保已登录
        /// </summary>
        /// <returns>是否已登录</returns>
        public static bool TryEnsureLoggedIn(out string? errorMessage)
        {
            if (IsLoggedIn)
            {
                errorMessage = null;
                return true;
            }
            errorMessage = "未登录或令牌缺失";
            return false;
        }

        private void ConfigureAuth()
        {
            _http.DefaultRequestHeaders.Remove("Authorization");
            if (!string.IsNullOrWhiteSpace(Global.Config.AccessToken))
            {
                _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {Global.Config.AccessToken}");
            }
        }

        public string BuildOAuthUrl(string mode = "code", string codeChallenge = "") => Global.CurrentProvider.BuildOAuthUrl(new OAuthRequest
        {
            Scopes = Global.CurrentProvider.Type == FrpProviderType.Lolia ? "all node:read" : "User,Node,Tunnel,Sign",
            RedirectUri = Global.CurrentProvider.Type == FrpProviderType.Locyan && mode == "callback"
                ? $"{Global.CurrentProvider.DashboardUrl}/auth/oauth/redirect-localhost?port={Global.OAuthPort}&ssl=false&path=/oauth/callback"
                : string.Empty,
            Mode = mode,
            CallbackPort = Global.OAuthPort,
            CodeChallenge = codeChallenge,
            CodeChallengeMethod = string.IsNullOrWhiteSpace(codeChallenge) ? string.Empty : "S256"
        });

        public async Task<FrpApiResult<FrpLoginResult>> LoginWithRefreshTokenAsync(string refreshToken, CancellationToken ct = default)
        {
            ConfigureAuth();
            var result = await Global.CurrentProvider.LoginWithRefreshTokenAsync(_http, refreshToken, ct);
            if (result.Success && result.Data != null)
            {
                Global.Config.ID = result.Data.UserId;
                Global.Config.AccessToken = result.Data.AccessToken;
                Global.Config.RefreshToken = result.Data.RefreshToken;
                Global.Config.Username = result.Data.User.Username;
                Global.Config.FrpToken = result.Data.FrpToken;
                ProviderAuth.SaveCurrent(save: false);
            }
            return result;
        }

        public async Task<FrpApiResult<string>> ExchangeCodeForRefreshTokenAsync(string code, string codeVerifier = "", CancellationToken ct = default)
        {
            ConfigureAuth();
            var redirectUri = $"http://127.0.0.1:{Global.OAuthPort}/oauth/callback";
            return await Global.CurrentProvider.ExchangeCodeForRefreshTokenAsync(_http, code, redirectUri, codeVerifier, ct);
        }

        public async Task<FrpApiResult<string>> GetAnnouncementAsync(CancellationToken ct = default)
        {
            ConfigureAuth();
            return await Global.CurrentProvider.GetAnnouncementAsync(_http, ct);
        }

        public async Task<FrpApiResult<FrpSignStatus>> GetSignStatusAsync(CancellationToken ct = default)
        {
            ConfigureAuth();
            return await Global.CurrentProvider.GetSignStatusAsync(_http, Global.Config.ID, ct);
        }

        public async Task<FrpApiResult<FrpSignResult>> SignAsync(CancellationToken ct = default)
        {
            ConfigureAuth();
            return await Global.CurrentProvider.SignAsync(_http, Global.Config.ID, ct);
        }

        public async Task<FrpApiResult<IReadOnlyList<FrpTunnel>>> GetTunnelsAsync(CancellationToken ct = default)
        {
            ConfigureAuth();
            return await Global.CurrentProvider.GetTunnelsAsync(_http, Global.Config.ID, ct);
        }

        public async Task<FrpApiResult<IReadOnlyList<FrpNode>>> GetNodesAsync(CancellationToken ct = default)
        {
            ConfigureAuth();
            return await Global.CurrentProvider.GetNodesAsync(_http, Global.Config.ID, ct);
        }

        public async Task<FrpApiResult<int>> GetRandomPortAsync(int nodeId, CancellationToken ct = default)
        {
            ConfigureAuth();
            return await Global.CurrentProvider.GetRandomPortAsync(_http, Global.Config.ID, nodeId, ct);
        }

        public async Task<FrpApiResult<CreateFrpTunnelResult>> CreateTunnelAsync(CreateFrpTunnelRequest request, CancellationToken ct = default)
        {
            ConfigureAuth();
            return await Global.CurrentProvider.CreateTunnelAsync(_http, Global.Config.ID, request, ct);
        }

        public async Task<FrpApiResult<object>> DeleteTunnelAsync(FrpTunnel tunnel, CancellationToken ct = default)
        {
            ConfigureAuth();
            return await Global.CurrentProvider.DeleteTunnelAsync(_http, Global.Config.ID, tunnel, ct);
        }

        public async Task<FrpApiResult<FrpcConfigResult>> GetFrpcConfigAsync(FrpTunnel tunnel, CancellationToken ct = default)
        {
            ConfigureAuth();
            return await Global.CurrentProvider.GetFrpcConfigAsync(_http, tunnel, ct);
        }

        #region GET 请求

        /// <summary>
        /// 发送 GET 请求（自动添加 Authorization header）
        /// </summary>
        public async Task<HttpResponseMessage> GetAsync(string url, CancellationToken ct = default)
        {
            ConfigureAuth();
            return await _http.GetAsyncLogged(url, ct);
        }

        /// <summary>
        /// 发送 GET 请求并读取字符串响应
        /// </summary>
        public async Task<string> GetStringAsync(string url, CancellationToken ct = default)
        {
            ConfigureAuth();
            return await _http.GetStringAsyncLogged(url, ct);
        }

        /// <summary>
        /// 发送 GET 请求并读取字节数组响应
        /// </summary>
        public async Task<byte[]> GetByteArrayAsync(string url, CancellationToken ct = default)
        {
            ConfigureAuth();
            return await _http.GetByteArrayAsyncLogged(url, ct);
        }

        /// <summary>
        /// 发送无需认证的 GET 请求
        /// </summary>
        public async Task<HttpResponseMessage> GetWithoutAuthAsync(string url, CancellationToken ct = default)
        {
            return await _httpExternal.GetAsyncLogged(url, ct);
        }

        /// <summary>
        /// 发送无需认证的 GET 请求并读取字符串
        /// </summary>
        public async Task<string> GetStringWithoutAuthAsync(string url, CancellationToken ct = default)
        {
            return await _httpExternal.GetStringAsyncLogged(url, ct);
        }

        /// <summary>
        /// 发送无需认证的 GET 请求并读取字节数组
        /// </summary>
        public async Task<byte[]> GetByteArrayWithoutAuthAsync(string url, CancellationToken ct = default)
        {
            return await _httpExternal.GetByteArrayAsyncLogged(url, ct);
        }

        #endregion

        #region POST 请求

        /// <summary>
        /// 发送 POST 请求（自动添加 Authorization header）
        /// </summary>
        public async Task<HttpResponseMessage> PostAsync(string url, HttpContent? content = null, CancellationToken ct = default)
        {
            ConfigureAuth();
            return await _http.PostAsyncLogged(url, content, ct);
        }

        /// <summary>
        /// 发送无需认证的 POST 请求
        /// </summary>
        public async Task<HttpResponseMessage> PostWithoutAuthAsync(string url, HttpContent? content = null, CancellationToken ct = default)
        {
            return await _httpExternal.PostAsyncLogged(url, content, ct);
        }

        #endregion

        #region PUT 请求

        /// <summary>
        /// 发送 PUT 请求（自动添加 Authorization header）
        /// </summary>
        public async Task<HttpResponseMessage> PutAsync(string url, HttpContent? content = null, CancellationToken ct = default)
        {
            ConfigureAuth();
            return await _http.PutAsyncLogged(url, content, ct);
        }

        /// <summary>
        /// 发送无需认证的 PUT 请求
        /// </summary>
        public async Task<HttpResponseMessage> PutWithoutAuthAsync(string url, HttpContent? content = null, CancellationToken ct = default)
        {
            return await _httpExternal.PutAsyncLogged(url, content, ct);
        }

        #endregion

        #region DELETE 请求

        /// <summary>
        /// 发送 DELETE 请求（自动添加 Authorization header）
        /// </summary>
        public async Task<HttpResponseMessage> DeleteAsync(string url, CancellationToken ct = default)
        {
            ConfigureAuth();
            return await _http.DeleteAsyncLogged(url, ct);
        }

        /// <summary>
        /// 发送无需认证的 DELETE 请求
        /// </summary>
        public async Task<HttpResponseMessage> DeleteWithoutAuthAsync(string url, CancellationToken ct = default)
        {
            return await _httpExternal.DeleteAsyncLogged(url, ct);
        }

        #endregion

        #region PATCH 请求

        /// <summary>
        /// 发送 PATCH 请求（自动添加 Authorization header）
        /// </summary>
        public async Task<HttpResponseMessage> PatchAsync(string url, HttpContent? content = null, CancellationToken ct = default)
        {
            ConfigureAuth();
            return await _http.PatchAsyncLogged(url, content, ct);
        }

        /// <summary>
        /// 发送无需认证的 PATCH 请求
        /// </summary>
        public async Task<HttpResponseMessage> PatchWithoutAuthAsync(string url, HttpContent? content = null, CancellationToken ct = default)
        {
            return await _httpExternal.PatchAsyncLogged(url, content, ct);
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _http.Dispose();
            _httpExternal.Dispose();
        }

        #endregion
    }
}
