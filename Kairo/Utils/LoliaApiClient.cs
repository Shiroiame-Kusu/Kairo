using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Kairo.Models;
using Kairo.Models.Api;
using Kairo.Utils.Logger;
using Kairo.Utils.Serialization;

namespace Kairo.Utils;

/// <summary>
/// Centralised HTTP client for the Lolia-Center v1 API.
/// Base URL: <c>https://api.lolia.link/api/v1</c>
///
/// All public methods return <see cref="ApiResponse{T}"/> so callers can
/// inspect <c>Code</c>, <c>Msg</c> and typed <c>Data</c> uniformly.
/// </summary>
internal static class LoliaApiClient
{
    private const string DefaultOAuthScope = "all node:read";

    // ── shared HttpClient (thread-safe for concurrent calls) ──────────
    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri(Global.LoliaApi.TrimEnd('/') + "/"),
        Timeout = TimeSpan.FromSeconds(30),
    };

    // ──────────────────────────  Helpers  ──────────────────────────

    private static string SerializeBody(object body)
        => body switch
        {
            CreateTunnelRequest req => JsonSerializer.Serialize(req, AppJsonContext.Default.CreateTunnelRequest),
            CreateOAuthAppRequest req => JsonSerializer.Serialize(req, AppJsonContext.Default.CreateOAuthAppRequest),
            OAuthApproveRequest req => JsonSerializer.Serialize(req, AppJsonContext.Default.OAuthApproveRequest),
            AddDomainRequest req => JsonSerializer.Serialize(req, AppJsonContext.Default.AddDomainRequest),
            VerifyDomainRequest req => JsonSerializer.Serialize(req, AppJsonContext.Default.VerifyDomainRequest),
            JsonNode node => node.ToJsonString(),
            _ => throw new NotSupportedException($"Unsupported JSON body type: {body.GetType().FullName}"),
        };

    private static T? DeserializeTyped<T>(string json)
    {
        object? value = typeof(T) switch
        {
            var t when t == typeof(UserInfoData) => JsonSerializer.Deserialize(json, AppJsonContext.Default.UserInfoData),
            var t when t == typeof(TunnelRealtimeData) => JsonSerializer.Deserialize(json, AppJsonContext.Default.TunnelRealtimeData),
            var t when t == typeof(TrafficStatsData) => JsonSerializer.Deserialize(json, AppJsonContext.Default.TrafficStatsData),
            var t when t == typeof(DailyTrafficData) => JsonSerializer.Deserialize(json, AppJsonContext.Default.DailyTrafficData),
            var t when t == typeof(TunnelTrafficListData) => JsonSerializer.Deserialize(json, AppJsonContext.Default.TunnelTrafficListData),
            var t when t == typeof(TunnelListData) => JsonSerializer.Deserialize(json, AppJsonContext.Default.TunnelListData),
            var t when t == typeof(TunnelDetailData) => JsonSerializer.Deserialize(json, AppJsonContext.Default.TunnelDetailData),
            var t when t == typeof(DeleteTunnelData) => JsonSerializer.Deserialize(json, AppJsonContext.Default.DeleteTunnelData),
            var t when t == typeof(FrpcConfigData) => JsonSerializer.Deserialize(json, AppJsonContext.Default.FrpcConfigData),
            var t when t == typeof(FrpcConfigByTokenData) => JsonSerializer.Deserialize(json, AppJsonContext.Default.FrpcConfigByTokenData),
            var t when t == typeof(DomainListData) => JsonSerializer.Deserialize(json, AppJsonContext.Default.DomainListData),
            var t when t == typeof(AddDomainData) => JsonSerializer.Deserialize(json, AppJsonContext.Default.AddDomainData),
            var t when t == typeof(VerifyDomainData) => JsonSerializer.Deserialize(json, AppJsonContext.Default.VerifyDomainData),
            var t when t == typeof(NodeListData) => JsonSerializer.Deserialize(json, AppJsonContext.Default.NodeListData),
            var t when t == typeof(OAuthAppListData) => JsonSerializer.Deserialize(json, AppJsonContext.Default.OAuthAppListData),
            var t when t == typeof(OAuthAppDetailData) => JsonSerializer.Deserialize(json, AppJsonContext.Default.OAuthAppDetailData),
            var t when t == typeof(OAuthApproveData) => JsonSerializer.Deserialize(json, AppJsonContext.Default.OAuthApproveData),
            var t when t == typeof(OAuthTokenData) => JsonSerializer.Deserialize(json, AppJsonContext.Default.OAuthTokenData),
            var t when t == typeof(ClientVersionData) => JsonSerializer.Deserialize(json, AppJsonContext.Default.ClientVersionData),
            var t when t == typeof(JsonNode) => JsonNode.Parse(json),
            _ => null,
        };

        if (value is null) return default;
        return (T)value;
    }

    /// <summary>Ensure the Authorization header is current.</summary>
    private static void ApplyAuth()
    {
        Http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Global.Config.AccessToken);
    }

    /// <summary>Send a GET and deserialise the <c>data</c> field.</summary>
    private static async Task<ApiResponse<T>> GetAsync<T>(string path, CancellationToken ct = default)
    {
        ApplyAuth();
        var resp = await Http.GetAsyncLogged(path, ct);
        return await ParseAsync<T>(resp, ct);
    }

    /// <summary>Send a POST with JSON body and deserialise the <c>data</c> field.</summary>
    private static async Task<ApiResponse<T>> PostJsonAsync<T>(string path, object? body,
        CancellationToken ct = default)
    {
        ApplyAuth();
        var content = body is null
            ? null
            : new StringContent(SerializeBody(body), Encoding.UTF8, "application/json");
        var resp = await Http.PostAsyncLogged(path, content, ct);
        return await ParseAsync<T>(resp, ct);
    }

    /// <summary>Send a POST with form-urlencoded body and deserialise the <c>data</c> field.</summary>
    private static async Task<ApiResponse<T>> PostFormAsync<T>(string path,
        IEnumerable<KeyValuePair<string, string>> fields, CancellationToken ct = default)
    {
        // Token endpoints typically don't need Bearer auth
        var content = new FormUrlEncodedContent(fields);
        var resp = await Http.PostAsyncLogged(path, content, ct);
        return await ParseAsync<T>(resp, ct);
    }

    /// <summary>Send a POST with empty body.</summary>
    private static async Task<ApiResponse<T>> PostAsync<T>(string path, CancellationToken ct = default)
    {
        ApplyAuth();
        var resp = await Http.PostAsyncLogged(path, null, ct);
        return await ParseAsync<T>(resp, ct);
    }

    /// <summary>Send a PUT with JSON body.</summary>
    private static async Task<ApiResponse<T>> PutJsonAsync<T>(string path, object? body,
        CancellationToken ct = default)
    {
        ApplyAuth();
        var content = body is null
            ? null
            : new StringContent(SerializeBody(body), Encoding.UTF8, "application/json");
        var resp = await Http.PutAsyncLogged(path, content, ct);
        return await ParseAsync<T>(resp, ct);
    }

    /// <summary>Send a DELETE and deserialise.</summary>
    private static async Task<ApiResponse<T>> DeleteAsync<T>(string path, CancellationToken ct = default)
    {
        ApplyAuth();
        var resp = await Http.DeleteAsyncLogged(path, ct);
        return await ParseAsync<T>(resp, ct);
    }

    /// <summary>Parse the standard <c>{ code, msg, data }</c> envelope.</summary>
    private static async Task<ApiResponse<T>> ParseAsync<T>(HttpResponseMessage resp, CancellationToken ct)
    {
        var raw = await resp.Content.ReadAsStringAsync(ct);

        int defaultCode = resp.IsSuccessStatusCode ? 200 : (int)resp.StatusCode;
        string defaultMsg = resp.IsSuccessStatusCode
            ? "ok"
            : $"HTTP {(int)resp.StatusCode}: {resp.ReasonPhrase}";

        // Fallback: parse via JsonNode so we can support both
        // { code,msg,data } and { status,msg,data } envelopes, and still handle
        // data shape mismatches gracefully.
        try
        {
            var node = JsonNode.Parse(raw);
            bool hasCode = node?["code"] is not null;
            bool hasStatus = node?["status"] is not null;
            bool hasDataEnvelope = node?["data"] is not null;

            // Raw-object responses (e.g. /oauth2/token) are not envelopes.
            // If no envelope markers exist, deserialize the whole payload as T.
            if (!hasCode && !hasStatus && !hasDataEnvelope)
            {
                try
                {
                    var direct = DeserializeTyped<T>(raw);
                    if (direct is not null)
                    {
                        return new ApiResponse<T>
                        {
                            Code = defaultCode,
                            Msg = defaultMsg,
                            Data = direct,
                        };
                    }
                }
                catch
                {
                    // continue with generic fallback below
                }
            }

            var code = node?["code"]?.GetValue<int>()
                       ?? node?["status"]?.GetValue<int>()
                       ?? defaultCode;
            var msg = node?["msg"]?.GetValue<string>() ?? defaultMsg;
            var dataNode = node?["data"];
            T? data = default;
            if (dataNode is not null)
            {
                try { data = DeserializeTyped<T>(dataNode.ToJsonString()); }
                catch { /* best effort */ }
            }

            return new ApiResponse<T> { Code = code, Msg = msg, Data = data };
        }
        catch
        {
            // Some endpoints (e.g. /oauth2/token) return raw JSON object instead
            // of an envelope. Try direct deserialization as the final fallback.
            try
            {
                var direct = DeserializeTyped<T>(raw);
                if (direct is not null)
                {
                    return new ApiResponse<T>
                    {
                        Code = defaultCode,
                        Msg = defaultMsg,
                        Data = direct,
                    };
                }
            }
            catch
            {
                // ignore
            }

            return new ApiResponse<T>
            {
                Code = defaultCode,
                Msg = defaultMsg,
            };
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  Public API Methods
    // ════════════════════════════════════════════════════════════════

    // ──────────────────────────  User  ──────────────────────────

    /// <summary>获取用户信息 — GET /user/info</summary>
    public static Task<ApiResponse<UserInfoData>> GetUserInfoAsync(CancellationToken ct = default)
        => GetAsync<UserInfoData>("user/info", ct);

    // ──────────────────────────  Traffic  ──────────────────────────

    /// <summary>获取单个隧道的实时数据 — GET /user/traffic/tunnel/{tunnelId}</summary>
    public static Task<ApiResponse<TunnelRealtimeData>> GetTunnelRealtimeAsync(string tunnelId,
        CancellationToken ct = default)
        => GetAsync<TunnelRealtimeData>($"user/traffic/tunnel/{Uri.EscapeDataString(tunnelId)}", ct);

    /// <summary>获取用户流量统计信息 — GET /user/traffic/stats</summary>
    public static Task<ApiResponse<TrafficStatsData>> GetTrafficStatsAsync(CancellationToken ct = default)
        => GetAsync<TrafficStatsData>("user/traffic/stats", ct);

    /// <summary>获取每日流量统计 — GET /user/traffic/daily?days={days}</summary>
    public static Task<ApiResponse<DailyTrafficData>> GetDailyTrafficAsync(int days = 7,
        CancellationToken ct = default)
        => GetAsync<DailyTrafficData>($"user/traffic/daily?days={days}", ct);

    /// <summary>获取用户各隧道的流量信息 — GET /user/traffic/tunnels?days={days}</summary>
    public static Task<ApiResponse<TunnelTrafficListData>> GetTunnelTrafficListAsync(int days = 2,
        CancellationToken ct = default)
        => GetAsync<TunnelTrafficListData>($"user/traffic/tunnels?days={days}", ct);

    // ──────────────────────────  Tunnels  ──────────────────────────

    /// <summary>获取用户隧道列表 — GET /user/tunnel?page={page}&amp;limit={limit}</summary>
    public static Task<ApiResponse<TunnelListData>> GetTunnelListAsync(int page = 1, int limit = 50,
        CancellationToken ct = default)
        => GetAsync<TunnelListData>($"user/tunnel?page={page}&limit={limit}", ct);

    /// <summary>获取隧道详情 — GET /user/tunnel/{tunnelName}</summary>
    public static Task<ApiResponse<TunnelDetailData>> GetTunnelDetailAsync(string tunnelName,
        CancellationToken ct = default)
        => GetAsync<TunnelDetailData>($"user/tunnel/{Uri.EscapeDataString(tunnelName)}", ct);

    /// <summary>创建隧道 — POST /user/tunnel</summary>
    public static Task<ApiResponse<JsonNode>> CreateTunnelAsync(CreateTunnelRequest request,
        CancellationToken ct = default)
        => PostJsonAsync<JsonNode>("user/tunnel", request, ct);

    /// <summary>删除隧道 — DELETE /user/tunnel/{tunnelName}</summary>
    public static Task<ApiResponse<DeleteTunnelData>> DeleteTunnelAsync(string tunnelName,
        CancellationToken ct = default)
        => DeleteAsync<DeleteTunnelData>($"user/tunnel/{Uri.EscapeDataString(tunnelName)}", ct);

    /// <summary>获取 frpc config — GET /user/frpc/config?tunnel={tunnelId}</summary>
    public static Task<ApiResponse<FrpcConfigData>> GetFrpcConfigAsync(string tunnelId,
        CancellationToken ct = default)
        => GetAsync<FrpcConfigData>($"user/frpc/config?tunnel={Uri.EscapeDataString(tunnelId)}", ct);

    /// <summary>token 获取 frpc config — GET /tunnel/frpc/config/{token}</summary>
    public static Task<ApiResponse<FrpcConfigByTokenData>> GetFrpcConfigByTokenAsync(string token,
        CancellationToken ct = default)
        => GetAsync<FrpcConfigByTokenData>($"tunnel/frpc/config/{Uri.EscapeDataString(token)}", ct);

    // ──────────────────────────  Domain Whitelist  ──────────────────────────

    /// <summary>获取域名列表 — GET /user/domain</summary>
    public static Task<ApiResponse<DomainListData>> GetDomainListAsync(CancellationToken ct = default)
        => GetAsync<DomainListData>("user/domain", ct);

    /// <summary>添加域名 — POST /user/domain</summary>
    public static Task<ApiResponse<AddDomainData>> AddDomainAsync(string domain, string remark = "",
        CancellationToken ct = default)
        => PostJsonAsync<AddDomainData>("user/domain",
            new AddDomainRequest { Domain = domain, Remark = remark }, ct);

    /// <summary>验证域名 — POST /user/domain/verify</summary>
    public static Task<ApiResponse<VerifyDomainData>> VerifyDomainAsync(string domain,
        CancellationToken ct = default)
        => PostJsonAsync<VerifyDomainData>("user/domain/verify",
            new VerifyDomainRequest { Domain = domain }, ct);

    /// <summary>删除域名 — DELETE /user/domain/{domainId}</summary>
    public static Task<ApiResponse<JsonNode>> DeleteDomainAsync(string domainId,
        CancellationToken ct = default)
        => DeleteAsync<JsonNode>($"user/domain/{Uri.EscapeDataString(domainId)}", ct);

    // ──────────────────────────  Nodes  ──────────────────────────

    /// <summary>获取节点列表 — POST /user/nodes</summary>
    public static Task<ApiResponse<NodeListData>> GetNodeListAsync(CancellationToken ct = default)
        => PostAsync<NodeListData>("user/nodes", ct);

    // ──────────────────────────  OAuth2 Management  ──────────────────────────

    /// <summary>获取用户创建的应用列表 — GET /user/oauth/apps</summary>
    public static Task<ApiResponse<OAuthAppListData>> GetOAuthAppsAsync(CancellationToken ct = default)
        => GetAsync<OAuthAppListData>("user/oauth/apps", ct);

    /// <summary>创建 OAuth 应用 — POST /user/oauth/app</summary>
    public static Task<ApiResponse<JsonNode>> CreateOAuthAppAsync(CreateOAuthAppRequest request,
        CancellationToken ct = default)
        => PostJsonAsync<JsonNode>("user/oauth/app", request, ct);

    /// <summary>获取 OAuth 应用详情 — GET /user/oauth/app/{id}</summary>
    public static Task<ApiResponse<OAuthAppDetailData>> GetOAuthAppDetailAsync(string id,
        CancellationToken ct = default)
        => GetAsync<OAuthAppDetailData>($"user/oauth/app/{Uri.EscapeDataString(id)}", ct);

    /// <summary>删除应用 — DELETE /user/oauth/app/{id}</summary>
    public static Task<ApiResponse<JsonNode>> DeleteOAuthAppAsync(string id,
        CancellationToken ct = default)
        => DeleteAsync<JsonNode>($"user/oauth/app/{Uri.EscapeDataString(id)}", ct);

    /// <summary>更新应用 — PUT /user/oauth/app/{id}</summary>
    public static Task<ApiResponse<JsonNode>> UpdateOAuthAppAsync(string id, JsonNode body,
        CancellationToken ct = default)
        => PutJsonAsync<JsonNode>($"user/oauth/app/{Uri.EscapeDataString(id)}", body, ct);

    /// <summary>重置 OAuth 应用密钥 — POST /user/oauth/app/{id}/reset-secret</summary>
    public static Task<ApiResponse<JsonNode>> ResetOAuthAppSecretAsync(string id,
        CancellationToken ct = default)
        => PostAsync<JsonNode>($"user/oauth/app/{Uri.EscapeDataString(id)}/reset-secret", ct);

    /// <summary>获取用户已授权的应用列表 — GET /user/oauth/authorizations</summary>
    public static Task<ApiResponse<JsonNode>> GetOAuthAuthorizationsAsync(CancellationToken ct = default)
        => GetAsync<JsonNode>("user/oauth/authorizations", ct);

    /// <summary>撤销用户对某个应用的授权 — DELETE /user/oauth/authorization/{clientId}</summary>
    public static Task<ApiResponse<JsonNode>> RevokeOAuthAuthorizationAsync(string clientId,
        CancellationToken ct = default)
        => DeleteAsync<JsonNode>($"user/oauth/authorization/{Uri.EscapeDataString(clientId)}", ct);

    // ──────────────────────────  OAuth2 Authorization  ──────────────────────────

    /// <summary>
    /// Build the full OAuth2 authorize URL for browser redirect.
    /// GET {Dashboard}/oauth/authorize?scope=…&amp;redirect_uri=…&amp;response_type=code&amp;client_id=…&amp;access_type=offline
    /// </summary>
    public static string BuildOAuth2AuthorizeUrl(string clientId, string redirectUri,
        string scope = DefaultOAuthScope, string? state = null)
    {
        var url =
            $"{Global.Dashboard}/oauth/authorize?scope={Uri.EscapeDataString(scope)}&redirect_uri={Uri.EscapeDataString(redirectUri)}&response_type=code&client_id={Uri.EscapeDataString(clientId)}&access_type=offline";
        if (!string.IsNullOrWhiteSpace(state))
            url += $"&state={Uri.EscapeDataString(state)}";
        return url;
    }

    /// <summary>OAuth2 授权确认请求 — POST /oauth2/approve</summary>
    public static Task<ApiResponse<OAuthApproveData>> OAuthApproveAsync(OAuthApproveRequest request,
        CancellationToken ct = default)
        => PostJsonAsync<OAuthApproveData>("oauth2/approve", request, ct);

    /// <summary>用授权码换取令牌 — POST /oauth2/token (grant_type=authorization_code)</summary>
    public static Task<ApiResponse<OAuthTokenData>> ExchangeCodeForTokenAsync(
        string code, string redirectUri, string clientId, string clientSecret,
        CancellationToken ct = default)
        => PostFormAsync<OAuthTokenData>("oauth2/token", new Dictionary<string, string>
        {
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["scope"] = DefaultOAuthScope,
            ["grant_type"] = "authorization_code",
        }, ct);

    /// <summary>用刷新令牌续期 — POST /oauth2/token (grant_type=refresh_token)</summary>
    public static Task<ApiResponse<OAuthTokenData>> RefreshTokenAsync(
        string refreshToken, string clientId, string clientSecret,
        CancellationToken ct = default)
        => PostFormAsync<OAuthTokenData>("oauth2/token", new Dictionary<string, string>
        {
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["scope"] = DefaultOAuthScope,
            ["grant_type"] = "refresh_token",
        }, ct);

    // ──────────────────────────  Client Version  ──────────────────────────

    /// <summary>获取客户端最新版本 — GET /client/version</summary>
    public static Task<ApiResponse<ClientVersionData>> GetClientVersionAsync(CancellationToken ct = default)
        => GetAsync<ClientVersionData>("client/version", ct);
}
