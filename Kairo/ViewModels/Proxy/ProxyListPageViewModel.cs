using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAvalonia.UI.Controls;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Kairo.Components.DashBoard;
using Kairo.Core.Localization;
using Kairo.Core.Models;
using Kairo.Core.Providers;
using Kairo.Utils;

namespace Kairo.ViewModels
{
    public class ProxyListPageViewModel : ViewModelBase, IDisposable
    {
        private readonly ApiClient _api = new();
        private bool _isLoaded;
        private bool _isLoading;
        private Task? _refreshTask;
        private bool _refreshQueued;
        private string _loadError = string.Empty;
        private ProxyCardViewModel? _selected;

        public ObservableCollection<ProxyCardViewModel> Proxies { get; } = new();

        public ProxyCardViewModel? Selected
        {
            get => _selected;
            private set
            {
                if (SetProperty(ref _selected, value))
                {
                    foreach (var vm in Proxies)
                    {
                        vm.IsSelected = vm == _selected;
                    }
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            private set
            {
                if (!SetProperty(ref _isLoading, value)) return;
                NotifyListState();
            }
        }

        /// <summary>首次加载失败且没有可显示的隧道时的错误信息</summary>
        public string LoadError
        {
            get => _loadError;
            private set
            {
                if (!SetProperty(ref _loadError, value)) return;
                NotifyListState();
            }
        }

        public bool ShowLoadingState => IsLoading && Proxies.Count == 0;
        public bool ShowErrorState => !IsLoading && Proxies.Count == 0 && !string.IsNullOrEmpty(LoadError);
        public bool ShowEmptyState => !IsLoading && Proxies.Count == 0 && string.IsNullOrEmpty(LoadError) && _isLoaded;
        public bool ShowList => Proxies.Count > 0;

        public string SummaryText
        {
            get
            {
                if (IsLoading && Proxies.Count == 0) return L.T("tunnels.loading");
                if (Proxies.Count == 0) return L.T("tunnels.none");
                var running = Proxies.Count(p => p.IsRunning);
                return running > 0
                    ? L.Plural("tunnels.summaryRunning", Proxies.Count, Proxies.Count, running)
                    : L.Plural("tunnels.summary", Proxies.Count);
            }
        }

        public string ProviderName => Global.CurrentProvider.DisplayName;

        public bool CanPingNodes => Global.CurrentProvider.Type != FrpProviderType.Lolia;

        public AsyncRelayCommand RefreshCommand { get; }
        public RelayCommand CreateCommand { get; }
        public RelayCommand NodePingCommand { get; }

        public event Action? OpenCreateWindowRequested;
        public event Action? OpenNodePingWindowRequested;

        public ProxyListPageViewModel()
        {
            RefreshCommand = new AsyncRelayCommand(RefreshAsync);
            CreateCommand = new RelayCommand(RequestCreateWindow);
            NodePingCommand = new RelayCommand(RequestNodePingWindow);
            FrpcProcessManager.ProxyExited += OnProxyExited;
            Proxies.CollectionChanged += (_, _) => NotifyListState();
        }

        public void Dispose()
        {
            FrpcProcessManager.ProxyExited -= OnProxyExited;
            _api.Dispose();
        }

        public void OnLoaded()
        {
            OnPropertyChanged(nameof(CanPingNodes));
            if (!_isLoaded)
            {
                _ = RefreshAsync();
            }
            else
            {
                UpdateRunningStates();
            }
        }

        public void OnUnloaded()
        {
            // no-op currently
        }

        public void Select(ProxyCardViewModel vm)
        {
            Selected = ReferenceEquals(Selected, vm) ? null : vm;
        }

        /// <summary>
        /// 刷新隧道列表。刷新进行中再次调用时不会并发请求（避免列表重复），而是在当前刷新结束后再刷新一次，
        /// 保证创建、删除隧道后看到的是最新数据；返回的任务在所有刷新完成后结束
        /// </summary>
        public Task RefreshAsync()
        {
            if (_refreshTask is { IsCompleted: false })
            {
                _refreshQueued = true;
                return _refreshTask;
            }

            _refreshTask = RefreshLoopAsync();
            return _refreshTask;
        }

        private async Task RefreshLoopAsync()
        {
            do
            {
                _refreshQueued = false;
                await RefreshOnceAsync();
            } while (_refreshQueued);
        }

        /// <summary>选中指定隧道（如刚创建的隧道），不在列表中时返回 null</summary>
        public ProxyCardViewModel? SelectProxy(int proxyId)
        {
            var vm = Proxies.FirstOrDefault(p => p.Proxy.Id == proxyId);
            if (vm != null) Selected = vm;
            return vm;
        }

        private async Task RefreshOnceAsync()
        {
            IsLoading = true;
            try
            {
                var result = await _api.GetTunnelsAsync();
                if (!result.Success)
                {
                    LoadError = string.IsNullOrWhiteSpace(result.Message) ? L.T("tunnels.fetchFailed") : result.Message;
                    AccessSnackbar(L.T("tunnels.fetchFailed"), result.Message, FAInfoBarSeverity.Error);
                    return;
                }

                LoadError = string.Empty;
                Proxies.Clear();
                foreach (var p in (result.Data ?? Array.Empty<FrpTunnel>()).Select(t => t.ToProxy()))
                {
                    var vm = new ProxyCardViewModel(p, this)
                    {
                        IsRunning = FrpcProcessManager.IsRunning(p.Id)
                    };
                    Proxies.Add(vm);
                }
                Selected = null;
            }
            catch (Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/ViewModels/Proxy/ProxyListPageViewModel.cs:107", ex);
                LoadError = ex.Message;
                AccessSnackbar(L.T("common.error"), ex.Message, FAInfoBarSeverity.Error);
            }
            finally
            {
                _isLoaded = true;
                IsLoading = false;
            }
        }

        public async Task DeleteProxyAsync(ProxyCardViewModel vm)
        {
            var message = vm.IsRunning
                ? L.T("tunnels.deleteRunningConfirm", vm.Name)
                : L.T("tunnels.deleteConfirm", vm.Name);
            if (!await DialogHelper.ConfirmAsync(Access.DashBoard, L.T("tunnels.deleteTitle"), message, L.T("common.delete"), destructive: true))
                return;

            try
            {
                if (vm.IsRunning && FrpcProcessManager.StopProxy(vm.Proxy.Id))
                    vm.IsRunning = false;

                var tunnel = new FrpTunnel { Id = vm.Proxy.Id, Name = vm.Proxy.ProxyName };
                var result = await _api.DeleteTunnelAsync(tunnel);
                if (result.Success)
                {
                    AccessSnackbar(L.T("tunnels.deleted"), vm.Proxy.ProxyName, FAInfoBarSeverity.Success);
                    await RefreshAsync();
                }
                else
                {
                    AccessSnackbar(L.T("tunnels.deleteFailed"), result.Message, FAInfoBarSeverity.Error);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/ViewModels/Proxy/ProxyListPageViewModel.cs:129", ex);
                AccessSnackbar(L.T("common.error"), ex.Message, FAInfoBarSeverity.Error);
            }
        }

        public async void StartProxy(ProxyCardViewModel vm)
        {
            if (vm.IsBusy) return;
            var frpcPath = ProviderFrpcPath.Get(Global.CurrentProvider);
            if (string.IsNullOrWhiteSpace(frpcPath) || !File.Exists(frpcPath))
            {
                AccessSnackbar(L.T("tunnels.frpcMissing"), L.T("tunnels.frpcMissingHint", Global.CurrentProvider.DisplayName), FAInfoBarSeverity.Warning);
                return;
            }
            if (FrpcProcessManager.IsRunning(vm.Proxy.Id))
            {
                FrpcProcessManager.StopProxy(vm.Proxy.Id);
            }

            vm.IsBusy = true;
            try
            {
                var frpToken = Global.Config.FrpToken;
                if (Global.CurrentProvider.Type == FrpProviderType.Lolia)
                {
                    using var api = new ApiClient();
                    var config = await api.GetFrpcConfigAsync(new FrpTunnel { Id = vm.Proxy.Id, Name = vm.Proxy.ProxyName, Token = vm.Proxy.Token });
                    if (!config.Success || string.IsNullOrWhiteSpace(config.Data?.Token))
                    {
                        AccessSnackbar(L.T("tunnels.startFailed"), config.Message, FAInfoBarSeverity.Error);
                        return;
                    }
                    frpToken = config.Data.Token;
                }

                FrpcProcessManager.StartProxy(vm.Proxy.Id, vm.Proxy.ProxyName, frpcPath, frpToken, Global.CurrentProvider,
                    _ =>
                    {
                        vm.IsRunning = true;
                        NotifyListState();

                        // 启动后自动复制访问地址，方便直接分享
                        var connAddr = vm.PublicAddress;
                        if (!string.IsNullOrEmpty(connAddr))
                        {
                            CopyToClipboardAsync(connAddr);
                            AccessSnackbar(L.T("tunnels.started"), L.T("tunnels.startedCopied", vm.Proxy.ProxyName, connAddr), FAInfoBarSeverity.Success);
                        }
                        else
                        {
                            AccessSnackbar(L.T("tunnels.started"), vm.Proxy.ProxyName, FAInfoBarSeverity.Success);
                        }
                    },
                    err => { AccessSnackbar(L.T("tunnels.startFailed"), err, FAInfoBarSeverity.Error); });
            }
            catch (Exception ex)
            {
                AppLogger.Exception("启动隧道失败", ex);
                AccessSnackbar(L.T("tunnels.startFailed"), ex.Message, FAInfoBarSeverity.Error);
            }
            finally
            {
                vm.IsBusy = false;
            }
        }

        public void CopyAddress(ProxyCardViewModel vm)
        {
            var address = vm.PublicAddress;
            if (string.IsNullOrEmpty(address)) return;
            CopyToClipboardAsync(address);
            AccessSnackbar(L.T("tunnels.copied"), address, FAInfoBarSeverity.Success);
        }

        private static async void CopyToClipboardAsync(string text)
        {
            try
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var clipboard = TopLevel.GetTopLevel(Access.DashBoard)?.Clipboard;
                    if (clipboard != null)
                    {
                        await clipboard.SetTextAsync(text);
                    }
                });
            }
            catch (Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/ViewModels/Proxy/ProxyListPageViewModel.cs:216", ex);
                // Ignore clipboard errors
            }
        }

        public void StopProxy(ProxyCardViewModel vm)
        {
            if (FrpcProcessManager.StopProxy(vm.Proxy.Id))
            {
                vm.IsRunning = false;
                NotifyListState();
                AccessSnackbar(L.T("tunnels.stopped"), vm.Proxy.ProxyName, FAInfoBarSeverity.Informational);
            }
            else
            {
                vm.IsRunning = false;
                NotifyListState();
                AccessSnackbar(L.T("tunnels.notRunning"), vm.Proxy.ProxyName, FAInfoBarSeverity.Warning);
            }
        }

        private void OnProxyExited(int proxyId)
        {
            void UpdateFlag()
            {
                foreach (var vm in Proxies)
                {
                    if (vm.Proxy.Id == proxyId)
                    {
                        vm.IsRunning = false;
                        break;
                    }
                }
                NotifyListState();
            }

            if (Dispatcher.UIThread.CheckAccess())
                UpdateFlag();
            else
                Dispatcher.UIThread.Post(UpdateFlag);
        }

        private void UpdateRunningStates()
        {
            foreach (var vm in Proxies)
            {
                vm.IsRunning = FrpcProcessManager.IsRunning(vm.Proxy.Id);
            }
            NotifyListState();
        }

        private void NotifyListState()
        {
            OnPropertyChanged(nameof(ShowLoadingState));
            OnPropertyChanged(nameof(ShowErrorState));
            OnPropertyChanged(nameof(ShowEmptyState));
            OnPropertyChanged(nameof(ShowList));
            OnPropertyChanged(nameof(SummaryText));
        }

        public void RequestCreateWindow()
        {
            OpenCreateWindowRequested?.Invoke();
        }

        public void RequestNodePingWindow()
        {
            if (!CanPingNodes) return;
            OpenNodePingWindowRequested?.Invoke();
        }

        private static void AccessSnackbar(string title, string? message, FAInfoBarSeverity severity)
        {
            (Access.DashBoard as DashBoard)?.OpenSnackbar(title, message, severity);
        }
    }
}
