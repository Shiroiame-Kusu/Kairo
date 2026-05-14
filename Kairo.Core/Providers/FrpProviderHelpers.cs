using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Kairo.Core.Models;

namespace Kairo.Core.Providers;

internal static class FrpProviderHelpers
{
    public static async Task<JsonObject?> TryGetJsonAsync(HttpClient http, string url, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            using var resp = await http.GetAsync(url, cts.Token);
            if (!resp.IsSuccessStatusCode) return null;
            var text = await resp.Content.ReadAsStringAsync(cts.Token);
            return JsonNode.Parse(text) as JsonObject;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<JsonObject?> ReadJsonAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(text)) return null;
        return JsonNode.Parse(text) as JsonObject;
    }

    public static FrpApiResult<JsonObject> ParseApiObject(JsonObject? json, string statusName, string messageName)
    {
        if (json == null) return FrpApiResult<JsonObject>.Fail(0, "响应格式错误");
        var code = GetInt(json[statusName]);
        var message = GetString(json[messageName]);
        return code == 200
            ? FrpApiResult<JsonObject>.Ok(json, code, message)
            : FrpApiResult<JsonObject>.Fail(code, message);
    }

    public static FrpDownloadRelease ParseGitHubRelease(JsonObject json)
    {
        var assets = new List<FrpDownloadAsset>();
        if (json["assets"] is JsonArray arr)
        {
            foreach (var item in arr.OfType<JsonObject>())
            {
                assets.Add(new FrpDownloadAsset
                {
                    Name = GetString(item["name"]),
                    DownloadUrl = GetString(item["browser_download_url"]),
                    Digest = GetString(item["digest"]),
                    Raw = item
                });
            }
        }

        var tag = GetString(json["tag_name"]);
        return new FrpDownloadRelease
        {
            Version = ExtractVersion(tag),
            ReleaseName = FirstNonEmpty(GetString(json["name"]), tag),
            TagName = tag,
            Assets = assets,
            Raw = json
        };
    }

    public static (string Platform, string Architecture) GetCurrentPlatform()
    {
        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X86 => "386",
            Architecture.Arm => "arm",
            Architecture.Arm64 => "arm64",
            _ => "amd64"
        };

        var platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "darwin" : "linux";
        return (platform, arch);
    }

    public static FrpDownloadAsset PickArchive(IReadOnlyList<FrpDownloadAsset> assets, string platform, string arch, params string[] prefixes)
    {
        var nonChecksum = assets
            .Where(a => !IsChecksumAsset(a.Name))
            .ToList();

        var candidates = nonChecksum
            .Where(a => prefixes.Any(prefix => a.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (candidates.Count == 0)
        {
            candidates = nonChecksum
                .Where(a => a.Name.Contains($"{platform}_{arch}", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        if (candidates.Count == 0)
        {
            var any = nonChecksum.FirstOrDefault() ?? throw new InvalidOperationException("未找到可用资产");
            return any;
        }

        FrpDownloadAsset? picked;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            picked = candidates.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            picked = candidates.FirstOrDefault(a => a.Name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                ?? candidates.FirstOrDefault(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        }

        return picked ?? candidates.First();
    }

    public static bool IsChecksumAsset(string name) =>
        name.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".sha256.txt", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".md5", StringComparison.OrdinalIgnoreCase) ||
        name.EndsWith(".md5.txt", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("sha256sum.txt", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("checksums.txt", StringComparison.OrdinalIgnoreCase);

    public static string ExtractVersion(string tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return string.Empty;
        var match = Regex.Match(tag, "v?(\\d+\\.\\d+\\.\\d+[a-zA-Z0-9.-]*)");
        return match.Success ? match.Groups[1].Value : tag.TrimStart('v');
    }

    public static string AppendQuery(string url, params (string Name, string Value)[] values)
    {
        var separator = url.Contains('?') ? '&' : '?';
        return url + separator + string.Join("&", values.Select(v => $"{Uri.EscapeDataString(v.Name)}={Uri.EscapeDataString(v.Value)}"));
    }

    public static int GetInt(JsonNode? node)
    {
        if (node == null) return 0;
        try
        {
            if (node is JsonValue value)
            {
                if (value.TryGetValue<int>(out var i)) return i;
                if (value.TryGetValue<long>(out var l)) return (int)l;
                if (value.TryGetValue<string>(out var s) && int.TryParse(s, out var parsed)) return parsed;
            }
        }
        catch { }
        return 0;
    }

    public static long GetLong(JsonNode? node)
    {
        if (node == null) return 0;
        try
        {
            if (node is JsonValue value)
            {
                if (value.TryGetValue<long>(out var l)) return l;
                if (value.TryGetValue<int>(out var i)) return i;
                if (value.TryGetValue<string>(out var s) && long.TryParse(s, out var parsed)) return parsed;
            }
        }
        catch { }
        return 0;
    }

    public static decimal GetDecimal(JsonNode? node)
    {
        if (node == null) return 0;
        try
        {
            if (node is JsonValue value)
            {
                if (value.TryGetValue<decimal>(out var d)) return d;
                if (value.TryGetValue<double>(out var dbl)) return (decimal)dbl;
                if (value.TryGetValue<long>(out var l)) return l;
                if (value.TryGetValue<string>(out var s) && decimal.TryParse(s, out var parsed)) return parsed;
            }
        }
        catch { }
        return 0;
    }

    public static bool GetBool(JsonNode? node)
    {
        if (node == null) return false;
        try
        {
            if (node is JsonValue value)
            {
                if (value.TryGetValue<bool>(out var b)) return b;
                if (value.TryGetValue<int>(out var i)) return i != 0;
                if (value.TryGetValue<string>(out var s) && bool.TryParse(s, out var parsed)) return parsed;
            }
        }
        catch { }
        return false;
    }

    public static string GetString(JsonNode? node)
    {
        if (node == null) return string.Empty;
        if (node is JsonValue value && value.TryGetValue<string>(out var str)) return str ?? string.Empty;
        return node.ToString();
    }

    public static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

    public static JsonObject ObjectOrEmpty(JsonNode? node) => node as JsonObject ?? new JsonObject();
}
