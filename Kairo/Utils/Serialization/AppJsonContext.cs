using System.Collections.Generic;
using System.Text.Json.Serialization;
using Kairo;
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
[JsonSerializable(typeof(Config))]
[JsonSerializable(typeof(List<Proxy>))]
[JsonSerializable(typeof(Proxy))]
[JsonSerializable(typeof(ProxyNode))]
[JsonSerializable(typeof(MainWindow.UserInfo))]
[JsonSerializable(typeof(MainWindow.UserInfo.LimitInfo))]
[JsonSerializable(typeof(CrashReport))]
[JsonSerializable(typeof(CrashReport.LogLine))]
internal partial class AppJsonContext : JsonSerializerContext
{
}
