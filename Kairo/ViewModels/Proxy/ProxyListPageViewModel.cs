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
                if (IsLoading && Proxies.Count == 0) return "正在加载隧道…";
                if (Proxies.Count == 0) return "暂无隧道";
                var running = Proxies.Count(p => p.IsRunning);
                return running > 0
                    ? $"共 {Proxies.Count} 个隧道 · {running} 个运行中"
                    : $"共 {Proxies.Count} 个隧道";
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

        public async Task RefreshAsync()
        {
            // 避免并发刷新导致列表重复
            if (IsLoading) return;
            IsLoading = true;
            try
            {
                var result = await _api.GetTunnelsAsync();
                if (!result.Success)
                {
                    LoadError = string.IsNullOrWhiteSpace(result.Message) ? "获取隧道失败" : result.Message;
                    AccessSnackbar("获取隧道失败", result.Message, FAInfoBarSeverity.Error);
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
                AccessSnackbar("异常", ex.Message, FAInfoBarSeverity.Error);
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
                ? $"隧道「{vm.Name}」正在运行，删除前会先停止它。删除后无法恢复，确定继续吗？"
                : $"确定要删除隧道「{vm.Name}」吗？删除后无法恢复。";
            if (!await DialogHelper.ConfirmAsync(Access.DashBoard, "删除隧道", message, "删除", destructive: true))
                return;

            try
            {
                if (vm.IsRunning && FrpcProcessManager.StopProxy(vm.Proxy.Id))
                    vm.IsRunning = false;

                var tunnel = new FrpTunnel { Id = vm.Proxy.Id, Name = vm.Proxy.ProxyName };
                var result = await _api.DeleteTunnelAsync(tunnel);
                if (result.Success)
                {
                    AccessSnackbar("已删除", vm.Proxy.ProxyName, FAInfoBarSeverity.Success);
                    await RefreshAsync();
                }
                else
                {
                    AccessSnackbar("删除失败", result.Message, FAInfoBarSeverity.Error);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/ViewModels/Proxy/ProxyListPageViewModel.cs:129", ex);
                AccessSnackbar("异常", ex.Message, FAInfoBarSeverity.Error);
            }
        }

        public async void StartProxy(ProxyCardViewModel vm)
        {
            if (vm.IsBusy) return;
            var frpcPath = ProviderFrpcPath.Get(Global.CurrentProvider);
            if (string.IsNullOrWhiteSpace(frpcPath) || !File.Exists(frpcPath))
            {
                AccessSnackbar("未找到 frpc", $"请在「设置」中下载或选择 {Global.CurrentProvider.DisplayName} frpc", FAInfoBarSeverity.Warning);
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
                        AccessSnackbar("启动失败", config.Message, FAInfoBarSeverity.Error);
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
                            AccessSnackbar("启动成功", $"{vm.Proxy.ProxyName} - 已复制 {connAddr}", FAInfoBarSeverity.Success);
                        }
                        else
                        {
                            AccessSnackbar("启动成功", vm.Proxy.ProxyName, FAInfoBarSeverity.Success);
                        }
                    },
                    err => { AccessSnackbar("启动失败", err, FAInfoBarSeverity.Error); });
            }
            catch (Exception ex)
            {
                AppLogger.Exception("启动隧道失败", ex);
                AccessSnackbar("启动失败", ex.Message, FAInfoBarSeverity.Error);
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
            AccessSnackbar("已复制", address, FAInfoBarSeverity.Success);
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
                AccessSnackbar("已停止", vm.Proxy.ProxyName, FAInfoBarSeverity.Informational);
            }
            else
            {
                vm.IsRunning = false;
                NotifyListState();
                AccessSnackbar("未在运行", vm.Proxy.ProxyName, FAInfoBarSeverity.Warning);
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
