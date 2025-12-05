using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using Downloader;
using Kairo.Utils;
using Kairo.Utils.Configuration;
using System.Security.Cryptography;
using System.Formats.Tar;
using System.Net;
using Kairo.Utils.Logger;

namespace Kairo.ViewModels
{
    public class DownloadFrpcWindowViewModel : ViewModelBase, IDisposable
    {
        private readonly HttpClient _http = new();
        private CancellationTokenSource _cts = new();
        private DownloadService? _downloadService;
        private string? _tempFile;
        private const int MaxAttempts = 3;
        private readonly RelayCommand _cancelCommand;
        private readonly RelayCommand _closeCommand;

        private string _statusText = "正在获取最新版本信息...";
        private double _progressValue;
        private bool _isIndeterminate = true;
        private string _progressText = string.Empty;
        private string _speedText = string.Empty;
        private string _tipText = string.Empty;
        private bool _canCancel = true;
        private bool _canClose;

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public double ProgressValue
        {
            get => _progressValue;
            set => SetProperty(ref _progressValue, value);
        }

        public bool IsIndeterminate
        {
            get => _isIndeterminate;
            set => SetProperty(ref _isIndeterminate, value);
        }

        public string ProgressText
        {
            get => _progressText;
            set => SetProperty(ref _progressText, value);
        }

        public string SpeedText
        {
            get => _speedText;
            set => SetProperty(ref _speedText, value);
        }

        public string TipText
        {
            get => _tipText;
            set => SetProperty(ref _tipText, value);
        }

        public bool CanCancel
        {
            get => _canCancel;
            set
            {
                if (SetProperty(ref _canCancel, value))
                {
                    _cancelCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool CanClose
        {
            get => _canClose;
            set
            {
                if (SetProperty(ref _canClose, value))
                {
                    _closeCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public RelayCommand CancelCommand => _cancelCommand;
        public RelayCommand CloseCommand => _closeCommand;

        public event Action? CloseRequested;

        public DownloadFrpcWindowViewModel()
        {
            _cancelCommand = new RelayCommand(Cancel, () => CanCancel);
            _closeCommand = new RelayCommand(() => CloseRequested?.Invoke(), () => CanClose);
            if (Global.Tips != null && Global.Tips.Count > 0)
                TipText = Global.Tips[Random.Shared.Next(0, Global.Tips.Count)];
        }

        public void Dispose()
        {
            try
            {
                _downloadService?.CancelAsync();
            }
            catch
            {
            }

            _cts.Cancel();
            _http.Dispose();
        }

        public async Task StartAsync()
        {
            // reset per-run state so retry on same instance works
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _downloadService = null;

            try
            {
                CanCancel = true;
                CanClose = false;
                IsIndeterminate = true;
                SetStatus("正在获取版本信息...");
                _http.DefaultRequestHeaders.UserAgent.ParseAdd("Kairo-FrpcDownloader");
                string apiMirror = "https://api-gh.1l1.icu/repos/LoCyan-Team/LocyanFrpPureApp/releases/latest";
                string apiOrigin = "https://api.github.com/repos/LoCyan-Team/LocyanFrpPureApp/releases/latest";
                JsonObject release = await TryFetch(apiMirror) ??
                                     await TryFetch(apiOrigin) ?? throw new Exception("无法获取版本信息");
                var (version, assets, asset, assetName, platform, arch) = SelectBestAsset(release);
                SetStatus($"最新版本: {version} 体系结构: {platform}-{arch}");

                string downloadUrl = GetNodeString(asset["browser_download_url"]);
                if (string.IsNullOrWhiteSpace(downloadUrl)) throw new Exception("下载地址缺失");
                if (Global.Config.UsingDownloadMirror)
                {
                    string releaseName = GetNodeString(release["name"]);
                    if (string.IsNullOrWhiteSpace(releaseName))
                        releaseName = GetNodeString(release["tag_name"]);
                    if (string.IsNullOrWhiteSpace(releaseName))
                        releaseName = version;
                    downloadUrl = "https://mirrors.locyan.cn/github-release/LoCyan-Team/LoCyanFrpPureApp/" +
                                  Uri.EscapeDataString(releaseName) + "/" + Uri.EscapeDataString(assetName);
                }

                Logger.Output(LogType.Info, "[FRPC] Using download URL:", downloadUrl);

                if (!CanCancel)
                    CanCancel = true;
                IsIndeterminate = false;
                Exception? lastError = null;
                for (int attempt = 1; attempt <= MaxAttempts; attempt++)
                {
                    if (_cts.IsCancellationRequested) break;
                    try
                    {
                        SetStatus(attempt == 1 ? "正在下载..." : $"正在下载...(重试 {attempt}/{MaxAttempts})");
                        string releaseNameForPass = GetNodeString(release["name"]);
                        if (string.IsNullOrWhiteSpace(releaseNameForPass))
                            releaseNameForPass = GetNodeString(release["tag_name"]);
                        if (string.IsNullOrWhiteSpace(releaseNameForPass))
                            releaseNameForPass = version;
                        await DownloadAndExtract(downloadUrl, version, platform, arch, _cts.Token, assets, assetName,
                            releaseNameForPass);
                        SetStatus("完成");
                        CanClose = true;
                        CanCancel = false;
                        return;
                    }
                    catch (OperationCanceledException)
                    {
                        SetStatus("已取消");
                        CanClose = true;
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
                            CanClose = true;
                            return;
                        }
                    }
                }

                if (lastError != null)
                {
                    SetStatus($"失败: {lastError.Message}");
                    CanClose = true;
                }
            }
            catch (OperationCanceledException)
            {
                SetStatus("已取消");
                CanClose = true;
            }
            catch (Exception ex)
            {
                SetStatus("失败: " + ex.Message);
                CanClose = true;
            }
        }

        private void Cancel()
        {
            if (!CanCancel) return;
            CanCancel = false;
            try
            {
                _downloadService?.CancelAsync();
            }
            catch
            {
            }

            _cts.Cancel();
            SetStatus("已取消");
            CanClose = true;
        }

        private async Task<JsonObject?> TryFetch(string url)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                using var resp = await _http.GetAsyncLogged(url, cts.Token);
                var text = await resp.Content.ReadAsStringAsync(cts.Token);
                return JsonNode.Parse(text) as JsonObject;
            }
            catch
            {
                return null;
            }
        }

        private (string version, JsonArray assets, JsonObject asset, string assetName, string platform, string arch)
            SelectBestAsset(JsonObject release)
        {
            var tag = GetNodeString(release["tag_name"]);
            var m = Regex.Match(tag, "v(\\d+\\.\\d+\\.\\d+)-\\d+");
            string version = m.Success ? m.Groups[1].Value : tag.TrimStart('v');
            var assets = release["assets"] as JsonArray ?? new JsonArray();
            string arch = RuntimeInformation.OSArchitecture switch
            {
                Architecture.X86 => "386",
                Architecture.Arm => "arm",
                Architecture.Arm64 => "arm64",
                _ => "amd64"
            };
            string platform = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows" :
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" :
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "darwin" : "windows";
            string basePattern = $"frp_LoCyanFrp-{version}_{platform}_{arch}";

            var assetObjects = assets.OfType<JsonObject>().ToList();
            var candidates = assetObjects
                .Where(a => GetAssetName(a).StartsWith(basePattern, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (candidates.Count == 0)
            {
                candidates = assetObjects
                    .Where(a => GetAssetName(a).Contains($"{platform}_{arch}"))
                    .ToList();
            }

            if (candidates.Count == 0)
            {
                var any = assetObjects.FirstOrDefault();
                if (any == null) throw new Exception("未找到资产");
                return (version, assets, any, GetAssetName(any), platform, arch);
            }

            JsonObject? pick = null;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                pick = candidates.FirstOrDefault(a =>
                    GetAssetName(a).EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                pick = candidates.FirstOrDefault(a =>
                           GetAssetName(a).EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)) ??
                       candidates.FirstOrDefault(a =>
                           GetAssetName(a).EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
            }

            pick ??= candidates.First();
            string assetName = GetAssetName(pick);
            return (version, assets, pick, assetName, platform, arch);
        }

        private void ResetProgressUI()
        {
            Dispatcher.UIThread.Post(() =>
            {
                ProgressValue = 0;
                ProgressText = string.Empty;
                SpeedText = string.Empty;
            });
        }

        private async Task DownloadAndExtract(string url, string version, string platform, string arch,
            CancellationToken token, JsonArray assets, string assetName, string releaseName)
        {
            string workDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kairo",
                "frpc");
            Directory.CreateDirectory(workDir);
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
                (Access.DashBoard as Components.DashBoard.DashBoard)?.OpenSnackbar("下载完成", finalPath,
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

        private async Task VerifyChecksumAsync(string filePath, JsonArray assets, string assetName,
            CancellationToken token, string releaseName)
        {
            string assetPrefix = assetName.Split('.')[0];
            var assetObjects = assets.OfType<JsonObject>().ToList();
            JsonObject? checksumAsset = assetObjects.FirstOrDefault(a =>
                                            GetAssetName(a).EndsWith(".sha256",
                                                StringComparison.OrdinalIgnoreCase) &&
                                            GetAssetName(a).Contains(assetPrefix))
                                        ?? assetObjects.FirstOrDefault(a =>
                                            GetAssetName(a).EndsWith(".sha256.txt",
                                                StringComparison.OrdinalIgnoreCase) &&
                                            GetAssetName(a).Contains(assetPrefix))
                                        ?? assetObjects.FirstOrDefault(a =>
                                            GetAssetName(a).EndsWith(".md5",
                                                StringComparison.OrdinalIgnoreCase) &&
                                            GetAssetName(a).Contains(assetPrefix))
                                        ?? assetObjects.FirstOrDefault(a =>
                                            GetAssetName(a).EndsWith(".md5.txt",
                                                StringComparison.OrdinalIgnoreCase) &&
                                            GetAssetName(a).Contains(assetPrefix));
            if (checksumAsset == null)
            {
                SetStatus("未提供校验文件, 跳过校验");
                return;
            }

            string checksumUrl = GetNodeString(checksumAsset["browser_download_url"]);
            if (string.IsNullOrWhiteSpace(checksumUrl))
            {
                SetStatus("校验文件链接缺失, 跳过校验");
                return;
            }

            if (Global.Config.UsingDownloadMirror)
            {
                string checksumFileName = GetAssetName(checksumAsset);
                checksumUrl = "https://mirrors.locyan.cn/github-release/LoCyan-Team/LoCyanFrpPureApp/" +
                              Uri.EscapeDataString(releaseName) + "/" + Uri.EscapeDataString(checksumFileName);
            }

            string checksumContent = await _http.GetStringAsyncLogged(checksumUrl, token);
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

        private static string GetNodeString(JsonNode? node)
        {
            if (node is JsonValue value && value.TryGetValue<string>(out var str))
                return str;
            return node?.ToString() ?? string.Empty;
        }

        private static string GetAssetName(JsonObject? obj) =>
            obj == null ? string.Empty : GetNodeString(obj["name"]);

        private void SetStatus(string txt) => Dispatcher.UIThread.Post(() =>
        {
            StatusText = txt;
        });

        private void UpdateProgress(double percent, long received, long total, double speedBytesPerSec)
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsIndeterminate = false;
                ProgressValue = percent;

                if (total > 0)
                    ProgressText =
                        FormatBytes(received) + " / " + FormatBytes(total) + $" ({percent:F1}%)";
                else
                    ProgressText = FormatBytes(received);

                string speedStr = speedBytesPerSec > 1024 * 1024
                    ? ($"{speedBytesPerSec / 1024d / 1024d:F2} MB/s")
                    : ($"{speedBytesPerSec / 1024d:F1} KB/s");
                SpeedText = $"速度: {speedStr}";
            });
        }
    }
}
