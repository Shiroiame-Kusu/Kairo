using Kairo.Core.Logging;
using Kairo.Core.Models;

namespace Kairo.Core.Providers;

public sealed class LocyanFrpProvider : IFrpProvider
{
    private const string GitHubOwner = "LoCyan-Team";
    private const string GitHubRepo = "LoCyanFrpPureApp";
    private const int APPID = 1;
    private const string ReleaseApiOrigin = $"https://api.github.com/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
    private const string ReleaseApiMirror = $"https://{AppConstants.GithubMirror}/repos/{GitHubOwner}/{GitHubRepo}/releases/latest";
    private const string DownloadMirrorRoot = "https://mirrors.locyan.cn/github-release/LoCyan-Team/LoCyanFrpPureApp";

    public FrpProviderType Type => FrpProviderType.Locyan;
    public string Id => "locyan";
    public string DisplayName => "LoCyanFrp";
    public string ApiBaseUrl => "https://api.locyanfrp.cn/v3";
    public string DashboardUrl => "https://dashboard.locyanfrp.cn";
    public bool SupportsOAuthLogin => true;
    public bool SupportsSign => true;
    public bool SupportsMinecraftRooms => true;

    public string BuildOAuthUrl(OAuthRequest request)
    {
        var clientId = request.ClientId > 0 ? request.ClientId : APPID;
        var url = $"{DashboardUrl}/auth/oauth/authorize";
        url = FrpProviderHelpers.AppendQuery(url,
            ("client_id", clientId.ToString()),
            ("scopes", string.IsNullOrWhiteSpace(request.Scopes) ? "User,Node,Tunnel,Sign" : request.Scopes));
        if (!string.IsNullOrWhiteSpace(request.RedirectUri))
            url += $"&redirect_uri={Uri.EscapeDataString(request.RedirectUri)}";
        if (!string.IsNullOrWhiteSpace(request.Mode))
            url += $"&mode={Uri.EscapeDataString(request.Mode)}";
        return url;
    }

    public async Task<FrpApiResult<string>> ExchangeCodeForRefreshTokenAsync(HttpClient http, string code, string redirectUri = "", string codeVerifier = "", CancellationToken ct = default)
    {
        using var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("code", code) });
        using var response = await http.PostAsyncLogged($"{ApiBaseUrl}/auth/oauth/refresh-token", content, ct);
        var apiResponse = await FrpProviderHelpers.ReadJsonAsync(response, FrpModelsJsonContext.Default.LocyanApiResponseLocyanRefreshTokenData, ct);
        var parsed = FrpProviderHelpers.ParseLocyanResponse(apiResponse);
        if (!parsed.Success) return FrpApiResult<string>.Fail(parsed.Code, parsed.Message);
        var token = parsed.Data?.RefreshToken ?? string.Empty;
        return string.IsNullOrWhiteSpace(token)
            ? FrpApiResult<string>.Fail(0, "API 返回的 Refresh Token 为空")
            : FrpApiResult<string>.Ok(token, parsed.Code, parsed.Message);
    }

    public async Task<FrpApiResult<FrpLoginResult>> LoginWithRefreshTokenAsync(HttpClient http, string refreshToken, CancellationToken ct = default)
    {
        using var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("app_id", APPID.ToString()),
            new KeyValuePair<string, string>("refresh_token", refreshToken)
        });
        using var response = await http.PostAsyncLogged($"{ApiBaseUrl}/auth/oauth/access-token", content, ct);
        var apiResponse = await FrpProviderHelpers.ReadJsonAsync(response, FrpModelsJsonContext.Default.LocyanApiResponseLocyanAccessTokenData, ct);
        var parsed = FrpProviderHelpers.ParseLocyanResponse(apiResponse);
        if (!parsed.Success) return FrpApiResult<FrpLoginResult>.Fail(parsed.Code, parsed.Message);

        var userId = parsed.Data?.UserId ?? 0;
        var accessToken = parsed.Data?.AccessToken ?? string.Empty;
        if (userId <= 0 || string.IsNullOrWhiteSpace(accessToken))
            return FrpApiResult<FrpLoginResult>.Fail(0, "Access Token 响应缺失必要字段");

        http.DefaultRequestHeaders.Remove("Authorization");
        http.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");

        var userResult = await GetUserInfoAsync(http, userId, ct);
        if (!userResult.Success) return FrpApiResult<FrpLoginResult>.Fail(userResult.Code, userResult.Message);

        var tokenResult = await GetFrpTokenAsync(http, userId, ct);
        if (!tokenResult.Success) return FrpApiResult<FrpLoginResult>.Fail(tokenResult.Code, tokenResult.Message);

        return FrpApiResult<FrpLoginResult>.Ok(new FrpLoginResult
        {
            UserId = userId,
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            User = userResult.Data!,
            FrpToken = tokenResult.Data ?? string.Empty
        }, parsed.Code, parsed.Message);
    }

    public async Task<FrpApiResult<FrpUserProfile>> GetUserInfoAsync(HttpClient http, int userId, CancellationToken ct = default)
    {
        var url = FrpProviderHelpers.AppendQuery($"{ApiBaseUrl}/user", ("user_id", userId.ToString()));
        using var response = await http.GetAsyncLogged(url, ct);
        var apiResponse = await FrpProviderHelpers.ReadJsonAsync(response, FrpModelsJsonContext.Default.LocyanApiResponseLocyanUserData, ct);
        var parsed = FrpProviderHelpers.ParseLocyanResponse(apiResponse);
        if (!parsed.Success || parsed.Data == null) return FrpApiResult<FrpUserProfile>.Fail(parsed.Code, parsed.Message);
        return FrpApiResult<FrpUserProfile>.Ok(ParseUser(parsed.Data), parsed.Code, parsed.Message);
    }

    public async Task<FrpApiResult<string>> GetFrpTokenAsync(HttpClient http, int userId, CancellationToken ct = default)
    {
        var url = FrpProviderHelpers.AppendQuery($"{ApiBaseUrl}/user/frp/token", ("user_id", userId.ToString()));
        using var response = await http.GetAsyncLogged(url, ct);
        var apiResponse = await FrpProviderHelpers.ReadJsonAsync(response, FrpModelsJsonContext.Default.LocyanApiResponseLocyanFrpTokenData, ct);
        var parsed = FrpProviderHelpers.ParseLocyanResponse(apiResponse);
        if (!parsed.Success) return FrpApiResult<string>.Fail(parsed.Code, parsed.Message);
        return FrpApiResult<string>.Ok(parsed.Data?.Token ?? string.Empty, parsed.Code, parsed.Message);
    }

    public async Task<FrpApiResult<string>> GetAnnouncementAsync(HttpClient http, CancellationToken ct = default)
    {
        using var response = await http.GetAsyncLogged($"{ApiBaseUrl}/site/notice", ct);
        var apiResponse = await FrpProviderHelpers.ReadJsonAsync(response, FrpModelsJsonContext.Default.LocyanApiResponseLocyanAnnouncementData, ct);
        var parsed = FrpProviderHelpers.ParseLocyanResponse(apiResponse);
        if (!parsed.Success) return FrpApiResult<string>.Fail(parsed.Code, parsed.Message);
        var announcement = FrpProviderHelpers.FirstNonEmpty(parsed.Data?.Announcement ?? string.Empty, parsed.Data?.Broadcast ?? string.Empty);
        return FrpApiResult<string>.Ok(announcement, parsed.Code, parsed.Message);
    }

    public async Task<FrpApiResult<FrpSignStatus>> GetSignStatusAsync(HttpClient http, int userId, CancellationToken ct = default)
    {
        var url = FrpProviderHelpers.AppendQuery($"{ApiBaseUrl}/sign", ("user_id", userId.ToString()));
        using var response = await http.GetAsyncLogged(url, ct);
        var apiResponse = await FrpProviderHelpers.ReadJsonAsync(response, FrpModelsJsonContext.Default.LocyanApiResponseLocyanSignStatusData, ct);
        var parsed = FrpProviderHelpers.ParseLocyanResponse(apiResponse);
        if (!parsed.Success) return FrpApiResult<FrpSignStatus>.Fail(parsed.Code, parsed.Message);
        return FrpApiResult<FrpSignStatus>.Ok(new FrpSignStatus
        {
            Signed = parsed.Data?.Status ?? false
        }, parsed.Code, parsed.Message);
    }

    public async Task<FrpApiResult<FrpSignResult>> SignAsync(HttpClient http, int userId, CancellationToken ct = default)
    {
        using var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("user_id", userId.ToString()) });
        using var response = await http.PostAsyncLogged($"{ApiBaseUrl}/sign", content, ct);
        var apiResponse = await FrpProviderHelpers.ReadJsonAsync(response, FrpModelsJsonContext.Default.LocyanApiResponseLocyanSignData, ct);
        var parsed = FrpProviderHelpers.ParseLocyanResponse(apiResponse);
        if (!parsed.Success) return FrpApiResult<FrpSignResult>.Fail(parsed.Code, parsed.Message);
        return FrpApiResult<FrpSignResult>.Ok(new FrpSignResult
        {
            GainedTrafficGb = parsed.Data?.GetTraffic ?? 0
        }, parsed.Code, parsed.Message);
    }

    public async Task<FrpApiResult<IReadOnlyList<FrpTunnel>>> GetTunnelsAsync(HttpClient http, int userId, CancellationToken ct = default)
    {
        var url = $"{ApiBaseUrl}/tunnels?user_id={userId}";
        using var response = await http.GetAsyncLogged(url, ct);
        var apiResponse = await FrpProviderHelpers.ReadJsonAsync(response, FrpModelsJsonContext.Default.LocyanApiResponseLocyanTunnelListData, ct);
        var parsed = FrpProviderHelpers.ParseLocyanResponse(apiResponse);
        if (!parsed.Success) return FrpApiResult<IReadOnlyList<FrpTunnel>>.Fail(parsed.Code, parsed.Message);
        return FrpApiResult<IReadOnlyList<FrpTunnel>>.Ok((parsed.Data?.List ?? new()).Select(ParseTunnel).ToList(), parsed.Code, parsed.Message);
    }

    public async Task<FrpApiResult<IReadOnlyList<FrpNode>>> GetNodesAsync(HttpClient http, int userId, CancellationToken ct = default)
    {
        var url = $"{ApiBaseUrl}/nodes?user_id={userId}";
        using var response = await http.GetAsyncLogged(url, ct);
        var apiResponse = await FrpProviderHelpers.ReadJsonAsync(response, FrpModelsJsonContext.Default.LocyanApiResponseLocyanNodeListData, ct);
        var parsed = FrpProviderHelpers.ParseLocyanResponse(apiResponse);
        if (!parsed.Success) return FrpApiResult<IReadOnlyList<FrpNode>>.Fail(parsed.Code, parsed.Message);
        return FrpApiResult<IReadOnlyList<FrpNode>>.Ok((parsed.Data?.List ?? new()).Select(ParseNode).ToList(), parsed.Code, parsed.Message);
    }

    public async Task<FrpApiResult<int>> GetRandomPortAsync(HttpClient http, int userId, int nodeId, CancellationToken ct = default)
    {
        var url = FrpProviderHelpers.AppendQuery($"{ApiBaseUrl}/node/port", ("user_id", userId.ToString()), ("node_id", nodeId.ToString()));
        using var response = await http.GetAsyncLogged(url, ct);
        var apiResponse = await FrpProviderHelpers.ReadJsonAsync(response, FrpModelsJsonContext.Default.LocyanApiResponseLocyanRandomPortData, ct);
        var parsed = FrpProviderHelpers.ParseLocyanResponse(apiResponse);
        if (!parsed.Success) return FrpApiResult<int>.Fail(parsed.Code, parsed.Message);
        return FrpApiResult<int>.Ok(parsed.Data?.Port ?? 0, parsed.Code, parsed.Message);
    }

    public async Task<FrpApiResult<CreateFrpTunnelResult>> CreateTunnelAsync(HttpClient http, int userId, CreateFrpTunnelRequest request, CancellationToken ct = default)
    {
        var form = new List<KeyValuePair<string, string>>
        {
            new("user_id", userId.ToString()),
            new("name", request.Name),
            new("local_ip", request.LocalIp),
            new("type", request.Type.ToUpperInvariant()),
            new("local_port", request.LocalPort.ToString()),
            new("node_id", request.NodeId.ToString()),
            new("use_encryption", request.UseEncryption ? "true" : "false"),
            new("use_compression", request.UseCompression ? "true" : "false")
        };
        if (request.RemotePort.HasValue) form.Add(new("remote_port", request.RemotePort.Value.ToString()));
        if (!string.IsNullOrWhiteSpace(request.SecretKey)) form.Add(new("secret_key", request.SecretKey));
        if (!string.IsNullOrWhiteSpace(request.Domain)) form.Add(new("domain", request.Domain));

        using var response = await http.PutAsyncLogged($"{ApiBaseUrl}/tunnel", new FormUrlEncodedContent(form), ct);
        var apiResponse = await FrpProviderHelpers.ReadJsonAsync(response, FrpModelsJsonContext.Default.LocyanApiResponseLocyanCreateTunnelData, ct);
        var parsed = FrpProviderHelpers.ParseLocyanResponse(apiResponse);
        if (!parsed.Success) return FrpApiResult<CreateFrpTunnelResult>.Fail(parsed.Code, parsed.Message);
        return FrpApiResult<CreateFrpTunnelResult>.Ok(new CreateFrpTunnelResult
        {
            TunnelId = parsed.Data?.TunnelId ?? 0,
            TunnelName = request.Name
        }, parsed.Code, parsed.Message);
    }

    public async Task<FrpApiResult<object>> DeleteTunnelAsync(HttpClient http, int userId, FrpTunnel tunnel, CancellationToken ct = default)
    {
        var url = $"{ApiBaseUrl}/tunnel?user_id={userId}&tunnel_id={tunnel.Id}";
        using var response = await http.DeleteAsyncLogged(url, ct);
        var apiResponse = await FrpProviderHelpers.ReadJsonAsync(response, FrpModelsJsonContext.Default.LocyanApiResponseObject, ct);
        var parsed = FrpProviderHelpers.ParseLocyanResponse(apiResponse);
        return parsed.Success
            ? FrpApiResult<object>.Ok(new object(), parsed.Code, parsed.Message)
            : FrpApiResult<object>.Fail(parsed.Code, parsed.Message);
    }

    public Task<FrpApiResult<FrpcConfigResult>> GetFrpcConfigAsync(HttpClient http, FrpTunnel tunnel, CancellationToken ct = default) =>
        Task.FromResult(FrpApiResult<FrpcConfigResult>.Ok(new FrpcConfigResult()));

    public string BuildFrpcArguments(FrpStartOptions options) => $"-u {options.FrpToken} -t {options.TunnelId}";

    public async Task<FrpDownloadRelease?> GetLatestFrpcReleaseAsync(HttpClient http, CancellationToken ct = default)
    {
        return await FrpProviderHelpers.TryGetGitHubReleaseAsync(http, ReleaseApiMirror, ct)
            ?? await FrpProviderHelpers.TryGetGitHubReleaseAsync(http, ReleaseApiOrigin, ct);
    }

    public FrpAssetSelection SelectBestAsset(FrpDownloadRelease release)
    {
        var (platform, arch) = FrpProviderHelpers.GetCurrentPlatform();
        var version = release.Version;
        var asset = FrpProviderHelpers.PickArchive(release.Assets, platform, arch,
            $"frp_LoCyanFrp-{version}_{platform}_{arch}",
            $"frp_LoCyanFrp-{release.TagName.TrimStart('v')}_{platform}_{arch}");
        return new FrpAssetSelection { Version = version, Platform = platform, Architecture = arch, Asset = asset };
    }

    public string GetDownloadUrl(FrpDownloadRelease release, FrpDownloadAsset asset, bool useMirror)
    {
        if (!useMirror) return asset.DownloadUrl;
        return $"{DownloadMirrorRoot}/{Uri.EscapeDataString(release.ReleaseName)}/{Uri.EscapeDataString(asset.Name)}";
    }

    public string? GetChecksumUrl(FrpDownloadRelease release, FrpDownloadAsset asset, bool useMirror)
    {
        var prefix = asset.Name.Split('.')[0];
        var checksum = release.Assets.FirstOrDefault(a =>
            FrpProviderHelpers.IsChecksumAsset(a.Name) && a.Name.Contains(prefix, StringComparison.OrdinalIgnoreCase));
        if (checksum == null) return null;
        if (!useMirror) return checksum.DownloadUrl;
        return $"{DownloadMirrorRoot}/{Uri.EscapeDataString(release.ReleaseName)}/{Uri.EscapeDataString(checksum.Name)}";
    }

    private static FrpUserProfile ParseUser(LocyanUserData user) => new()
    {
        Id = user.Id,
        Username = user.Username,
        Email = user.Email,
        Avatar = user.Avatar,
        Qq = user.Qq,
        RegTime = user.RegTime,
        Traffic = user.Traffic,
        Inbound = user.Limit?.Inbound != 0 ? user.Limit?.Inbound ?? 0 : user.Inbound,
        Outbound = user.Limit?.Outbound != 0 ? user.Limit?.Outbound ?? 0 : user.Outbound
    };

    private static FrpTunnel ParseTunnel(LocyanTunnelData tunnel) => new()
    {
        Id = tunnel.Id,
        Name = tunnel.Name,
        Type = tunnel.Type,
        LocalIp = tunnel.LocalIp,
        LocalPort = tunnel.LocalPort,
        RemotePort = tunnel.RemotePort == 0 ? null : tunnel.RemotePort,
        UseCompression = tunnel.UseCompression,
        UseEncryption = tunnel.UseEncryption,
        Domain = FrpProviderHelpers.FirstNonEmpty(tunnel.Domain, tunnel.CustomDomain),
        SecretKey = tunnel.SecretKey,
        Node = tunnel.Node == null ? null : ParseNode(tunnel.Node)
    };

    private static FrpNode ParseNode(LocyanNodeData node) => new()
    {
        Id = node.Id,
        Name = node.Name,
        Host = node.Host,
        Ip = node.Ip,
        Description = FrpProviderHelpers.FirstNonEmpty(node.Additional?.Description ?? string.Empty, node.Description),
        PortRanges = node.PortRange
    };
}
