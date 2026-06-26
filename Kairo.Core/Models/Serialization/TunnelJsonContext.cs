using System.Text.Json.Serialization;

namespace Kairo.Core.Models;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(Tunnel))]
[JsonSerializable(typeof(TunnelNode))]
[JsonSerializable(typeof(List<Tunnel>))]
public partial class TunnelJsonContext : JsonSerializerContext
{
}
