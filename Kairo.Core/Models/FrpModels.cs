using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Kairo.Core.Models;

public sealed class FrpApiResult<T>
{
    public bool Success { get; init; }
    public int Code { get; init; }
    public string Message { get; init; } = string.Empty;
    public T? Data { get; init; }

    public static FrpApiResult<T> Ok(T? data, int code = 200, string message = "") => new()
    {
        Success = true,
        Code = code,
        Message = message,
        Data = data
    };

    public static FrpApiResult<T> Fail(int code, string message) => new()
    {
        Success = false,
        Code = code,
        Message = string.IsNullOrWhiteSpace(message) ? "未知错误" : message
    };
}

public sealed class FrpLoginResult
{
    public int UserId { get; init; }
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public FrpUserProfile User { get; init; } = new();
    public string FrpToken { get; init; } = string.Empty;
}

public sealed class FrpUserProfile
{
    public int Id { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Avatar { get; init; } = string.Empty;
    public long Qq { get; init; }
    public string RegTime { get; init; } = string.Empty;
    public decimal Traffic { get; init; }
    public int Inbound { get; init; }
    public int Outbound { get; init; }
    public bool TodayChecked { get; init; }
    public JsonObject Raw { get; init; } = new();
}

public sealed class FrpTunnel
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string LocalIp { get; init; } = string.Empty;
    public int LocalPort { get; init; }
    public int? RemotePort { get; init; }
    public bool UseCompression { get; init; }
    public bool UseEncryption { get; init; }
    public string? Domain { get; init; }
    public string? SecretKey { get; init; }
    public FrpNode? Node { get; init; }
    public JsonObject Raw { get; init; } = new();
}

public sealed class FrpNode
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public string Ip { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<string> PortRanges { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SupportedProtocols { get; init; } = Array.Empty<string>();
    public JsonObject Raw { get; init; } = new();
}

public sealed class CreateFrpTunnelRequest
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string LocalIp { get; init; } = string.Empty;
    public int LocalPort { get; init; }
    public int NodeId { get; init; }
    public int? RemotePort { get; init; }
    public bool UseEncryption { get; init; }
    public bool UseCompression { get; init; }
    public string SecretKey { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
}

public sealed class CreateFrpTunnelResult
{
    public int TunnelId { get; init; }
    public string TunnelName { get; init; } = string.Empty;
}

public sealed class FrpSignStatus
{
    public bool Signed { get; init; }
}

public sealed class FrpSignResult
{
    public decimal GainedTrafficGb { get; init; }
}

public sealed class FrpStartOptions
{
    public int TunnelId { get; init; }
    public string TunnelName { get; init; } = string.Empty;
    public string FrpToken { get; init; } = string.Empty;
    public string ApiBaseUrl { get; init; } = string.Empty;
}

public sealed class FrpcConfigResult
{
    public string Config { get; init; } = string.Empty;
    public string Token { get; init; } = string.Empty;
}

public sealed class FrpDownloadRelease
{
    public string Version { get; init; } = string.Empty;
    public string ReleaseName { get; init; } = string.Empty;
    public string TagName { get; init; } = string.Empty;
    public IReadOnlyList<FrpDownloadAsset> Assets { get; init; } = Array.Empty<FrpDownloadAsset>();
    public JsonObject Raw { get; init; } = new();
}

public sealed class FrpDownloadAsset
{
    public string Name { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public string? Digest { get; init; }
    public JsonObject Raw { get; init; } = new();
}

public sealed class FrpAssetSelection
{
    public string Version { get; init; } = string.Empty;
    public string Platform { get; init; } = string.Empty;
    public string Architecture { get; init; } = string.Empty;
    public FrpDownloadAsset Asset { get; init; } = new();
}

public sealed class LoliaNodeListRequest
{
    public int Page { get; init; }
    public int Limit { get; init; }
}

public sealed class LoliaCreateTunnelRequest
{
    public int NodeId { get; init; }
    public string Type { get; init; } = string.Empty;
    public string LocalIp { get; init; } = string.Empty;
    public int LocalPort { get; init; }
    public int RemotePort { get; init; }
    public string CustomDomain { get; init; } = string.Empty;
    public string Remark { get; init; } = string.Empty;
}

public sealed class LoliaOAuthTokenData
{
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public string TokenType { get; init; } = string.Empty;
    public int ExpiresIn { get; init; }
    public string Scope { get; init; } = string.Empty;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(FrpDownloadRelease))]
[JsonSerializable(typeof(FrpDownloadAsset))]
[JsonSerializable(typeof(List<FrpDownloadAsset>))]
[JsonSerializable(typeof(LoliaNodeListRequest))]
[JsonSerializable(typeof(LoliaCreateTunnelRequest))]
[JsonSerializable(typeof(LoliaOAuthTokenData))]
public partial class FrpModelsJsonContext : JsonSerializerContext
{
}
