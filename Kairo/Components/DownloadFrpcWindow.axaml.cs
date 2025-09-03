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

namespace Kairo.Components
{
    
    public partial class DownloadFrpcWindow : Window
    {
        private readonly HttpClient _http = new();
        private CancellationTokenSource _cts = new();
        private DownloadService? _downloadService;
        private string? _tempFile;
        private long _lastBytes;
        private DateTime _lastTime;
        private const int MaxAttempts = 3;

        public DownloadFrpcWindow()
        {
            InitializeComponent();
            TipText.Text = Global.Tips[Random.Shared.Next(0, Global.Tips.Count)];
            Opened += async (_, _) => await StartAsync();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private async Task StartAsync()
        {
            try
            {
                StatusText.Text = "正在获取版本信息...";
                _http.DefaultRequestHeaders.UserAgent.ParseAdd("Kairo-FrpcDownloader");
                string apiMirror = "https://api-gh.1l1.icu/repos/LoCyan-Team/LocyanFrpPureApp/releases/latest";
                string apiOrigin = "https://api.github.com/repos/LoCyan-Team/LocyanFrpPureApp/releases/latest";
                JObject release = await TryFetch(apiMirror) ?? await TryFetch(apiOrigin) ?? throw new Exception("无法获取版本信息");
                var (version, assets, asset, assetName, platform, arch) = SelectBestAsset(release);
                StatusText.Text = $"最新版本: {version} 体系结构: {platform}-{arch}";

                string downloadUrl = asset["browser_download_url"]?.ToString() ?? throw new Exception("下载地址缺失");
                if (Global.Config.UsingDownloadMirror)
                    downloadUrl = Global.GithubMirror + downloadUrl;

                Progress.IsIndeterminate = false;
                Exception? lastError = null;
                for (int attempt = 1; attempt <= MaxAttempts; attempt++)
                {
                    if (_cts.IsCancellationRequested) break;
                    try
                    {
                        StatusText.Text = attempt == 1 ? "正在下载..." : $"正在下载...(重试 {attempt}/{MaxAttempts})";
                        await DownloadAndExtract(downloadUrl, version, platform, arch, _cts.Token, assets, assetName);
                        StatusText.Text = "完成";
                        CloseBtn.IsEnabled = true;
                        CancelBtn.IsEnabled = false;
                        return;
                    }
                    catch (OperationCanceledException)
                    {
                        StatusText.Text = "已取消";
                        CloseBtn.IsEnabled = true;
                        return;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        if (attempt < MaxAttempts && !_cts.IsCancellationRequested)
                        {
                            StatusText.Text = $"失败: {ex.Message} - 正在重试 ({attempt}/{MaxAttempts})";
                            await Task.Delay(1500, _cts.Token);
                            ResetProgressUI();
                            continue;
                        }
                        else
                        {
                            StatusText.Text = $"失败: {ex.Message}";
                            CloseBtn.IsEnabled = true;
                            return;
                        }
                    }
                }
                if (lastError != null)
                {
                    StatusText.Text = $"失败: {lastError.Message}";
                    CloseBtn.IsEnabled = true;
                }
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "已取消";
                CloseBtn.IsEnabled = true;
            }
            catch (Exception ex)
            {
                StatusText.Text = "失败: " + ex.Message;
                CloseBtn.IsEnabled = true;
            }
        }

        private void CancelBtn_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            CancelBtn.IsEnabled = false;
            try { _downloadService?.CancelAsync(); } catch { }
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
            catch { return null; }
        }

        private (string version, JArray assets, JToken asset, string assetName, string platform, string arch) SelectBestAsset(JObject release)
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
            string platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" : "windows";
            string basePattern = $"frp_LoCyanFrp-{version}_{platform}_{arch}";
            var candidates = assets.Where(a => (a["name"]?.ToString() ?? "").StartsWith(basePattern, StringComparison.OrdinalIgnoreCase)).ToList();
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
                pick = candidates.FirstOrDefault(a => (a["name"]?.ToString() ?? "").EndsWith(".tar.gz")) ?? candidates.FirstOrDefault(a => (a["name"]?.ToString() ?? "").EndsWith(".zip"));
            }
            pick ??= candidates.First();
            string assetName = pick["name"]?.ToString() ?? string.Empty;
            return (version, assets, pick, assetName, platform, arch);
        }

        private void ResetProgressUI()
        {
            Dispatcher.UIThread.Post(() =>
            {
                Progress.Value = 0;
                ProgressText.Text = string.Empty;
                SpeedText.Text = string.Empty;
            });
        }

        private async Task DownloadAndExtract(string url, string version, string platform, string arch, CancellationToken token, JArray assets, string assetName)
        {
            string workDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kairo", "frpc");
            Directory.CreateDirectory(workDir);
            _tempFile = Path.Combine(workDir, "frpc_download.tmp");
            if (File.Exists(_tempFile)) File.Delete(_tempFile);

            var config = new DownloadConfiguration
            {
                BufferBlockSize = 8192,
                MaxTryAgainOnFailover = 3,
                ParallelDownload = true,
                ParallelCount = 4,
                Timeout = 10000
            };
            _downloadService = new DownloadService(config);
            var tcs = new TaskCompletionSource<bool>();

            using var reg = token.Register(() =>
            {
                try { _downloadService?.CancelAsync(); } catch { }
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
                Dispatcher.UIThread.Post(() =>
                {
                    if (e.TotalBytesToReceive > 0)
                    {
                        Progress.Value = e.ProgressPercentage;
                        ProgressText.Text = FormatBytes(e.ReceivedBytesSize) + " / " + FormatBytes(e.TotalBytesToReceive) + $" ({e.ProgressPercentage:F1}%)";
                    }
                    else
                    {
                        ProgressText.Text = FormatBytes(e.ReceivedBytesSize);
                    }
                    string speedStr = e.BytesPerSecondSpeed > 1024 * 1024 ? ($"{e.BytesPerSecondSpeed / 1024d / 1024d:F2} MB/s") : ($"{e.BytesPerSecondSpeed / 1024d:F1} KB/s");
                    SpeedText.Text = $"速度: {speedStr}";
                });
            };

            await _downloadService.DownloadFileTaskAsync(url, _tempFile);
            await tcs.Task; // ensure completion events processed

            // Hash verification
            StatusText.Text = "正在校验...";
            try
            {
                await VerifyChecksumAsync(_tempFile, assets, assetName, token);
            }
            catch (Exception ex)
            {
                throw new Exception($"校验失败: {ex.Message}");
            }

            // Extract
            StatusText.Text = "正在解压...";
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
                    System.Diagnostics.Process.Start("/bin/chmod", $"+x '{finalPath}'");
                }
            }
            catch { }
            Global.Config.FrpcPath = finalPath;
            ConfigManager.Save();
            Dispatcher.UIThread.Post(() => (Access.DashBoard as DashBoard)?.OpenSnackbar("下载完成", finalPath, FluentAvalonia.UI.Controls.InfoBarSeverity.Success));
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

        private async Task VerifyChecksumAsync(string filePath, JArray assets, string assetName, CancellationToken token)
        {
            // Prefer SHA256 then MD5
            JToken? checksumAsset = assets.FirstOrDefault(a => (a["name"]?.ToString() ?? "").EndsWith(".sha256", StringComparison.OrdinalIgnoreCase) && (a["name"]?.ToString() ?? "").Contains(assetName.Split('.')[0]))
                                     ?? assets.FirstOrDefault(a => (a["name"]?.ToString() ?? "").EndsWith(".sha256.txt", StringComparison.OrdinalIgnoreCase) && (a["name"]?.ToString() ?? "").Contains(assetName.Split('.')[0]))
                                     ?? assets.FirstOrDefault(a => (a["name"]?.ToString() ?? "").EndsWith(".md5", StringComparison.OrdinalIgnoreCase) && (a["name"]?.ToString() ?? "").Contains(assetName.Split('.')[0]))
                                     ?? assets.FirstOrDefault(a => (a["name"]?.ToString() ?? "").EndsWith(".md5.txt", StringComparison.OrdinalIgnoreCase) && (a["name"]?.ToString() ?? "").Contains(assetName.Split('.')[0]));
            if (checksumAsset == null)
            {
                StatusText.Text = "未提供校验文件, 跳过校验";
                return;
            }
            string checksumUrl = checksumAsset["browser_download_url"]?.ToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(checksumUrl))
            {
                StatusText.Text = "校验文件链接缺失, 跳过校验";
                return;
            }
            if (Global.Config.UsingDownloadMirror)
                checksumUrl = Global.GithubMirror + checksumUrl;
            string checksumContent = await _http.GetStringAsync(checksumUrl, token);
            // Parse first valid hash line
            string? expectedHash = null;
            bool sha256 = false;
            foreach (var line in checksumContent.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.Length == 0) continue;
                var parts = trimmed.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (Regex.IsMatch(parts[0], "^[a-fA-F0-9]{64}$")) { expectedHash = parts[0].ToLowerInvariant(); sha256 = true; break; }
                if (Regex.IsMatch(parts[0], "^[a-fA-F0-9]{32}$")) { expectedHash = parts[0].ToLowerInvariant(); sha256 = false; break; }
            }
            if (expectedHash == null)
            {
                StatusText.Text = "校验文件无有效哈希, 跳过校验";
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
            StatusText.Text = "校验通过";
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
    }


}
