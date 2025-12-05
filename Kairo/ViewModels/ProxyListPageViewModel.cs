using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using FluentAvalonia.UI.Controls;
using Kairo.Components.DashBoard;
using Kairo.Utils;
using Kairo.Utils.Logger;
using Kairo.Utils.Serialization;
using System.Text.Json;

namespace Kairo.ViewModels
{
    public class ProxyListPageViewModel : ViewModelBase, IDisposable
    {
        private readonly HttpClient _http = new();
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
            _http.Dispose();
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
                _http.DefaultRequestHeaders.Remove("Authorization");
                _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {Global.Config.AccessToken}");
                var url = $"{Global.APIList.GetAllProxy}{Global.Config.ID}";
                var resp = await _http.GetAsyncLogged(url);
                var body = await resp.Content.ReadAsStringAsync();
                var json = JsonNode.Parse(body);
                var status = json?["status"]?.GetValue<int>() ?? 0;
                if (status != 200)
                {
                    AccessSnackbar("获取隧道失败", json?["message"]?.GetValue<string>(), InfoBarSeverity.Error);
                    return;
                }
                var arrToken = json?["data"]?["list"];
                var proxies = arrToken?.Deserialize(AppJsonContext.Default.ListProxy) ?? new();

                Proxies.Clear();
                foreach (var p in proxies)
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
                AccessSnackbar("异常", ex.Message, InfoBarSeverity.Error);
            }
        }

        public async Task DeleteProxyAsync(ProxyCardViewModel vm)
        {
            try
            {
                using HttpClient hc = new();
                hc.DefaultRequestHeaders.Add("Authorization", $"Bearer {Global.Config.AccessToken}");
                var url = $"{Global.APIList.DeleteProxy}{Global.Config.ID}&tunnel_id={vm.Proxy.Id}";
                var resp = await hc.DeleteAsyncLogged(url);
                var body = await resp.Content.ReadAsStringAsync();
                var json = JsonNode.Parse(body);
                if (json?["status"]?.GetValue<int>() == 200)
                {
                    AccessSnackbar("已删除", vm.Proxy.ProxyName, InfoBarSeverity.Success);
                    await RefreshAsync();
                }
                else
                {
                    AccessSnackbar("删除失败", json?["message"]?.GetValue<string>(), InfoBarSeverity.Error);
                }
            }
            catch (Exception ex)
            {
                AccessSnackbar("异常", ex.Message, InfoBarSeverity.Error);
            }
        }

        public void StartProxy(ProxyCardViewModel vm)
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
            FrpcProcessManager.StartProxy(vm.Proxy.Id, Global.Config.FrpcPath, Global.Config.FrpToken,
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
            foreach (var vm in Proxies)
            {
                if (vm.Proxy.Id == proxyId)
                {
                    vm.IsRunning = false;
                    break;
                }
            }
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
