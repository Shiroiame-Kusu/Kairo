using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Kairo.Core.Models;
using Kairo.Core.Providers;

namespace Kairo.Core.Services;

public sealed class FrpcDownloadService
{
    private const int MaxAttempts = 3;
    private readonly HttpClient _http;

    public FrpcDownloadService(HttpClient http)
    {
        _http = http;
    }

    public async Task<FrpcInstallResult> InstallAsync(
        IFrpProvider provider,
        FrpcInstallOptions options,
        IProgress<FrpcDownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report(new FrpcDownloadProgress { Stage = FrpcDownloadStage.FetchingRelease, Message = "正在获取版本信息..." });
        var release = await provider.GetLatestFrpcReleaseAsync(_http, ct) ?? throw new InvalidOperationException("无法获取版本信息");
        var selection = provider.SelectBestAsset(release);
        var asset = selection.Asset;
        if (string.IsNullOrWhiteSpace(asset.DownloadUrl)) throw new InvalidOperationException("下载地址缺失");

        var downloadUrl = provider.GetDownloadUrl(release, asset, options.UseMirror && !options.ForceOrigin);
        var workDir = string.IsNullOrWhiteSpace(options.WorkDirectory)
            ? Path.Combine(EnvironmentDetector.GetApplicationDataPath(), "Kairo", "frpc", provider.Id)
            : options.WorkDirectory;
        Directory.CreateDirectory(workDir);

        var tempFile = Path.Combine(workDir, string.IsNullOrWhiteSpace(asset.Name) ? "frpc_download" : asset.Name);
        if (File.Exists(tempFile)) File.Delete(tempFile);

        Exception? lastError = null;
        var currentUrl = downloadUrl;
        var canFallback = options.UseMirror && !options.ForceOrigin && !string.Equals(downloadUrl, asset.DownloadUrl, StringComparison.OrdinalIgnoreCase);

        for (var sourceRound = 0; sourceRound < (canFallback ? 2 : 1); sourceRound++)
        {
            if (sourceRound == 1)
            {
                currentUrl = asset.DownloadUrl;
                if (File.Exists(tempFile)) File.Delete(tempFile);
                progress?.Report(new FrpcDownloadProgress { Stage = FrpcDownloadStage.Downloading, Message = "镜像失败，切换到 GitHub 源..." });
            }

            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Report(new FrpcDownloadProgress
                    {
                        Stage = FrpcDownloadStage.Downloading,
                        Message = attempt == 1 ? "正在下载..." : $"正在下载...(重试 {attempt}/{MaxAttempts})",
                        DownloadUrl = currentUrl
                    });
                    await DownloadFileAsync(currentUrl, tempFile, progress, ct);

                    progress?.Report(new FrpcDownloadProgress { Stage = FrpcDownloadStage.Verifying, Message = "正在校验..." });
                    await VerifyChecksumAsync(provider, release, asset, tempFile, options.UseMirror && !options.ForceOrigin && sourceRound == 0, ct);

                    progress?.Report(new FrpcDownloadProgress { Stage = FrpcDownloadStage.Extracting, Message = "正在解压..." });
                    var finalPath = await ExtractAsync(tempFile, workDir, ct);

                    progress?.Report(new FrpcDownloadProgress { Stage = FrpcDownloadStage.Completed, Message = "完成", FrpcPath = finalPath });
                    return new FrpcInstallResult
                    {
                        Success = true,
                        FrpcPath = finalPath,
                        Version = selection.Version,
                        ProviderId = provider.Id
                    };
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    if (attempt < MaxAttempts)
                    {
                        await Task.Delay(1500, ct);
                        continue;
                    }
                }
            }
        }

        return new FrpcInstallResult
        {
            Success = false,
            Message = lastError?.Message ?? "下载失败"
        };
    }

    public static string GetManagedDirectory(IFrpProvider provider) =>
        Path.Combine(EnvironmentDetector.GetApplicationDataPath(), "Kairo", "frpc", provider.Id);

    public static bool IsManagedFrpcPath(string? path, IFrpProvider provider)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            var full = Path.GetFullPath(path);
            var workDir = Path.GetFullPath(GetManagedDirectory(provider));
            return full.StartsWith(workDir + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private async Task DownloadFileAsync(string url, string destPath, IProgress<FrpcDownloadProgress>? progress, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength ?? -1;
        var received = 0L;
        var started = Stopwatch.StartNew();
        var lastReport = DateTime.UtcNow;

        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
        var buffer = new byte[8192];
        int read;
        while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
            received += read;

            if ((DateTime.UtcNow - lastReport).TotalMilliseconds >= 200)
            {
                lastReport = DateTime.UtcNow;
                var percent = total > 0 ? received * 100d / total : 0;
                var speed = started.Elapsed.TotalSeconds > 0 ? received / started.Elapsed.TotalSeconds : 0;
                progress?.Report(new FrpcDownloadProgress
                {
                    Stage = FrpcDownloadStage.Downloading,
                    ReceivedBytes = received,
                    TotalBytes = total,
                    Percent = percent,
                    SpeedBytesPerSecond = speed
                });
            }
        }
    }

    private async Task VerifyChecksumAsync(IFrpProvider provider, FrpDownloadRelease release, FrpDownloadAsset asset, string filePath, bool useMirror, CancellationToken ct)
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
            checksumContent = await _http.GetStringAsync(checksumUrl, ct);
        }
        catch
        {
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
        bool fallbackSha256 = false;

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

    private static async Task<string> ExtractAsync(string archivePath, string workDir, CancellationToken ct)
    {
        var extractDir = Path.Combine(workDir, "extract");
        if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
        Directory.CreateDirectory(extractDir);

        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            ZipFile.ExtractToDirectory(archivePath, extractDir);
        }
        else if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            await ExtractTarGzAsync(archivePath, extractDir, ct);
        }
        else
        {
            throw new InvalidOperationException("不支持的压缩格式");
        }

        var exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "frpc.exe" : "frpc";
        var frpcPath = Directory.GetFiles(extractDir, exeName, SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new InvalidOperationException("未找到 frpc 可执行文件");

        var finalPath = Path.Combine(workDir, exeName);
        File.Copy(frpcPath, finalPath, true);
        EnsureExecutable(finalPath);

        TryCleanup(archivePath, extractDir);
        return finalPath;
    }

    private static async Task ExtractTarGzAsync(string gzFile, string extractDir, CancellationToken ct)
    {
        await using var fs = File.OpenRead(gzFile);
        await using var gzip = new GZipStream(fs, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);

        TarEntry? entry;
        while ((entry = await tar.GetNextEntryAsync(cancellationToken: ct)) != null)
        {
            var fullPath = Path.Combine(extractDir, entry.Name.TrimStart('.', '/'));
            switch (entry.EntryType)
            {
                case TarEntryType.Directory:
                    Directory.CreateDirectory(fullPath);
                    break;
                case TarEntryType.RegularFile:
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                    await using (var outFs = File.Create(fullPath))
                    {
                        if (entry.DataStream != null)
                            await entry.DataStream.CopyToAsync(outFs, ct);
                    }
                    break;
            }
        }
    }

    private static void EnsureExecutable(string path)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/chmod",
                Arguments = $"+x \"{path}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            process?.WaitForExit(3000);
        }
        catch { }
    }

    private static void TryCleanup(string archivePath, string extractDir)
    {
        try { if (File.Exists(archivePath)) File.Delete(archivePath); } catch { }
        try { if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true); } catch { }
    }
}

public sealed class FrpcInstallOptions
{
    public bool UseMirror { get; init; }
    public bool ForceOrigin { get; init; }
    public string WorkDirectory { get; init; } = string.Empty;
}

public sealed class FrpcInstallResult
{
    public bool Success { get; init; }
    public string FrpcPath { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string ProviderId { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}

public sealed class FrpcDownloadProgress
{
    public FrpcDownloadStage Stage { get; init; }
    public string Message { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public long ReceivedBytes { get; init; }
    public long TotalBytes { get; init; }
    public double Percent { get; init; }
    public double SpeedBytesPerSecond { get; init; }
    public string FrpcPath { get; init; } = string.Empty;
}

public enum FrpcDownloadStage
{
    FetchingRelease,
    Downloading,
    Verifying,
    Extracting,
    Completed
}
