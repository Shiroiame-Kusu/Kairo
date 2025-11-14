using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Kairo.Utils;
using Kairo.Components;
using Kairo.Utils.Logger; // add

namespace Kairo.Components.DashBoard;

public partial class ProxyListPage : UserControl
{
    private readonly HttpClient _http = new();
    private bool _loaded; // rename semantic: first data load done
    // Map proxyId -> card
    private readonly Dictionary<int, ProxyCard> _cardByProxyId = new();
    private WrapPanel? _listPanel; // cached reference
    private int? _selectedProxyId;

    public ProxyListPage()
    {
        InitializeComponent();
        _listPanel = this.FindControl<WrapPanel>("ListPanel");
        Loaded += OnLoaded;
        Unloaded += (_, __) => { FrpcProcessManager.ProxyExited -= OnProxyExited; CloseAllTransientUI(); };
    }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        // Always (re)subscribe when page becomes visible again
        FrpcProcessManager.ProxyExited -= OnProxyExited; // ensure no duplicate
        FrpcProcessManager.ProxyExited += OnProxyExited;

        if (!_loaded)
        {
            _loaded = true;
            if (Design.IsDesignMode)
            {
                if (_listPanel == null) return; // nothing to populate
                _listPanel.Children.Clear();
                var placeholder = new Proxy
                {
                    Id = 0,
                    ProxyName = "示例隧道",
                    ProxyType = "TCP",
                    LocalIp = "127.0.0.1",
                    LocalPort = 7000,
                    RemotePort = 6000,
                    UseCompression = false,
                    UseEncryption = false,
                    Domain = "example.local",
                    NodeInfo = new ProxyNode { Id = 1, Host = "node1.locyanfrp.cn" }
                };
                _listPanel.Children.Add(BuildCard(placeholder, 0));
                return;
            }
            await LoadProxies();
        }
        else
        {
            // Subsequent navigations back: refresh visual running states (processes may have exited while hidden)
            Dispatcher.UIThread.Post(UpdateAllCardVisuals);
        }
    }

    private void UpdateAllCardVisuals()
    {
        foreach (var kv in _cardByProxyId)
        {
            kv.Value.UpdateRunningState(FrpcProcessManager.IsRunning(kv.Key));
        }
    }

    private void OnProxyExited(int proxyId)
    {
        Dispatcher.UIThread.Post(() => UpdateCardVisual(proxyId));
    }

    public void CloseAllTransientUI()
    {
        foreach (var card in _cardByProxyId.Values)
        {
            try { card.HideFlyout(); } catch { }
        }
    }

    private async Task LoadProxies()
    {
        try
        {
            _selectedProxyId = null; // reset selection
            if (Design.IsDesignMode) return; // extra guard
            _http.DefaultRequestHeaders.Remove("Authorization");
            _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {Global.Config.AccessToken}");
            var url = $"{Global.APIList.GetAllProxy}{Global.Config.ID}";
            var resp = await _http.GetAsyncLogged(url);
            var body = await resp.Content.ReadAsStringAsync();
            var json = JObject.Parse(body);
            if ((int?)json["status"] != 200)
            {
                (Access.DashBoard as DashBoard)?.OpenSnackbar("获取隧道失败", json["message"]?.ToString(), FluentAvalonia.UI.Controls.InfoBarSeverity.Error);
                return;
            }
            var arrToken = json["data"]?["list"]; // v3 uses data.list
            if (arrToken == null)
            {
                _listPanel?.Children.Clear();
                _cardByProxyId.Clear();
                return;
            }
            var proxies = JsonConvert.DeserializeObject<List<Proxy>>(arrToken.ToString()) ?? new();
            Dispatcher.UIThread.Post(() =>
            {
                if (_listPanel == null) return;
                _listPanel.Children.Clear();
                _cardByProxyId.Clear();
                int idx = 0;
                foreach (var p in proxies)
                {
                    _listPanel.Children.Add(BuildCard(p, idx++));
                }
            });
        }
        catch (Exception ex)
        {
            (Access.DashBoard as DashBoard)?.OpenSnackbar("异常", ex.Message, FluentAvalonia.UI.Controls.InfoBarSeverity.Error);
        }
    }

    private Control BuildCard(Proxy proxy, int index)
    {
        var card = new ProxyCard();
        card.Initialize(proxy);
        _cardByProxyId[proxy.Id] = card;

        // Wire events
        card.RequestSelect += (_, p) => SelectProxy(p.Id);
        card.RequestRefresh += async (_, __) => await LoadProxies();
        card.RequestCreate += (_, __) => CreateProxyBtn_OnClick(null, null!);
        card.RequestDelete += async (_, p) => await DeleteProxy(p);
        card.RequestStart += (_, p) => TryStartProxy(p, true);
        card.RequestStop += (_, p) =>
        {
            if (FrpcProcessManager.StopProxy(p.Id))
            {
                UpdateCardVisual(p.Id);
                (Access.DashBoard as DashBoard)?.OpenSnackbar("已停止", p.ProxyName, FluentAvalonia.UI.Controls.InfoBarSeverity.Informational);
            }
            else
            {
                (Access.DashBoard as DashBoard)?.OpenSnackbar("未在运行", p.ProxyName, FluentAvalonia.UI.Controls.InfoBarSeverity.Warning);
            }
        };

        UpdateCardVisual(proxy.Id);
        return card;
    }

    private void SelectProxy(int proxyId)
    {
        if (_selectedProxyId == proxyId) return;
        if (_selectedProxyId.HasValue && _cardByProxyId.TryGetValue(_selectedProxyId.Value, out var oldSel))
        {
            var oldBorder = oldSel.GetRootBorder();
            if (oldBorder != null) oldBorder.Classes.Remove("selected");
        }
        _selectedProxyId = proxyId;
        if (_cardByProxyId.TryGetValue(proxyId, out var newSel))
        {
            var newBorder = newSel.GetRootBorder();
            if (newBorder != null && !newBorder.Classes.Contains("selected")) newBorder.Classes.Add("selected");
        }
    }

    private void UpdateCardVisual(int proxyId)
    {
        if (_cardByProxyId.TryGetValue(proxyId, out var card))
        {
            bool running = FrpcProcessManager.IsRunning(proxyId);
            card.UpdateRunningState(running);
        }
    }

    private void TryStartProxy(Proxy proxy, bool showMessage = false)
    {
        if (string.IsNullOrWhiteSpace(Global.Config.FrpcPath))
        {
            (Access.DashBoard as DashBoard)?.OpenSnackbar("未配置frpc", "请在设置中指定或下载frpc", FluentAvalonia.UI.Controls.InfoBarSeverity.Warning);
            return;
        }
        if (FrpcProcessManager.IsRunning(proxy.Id))
        {
            FrpcProcessManager.StopProxy(proxy.Id);
        }
        FrpcProcessManager.StartProxy(proxy.Id, Global.Config.FrpcPath, Global.Config.FrpToken,
            _ => { UpdateCardVisual(proxy.Id); if (showMessage) (Access.DashBoard as DashBoard)?.OpenSnackbar("启动成功", proxy.ProxyName, FluentAvalonia.UI.Controls.InfoBarSeverity.Success); },
            err => { (Access.DashBoard as DashBoard)?.OpenSnackbar("启动失败", err, FluentAvalonia.UI.Controls.InfoBarSeverity.Error); });
    }

    private async Task DeleteProxy(Proxy proxy)
    {
        try
        {
            using HttpClient hc = new();
            hc.DefaultRequestHeaders.Add("Authorization", $"Bearer {Global.Config.AccessToken}");
            var url = $"{Global.APIList.DeleteProxy}{Global.Config.ID}&tunnel_id={proxy.Id}";
            var resp = await hc.DeleteAsyncLogged(url);
            var body = await resp.Content.ReadAsStringAsync();
            var json = JObject.Parse(body);
            if ((int)json["status"] == 200)
            {
                (Access.DashBoard as DashBoard)?.OpenSnackbar("已删除", proxy.ProxyName, FluentAvalonia.UI.Controls.InfoBarSeverity.Success);
                await LoadProxies();
            }
            else
            {
                (Access.DashBoard as DashBoard)?.OpenSnackbar("删除失败", json["message"]?.ToString(), FluentAvalonia.UI.Controls.InfoBarSeverity.Error);
            }
        }
        catch (Exception ex)
        {
            (Access.DashBoard as DashBoard)?.OpenSnackbar("异常", ex.Message, FluentAvalonia.UI.Controls.InfoBarSeverity.Error);
        }
    }

    private async void RefreshBtn_OnClick(object? sender, RoutedEventArgs e) => await LoadProxies();
    private void CreateProxyBtn_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var win = new CreateProxyWindow();
            win.Created += async (id, name) =>
            {
                (Access.DashBoard as DashBoard)?.OpenSnackbar("创建成功", name, FluentAvalonia.UI.Controls.InfoBarSeverity.Success);
                await LoadProxies();
            };
            if (Access.DashBoard is Window owner)
                win.Show(owner);
            else
                win.Show();
        }
        catch (Exception ex)
        {
            (Access.DashBoard as DashBoard)?.OpenSnackbar("打开失败", ex.Message, FluentAvalonia.UI.Controls.InfoBarSeverity.Error);
        }
    }

    private void NodePingBtn_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var win = new NodePingWindow();
            if (Access.DashBoard is Window owner)
                win.Show(owner);
            else
                win.Show();
        }
        catch (Exception ex)
        {
            (Access.DashBoard as DashBoard)?.OpenSnackbar("打开失败", ex.Message, FluentAvalonia.UI.Controls.InfoBarSeverity.Error);
        }
    }
}
