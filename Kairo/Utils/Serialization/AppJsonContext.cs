using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Kairo.Models;
using Kairo.Models.Api;
using Kairo.Components;
using Kairo.Utils.Configuration;
using static Kairo.Utils.CrashInterception;

namespace Kairo.Utils.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    GenerationMode = JsonSourceGenerationMode.Metadata | JsonSourceGenerationMode.Serialization,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    IncludeFields = true,
    Converters = new[] { typeof(BigDecimalJsonConverter) })]
// ── Core types ──
[JsonSerializable(typeof(Config))]
[JsonSerializable(typeof(List<Proxy>))]
[JsonSerializable(typeof(Proxy))]
[JsonSerializable(typeof(ProxyNode))]
[JsonSerializable(typeof(UserInfo))]
[JsonSerializable(typeof(CrashReport))]
[JsonSerializable(typeof(CrashReport.LogLine))]
// ── API response envelope ──
[JsonSerializable(typeof(ApiResponse<UserInfoData>))]
[JsonSerializable(typeof(ApiResponse<TunnelRealtimeData>))]
[JsonSerializable(typeof(ApiResponse<TrafficStatsData>))]
[JsonSerializable(typeof(ApiResponse<DailyTrafficData>))]
[JsonSerializable(typeof(ApiResponse<TunnelTrafficListData>))]
[JsonSerializable(typeof(ApiResponse<TunnelListData>))]
[JsonSerializable(typeof(ApiResponse<TunnelDetailData>))]
[JsonSerializable(typeof(ApiResponse<DeleteTunnelData>))]
[JsonSerializable(typeof(ApiResponse<FrpcConfigData>))]
[JsonSerializable(typeof(ApiResponse<FrpcConfigByTokenData>))]
[JsonSerializable(typeof(ApiResponse<DomainListData>))]
[JsonSerializable(typeof(ApiResponse<AddDomainData>))]
[JsonSerializable(typeof(ApiResponse<VerifyDomainData>))]
[JsonSerializable(typeof(ApiResponse<NodeListData>))]
[JsonSerializable(typeof(ApiResponse<OAuthAppListData>))]
[JsonSerializable(typeof(ApiResponse<OAuthAppDetailData>))]
[JsonSerializable(typeof(ApiResponse<OAuthApproveData>))]
[JsonSerializable(typeof(ApiResponse<OAuthTokenData>))]
[JsonSerializable(typeof(ApiResponse<ClientVersionData>))]
[JsonSerializable(typeof(ApiResponse<JsonNode>))]
// ── API data models ──
[JsonSerializable(typeof(UserInfoData))]
[JsonSerializable(typeof(TunnelRealtimeData))]
[JsonSerializable(typeof(TrafficStatsData))]
[JsonSerializable(typeof(DailyTrafficData))]
[JsonSerializable(typeof(DailyTrafficStat))]
[JsonSerializable(typeof(TunnelTrafficListData))]
[JsonSerializable(typeof(TunnelTrafficItem))]
[JsonSerializable(typeof(TunnelListData))]
[JsonSerializable(typeof(TunnelItem))]
[JsonSerializable(typeof(TunnelDetailData))]
[JsonSerializable(typeof(CreateTunnelRequest))]
[JsonSerializable(typeof(DeleteTunnelData))]
[JsonSerializable(typeof(FrpcConfigData))]
[JsonSerializable(typeof(FrpcConfigByTokenData))]
[JsonSerializable(typeof(DomainListData))]
[JsonSerializable(typeof(AddDomainRequest))]
[JsonSerializable(typeof(AddDomainData))]
[JsonSerializable(typeof(VerifyDomainRequest))]
[JsonSerializable(typeof(VerifyDomainData))]
[JsonSerializable(typeof(NodeListData))]
[JsonSerializable(typeof(NodeData))]
[JsonSerializable(typeof(OAuthAppListData))]
[JsonSerializable(typeof(OAuthAppDetailData))]
[JsonSerializable(typeof(CreateOAuthAppRequest))]
[JsonSerializable(typeof(OAuthApproveRequest))]
[JsonSerializable(typeof(OAuthApproveData))]
[JsonSerializable(typeof(OAuthTokenData))]
[JsonSerializable(typeof(ClientVersionData))]
internal partial class AppJsonContext : JsonSerializerContext
{
}
