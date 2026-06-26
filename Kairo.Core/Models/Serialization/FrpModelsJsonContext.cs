using System.Text.Json.Serialization;

namespace Kairo.Core.Models;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(FrpDownloadRelease))]
[JsonSerializable(typeof(FrpDownloadAsset))]
[JsonSerializable(typeof(List<FrpDownloadAsset>))]
[JsonSerializable(typeof(GitHubReleaseData))]
[JsonSerializable(typeof(GitHubReleaseAssetData))]
[JsonSerializable(typeof(LoliaNodeListRequest))]
[JsonSerializable(typeof(LoliaCreateTunnelRequest))]
[JsonSerializable(typeof(LocyanApiResponse<LocyanRefreshTokenData>))]
[JsonSerializable(typeof(LocyanApiResponse<LocyanAccessTokenData>))]
[JsonSerializable(typeof(LocyanApiResponse<LocyanUserData>))]
[JsonSerializable(typeof(LocyanApiResponse<LocyanFrpTokenData>))]
[JsonSerializable(typeof(LocyanApiResponse<LocyanAnnouncementData>))]
[JsonSerializable(typeof(LocyanApiResponse<LocyanSignStatusData>))]
[JsonSerializable(typeof(LocyanApiResponse<LocyanSignData>))]
[JsonSerializable(typeof(LocyanApiResponse<LocyanTunnelListData>))]
[JsonSerializable(typeof(LocyanApiResponse<LocyanNodeListData>))]
[JsonSerializable(typeof(LocyanApiResponse<LocyanRandomPortData>))]
[JsonSerializable(typeof(LocyanApiResponse<LocyanCreateTunnelData>))]
[JsonSerializable(typeof(LocyanApiResponse<object>))]
[JsonSerializable(typeof(LoliaOAuthTokenData))]
[JsonSerializable(typeof(LoliaApiResponse<LoliaOAuthTokenData>))]
[JsonSerializable(typeof(LoliaApiResponse<LoliaUserInfoData>))]
[JsonSerializable(typeof(LoliaApiResponse<LoliaTunnelListData>))]
[JsonSerializable(typeof(LoliaApiResponse<LoliaTunnelData>))]
[JsonSerializable(typeof(LoliaApiResponse<LoliaNodeListData>))]
[JsonSerializable(typeof(LoliaApiResponse<LoliaCreateTunnelData>))]
[JsonSerializable(typeof(LoliaApiResponse<LoliaCheckinData>))]
[JsonSerializable(typeof(LoliaApiResponse<object>))]
public partial class FrpModelsJsonContext : JsonSerializerContext
{
}
