using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Kairo.Core.Services;
using Kairo.Utils;
using Kairo.Utils.Configuration;

namespace Kairo.ViewModels
{
    public class DownloadFrpcWindowViewModel : ViewModelBase, IDisposable
    {
        private readonly HttpClient _http = new();
        private readonly FrpcDownloadService _downloadService;
        private CancellationTokenSource _cts = new();
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
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"Kairo/{Global.Version}");
            _downloadService = new FrpcDownloadService(_http);
            _cancelCommand = new RelayCommand(Cancel, () => CanCancel);
            _closeCommand = new RelayCommand(() => CloseRequested?.Invoke(), () => CanClose);
            if (Global.Tips != null && Global.Tips.Count > 0)
                TipText = Global.Tips[Random.Shared.Next(0, Global.Tips.Count)];
        }

        public void Dispose()
        {
            try { _cts.Cancel(); } catch { }
            _cts.Dispose();
            _http.Dispose();
        }

        public async Task StartAsync()
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = new CancellationTokenSource();

            try
            {
                CanCancel = true;
                CanClose = false;
                ResetProgressUI();
                SetStatus("正在获取版本信息...");

                var result = await _downloadService.InstallAsync(
                    Global.CurrentProvider,
                    new FrpcInstallOptions { UseMirror = Global.Config.UsingDownloadMirror },
                    new Progress<FrpcDownloadProgress>(UpdateProgress),
                    _cts.Token);

                if (!result.Success)
                {
                    SetStatus($"失败: {result.Message}");
                    CanClose = true;
                    CanCancel = false;
                    return;
                }

                ProviderFrpcPath.Set(Global.CurrentProvider, result.FrpcPath, save: false);
                Global.Config.FrpcVersion = result.Version;
                ConfigManager.Save();

                SetStatus("完成");
                Dispatcher.UIThread.Post(() =>
                {
                    IsIndeterminate = false;
                    ProgressValue = 100;
                    ProgressText = "完成";
                    SpeedText = string.Empty;
                    CanClose = true;
                    CanCancel = false;
                    (Access.DashBoard as Components.DashBoard.DashBoard)?.OpenSnackbar(
                        "下载完成",
                        result.FrpcPath,
                        InfoBarSeverity.Success);
                });
            }
            catch (OperationCanceledException)
            {
                SetStatus("已取消");
                Dispatcher.UIThread.Post(() =>
                {
                    CanClose = true;
                    CanCancel = false;
                });
            }
            catch (Exception ex)
            {
                SetStatus("失败: " + ex.Message);
                Dispatcher.UIThread.Post(() =>
                {
                    CanClose = true;
                    CanCancel = false;
                });
            }
        }

        private void Cancel()
        {
            if (!CanCancel) return;
            CanCancel = false;
            _cts.Cancel();
            SetStatus("已取消");
            CanClose = true;
        }

        private void ResetProgressUI()
        {
            Dispatcher.UIThread.Post(() =>
            {
                IsIndeterminate = true;
                ProgressValue = 0;
                ProgressText = string.Empty;
                SpeedText = string.Empty;
            });
        }

        private void SetStatus(string txt) => Dispatcher.UIThread.Post(() =>
        {
            StatusText = txt;
        });

        private void UpdateProgress(FrpcDownloadProgress progress)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!string.IsNullOrWhiteSpace(progress.Message))
                    StatusText = progress.Message;

                switch (progress.Stage)
                {
                    case FrpcDownloadStage.FetchingRelease:
                    case FrpcDownloadStage.Verifying:
                    case FrpcDownloadStage.Extracting:
                        IsIndeterminate = true;
                        break;
                    case FrpcDownloadStage.Downloading:
                        IsIndeterminate = progress.TotalBytes <= 0 && progress.ReceivedBytes <= 0;
                        if (progress.ReceivedBytes > 0)
                        {
                            ProgressValue = progress.Percent;
                            ProgressText = progress.TotalBytes > 0
                                ? $"{FormatBytes(progress.ReceivedBytes)} / {FormatBytes(progress.TotalBytes)} ({progress.Percent:F1}%)"
                                : FormatBytes(progress.ReceivedBytes);
                            SpeedText = progress.SpeedBytesPerSecond > 0
                                ? $"速度: {FormatSpeed(progress.SpeedBytesPerSecond)}"
                                : string.Empty;
                        }
                        break;
                    case FrpcDownloadStage.Completed:
                        IsIndeterminate = false;
                        ProgressValue = 100;
                        ProgressText = "完成";
                        SpeedText = string.Empty;
                        break;
                }
            });
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

        private static string FormatSpeed(double bytesPerSecond) => bytesPerSecond > 1024 * 1024
            ? $"{bytesPerSecond / 1024d / 1024d:F2} MB/s"
            : $"{bytesPerSecond / 1024d:F1} KB/s";
    }
}
