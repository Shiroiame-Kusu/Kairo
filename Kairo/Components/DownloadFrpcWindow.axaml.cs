using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Downloader;
using Kairo.Utils;
using Kairo.Utils.Configuration;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using System.Formats.Tar;
using System.Net;
using System.Reflection;
using Kairo.Utils.Logger;
using Microsoft.Extensions.Logging;

namespace Kairo.Components
{
    public partial class DownloadFrpcWindow : Window
    {
        private readonly HttpClient _http = new();
        private CancellationTokenSource _cts = new();
        private DownloadService? _downloadService;
        private string? _tempFile;
        private const int MaxAttempts = 3;
        private TextBlock? _statusTextRef; // null-safe refs
        private ProgressBar? _progressRef;
        private TextBlock? _progressTextRef;
        private TextBlock? _speedTextRef;
        private TextBlock? _tipTextRef;
        private Button? _cancelBtnRef;
        private Button? _closeBtnRef;
        CookieContainer cookies = new();

        public DownloadFrpcWindow()
        {
            InitializeComponent();
            // Acquire controls defensively (compiled XAML fields may be null on some platforms / build modes)
            _statusTextRef = this.FindControl<TextBlock>("StatusText");
            _progressRef = this.FindControl<ProgressBar>("Progress");
            _progressTextRef = this.FindControl<TextBlock>("ProgressText");
            _speedTextRef = this.FindControl<TextBlock>("SpeedText");
            _tipTextRef = this.FindControl<TextBlock>("TipText");
            _cancelBtnRef = this.FindControl<Button>("CancelBtn");
            _closeBtnRef = this.FindControl<Button>("CloseBtn");
            if (_tipTextRef != null && Global.Tips != null && Global.Tips.Count > 0)
                _tipTextRef.Text = Global.Tips[Random.Shared.Next(0, Global.Tips.Count)];
            Opened += async (_, _) => await StartAsync();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void UpdateProgress(double percent, long received, long total, double speedBytesPerSec)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_progressRef != null)
                {
                    _progressRef.IsIndeterminate = false;
                    _progressRef.Value = percent;
                }

                if (_progressTextRef != null)
                {
                    if (total > 0)
                        _progressTextRef.Text =
                            FormatBytes(received) + " / " + FormatBytes(total) + $" ({percent:F1}%)";
                    else
                        _progressTextRef.Text = FormatBytes(received);
                }

                if (_speedTextRef != null)
                {
                    string speedStr = speedBytesPerSec > 1024 * 1024
                        ? ($"{speedBytesPerSec / 1024d / 1024d:F2} MB/s")
                        : ($"{speedBytesPerSec / 1024d:F1} KB/s");
                    _speedTextRef.Text = $"速度: {speedStr}";
                }
            });
        }

        private async Task StartAsync()
        {
            try
            {
                SetStatus("正在获取版本信息...");
                _http.DefaultRequestHeaders.UserAgent.ParseAdd("Kairo-FrpcDownloader");
                string apiMirror = "https://api-gh.1l1.icu/repos/LoCyan-Team/LocyanFrpPureApp/releases/latest";
                string apiOrigin = "https://api.github.com/repos/LoCyan-Team/LocyanFrpPureApp/releases/latest";
                JObject release = await TryFetch(apiMirror) ??
                                  await TryFetch(apiOrigin) ?? throw new Exception("无法获取版本信息");
                var (version, assets, asset, assetName, platform, arch) = SelectBestAsset(release);
                SetStatus($"最新版本: {version} 体系结构: {platform}-{arch}");

                string downloadUrl = asset["browser_download_url"]?.ToString() ?? throw new Exception("下载地址缺失");
                if (Global.Config.UsingDownloadMirror)
                {
                    // locyan mirrors format: https://mirrors.locyan.cn/github-release/LoCyan-Team/LoCyanFrpPureApp/{releaseName}/ + assetName
                    string releaseName = release["name"]?.ToString() ?? release["tag_name"]?.ToString() ?? version;
                    downloadUrl = "https://mirrors.locyan.cn/github-release/LoCyan-Team/LoCyanFrpPureApp/" +
                                  Uri.EscapeDataString(releaseName) + "/" + Uri.EscapeDataString(assetName);
                }

                if (_progressRef != null) _progressRef.IsIndeterminate = false;
                Exception? lastError = null;
                for (int attempt = 1; attempt <= MaxAttempts; attempt++)
                {
                    if (_cts.IsCancellationRequested) break;
                    try
                    {
                        SetStatus(attempt == 1 ? "正在下载..." : $"正在下载...(重试 {attempt}/{MaxAttempts})");
                        // Pass release name for checksum mirror construction
                        string releaseNameForPass =
                            release["name"]?.ToString() ?? release["tag_name"]?.ToString() ?? version;
                        await DownloadAndExtract(downloadUrl, version, platform, arch, _cts.Token, assets, assetName,
                            releaseNameForPass);
                        SetStatus("完成");
                        if (_closeBtnRef != null) _closeBtnRef.IsEnabled = true;
                        if (_cancelBtnRef != null) _cancelBtnRef.IsEnabled = false;
                        return;
                    }
                    catch (OperationCanceledException)
                    {
                        SetStatus("已取消");
                        if (_closeBtnRef != null) _closeBtnRef.IsEnabled = true;
                        return;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        if (attempt < MaxAttempts && !_cts.IsCancellationRequested)
                        {
                            SetStatus($"失败: {ex.Message} - 正在重试 ({attempt}/{MaxAttempts})");
                            await Task.Delay(1500, _cts.Token);
                            ResetProgressUI();
                            continue;
                        }
                        else
                        {
                            SetStatus($"失败: {ex.Message}");
                            if (_closeBtnRef != null) _closeBtnRef.IsEnabled = true;
                            return;
                        }
                    }
                }

                if (lastError != null)
                {
                    SetStatus($"失败: {lastError.Message}");
                    if (_closeBtnRef != null) _closeBtnRef.IsEnabled = true;
                }
            }
            catch (OperationCanceledException)
            {
                SetStatus("已取消");
                if (_closeBtnRef != null) _closeBtnRef.IsEnabled = true;
            }
            catch (Exception ex)
            {
                SetStatus("失败: " + ex.Message);
                if (_closeBtnRef != null) _closeBtnRef.IsEnabled = true;
            }
        }

        private void CancelBtn_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_cancelBtnRef != null) _cancelBtnRef.IsEnabled = false;
            try
            {
                _downloadService?.CancelAsync();
            }
            catch
            {
            }

            _cts.Cancel();
        }

        private void CloseBtn_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            Close();
        }

        private async Task<JObject?> TryFetch(string url)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                var resp = await _http.GetAsync(url, cts.Token);
                if (!resp.IsSuccessStatusCode) return null;
                return JObject.Parse(await resp.Content.ReadAsStringAsync(cts.Token));
            }
            catch
            {
                return null;
            }
        }

        private (string version, JArray assets, JToken asset, string assetName, string platform, string arch)
            SelectBestAsset(JObject release)
        {
            var tag = release["tag_name"]?.ToString() ?? string.Empty;
            var m = Regex.Match(tag, "v(\\d+\\.\\d+\\.\\d+)-\\d+");
            string version = m.Success ? m.Groups[1].Value : tag.TrimStart('v');
            var assets = release["assets"] as JArray ?? new JArray();
            string arch = RuntimeInformation.OSArchitecture switch
            {
                Architecture.X86 => "386",
                Architecture.Arm => "arm",
                Architecture.Arm64 => "arm64",
                _ => "amd64"
            };
            string platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" :
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" : "windows";
            string basePattern = $"frp_LoCyanFrp-{version}_{platform}_{arch}";
            var candidates = assets.Where(a =>
                (a["name"]?.ToString() ?? "").StartsWith(basePattern, StringComparison.OrdinalIgnoreCase)).ToList();
            if (candidates.Count == 0)
            {
                // fallback widen search
                candidates = assets.Where(a => (a["name"]?.ToString() ?? "").Contains($"{platform}_{arch}")).ToList();
            }

            if (candidates.Count == 0)
            {
                var any = assets.FirstOrDefault();
                if (any == null) throw new Exception("未找到资产");
                return (version, assets, any, any["name"]?.ToString() ?? string.Empty, platform, arch);
            }

            // rank preference
            JToken? pick = null;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                pick = candidates.FirstOrDefault(a => (a["name"]?.ToString() ?? "").EndsWith(".zip"));
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                pick = candidates.FirstOrDefault(a => (a["name"]?.ToString() ?? "").EndsWith(".tar.gz")) ??
                       candidates.FirstOrDefault(a => (a["name"]?.ToString() ?? "").EndsWith(".zip"));
            }

            pick ??= candidates.First();
            string assetName = pick["name"]?.ToString() ?? string.Empty;
            return (version, assets, pick, assetName, platform, arch);
        }

        private void ResetProgressUI()
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_progressRef != null) _progressRef.Value = 0;
                if (_progressTextRef != null) _progressTextRef.Text = string.Empty;
                if (_speedTextRef != null) _speedTextRef.Text = string.Empty;
            });
        }

        private async Task DownloadAndExtract(string url, string version, string platform, string arch,
            CancellationToken token, JArray assets, string assetName, string releaseName)
        {
            string workDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kairo",
                "frpc");
            Directory.CreateDirectory(workDir);
            // Preserve the original asset file name (with extension) so we can detect archive type (.zip / .tar.gz)
            // Previously used a .tmp extension which broke the EndsWith checks and caused "未找到 frpc 可执行文件" later.
            string downloadFileName = string.IsNullOrWhiteSpace(assetName) ? "frpc_download" : assetName;
            _tempFile = Path.Combine(workDir, downloadFileName);
            if (File.Exists(_tempFile)) File.Delete(_tempFile);

            var config = new DownloadConfiguration
            {
                BufferBlockSize = 8192,
                ChunkCount = Math.Clamp(Environment.ProcessorCount, 4, 12),
                ParallelDownload = true,
                ParallelCount = Math.Clamp(Environment.ProcessorCount, 4, 12),
                Timeout = 5000,
                MaxTryAgainOnFailure = 3
            };
            _downloadService = new DownloadService(config);
            var tcs = new TaskCompletionSource<bool>();
            using var reg = token.Register(() =>
            {
                try
                {
                    _downloadService?.CancelAsync();
                }
                catch
                {
                }
            });
            _downloadService.DownloadFileCompleted += (s, e) =>
            {
                if (e.Error != null)
                    tcs.TrySetException(e.Error);
                else if (e.Cancelled)
                    tcs.TrySetCanceled();
                else
                    tcs.TrySetResult(true);
            };
            _downloadService.DownloadProgressChanged += (s, e) =>
            {
                double percent = e.ProgressPercentage;
                UpdateProgress(percent, e.ReceivedBytesSize, e.TotalBytesToReceive, e.BytesPerSecondSpeed);
            };
            await _downloadService.DownloadFileTaskAsync(url, _tempFile);
            await tcs.Task;
            SetStatus("正在校验...");
            try
            {
                await VerifyChecksumAsync(_tempFile, assets, assetName, token, releaseName);
            }
            catch (Exception ex)
            {
                throw new Exception($"校验失败: {ex.Message}");
            }

            SetStatus("正在解压...");
            string extractDir = Path.Combine(workDir, "extract");
            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
            Directory.CreateDirectory(extractDir);
            if (_tempFile.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                ZipFile.ExtractToDirectory(_tempFile, extractDir);
            }
            else if (_tempFile.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
            {
                ExtractTarGz(_tempFile, extractDir);
            }

            string exeName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "frpc.exe" : "frpc";
            var frpcPath = Directory.GetFiles(extractDir, exeName, SearchOption.AllDirectories).FirstOrDefault();
            if (frpcPath == null) throw new Exception("未找到 frpc 可执行文件");
            string finalPath = Path.Combine(workDir, exeName);
            File.Copy(frpcPath, finalPath, true);
            try
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    System.Diagnostics.Process.Start("/bin/chmod", $"+x {finalPath}");
                }
            }
            catch
            {
            }

            Global.Config.FrpcPath = finalPath;
            ConfigManager.Save();
            Dispatcher.UIThread.Post(() =>
                (Access.DashBoard as DashBoard.DashBoard)?.OpenSnackbar("下载完成", finalPath,
                    FluentAvalonia.UI.Controls.InfoBarSeverity.Success));
        }

        private void ExtractTarGz(string gzFile, string extractDir)
        {
            using var fs = File.OpenRead(gzFile);
            using var gzip = new GZipStream(fs, CompressionMode.Decompress, leaveOpen: false);
            using var tar = new TarReader(gzip, leaveOpen: false);
            TarEntry? entry;
            while ((entry = tar.GetNextEntry()) != null)
            {
                var fullPath = Path.Combine(extractDir, entry.Name.TrimStart('.', '/'));
                switch (entry.EntryType)
                {
                    case TarEntryType.Directory:
                        Directory.CreateDirectory(fullPath);
                        break;
                    case TarEntryType.RegularFile:
                        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                        using (var outFs = File.Open(fullPath, FileMode.Create, FileAccess.Write))
                        {
                            entry.DataStream?.CopyTo(outFs);
                        }

                        break;
                    default:
                        break;
                }
            }
        }

        private async Task VerifyChecksumAsync(string filePath, JArray assets, string assetName,
            CancellationToken token, string releaseName)
        {
            // Prefer SHA256 then MD5
            JToken? checksumAsset = assets.FirstOrDefault(a =>
                                        (a["name"]?.ToString() ?? "").EndsWith(".sha256",
                                            StringComparison.OrdinalIgnoreCase) &&
                                        (a["name"]?.ToString() ?? "").Contains(assetName.Split('.')[0]))
                                    ?? assets.FirstOrDefault(a =>
                                        (a["name"]?.ToString() ?? "").EndsWith(".sha256.txt",
                                            StringComparison.OrdinalIgnoreCase) &&
                                        (a["name"]?.ToString() ?? "").Contains(assetName.Split('.')[0]))
                                    ?? assets.FirstOrDefault(a =>
                                        (a["name"]?.ToString() ?? "").EndsWith(".md5",
                                            StringComparison.OrdinalIgnoreCase) &&
                                        (a["name"]?.ToString() ?? "").Contains(assetName.Split('.')[0]))
                                    ?? assets.FirstOrDefault(a =>
                                        (a["name"]?.ToString() ?? "").EndsWith(".md5.txt",
                                            StringComparison.OrdinalIgnoreCase) &&
                                        (a["name"]?.ToString() ?? "").Contains(assetName.Split('.')[0]));
            if (checksumAsset == null)
            {
                SetStatus("未提供校验文件, 跳过校验");
                return;
            }

            string checksumUrl = checksumAsset["browser_download_url"]?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(checksumUrl))
            {
                SetStatus("校验文件链接缺失, 跳过校验");
                return;
            }

            if (Global.Config.UsingDownloadMirror)
            {
                // Mirror path uses release folder + checksum file name
                string checksumFileName = checksumAsset["name"]?.ToString() ?? string.Empty;
                checksumUrl = "https://mirrors.locyan.cn/github-release/LoCyan-Team/LoCyanFrpPureApp/" +
                              Uri.EscapeDataString(releaseName) + "/" + Uri.EscapeDataString(checksumFileName);
            }

            string checksumContent = await _http.GetStringAsync(checksumUrl, token);
            // Parse first valid hash line
            string? expectedHash = null;
            bool sha256 = false;
            foreach (var line in checksumContent.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (Regex.IsMatch(parts[0], "^[a-fA-F0-9]{64}$"))
                {
                    expectedHash = parts[0].ToLowerInvariant();
                    sha256 = true;
                    break;
                }

                if (Regex.IsMatch(parts[0], "^[a-fA-F0-9]{32}$"))
                {
                    expectedHash = parts[0].ToLowerInvariant();
                    sha256 = false;
                    break;
                }
            }

            if (expectedHash == null)
            {
                SetStatus("校验文件无有效哈希, 跳过校验");
                return;
            }

            string actualHash;
            using (var fs = File.OpenRead(filePath))
            {
                if (sha256)
                    actualHash = Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
                else
                    actualHash = Convert.ToHexString(MD5.HashData(fs)).ToLowerInvariant();
            }

            if (!string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception("文件哈希不匹配");
            }

            SetStatus("校验通过");
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            double kb = bytes / 1024d;
            if (kb < 1024) return kb.ToString("F1") + " KB";
            double mb = kb / 1024d;
            if (mb < 1024) return mb.ToString("F2") + " MB";
            double gb = mb / 1024d;
            return gb.ToString("F2") + " GB";
        }

        private void SetStatus(string txt) => Dispatcher.UIThread.Post(() =>
        {
            if (_statusTextRef != null) _statusTextRef.Text = txt;
        });
    }
}