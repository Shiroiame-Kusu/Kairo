using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using Kairo.Core.Logging;
using Kairo.Core.Models;

namespace Kairo.Core.Providers;

internal static class FrpProviderHelpers
{
    public static async Task<FrpDownloadRelease?> TryGetGitHubReleaseAsync(HttpClient http, string url, CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));
            using var resp = await http.GetAsyncLogged(url, cts.Token);
            if (!resp.IsSuccessStatusCode) return null;
            var release = await ReadJsonAsync(resp, FrpModelsJsonContext.Default.GitHubReleaseData, cts.Token);
            return release == null ? null : ParseGitHubRelease(release);
        }
        catch (System.Exception ex)
        {
            Kairo.Core.Logging.CoreLogger.Output(Kairo.Core.Logging.CoreLogLevel.Error, "Unhandled exception in Kairo.Core/Providers/FrpProviderHelpers.cs:23", ex);
            return null;
        }
    }

    public static string ToGitHubReleaseMirrorUrl(string downloadUrl)
    {
        const string githubPrefix = "https://github.com/";
        if (!downloadUrl.StartsWith(githubPrefix, StringComparison.OrdinalIgnoreCase)) return downloadUrl;
        return $"{AppConstants.GithubMirror}/github.com/{downloadUrl[githubPrefix.Length..]}";
    }

    public static async Task<T?> ReadJsonAsync<T>(HttpResponseMessage response, JsonTypeInfo<T> typeInfo, CancellationToken ct)
    {
        var text = await response.Content.ReadAsStringAsync(ct);
        if (string.IsNullOrWhiteSpace(text)) return default;
        return JsonSerializer.Deserialize(text, typeInfo);
    }

    public static FrpApiResult<T> ParseLoliaResponse<T>(LoliaApiResponse<T>? response)
    {
        if (response == null) return FrpApiResult<T>.Fail(0, "响应格式错误");
        return response.Code == 200
            ? FrpApiResult<T>.Ok(response.Data, response.Code, response.Msg)
            : FrpApiResult<T>.Fail(response.Code, response.Msg);
    }

    public static FrpApiResult<T> ParseLocyanResponse<T>(LocyanApiResponse<T>? response)
    {
        if (response == null) return FrpApiResult<T>.Fail(0, "响应格式错误");
        return response.Status == 200
            ? FrpApiResult<T>.Ok(response.Data, response.Status, response.Message)
            : FrpApiResult<T>.Fail(response.Status, response.Message);
    }

    private static FrpDownloadRelease ParseGitHubRelease(GitHubReleaseData release)
    {
        var tag = release.TagName;
        return new FrpDownloadRelease
        {
            Version = ExtractVersion(tag),
            ReleaseName = FirstNonEmpty(release.Name, tag),
            TagName = tag,
            Assets = release.Assets.Select(asset => new FrpDownloadAsset
            {
                Name = asset.Name,
                DownloadUrl = asset.BrowserDownloadUrl,
                Digest = asset.Digest
            }).ToList()
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

    public static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;
}
