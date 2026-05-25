using System.Collections.Generic;
using System.Text.Json.Serialization;
using Kairo.Models;
using Kairo.Components;
using Kairo.Core.Configuration;
using Kairo.Utils.Configuration;

namespace Kairo.Utils.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false,
    GenerationMode = JsonSourceGenerationMode.Metadata | JsonSourceGenerationMode.Serialization,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    IncludeFields = true,
    Converters = new[] { typeof(BigDecimalJsonConverter) })]
[JsonSerializable(typeof(Config))]
[JsonSerializable(typeof(List<Proxy>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, ProviderAuthState>))]
[JsonSerializable(typeof(ProviderAuthState))]
[JsonSerializable(typeof(Proxy))]
[JsonSerializable(typeof(ProxyNode))]
[JsonSerializable(typeof(UserInfo))]
[JsonSerializable(typeof(UserInfo.LimitInfo))]
[JsonSerializable(typeof(CrashReport))]
[JsonSerializable(typeof(CrashReport.LogLine))]
[JsonSerializable(typeof(MinecraftApiResponse<MinecraftRoomListData>))]
[JsonSerializable(typeof(MinecraftApiResponse<MinecraftRoomData>))]
[JsonSerializable(typeof(MinecraftApiResponse<MinecraftCreateRoomData>))]
[JsonSerializable(typeof(MinecraftApiResponse<MinecraftEmptyData>))]
[JsonSerializable(typeof(List<GitHubReleaseSummary>))]
[JsonSerializable(typeof(GitHubReleaseSummary))]
internal partial class AppJsonContext : JsonSerializerContext
{
}
