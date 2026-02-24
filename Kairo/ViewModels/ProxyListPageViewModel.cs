using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using FluentAvalonia.UI.Controls;
using Avalonia.Threading;
using Kairo.Components;
using Kairo.Components.DashBoard;
using Kairo.Utils;
using Kairo.Utils.Logger;

namespace Kairo.ViewModels
{
    public class ProxyListPageViewModel : ViewModelBase, IDisposable
    {
        private bool _isLoaded;
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
        }

        public void Dispose()
        {
            FrpcProcessManager.ProxyExited -= OnProxyExited;
        }

        public void OnLoaded()
        {
            if (!_isLoaded)
            {
                _isLoaded = true;
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
            Selected = vm;
        }

        public async Task RefreshAsync()
        {
            try
            {
                var result = await LoliaApiClient.GetTunnelListAsync(1, 200);
                if (!result.IsSuccess || result.Data == null)
                {
                    AccessSnackbar("获取隧道失败", result.Msg, InfoBarSeverity.Error);
                    return;
                }

                Proxies.Clear();
                foreach (var t in result.Data.List)
                {
                    var proxy = new Proxy
                    {
                        Id = t.Id,
                        TunnelName = t.Name,
                        ProxyName = string.IsNullOrWhiteSpace(t.Remark) ? t.Name : t.Remark,
                        ProxyType = t.Type,
                        LocalIp = t.LocalIp,
                        LocalPort = t.LocalPort,
                        RemotePort = t.RemotePort,
                        Domain = t.CustomDomain,
                        NodeInfo = new ProxyNode { Id = t.NodeId },
                    };
                    var vm = new ProxyCardViewModel(proxy, this)
                    {
                        IsRunning = FrpcProcessManager.IsRunning(proxy.Id)
                    };
                    Proxies.Add(vm);
                }
                Selected = null;
            }
            catch (Exception ex)
            {
                AccessSnackbar("异常", ex.Message, InfoBarSeverity.Error);
            }
        }

        public async Task DeleteProxyAsync(ProxyCardViewModel vm)
        {
            try
            {
                var tunnelName = string.IsNullOrWhiteSpace(vm.Proxy.TunnelName)
                    ? vm.Proxy.ProxyName
                    : vm.Proxy.TunnelName;
                var result = await LoliaApiClient.DeleteTunnelAsync(tunnelName);
                if (result.IsSuccess)
                {
                    AccessSnackbar("已删除", vm.Proxy.ProxyName, InfoBarSeverity.Success);
                    await RefreshAsync();
                }
                else
                {
                    AccessSnackbar("删除失败", result.Msg, InfoBarSeverity.Error);
                }
            }
            catch (Exception ex)
            {
                AccessSnackbar("异常", ex.Message, InfoBarSeverity.Error);
            }
        }

        public async void StartProxy(ProxyCardViewModel vm)
        {
            if (string.IsNullOrWhiteSpace(Global.Config.FrpcPath))
            {
                AccessSnackbar("未配置frpc", "请在设置中指定或下载frpc", InfoBarSeverity.Warning);
                return;
            }
            if (FrpcProcessManager.IsRunning(vm.Proxy.Id))
            {
                FrpcProcessManager.StopProxy(vm.Proxy.Id);
            }
            var tunnelName = string.IsNullOrWhiteSpace(vm.Proxy.TunnelName)
                ? vm.Proxy.ProxyName
                : vm.Proxy.TunnelName;
            await FrpcProcessManager.StartProxyAsync(vm.Proxy.Id, tunnelName, Global.Config.FrpcPath,
                _ =>
                {
                    vm.IsRunning = true;
                    AccessSnackbar("启动成功", vm.Proxy.ProxyName, InfoBarSeverity.Success);
                },
                err => { AccessSnackbar("启动失败", err, InfoBarSeverity.Error); });
        }

        public void StopProxy(ProxyCardViewModel vm)
        {
            if (FrpcProcessManager.StopProxy(vm.Proxy.Id))
            {
                vm.IsRunning = false;
                AccessSnackbar("已停止", vm.Proxy.ProxyName, InfoBarSeverity.Informational);
            }
            else
            {
                AccessSnackbar("未在运行", vm.Proxy.ProxyName, InfoBarSeverity.Warning);
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
        }

        public void RequestCreateWindow()
        {
            OpenCreateWindowRequested?.Invoke();
        }

        public void RequestNodePingWindow()
        {
            OpenNodePingWindowRequested?.Invoke();
        }

        private static void AccessSnackbar(string title, string? message, InfoBarSeverity severity)
        {
            (Access.DashBoard as DashBoard)?.OpenSnackbar(title, message, severity);
        }
    }
}
