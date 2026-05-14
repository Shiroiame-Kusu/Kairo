using System.Text.Json.Nodes;
using Kairo.Core.Models;

namespace Kairo.Core.Providers;

public sealed class LocyanFrpProvider : IFrpProvider
{
    private const string GitHubOwner = "LoCyan-Team";
    private const string GitHubRepo = "LoCyanFrpPureApp";
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
        var url = $"{DashboardUrl}/auth/oauth/authorize";
        url = FrpProviderHelpers.AppendQuery(url,
            ("client_id", request.ClientId.ToString()),
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
        using var response = await http.PostAsync($"{ApiBaseUrl}/auth/oauth/refresh-token", content, ct);
        var json = await FrpProviderHelpers.ReadJsonAsync(response, ct);
        var parsed = ParseResponse(json);
        if (!parsed.Success) return FrpApiResult<string>.Fail(parsed.Code, parsed.Message);
        var token = FrpProviderHelpers.GetString(parsed.Data?["data"]?["refresh_token"]);
        return string.IsNullOrWhiteSpace(token)
            ? FrpApiResult<string>.Fail(0, "API 返回的 Refresh Token 为空")
            : FrpApiResult<string>.Ok(token, parsed.Code, parsed.Message);
    }

    public async Task<FrpApiResult<FrpLoginResult>> LoginWithRefreshTokenAsync(HttpClient http, string refreshToken, CancellationToken ct = default)
    {
        using var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("app_id", AppConstants.APPID.ToString()),
            new KeyValuePair<string, string>("refresh_token", refreshToken)
        });
        using var response = await http.PostAsync($"{ApiBaseUrl}/auth/oauth/access-token", content, ct);
        var json = await FrpProviderHelpers.ReadJsonAsync(response, ct);
        var parsed = ParseResponse(json);
        if (!parsed.Success) return FrpApiResult<FrpLoginResult>.Fail(parsed.Code, parsed.Message);

        var data = parsed.Data?["data"];
        var userId = FrpProviderHelpers.GetInt(data?["user_id"]);
        var accessToken = FrpProviderHelpers.GetString(data?["access_token"]);
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
        using var response = await http.GetAsync(url, ct);
        var json = await FrpProviderHelpers.ReadJsonAsync(response, ct);
        var parsed = ParseResponse(json);
        if (!parsed.Success) return FrpApiResult<FrpUserProfile>.Fail(parsed.Code, parsed.Message);
        var obj = FrpProviderHelpers.ObjectOrEmpty(parsed.Data?["data"]);
        return FrpApiResult<FrpUserProfile>.Ok(ParseUser(obj), parsed.Code, parsed.Message);
    }

    public async Task<FrpApiResult<string>> GetFrpTokenAsync(HttpClient http, int userId, CancellationToken ct = default)
    {
        var url = FrpProviderHelpers.AppendQuery($"{ApiBaseUrl}/user/frp/token", ("user_id", userId.ToString()));
        using var response = await http.GetAsync(url, ct);
        var json = await FrpProviderHelpers.ReadJsonAsync(response, ct);
        var parsed = ParseResponse(json);
        if (!parsed.Success) return FrpApiResult<string>.Fail(parsed.Code, parsed.Message);
        return FrpApiResult<string>.Ok(FrpProviderHelpers.GetString(parsed.Data?["data"]?["token"]), parsed.Code, parsed.Message);
    }

    public async Task<FrpApiResult<string>> GetAnnouncementAsync(HttpClient http, CancellationToken ct = default)
    {
        using var response = await http.GetAsync($"{ApiBaseUrl}/site/notice", ct);
        var json = await FrpProviderHelpers.ReadJsonAsync(response, ct);
        var parsed = ParseResponse(json);
        if (!parsed.Success) return FrpApiResult<string>.Fail(parsed.Code, parsed.Message);
        return FrpApiResult<string>.Ok(FrpProviderHelpers.GetString(parsed.Data?["data"]?["broadcast"]), parsed.Code, parsed.Message);
    }

    public async Task<FrpApiResult<FrpSignStatus>> GetSignStatusAsync(HttpClient http, int userId, CancellationToken ct = default)
    {
        var url = FrpProviderHelpers.AppendQuery($"{ApiBaseUrl}/sign", ("user_id", userId.ToString()));
        using var response = await http.GetAsync(url, ct);
        var json = await FrpProviderHelpers.ReadJsonAsync(response, ct);
        var parsed = ParseResponse(json);
        if (!parsed.Success) return FrpApiResult<FrpSignStatus>.Fail(parsed.Code, parsed.Message);
        return FrpApiResult<FrpSignStatus>.Ok(new FrpSignStatus
        {
            Signed = FrpProviderHelpers.GetBool(parsed.Data?["data"]?["status"])
        }, parsed.Code, parsed.Message);
    }

    public async Task<FrpApiResult<FrpSignResult>> SignAsync(HttpClient http, int userId, CancellationToken ct = default)
    {
        using var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("user_id", userId.ToString()) });
        using var response = await http.PostAsync($"{ApiBaseUrl}/sign", content, ct);
        var json = await FrpProviderHelpers.ReadJsonAsync(response, ct);
        var parsed = ParseResponse(json);
        if (!parsed.Success) return FrpApiResult<FrpSignResult>.Fail(parsed.Code, parsed.Message);
        return FrpApiResult<FrpSignResult>.Ok(new FrpSignResult
        {
            GainedTrafficGb = FrpProviderHelpers.GetDecimal(parsed.Data?["data"]?["get_traffic"])
        }, parsed.Code, parsed.Message);
    }

    public async Task<FrpApiResult<IReadOnlyList<FrpTunnel>>> GetTunnelsAsync(HttpClient http, int userId, CancellationToken ct = default)
    {
        var url = $"{ApiBaseUrl}/tunnels?user_id={userId}";
        using var response = await http.GetAsync(url, ct);
        var json = await FrpProviderHelpers.ReadJsonAsync(response, ct);
        var parsed = ParseResponse(json);
        if (!parsed.Success) return FrpApiResult<IReadOnlyList<FrpTunnel>>.Fail(parsed.Code, parsed.Message);
        var list = (parsed.Data?["data"]?["list"] as JsonArray) ?? new JsonArray();
        return FrpApiResult<IReadOnlyList<FrpTunnel>>.Ok(list.OfType<JsonObject>().Select(ParseTunnel).ToList(), parsed.Code, parsed.Message);
    }

    public async Task<FrpApiResult<IReadOnlyList<FrpNode>>> GetNodesAsync(HttpClient http, int userId, CancellationToken ct = default)
    {
        var url = $"{ApiBaseUrl}/nodes?user_id={userId}";
        using var response = await http.GetAsync(url, ct);
        var json = await FrpProviderHelpers.ReadJsonAsync(response, ct);
        var parsed = ParseResponse(json);
        if (!parsed.Success) return FrpApiResult<IReadOnlyList<FrpNode>>.Fail(parsed.Code, parsed.Message);
        var list = (parsed.Data?["data"]?["list"] as JsonArray) ?? new JsonArray();
        return FrpApiResult<IReadOnlyList<FrpNode>>.Ok(list.OfType<JsonObject>().Select(ParseNode).ToList(), parsed.Code, parsed.Message);
    }

    public async Task<FrpApiResult<int>> GetRandomPortAsync(HttpClient http, int userId, int nodeId, CancellationToken ct = default)
    {
        var url = FrpProviderHelpers.AppendQuery($"{ApiBaseUrl}/node/port", ("user_id", userId.ToString()), ("node_id", nodeId.ToString()));
        using var response = await http.GetAsync(url, ct);
        var json = await FrpProviderHelpers.ReadJsonAsync(response, ct);
        var parsed = ParseResponse(json);
        if (!parsed.Success) return FrpApiResult<int>.Fail(parsed.Code, parsed.Message);
        return FrpApiResult<int>.Ok(FrpProviderHelpers.GetInt(parsed.Data?["data"]?["port"]), parsed.Code, parsed.Message);
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

        using var response = await http.PutAsync($"{ApiBaseUrl}/tunnel", new FormUrlEncodedContent(form), ct);
        var json = await FrpProviderHelpers.ReadJsonAsync(response, ct);
        var parsed = ParseResponse(json);
        if (!parsed.Success) return FrpApiResult<CreateFrpTunnelResult>.Fail(parsed.Code, parsed.Message);
        return FrpApiResult<CreateFrpTunnelResult>.Ok(new CreateFrpTunnelResult
        {
            TunnelId = FrpProviderHelpers.GetInt(parsed.Data?["data"]?["tunnel_id"]),
            TunnelName = request.Name
        }, parsed.Code, parsed.Message);
    }

    public async Task<FrpApiResult<object>> DeleteTunnelAsync(HttpClient http, int userId, FrpTunnel tunnel, CancellationToken ct = default)
    {
        var url = $"{ApiBaseUrl}/tunnel?user_id={userId}&tunnel_id={tunnel.Id}";
        using var response = await http.DeleteAsync(url, ct);
        var json = await FrpProviderHelpers.ReadJsonAsync(response, ct);
        var parsed = ParseResponse(json);
        return parsed.Success
            ? FrpApiResult<object>.Ok(new object(), parsed.Code, parsed.Message)
            : FrpApiResult<object>.Fail(parsed.Code, parsed.Message);
    }

    public Task<FrpApiResult<FrpcConfigResult>> GetFrpcConfigAsync(HttpClient http, FrpTunnel tunnel, CancellationToken ct = default) =>
        Task.FromResult(FrpApiResult<FrpcConfigResult>.Ok(new FrpcConfigResult()));

    public string BuildFrpcArguments(FrpStartOptions options) => $"-u {options.FrpToken} -t {options.TunnelId}";

    public async Task<FrpDownloadRelease?> GetLatestFrpcReleaseAsync(HttpClient http, CancellationToken ct = default)
    {
        var release = await FrpProviderHelpers.TryGetJsonAsync(http, ReleaseApiMirror, ct)
            ?? await FrpProviderHelpers.TryGetJsonAsync(http, ReleaseApiOrigin, ct);
        return release == null ? null : FrpProviderHelpers.ParseGitHubRelease(release);
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

    private static FrpApiResult<JsonObject> ParseResponse(JsonObject? json) =>
        FrpProviderHelpers.ParseApiObject(json, "status", "message");

    private static FrpUserProfile ParseUser(JsonObject obj)
    {
        var limit = obj["limit"] as JsonObject;
        return new FrpUserProfile
        {
            Id = FrpProviderHelpers.GetInt(obj["id"]),
            Username = FrpProviderHelpers.GetString(obj["username"]),
            Email = FrpProviderHelpers.GetString(obj["email"]),
            Avatar = FrpProviderHelpers.GetString(obj["avatar"]),
            Qq = FrpProviderHelpers.GetLong(obj["qq"]),
            RegTime = FrpProviderHelpers.GetString(obj["reg_time"]),
            Traffic = FrpProviderHelpers.GetDecimal(obj["traffic"]),
            Inbound = FrpProviderHelpers.GetInt(limit?["inbound"]) != 0 ? FrpProviderHelpers.GetInt(limit?["inbound"]) : FrpProviderHelpers.GetInt(obj["inbound"]),
            Outbound = FrpProviderHelpers.GetInt(limit?["outbound"]) != 0 ? FrpProviderHelpers.GetInt(limit?["outbound"]) : FrpProviderHelpers.GetInt(obj["outbound"]),
            Raw = obj
        };
    }

    private static FrpTunnel ParseTunnel(JsonObject obj)
    {
        return new FrpTunnel
        {
            Id = FrpProviderHelpers.GetInt(obj["id"]),
            Name = FrpProviderHelpers.GetString(obj["name"]),
            Type = FrpProviderHelpers.GetString(obj["type"]),
            LocalIp = FrpProviderHelpers.GetString(obj["local_ip"]),
            LocalPort = FrpProviderHelpers.GetInt(obj["local_port"]),
            RemotePort = FrpProviderHelpers.GetInt(obj["remote_port"]) == 0 ? null : FrpProviderHelpers.GetInt(obj["remote_port"]),
            UseCompression = FrpProviderHelpers.GetBool(obj["use_compression"]),
            UseEncryption = FrpProviderHelpers.GetBool(obj["use_encryption"]),
            Domain = FrpProviderHelpers.FirstNonEmpty(FrpProviderHelpers.GetString(obj["domain"]), FrpProviderHelpers.GetString(obj["custom_domain"])),
            SecretKey = FrpProviderHelpers.GetString(obj["secret_key"]),
            Node = ParseNode(FrpProviderHelpers.ObjectOrEmpty(obj["node"])),
            Raw = obj
        };
    }

    private static FrpNode ParseNode(JsonObject obj)
    {
        var portRanges = new List<string>();
        if (obj["port_range"] is JsonArray rangeArray)
        {
            portRanges.AddRange(rangeArray.Select(FrpProviderHelpers.GetString).Where(s => !string.IsNullOrWhiteSpace(s)));
        }
        var additional = obj["additional"] as JsonObject;
        return new FrpNode
        {
            Id = FrpProviderHelpers.GetInt(obj["id"]),
            Name = FrpProviderHelpers.GetString(obj["name"]),
            Host = FrpProviderHelpers.GetString(obj["host"]),
            Ip = FrpProviderHelpers.GetString(obj["ip"]),
            Description = FrpProviderHelpers.FirstNonEmpty(FrpProviderHelpers.GetString(additional?["description"]), FrpProviderHelpers.GetString(obj["description"])),
            PortRanges = portRanges,
            Raw = obj
        };
    }
}
