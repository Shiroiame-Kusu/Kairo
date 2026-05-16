using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kairo.Models;
using Kairo.Utils.Serialization;

namespace Kairo.Utils;

internal sealed class MinecraftRoomApiClient
{
    private readonly ApiClient _api;

    public MinecraftRoomApiClient(ApiClient api)
    {
        _api = api;
    }

    public async Task<MinecraftApiResponse<MinecraftRoomListData>?> GetRoomsAsync(CancellationToken ct = default)
    {
        var url = $"{Global.CurrentProvider.ApiBaseUrl}/game/minecraft/games?user_id={Global.Config.ID}";
        using var response = await _api.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Deserialize(body, AppJsonContext.Default.MinecraftApiResponseMinecraftRoomListData);
    }

    public async Task<MinecraftApiResponse<MinecraftEmptyData>?> DeleteRoomAsync(string code, CancellationToken ct = default)
    {
        var url = $"{Global.CurrentProvider.ApiBaseUrl}/game/minecraft/game?user_id={Global.Config.ID}&code={Uri.EscapeDataString(code)}";
        using var response = await _api.DeleteAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Deserialize(body, AppJsonContext.Default.MinecraftApiResponseMinecraftEmptyData);
    }

    public async Task<MinecraftApiResponse<MinecraftRoomData>?> GetRoomAsync(string code, CancellationToken ct = default)
    {
        var url = $"{Global.CurrentProvider.ApiBaseUrl}/game/minecraft/game?code={Uri.EscapeDataString(code)}";
        using var response = await _api.GetAsync(url, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Deserialize(body, AppJsonContext.Default.MinecraftApiResponseMinecraftRoomData);
    }

    public async Task<MinecraftApiResponse<MinecraftCreateRoomData>?> CreateRoomAsync(int tunnelId, CancellationToken ct = default)
    {
        using var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("user_id", Global.Config.ID.ToString()),
            new KeyValuePair<string, string>("tunnel_id", tunnelId.ToString())
        });
        using var response = await _api.PutAsync($"{Global.CurrentProvider.ApiBaseUrl}/game/minecraft/game", content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        return Deserialize(body, AppJsonContext.Default.MinecraftApiResponseMinecraftCreateRoomData);
    }

    private static T? Deserialize<T>(string body, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        return string.IsNullOrWhiteSpace(body) ? default : JsonSerializer.Deserialize(body, typeInfo);
    }
}
