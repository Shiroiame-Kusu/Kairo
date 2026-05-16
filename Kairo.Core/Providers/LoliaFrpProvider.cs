using System.Text.Json;
using System.Text.Json.Nodes;
using Kairo.Core.Logging;
using Kairo.Core.Models;

namespace Kairo.Core.Providers;

public sealed class LoliaFrpProvider : IFrpProvider
{
    private const string ReleaseApiOrigin = "https://api.github.com/repos/Lolia-FRP/lolia-frp/releases/latest";
    private const string ApiRoot = "https://api.lolia.link/api/v1";
    private const string OAuthClientId = "4qav2seu7hooz62f";
    private const string DefaultOAuthScope = "all node:read";

    public FrpProviderType Type => FrpProviderType.Lolia;
    public string Id => "lolia";
    public string DisplayName => "LoliaFRP";
    public string ApiBaseUrl => ApiRoot;
    public string DashboardUrl => "https://dash.lolia.link";
    public bool SupportsOAuthLogin => true;
    public bool SupportsSign => true;
    public bool SupportsMinecraftRooms => false;

    public string BuildOAuthUrl(OAuthRequest request)
    {
        var clientId = string.IsNullOrWhiteSpace(request.ClientIdText) ? OAuthClientId : request.ClientIdText;
        var redirectUri = ResolveRedirectUri(request);
        var url = FrpProviderHelpers.AppendQuery($"{DashboardUrl}/oauth/authorize",
            ("scope", string.IsNullOrWhiteSpace(request.Scopes) ? DefaultOAuthScope : request.Scopes),
            ("redirect_uri", redirectUri),
            ("response_type", "code"),
            ("client_id", clientId),
            ("access_type", "offline"));
        if (!string.IsNullOrWhiteSpace(request.CodeChallenge))
            url += $"&code_challenge={Uri.EscapeDataString(request.CodeChallenge)}&code_challenge_method={Uri.EscapeDataString(string.IsNullOrWhiteSpace(request.CodeChallengeMethod) ? "S256" : request.CodeChallengeMethod)}";
        return string.IsNullOrWhiteSpace(request.State) ? url : $"{url}&state={Uri.EscapeDataString(request.State)}";
    }

    public async Task<FrpApiResult<string>> ExchangeCodeForRefreshTokenAsync(HttpClient http, string code, string redirectUri = "", string codeVerifier = "", CancellationToken ct = default)
    {
        var form = new Dictionary<string, string>
        {
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = OAuthClientId,
            ["scope"] = DefaultOAuthScope,
            ["grant_type"] = "authorization_code"
        };
        if (!string.IsNullOrWhiteSpace(codeVerifier))
            form["code_verifier"] = codeVerifier;

        var token = await RequestOAuthTokenAsync(http, form, ct);
        if (!token.Success || token.Data == null)
            return FrpApiResult<string>.Fail(token.Code, token.Message);
        return string.IsNullOrWhiteSpace(token.Data.RefreshToken)
            ? FrpApiResult<string>.Fail(0, "OAuth 响应缺失 Refresh Token")
            : FrpApiResult<string>.Ok(token.Data.RefreshToken, token.Code, token.Message);
    }

    public async Task<FrpApiResult<FrpLoginResult>> LoginWithRefreshTokenAsync(HttpClient http, string refreshToken, CancellationToken ct = default)
    {
        var token = await RequestOAuthTokenAsync(http, new Dictionary<string, string>
        {
            ["refresh_token"] = refreshToken,
            ["client_id"] = OAuthClientId,
            ["scope"] = DefaultOAuthScope,
            ["grant_type"] = "refresh_token"
        }, ct);
        if (!token.Success || token.Data == null)
            return FrpApiResult<FrpLoginResult>.Fail(token.Code, token.Message);

        var accessToken = token.Data.AccessToken;
        if (string.IsNullOrWhiteSpace(accessToken))
            return FrpApiResult<FrpLoginResult>.Fail(0, "OAuth 响应缺失 Access Token");

        http.DefaultRequestHeaders.Remove("Authorization");
        http.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");

        var userResult = await GetUserInfoAsync(http, 0, ct);
        if (!userResult.Success || userResult.Data == null)
            return FrpApiResult<FrpLoginResult>.Fail(userResult.Code, userResult.Message);

        return FrpApiResult<FrpLoginResult>.Ok(new FrpLoginResult
        {
            UserId = userResult.Data.Id,
            AccessToken = accessToken,
            RefreshToken = string.IsNullOrWhiteSpace(token.Data.RefreshToken) ? refreshToken : token.Data.RefreshToken,
            User = userResult.Data,
            FrpToken = string.Empty
        }, token.Code, token.Message);
    }

    public async Task<FrpApiResult<FrpUserProfile>> GetUserInfoAsync(HttpClient http, int userId, CancellationToken ct = default)
    {
        using var response = await http.GetAsyncLogged($"{ApiBaseUrl}/user/info", ct);
        var apiResponse = await FrpProviderHelpers.ReadJsonAsync(response, FrpModelsJsonContext.Default.LoliaApiResponseLoliaUserInfoData, ct);
        var parsed = FrpProviderHelpers.ParseLoliaResponse(apiResponse);
        if (!parsed.Success || parsed.Data == null) return FrpApiResult<FrpUserProfile>.Fail(parsed.Code, parsed.Message);
        return FrpApiResult<FrpUserProfile>.Ok(ParseUser(parsed.Data), parsed.Code, parsed.Message);
    }

    public Task<FrpApiResult<string>> GetFrpTokenAsync(HttpClient http, int userId, CancellationToken ct = default) =>
        Task.FromResult(FrpApiResult<string>.Fail(0, "LoliaFRP 使用隧道 token 启动，文档未公开全局 FRP Token 接口"));

    public Task<FrpApiResult<string>> GetAnnouncementAsync(HttpClient http, CancellationToken ct = default) =>
        Task.FromResult(FrpApiResult<string>.Ok(string.Empty));

    public Task<FrpApiResult<FrpSignStatus>> GetSignStatusAsync(HttpClient http, int userId, CancellationToken ct = default) =>
        Task.FromResult(FrpApiResult<FrpSignStatus>.Ok(new FrpSignStatus { Signed = false }));

    public async Task<FrpApiResult<FrpSignResult>> SignAsync(HttpClient http, int userId, CancellationToken ct = default)
    {
        using var response = await http.PostAsyncLogged($"{ApiBaseUrl}/user/checkin", null, ct);
        var apiResponse = await FrpProviderHelpers.ReadJsonAsync(response, FrpModelsJsonContext.Default.LoliaApiResponseLoliaCheckinData, ct);
        var parsed = FrpProviderHelpers.ParseLoliaResponse(apiResponse);
        if (!parsed.Success) return FrpApiResult<FrpSignResult>.Fail(parsed.Code, parsed.Message);

        return FrpApiResult<FrpSignResult>.Ok(new FrpSignResult
        {
            GainedTrafficGb = parsed.Data?.TrafficGb ?? 0
        }, parsed.Code, parsed.Message);
    }

    public async Task<FrpApiResult<IReadOnlyList<FrpTunnel>>> GetTunnelsAsync(HttpClient http, int userId, CancellationToken ct = default)
    {
        var url = FrpProviderHelpers.AppendQuery($"{ApiBaseUrl}/user/tunnel", ("page", "1"), ("limit", "1000"));
        using var response = await http.GetAsyncLogged(url, ct);
        var apiResponse = await FrpProviderHelpers.ReadJsonAsync(response, FrpModelsJsonContext.Default.LoliaApiResponseLoliaTunnelListData, ct);
        var parsed = FrpProviderHelpers.ParseLoliaResponse(apiResponse);
        if (!parsed.Success) return FrpApiResult<IReadOnlyList<FrpTunnel>>.Fail(parsed.Code, parsed.Message);
        return FrpApiResult<IReadOnlyList<FrpTunnel>>.Ok((parsed.Data?.List ?? new()).Select(ParseTunnel).ToList(), parsed.Code, parsed.Message);
    }

    public async Task<FrpApiResult<IReadOnlyList<FrpNode>>> GetNodesAsync(HttpClient http, int userId, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsyncLogged($"{ApiBaseUrl}/user/nodes",
            new LoliaNodeListRequest { Page = 1, Limit = 1000 },
            FrpModelsJsonContext.Default.LoliaNodeListRequest,
            ct);
        var apiResponse = await FrpProviderHelpers.ReadJsonAsync(response, FrpModelsJsonContext.Default.LoliaApiResponseLoliaNodeListData, ct);
        var parsed = FrpProviderHelpers.ParseLoliaResponse(apiResponse);
        if (!parsed.Success) return FrpApiResult<IReadOnlyList<FrpNode>>.Fail(parsed.Code, parsed.Message);
        return FrpApiResult<IReadOnlyList<FrpNode>>.Ok((parsed.Data?.Nodes ?? new()).Select(ParseNode).ToList(), parsed.Code, parsed.Message);
    }

    public Task<FrpApiResult<int>> GetRandomPortAsync(HttpClient http, int userId, int nodeId, CancellationToken ct = default) =>
        Task.FromResult(FrpApiResult<int>.Fail(0, "LoliaFRP 文档未提供随机端口接口"));

    public async Task<FrpApiResult<CreateFrpTunnelResult>> CreateTunnelAsync(HttpClient http, int userId, CreateFrpTunnelRequest request, CancellationToken ct = default)
    {
        using var response = await http.PostAsJsonAsyncLogged($"{ApiBaseUrl}/user/tunnel",
            new LoliaCreateTunnelRequest
            {
                NodeId = request.NodeId,
                Type = request.Type.ToLowerInvariant(),
                LocalIp = request.LocalIp,
                LocalPort = request.LocalPort,
                RemotePort = request.RemotePort ?? 0,
                CustomDomain = request.Domain,
                Remark = request.Name
            },
            FrpModelsJsonContext.Default.LoliaCreateTunnelRequest,
            ct);
        var apiResponse = await FrpProviderHelpers.ReadJsonAsync(response, FrpModelsJsonContext.Default.LoliaApiResponseLoliaCreateTunnelData, ct);
        var parsed = FrpProviderHelpers.ParseLoliaResponse(apiResponse);
        if (!parsed.Success) return FrpApiResult<CreateFrpTunnelResult>.Fail(parsed.Code, parsed.Message);
        return FrpApiResult<CreateFrpTunnelResult>.Ok(new CreateFrpTunnelResult
        {
            TunnelId = parsed.Data?.Id ?? 0,
            TunnelName = FrpProviderHelpers.FirstNonEmpty(parsed.Data?.Name ?? string.Empty, request.Name)
        }, parsed.Code, parsed.Message);
    }

    public async Task<FrpApiResult<object>> DeleteTunnelAsync(HttpClient http, int userId, FrpTunnel tunnel, CancellationToken ct = default)
    {
        var tunnelName = string.IsNullOrWhiteSpace(tunnel.Name) ? tunnel.Id.ToString() : tunnel.Name;
        using var response = await http.DeleteAsyncLogged($"{ApiBaseUrl}/user/tunnel/{Uri.EscapeDataString(tunnelName)}", ct);
        var apiResponse = await FrpProviderHelpers.ReadJsonAsync(response, FrpModelsJsonContext.Default.LoliaApiResponseObject, ct);
        var parsed = FrpProviderHelpers.ParseLoliaResponse(apiResponse);
        return parsed.Success
            ? FrpApiResult<object>.Ok(new object(), parsed.Code, parsed.Message)
            : FrpApiResult<object>.Fail(parsed.Code, parsed.Message);
    }

    public async Task<FrpApiResult<FrpcConfigResult>> GetFrpcConfigAsync(HttpClient http, FrpTunnel tunnel, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(tunnel.Token))
            return FrpApiResult<FrpcConfigResult>.Ok(new FrpcConfigResult { Token = tunnel.Token });

        var tunnelName = string.IsNullOrWhiteSpace(tunnel.Name) ? tunnel.Id.ToString() : tunnel.Name;
        using var response = await http.GetAsyncLogged($"{ApiBaseUrl}/user/tunnel/{Uri.EscapeDataString(tunnelName)}", ct);
        var apiResponse = await FrpProviderHelpers.ReadJsonAsync(response, FrpModelsJsonContext.Default.LoliaApiResponseLoliaTunnelData, ct);
        var parsed = FrpProviderHelpers.ParseLoliaResponse(apiResponse);
        if (!parsed.Success) return FrpApiResult<FrpcConfigResult>.Fail(parsed.Code, parsed.Message);

        var token = parsed.Data?.TunnelToken ?? string.Empty;
        return string.IsNullOrWhiteSpace(token)
            ? FrpApiResult<FrpcConfigResult>.Fail(0, "API 返回的 tunnel_token 为空")
            : FrpApiResult<FrpcConfigResult>.Ok(new FrpcConfigResult { Token = token }, parsed.Code, parsed.Message);
    }

    public string BuildFrpcArguments(FrpStartOptions options) => $"-t {options.TunnelId}:{options.FrpToken}";

    public async Task<FrpDownloadRelease?> GetLatestFrpcReleaseAsync(HttpClient http, CancellationToken ct = default)
    {
        return await FrpProviderHelpers.TryGetGitHubReleaseAsync(http, ReleaseApiOrigin, ct);
    }

    public FrpAssetSelection SelectBestAsset(FrpDownloadRelease release)
    {
        var (platform, arch) = FrpProviderHelpers.GetCurrentPlatform();
        var asset = FrpProviderHelpers.PickArchive(release.Assets, platform, arch, $"LoliaFrp_{platform}_{arch}");
        return new FrpAssetSelection { Version = release.Version, Platform = platform, Architecture = arch, Asset = asset };
    }

    public string GetDownloadUrl(FrpDownloadRelease release, FrpDownloadAsset asset, bool useMirror) => asset.DownloadUrl;

    public string? GetChecksumUrl(FrpDownloadRelease release, FrpDownloadAsset asset, bool useMirror) =>
        release.Assets.FirstOrDefault(a => a.Name.Equals("sha256sum.txt", StringComparison.OrdinalIgnoreCase))?.DownloadUrl;

    private static async Task<FrpApiResult<LoliaOAuthTokenData>> RequestOAuthTokenAsync(HttpClient http, Dictionary<string, string> form, CancellationToken ct)
    {
        using var response = await http.PostAsyncLogged($"{ApiRoot}/oauth2/token", new FormUrlEncodedContent(form), ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(text))
            return FrpApiResult<LoliaOAuthTokenData>.Fail((int)response.StatusCode, "OAuth 响应格式错误");

        try
        {
            if (text.Contains("\"data\"", StringComparison.Ordinal))
            {
                var wrapped = JsonSerializer.Deserialize(text, FrpModelsJsonContext.Default.LoliaApiResponseLoliaOAuthTokenData);
                var parsed = FrpProviderHelpers.ParseLoliaResponse(wrapped);
                return parsed.Success && parsed.Data != null
                    ? parsed
                    : FrpApiResult<LoliaOAuthTokenData>.Fail(parsed.Code, parsed.Message);
            }

            var token = JsonSerializer.Deserialize(text, FrpModelsJsonContext.Default.LoliaOAuthTokenData);
            return token == null
                ? FrpApiResult<LoliaOAuthTokenData>.Fail((int)response.StatusCode, "OAuth 响应格式错误")
                : FrpApiResult<LoliaOAuthTokenData>.Ok(token, response.IsSuccessStatusCode ? 200 : (int)response.StatusCode, response.ReasonPhrase ?? string.Empty);
        }
        catch (JsonException)
        {
            return FrpApiResult<LoliaOAuthTokenData>.Fail((int)response.StatusCode, "OAuth 响应格式错误");
        }
    }

    private static string ResolveRedirectUri(OAuthRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.RedirectUri)) return request.RedirectUri;
        return request.CallbackPort > 0 ? $"http://127.0.0.1:{request.CallbackPort}/oauth/callback" : string.Empty;
    }

    private static FrpUserProfile ParseUser(LoliaUserInfoData user) => new()
    {
        Id = user.Id,
        Username = user.Username,
        Email = user.Email,
        Avatar = user.Avatar,
        Traffic = (user.TrafficLimit - user.TrafficUsed) / 1024m / 1024m,
        Inbound = user.BandwidthLimit * 1024 / 8,
        Outbound = user.BandwidthLimit * 1024 / 8,
        TodayChecked = user.TodayChecked
    };

    private static FrpTunnel ParseTunnel(LoliaTunnelData tunnel) => new()
    {
        Id = tunnel.Id,
        Name = tunnel.Name,
        Token = tunnel.TunnelToken,
        Type = tunnel.Type,
        LocalIp = tunnel.LocalIp,
        LocalPort = tunnel.LocalPort,
        RemotePort = tunnel.RemotePort,
        Domain = tunnel.CustomDomain,
        Node = new FrpNode
        {
            Id = tunnel.NodeId,
            Name = tunnel.NodeName,
            Host = tunnel.NodeAddress,
            Ip = tunnel.NodeAddress
        }
    };

    private static FrpNode ParseNode(LoliaNodeData node)
    {
        var host = FrpProviderHelpers.FirstNonEmpty(node.Host, node.Address, node.NodeAddress, node.ServerAddress);
        var ip = FrpProviderHelpers.FirstNonEmpty(node.Ip, host);

        return new FrpNode
        {
            Id = node.Id,
            Name = node.Name,
            Host = host,
            Ip = ip,
            Description = FrpProviderHelpers.FirstNonEmpty(node.Description, node.Remark),
            PortRanges = node.PortRanges,
            SupportedProtocols = node.SupportedProtocols
        };
    }
}
