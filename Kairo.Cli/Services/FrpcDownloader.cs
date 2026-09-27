using System.Diagnostics;
using Kairo.Core;
using Kairo.Core.Models;
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
    private const int BarWidth = 24;
    private readonly HttpClient _http = new();
    private readonly FrpcDownloadService _downloadService;
    private readonly IFrpProvider _provider;
    private bool _progressLineActive;
    private int _lastReportedDecile = -1;

    public class DownloadResult
    {
        public bool Success { get; set; }
        public string? FrpcPath { get; set; }
        public string? Message { get; set; }
    }

    public FrpcDownloader(IFrpProvider provider)
    {
        _provider = provider;
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
            Logger.Debug($"开始下载 frpc: provider={_provider.Id}, forceGitHub={ForceGitHub}");

            if (ForceGitHub)
                ConsoleUi.Step("下载", "使用 GitHub 源（用户指定）");

            var result = await _downloadService.InstallAsync(
                _provider,
                new FrpcInstallOptions
                {
                    UseMirror = CliConfigManager.Config.UsingDownloadMirror,
                    ForceOrigin = ForceGitHub
                },
                new InlineProgress<FrpcDownloadProgress>(ReportProgress),
                token);

            EndProgressLine();
            overallSw.Stop();
            if (!result.Success)
            {
                Logger.Error($"frpc 下载失败: {result.Message}");
                Logger.MethodExit("失败");
                return new DownloadResult { Success = false, Message = result.Message };
            }

            ProviderFrpcPath.Set(_provider, result.FrpcPath);

            Logger.Debug($"下载流程完成，总耗时: {overallSw.ElapsedMilliseconds}ms, frpc={result.FrpcPath}");
            Logger.MethodExit("成功");
            return new DownloadResult { Success = true, FrpcPath = result.FrpcPath };
        }
        catch (OperationCanceledException)
        {
            EndProgressLine();
            Logger.Warning("下载被取消");
            Logger.MethodExit("取消");
            return new DownloadResult { Success = false, Message = "下载已取消" };
        }
        catch (Exception ex)
        {
            EndProgressLine();
            Logger.Exception(ex, "DownloadAsync 发生未预期异常");
            Logger.MethodExit("异常");
            return new DownloadResult { Success = false, Message = ex.Message };
        }
    }

    private void ReportProgress(FrpcDownloadProgress progress)
    {
        switch (progress.Stage)
        {
            case FrpcDownloadStage.FetchingRelease:
            case FrpcDownloadStage.Verifying:
            case FrpcDownloadStage.Extracting:
                if (!string.IsNullOrWhiteSpace(progress.Message))
                {
                    EndProgressLine();
                    ConsoleUi.Step("下载", progress.Message);
                }
                break;
            case FrpcDownloadStage.Downloading:
                if (!string.IsNullOrWhiteSpace(progress.Message))
                {
                    EndProgressLine();
                    ConsoleUi.Step("下载", progress.Message);
                    if (!string.IsNullOrWhiteSpace(progress.DownloadUrl))
                        Logger.Debug($"下载地址: {progress.DownloadUrl}");
                }

                if (progress.ReceivedBytes > 0)
                    DrawProgress(progress);
                break;
            case FrpcDownloadStage.Completed:
                EndProgressLine();
                ConsoleUi.Success("frpc 下载完成");
                break;
        }
    }

    private void DrawProgress(FrpcDownloadProgress progress)
    {
        var hasTotal = progress.TotalBytes > 0;
        var sizeText = hasTotal
            ? $"{FormatBytes(progress.ReceivedBytes)} / {FormatBytes(progress.TotalBytes)}"
            : FormatBytes(progress.ReceivedBytes);

        if (Console.IsOutputRedirected)
        {
            // 非终端输出时不能刷新同一行，每 10% 输出一次
            if (!hasTotal) return;
            var decile = (int)(progress.Percent / 10);
            if (decile == _lastReportedDecile) return;
            _lastReportedDecile = decile;
            Console.WriteLine($"[下载] {progress.Percent:F0}% {sizeText}");
            return;
        }

        var bar = hasTotal ? BuildBar(progress.Percent) : string.Empty;
        var percent = hasTotal ? $"{progress.Percent,5:F1}%" : string.Empty;
        var line = $"\r  {bar} {percent}  {sizeText}  {FormatSpeed(progress.SpeedBytesPerSecond)}";
        Console.Write(line.PadRight(Math.Min(ConsoleUi.GetWindowWidth() - 1, 90)));
        _progressLineActive = true;
    }

    private void EndProgressLine()
    {
        if (!_progressLineActive) return;
        _progressLineActive = false;
        Console.WriteLine();
    }

    /// <summary>
    /// 在调用方线程同步回调：Progress&lt;T&gt; 在控制台程序中会被投递到线程池，导致进度乱序
    /// </summary>
    private sealed class InlineProgress<T>(Action<T> handler) : IProgress<T>
    {
        private readonly object _lock = new();

        public void Report(T value)
        {
            lock (_lock) handler(value);
        }
    }

    private static string BuildBar(double percent)
    {
        var filled = (int)Math.Round(Math.Clamp(percent, 0, 100) / 100 * BarWidth);
        var body = filled >= BarWidth
            ? new string('=', BarWidth)
            : new string('=', Math.Max(0, filled - 1)) + (filled > 0 ? ">" : string.Empty) + new string(' ', BarWidth - filled);
        return $"[{body}]";
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
