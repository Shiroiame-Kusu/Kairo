using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Kairo.Models;

public sealed class MinecraftApiResponse<T>
{
    [JsonPropertyName("status")]
    public int Status { get; init; }

    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    [JsonPropertyName("data")]
    public T? Data { get; init; }
}

public sealed class MinecraftRoomListData
{
    [JsonPropertyName("list")]
    public List<MinecraftRoomData> List { get; init; } = new();
}

public sealed class MinecraftRoomData
{
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("proxy_id")]
    public int ProxyId { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("host")]
    public string Host { get; init; } = string.Empty;

    [JsonPropertyName("port")]
    public int Port { get; init; }
}

public sealed class MinecraftCreateRoomData
{
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;
}

public sealed class MinecraftEmptyData
{
}
