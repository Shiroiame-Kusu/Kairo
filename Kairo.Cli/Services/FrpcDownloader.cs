using System.Diagnostics;
using Kairo.Core;
using Kairo.Core.Providers;
using Kairo.Core.Services;
using Kairo.Cli.Configuration;
using Kairo.Cli.Utils;

namespace Kairo.Cli.Services;

/// <summary>
/// frpc 下载服务
/// </summary>
public class FrpcDownloader : IDisposable
{
    private readonly HttpClient _http = new();
    private readonly FrpcDownloadService _downloadService;

    public class DownloadResult
    {
        public bool Success { get; set; }
        public string? FrpcPath { get; set; }
        public string? Message { get; set; }
    }

    public FrpcDownloader()
    {
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"Kairo/{AppConstants.Version}");
        _downloadService = new FrpcDownloadService(_http);
    }

    public void Dispose()
    {
        Logger.Debug("FrpcDownloader Dispose 调用");
        _http.Dispose();
    }

    /// <summary>
    /// 是否强制使用 GitHub 源（通过命令行参数设置）
    /// </summary>
    public bool ForceGitHub { get; set; } = false;

    public async Task<DownloadResult> DownloadAsync(CancellationToken token = default)
    {
        Logger.MethodEntry();
        var overallSw = Stopwatch.StartNew();

        try
        {
            var provider = FrpProviderRegistry.Get(CliConfigManager.Config.ProviderId);
            Console.WriteLine($"[下载] 正在获取 {provider.DisplayName} frpc 最新版本信息...");
            Logger.Info($"开始下载 frpc: provider={provider.Id}, forceGitHub={ForceGitHub}");

            if (ForceGitHub)
                Console.WriteLine("[下载] 使用 GitHub 源（用户指定）");

            var result = await _downloadService.InstallAsync(
                provider,
                new FrpcInstallOptions
                {
                    UseMirror = CliConfigManager.Config.UsingDownloadMirror,
                    ForceOrigin = ForceGitHub
                },
                new Progress<FrpcDownloadProgress>(ReportProgress),
                token);

            overallSw.Stop();
            if (!result.Success)
            {
                Logger.Error($"frpc 下载失败: {result.Message}");
                Logger.MethodExit("失败");
                return new DownloadResult { Success = false, Message = result.Message };
            }

            ProviderFrpcPath.Set(provider, result.FrpcPath);

            Logger.Info($"下载流程完成，总耗时: {overallSw.ElapsedMilliseconds}ms, frpc={result.FrpcPath}");
            Logger.MethodExit("成功");
            Console.WriteLine($"[成功] frpc 已下载到: {result.FrpcPath}");
            return new DownloadResult { Success = true, FrpcPath = result.FrpcPath };
        }
        catch (OperationCanceledException)
        {
            Logger.Warning("下载被取消");
            Logger.MethodExit("取消");
            return new DownloadResult { Success = false, Message = "下载已取消" };
        }
        catch (Exception ex)
        {
            Logger.Exception(ex, "DownloadAsync 发生未预期异常");
            Logger.MethodExit("异常");
            return new DownloadResult { Success = false, Message = ex.Message };
        }
    }

    private static void ReportProgress(FrpcDownloadProgress progress)
    {
        switch (progress.Stage)
        {
            case FrpcDownloadStage.FetchingRelease:
            case FrpcDownloadStage.Verifying:
            case FrpcDownloadStage.Extracting:
                if (!string.IsNullOrWhiteSpace(progress.Message))
                    Console.WriteLine($"[下载] {progress.Message}");
                break;
            case FrpcDownloadStage.Downloading:
                if (!string.IsNullOrWhiteSpace(progress.Message))
                {
                    Console.WriteLine($"[下载] {progress.Message}");
                    if (!string.IsNullOrWhiteSpace(progress.DownloadUrl))
                        Logger.Debug($"下载地址: {progress.DownloadUrl}");
                }

                if (progress.ReceivedBytes > 0)
                {
                    if (progress.TotalBytes > 0)
                        Console.Write($"\r[下载] 进度: {FormatBytes(progress.ReceivedBytes)} / {FormatBytes(progress.TotalBytes)} ({progress.Percent:F1}%) {FormatSpeed(progress.SpeedBytesPerSecond)}    ");
                    else
                        Console.Write($"\r[下载] 已下载: {FormatBytes(progress.ReceivedBytes)} {FormatSpeed(progress.SpeedBytesPerSecond)}    ");
                }
                break;
            case FrpcDownloadStage.Completed:
                Console.WriteLine();
                Console.WriteLine("[下载] 完成");
                break;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return bytes + " B";
        double kb = bytes / 1024d;
        if (kb < 1024) return kb.ToString("F1") + " KB";
        double mb = kb / 1024d;
        return mb < 1024 ? mb.ToString("F2") + " MB" : (mb / 1024d).ToString("F2") + " GB";
    }

    private static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond <= 0) return string.Empty;
        return bytesPerSecond > 1024 * 1024
            ? $"{bytesPerSecond / 1024d / 1024d:F2} MB/s"
            : $"{bytesPerSecond / 1024d:F1} KB/s";
    }
}
