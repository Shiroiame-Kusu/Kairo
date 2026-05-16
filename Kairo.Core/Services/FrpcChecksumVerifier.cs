using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Kairo.Core.Logging;
using Kairo.Core.Models;
using Kairo.Core.Providers;

namespace Kairo.Core.Services;

internal sealed class FrpcChecksumVerifier
{
    private readonly HttpClient _http;

    public FrpcChecksumVerifier(HttpClient http)
    {
        _http = http;
    }

    public async Task VerifyAsync(IFrpProvider provider, FrpDownloadRelease release, FrpDownloadAsset asset, string filePath, bool useMirror, CancellationToken ct)
    {
        var digest = asset.Digest;
        if (!string.IsNullOrWhiteSpace(digest) && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
        {
            var expected = digest["sha256:".Length..].Trim().ToLowerInvariant();
            await VerifyHashAsync(filePath, expected, sha256: true, ct);
            return;
        }

        var checksumUrl = provider.GetChecksumUrl(release, asset, useMirror);
        if (string.IsNullOrWhiteSpace(checksumUrl)) return;

        string checksumContent;
        try
        {
            checksumContent = await _http.GetStringAsyncLogged(checksumUrl, ct);
        }
        catch (System.Exception ex)
        {
            Kairo.Core.Logging.CoreLogger.Output(Kairo.Core.Logging.CoreLogLevel.Error, "Unhandled exception in Kairo.Core/Services/FrpcChecksumVerifier.cs:36", ex);
            return;
        }

        var expectedHash = FindHashForAsset(checksumContent, asset.Name, out var sha256);
        if (string.IsNullOrWhiteSpace(expectedHash)) return;
        await VerifyHashAsync(filePath, expectedHash, sha256, ct);
    }

    private static string? FindHashForAsset(string checksumContent, string assetName, out bool sha256)
    {
        sha256 = false;
        string? fallback = null;
        var fallbackSha256 = false;

        foreach (var line in checksumContent.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            var hash = parts[0].Trim().ToLowerInvariant();
            var isSha256 = Regex.IsMatch(hash, "^[a-fA-F0-9]{64}$");
            var isMd5 = Regex.IsMatch(hash, "^[a-fA-F0-9]{32}$");
            if (!isSha256 && !isMd5) continue;

            fallback ??= hash;
            fallbackSha256 = isSha256;

            if (parts.Any(p => p.Contains(assetName, StringComparison.OrdinalIgnoreCase)))
            {
                sha256 = isSha256;
                return hash;
            }
        }

        sha256 = fallbackSha256;
        return fallback;
    }

    private static async Task VerifyHashAsync(string filePath, string expectedHash, bool sha256, CancellationToken ct)
    {
        await using var fs = File.OpenRead(filePath);
        var actual = sha256
            ? Convert.ToHexString(await SHA256.HashDataAsync(fs, ct)).ToLowerInvariant()
            : Convert.ToHexString(await MD5.HashDataAsync(fs, ct)).ToLowerInvariant();
        if (!string.Equals(expectedHash, actual, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("文件哈希不匹配");
    }
}
