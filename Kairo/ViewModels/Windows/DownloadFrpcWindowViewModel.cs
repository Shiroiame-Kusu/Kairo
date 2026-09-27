using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Kairo.Core.Localization;
using Kairo.Core.Models;
using Kairo.Core.Services;
using Kairo.Utils;
using Kairo.Utils.Configuration;
using Kairo.Components.DashBoard;

namespace Kairo.ViewModels
{
    public class DownloadFrpcWindowViewModel : ViewModelBase, IDisposable
    {
        private readonly HttpClient _http = new();
        private readonly FrpcDownloadService _downloadService;
        private CancellationTokenSource _cts = new();
        private readonly RelayCommand _cancelCommand;
        private readonly RelayCommand _closeCommand;
        private readonly RelayCommand _retryCommand;
        private bool _isFailed;
        private bool _isCompleted;

        private string _statusText = L.T("download.fetchingLatest");
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
        public RelayCommand RetryCommand => _retryCommand;

        public string HeaderText => L.T("download.header", Global.CurrentProvider.DisplayName);

        /// <summary>下载失败或被取消，可以重试</summary>
        public bool IsFailed
        {
            get => _isFailed;
            private set
            {
                if (!SetProperty(ref _isFailed, value)) return;
                OnPropertyChanged(nameof(ShowProgress));
                _retryCommand.RaiseCanExecuteChanged();
            }
        }

        public bool IsCompleted
        {
            get => _isCompleted;
            private set => SetProperty(ref _isCompleted, value);
        }

        public bool ShowProgress => !IsFailed;

        public event Action? CloseRequested;

        public DownloadFrpcWindowViewModel()
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"Kairo/{Global.Version}");
            _downloadService = new FrpcDownloadService(_http);
            _cancelCommand = new RelayCommand(Cancel, () => CanCancel);
            _closeCommand = new RelayCommand(() => CloseRequested?.Invoke(), () => CanClose);
            _retryCommand = new RelayCommand(() => _ = StartAsync(), () => IsFailed);
            if (Global.Tips != null && Global.Tips.Count > 0)
                TipText = Global.Tips[Random.Shared.Next(0, Global.Tips.Count)];
        }

        public void Dispose()
        {
            try { _cts.Cancel(); }
            catch (Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/ViewModels/Windows/DownloadFrpcWindowViewModel.cs:108", ex);
            }
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
                IsFailed = false;
                IsCompleted = false;
                CanCancel = true;
                CanClose = false;
                ResetProgressUI();
                SetStatus(L.T("core.download.fetchingRelease"));

                var result = await _downloadService.InstallAsync(
                    Global.CurrentProvider,
                    new FrpcInstallOptions { UseMirror = Global.Config.UsingDownloadMirror },
                    new Progress<FrpcDownloadProgress>(UpdateProgress),
                    _cts.Token);

                if (!result.Success)
                {
                    SetStatus(L.T("download.failed", result.Message));
                    CanClose = true;
                    CanCancel = false;
                    IsFailed = true;
                    return;
                }

                ProviderFrpcPath.Set(Global.CurrentProvider, result.FrpcPath, save: false);
                Global.Config.FrpcVersion = result.Version;
                ConfigManager.Save();

                SetStatus(L.T("core.download.completed"));
                Dispatcher.UIThread.Post(() =>
                {
                    IsIndeterminate = false;
                    ProgressValue = 100;
                    ProgressText = L.T("core.download.completed");
                    SpeedText = string.Empty;
                    CanClose = true;
                    CanCancel = false;
                    IsCompleted = true;
                    (Access.DashBoard as DashBoard)?.OpenSnackbar(
                        L.T("download.completed"),
                        result.FrpcPath,
                        FAInfoBarSeverity.Success);
                });
            }
            catch (OperationCanceledException ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/ViewModels/Windows/DownloadFrpcWindowViewModel.cs:159", ex);
                SetStatus(L.T("download.cancelled"));
                Dispatcher.UIThread.Post(() =>
                {
                    CanClose = true;
                    CanCancel = false;
                    IsFailed = true;
                });
            }
            catch (Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/ViewModels/Windows/DownloadFrpcWindowViewModel.cs:168", ex);
                SetStatus(L.T("download.failed", ex.Message));
                Dispatcher.UIThread.Post(() =>
                {
                    CanClose = true;
                    CanCancel = false;
                    IsFailed = true;
                });
            }
        }

        private void Cancel()
        {
            if (!CanCancel) return;
            CanCancel = false;
            _cts.Cancel();
            SetStatus(L.T("download.cancelled"));
            CanClose = true;
            IsFailed = true;
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
                                ? L.T("download.speed", FormatSpeed(progress.SpeedBytesPerSecond))
                                : string.Empty;
                        }
                        break;
                    case FrpcDownloadStage.Completed:
                        IsIndeterminate = false;
                        ProgressValue = 100;
                        ProgressText = L.T("core.download.completed");
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
