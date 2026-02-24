using System.Text.Json.Serialization;

namespace Kairo.Models;

/// <summary>
/// Generic envelope returned by the Lolia-Center v1 API.
/// All endpoints return <c>{ "code": int, "msg": string, "data": T }</c>.
/// </summary>
public class ApiResponse<T>
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("msg")] public string Msg { get; set; } = string.Empty;
    [JsonPropertyName("data")] public T? Data { get; set; }

    [JsonIgnore] public bool IsSuccess => Code == 0 || Code == 200 || Code == 201;
}
