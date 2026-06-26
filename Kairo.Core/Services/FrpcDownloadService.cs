using System.Diagnostics;
using Kairo.Core.Logging;
using Kairo.Core.Models;
using Kairo.Core.Providers;

namespace Kairo.Core.Services;

public sealed class FrpcDownloadService
{
    private const int MaxAttempts = 3;
    private readonly HttpClient _http;
    private readonly FrpcChecksumVerifier _checksumVerifier;

    public FrpcDownloadService(HttpClient http)
    {
        _http = http;
        _checksumVerifier = new FrpcChecksumVerifier(http);
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
                    await _checksumVerifier.VerifyAsync(provider, release, asset, tempFile, options.UseMirror && !options.ForceOrigin && sourceRound == 0, ct);

                    progress?.Report(new FrpcDownloadProgress { Stage = FrpcDownloadStage.Extracting, Message = "正在解压..." });
                    var finalPath = await FrpcArchiveExtractor.ExtractAsync(tempFile, workDir, ct);

                    progress?.Report(new FrpcDownloadProgress { Stage = FrpcDownloadStage.Completed, Message = "完成", FrpcPath = finalPath });
                    return new FrpcInstallResult
                    {
                        Success = true,
                        FrpcPath = finalPath,
                        Version = selection.Version,
                        ProviderId = provider.Id
                    };
                }
                catch (OperationCanceledException ex)
                {
                    Kairo.Core.Logging.CoreLogger.Output(Kairo.Core.Logging.CoreLogLevel.Error, "Unhandled exception in Kairo.Core/Services/FrpcDownloadService.cs:82", ex);
                    throw;
                }
                catch (Exception ex)
                {
                    Kairo.Core.Logging.CoreLogger.Output(Kairo.Core.Logging.CoreLogLevel.Error, "Unhandled exception in Kairo.Core/Services/FrpcDownloadService.cs:86", ex);
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
        catch (System.Exception ex)
        {
            Kairo.Core.Logging.CoreLogger.Output(Kairo.Core.Logging.CoreLogLevel.Error, "Unhandled exception in Kairo.Core/Services/FrpcDownloadService.cs:117", ex);
            return false;
        }
    }

    private async Task DownloadFileAsync(string url, string destPath, IProgress<FrpcDownloadProgress>? progress, CancellationToken ct)
    {
        using var response = await _http.GetAsyncLogged(url, HttpCompletionOption.ResponseHeadersRead, ct);
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
}
