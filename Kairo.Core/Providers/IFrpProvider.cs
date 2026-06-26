using Kairo.Core.Models;

namespace Kairo.Core.Providers;

public interface IFrpProvider
{
    FrpProviderType Type { get; }
    string Id { get; }
    string DisplayName { get; }
    string ApiBaseUrl { get; }
    string DashboardUrl { get; }
    bool SupportsOAuthLogin { get; }
    bool SupportsSign { get; }
    bool SupportsMinecraftRooms { get; }

    string BuildOAuthUrl(OAuthRequest request);
    Task<FrpApiResult<string>> ExchangeCodeForRefreshTokenAsync(HttpClient http, string code, string redirectUri = "", string codeVerifier = "", CancellationToken ct = default);
    Task<FrpApiResult<FrpLoginResult>> LoginWithRefreshTokenAsync(HttpClient http, string refreshToken, CancellationToken ct = default);
    Task<FrpApiResult<FrpUserProfile>> GetUserInfoAsync(HttpClient http, int userId, CancellationToken ct = default);
    Task<FrpApiResult<string>> GetFrpTokenAsync(HttpClient http, int userId, CancellationToken ct = default);
    Task<FrpApiResult<string>> GetAnnouncementAsync(HttpClient http, CancellationToken ct = default);
    Task<FrpApiResult<FrpSignStatus>> GetSignStatusAsync(HttpClient http, int userId, CancellationToken ct = default);
    Task<FrpApiResult<FrpSignResult>> SignAsync(HttpClient http, int userId, CancellationToken ct = default);
    Task<FrpApiResult<IReadOnlyList<FrpTunnel>>> GetTunnelsAsync(HttpClient http, int userId, CancellationToken ct = default);
    Task<FrpApiResult<IReadOnlyList<FrpNode>>> GetNodesAsync(HttpClient http, int userId, CancellationToken ct = default);
    Task<FrpApiResult<int>> GetRandomPortAsync(HttpClient http, int userId, int nodeId, CancellationToken ct = default);
    Task<FrpApiResult<CreateFrpTunnelResult>> CreateTunnelAsync(HttpClient http, int userId, CreateFrpTunnelRequest request, CancellationToken ct = default);
    Task<FrpApiResult<object>> DeleteTunnelAsync(HttpClient http, int userId, FrpTunnel tunnel, CancellationToken ct = default);
    Task<FrpApiResult<FrpcConfigResult>> GetFrpcConfigAsync(HttpClient http, FrpTunnel tunnel, CancellationToken ct = default);
    string BuildFrpcArguments(FrpStartOptions options);
    Task<FrpDownloadRelease?> GetLatestFrpcReleaseAsync(HttpClient http, CancellationToken ct = default);
    FrpAssetSelection SelectBestAsset(FrpDownloadRelease release);
    string GetDownloadUrl(FrpDownloadRelease release, FrpDownloadAsset asset, bool useMirror);
    string? GetChecksumUrl(FrpDownloadRelease release, FrpDownloadAsset asset, bool useMirror);
}

public sealed class OAuthRequest
{
    public int ClientId { get; init; }
    public string ClientIdText { get; init; } = string.Empty;
    public string Scopes { get; init; } = string.Empty;
    public string RedirectUri { get; init; } = string.Empty;
    public string Mode { get; init; } = "code";
    public int CallbackPort { get; init; }
    public string State { get; init; } = string.Empty;
    public string CodeChallenge { get; init; } = string.Empty;
    public string CodeChallengeMethod { get; init; } = string.Empty;
}
