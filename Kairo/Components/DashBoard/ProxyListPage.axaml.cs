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
using System.Diagnostics;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Input;
using FluentAvalonia.UI.Controls; // for MenuFlyout
using Avalonia.Controls.Primitives; // Popup

namespace Kairo.Components;

public partial class ProxyListPage : UserControl
{
    private readonly HttpClient _http = new();
    private bool _loaded;
    private readonly Dictionary<int, Border> _cardByProxyId = new();
    private readonly Dictionary<int, Ellipse> _indicatorByProxyId = new();
    private readonly Dictionary<int, MenuFlyout> _flyoutByProxyId = new();
    private readonly Dictionary<int, MenuFlyoutItem> _startFlyoutItemByProxyId = new();
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
        if (_loaded) return; _loaded = true;
        FrpcProcessManager.ProxyExited += OnProxyExited; // subscribe for indicator updates
        if (Design.IsDesignMode)
        {
            if (_listPanel == null) return; // nothing to populate
            _listPanel.Children.Clear();
            var placeholder = new Proxy
            {
                Id = 0,
                ProxyName = "示例隧道",
                ProxyType = "tcp",
                LocalIp = "127.0.0.1",
                LocalPort = 7000,
                RemotePort = "6000",
                UseCompression = "false",
                UseEncryption = "false",
                Domain = "example.local",
                Node = 1,
                Icp = string.Empty
            };
            _listPanel.Children.Add(BuildCard(placeholder, 0));
            return;
        }
        await LoadProxies();
    }

    private void OnProxyExited(int proxyId)
    {
        Dispatcher.UIThread.Post(() => UpdateCardVisual(proxyId));
    }

    public void CloseAllTransientUI()
    {
        foreach (var f in _flyoutByProxyId.Values)
        {
            try { f.Hide(); } catch { }
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
            var resp = await _http.GetAsync($"{Global.APIList.GetAllProxy}{Global.Config.ID}");
            var json = JObject.Parse(await resp.Content.ReadAsStringAsync());
            if ((int?)json["status"] != 200)
            {
                (Access.DashBoard as DashBoard)?.OpenSnackbar("获取隧道失败", json["message"]?.ToString(), FluentAvalonia.UI.Controls.InfoBarSeverity.Error);
                return;
            }
            var arrToken = json["data"]?["proxies"] ?? json["data"]?["list"]; // fallback to legacy key 'list'
            if (arrToken == null)
            {
                _listPanel?.Children.Clear();
                return;
            }
            var proxies = JsonConvert.DeserializeObject<List<Proxy>>(arrToken.ToString()) ?? new();
            Dispatcher.UIThread.Post(() =>
            {
                if (_listPanel == null) return;
                _listPanel.Children.Clear();
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
        var nameBlock = new TextBlock { Text = proxy.ProxyName, FontWeight = FontWeight.SemiBold };
        var indicator = new Ellipse
        {
            Width = 10,
            Height = 10,
            StrokeThickness = 2,
            Margin = new Thickness(6,0,0,0)
        };
        _indicatorByProxyId[proxy.Id] = indicator;

        var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
        headerPanel.Children.Add(nameBlock);
        headerPanel.Children.Add(indicator);

        var route = new TextBlock { Text = $"{proxy.LocalIp}:{proxy.LocalPort} -> Node{proxy.Node}:{proxy.RemotePort}", FontSize = 12 };
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(headerPanel);
        panel.Children.Add(route);
        var border = new Border
        {
            Child = panel,
            // Margin now provided by style (proxy-card) to ensure consistent spacing & full border visibility
            Width = 220,
            Tag = proxy
        };
        border.Classes.Add("proxy-card");
        border.PointerPressed += (s,e)=>{ 
            var pt = e.GetCurrentPoint(border);
            if(pt.Properties.IsLeftButtonPressed)
            {
                SelectProxy(proxy.Id);
            }
            else if(pt.Properties.IsRightButtonPressed)
            {
                SelectProxy(proxy.Id);
                if(_flyoutByProxyId.TryGetValue(proxy.Id, out var fly))
                {
                    fly.ShowAt(border);
                    e.Handled = true;
                }
            }
        };
        border.ContextRequested += (s,e)=>{
            if(_flyoutByProxyId.TryGetValue(proxy.Id, out var mf))
            {
                SelectProxy(proxy.Id);
                mf.ShowAt(border);
                e.Handled = true;
            }
        };
        _cardByProxyId[proxy.Id] = border;
        BuildFlyout(border, proxy);
        UpdateCardVisual(proxy.Id);
        // Double click start (use DoubleTapped event)
        border.DoubleTapped += (_, __) => TryStartProxy(proxy);
        return border;
    }

    private void SelectProxy(int proxyId)
    {
        if (_selectedProxyId == proxyId) return;
        if (_selectedProxyId.HasValue && _cardByProxyId.TryGetValue(_selectedProxyId.Value, out var oldSel))
            oldSel.Classes.Remove("selected");
        _selectedProxyId = proxyId;
        if (_cardByProxyId.TryGetValue(proxyId, out var newSel) && !newSel.Classes.Contains("selected"))
            newSel.Classes.Add("selected");
    }

    private void BuildFlyout(Border border, Proxy proxy)
    {
        var flyout = new MenuFlyout();
        MenuFlyoutItem Make(string text, Action act)
        {
            var it = new MenuFlyoutItem { Text = text };
            it.Click += (_, __) => act();
            it.PointerEntered += (_, __) => it.Focus();
            return it;
        }
        var refresh = Make("刷新", async () => await LoadProxies());
        var create = Make("新建隧道", () => CreateProxyBtn_OnClick(null, null!));
        var delete = Make("删除隧道", async () => await DeleteProxy(proxy));
        var start = Make(FrpcProcessManager.IsRunning(proxy.Id)?"重新启动":"启动隧道", () => TryStartProxy(proxy, true));
        _startFlyoutItemByProxyId[proxy.Id] = start;
        var stop = Make("停止隧道", () =>
        {
            if (FrpcProcessManager.StopProxy(proxy.Id))
            {
                UpdateCardVisual(proxy.Id);
                (Access.DashBoard as DashBoard)?.OpenSnackbar("已停止", proxy.ProxyName, FluentAvalonia.UI.Controls.InfoBarSeverity.Informational);
            }
            else
            {
                (Access.DashBoard as DashBoard)?.OpenSnackbar("未在运行", proxy.ProxyName, FluentAvalonia.UI.Controls.InfoBarSeverity.Warning);
            }
        });
        flyout.Items.Add(refresh);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(create);
        flyout.Items.Add(delete);
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(start);
        flyout.Items.Add(stop);
        _flyoutByProxyId[proxy.Id] = flyout;
    }

    private void UpdateCardVisual(int proxyId)
    {
        if (_cardByProxyId.TryGetValue(proxyId, out var border))
        {
            bool running = FrpcProcessManager.IsRunning(proxyId);
            if (running)
            {
                if (!border.Classes.Contains("running")) border.Classes.Add("running");
            }
            else
            {
                if (border.Classes.Contains("running")) border.Classes.Remove("running");
            }
            if (_indicatorByProxyId.TryGetValue(proxyId, out var ellipse))
            {
                if (running)
                {
                    ellipse.Stroke = Brushes.LightGreen;
                    ellipse.Fill = Brushes.LightGreen;
                }
                else
                {
                    ellipse.Stroke = Brushes.Gray;
                    ellipse.Fill = Brushes.Gray;
                }
            }
            if (_startFlyoutItemByProxyId.TryGetValue(proxyId, out var startItem))
            {
                startItem.Text = running ? "重新启动" : "启动隧道";
            }
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
            var resp = await hc.DeleteAsync($"{Global.APIList.DeleteProxy}{Global.Config.ID}&proxy_id={proxy.Id}");
            var json = JObject.Parse(await resp.Content.ReadAsStringAsync());
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
            Process.Start(new ProcessStartInfo("https://dashboard.locyanfrp.cn/proxies/add") { UseShellExecute = true });
            (Access.DashBoard as DashBoard)?.OpenSnackbar("已打开", "浏览器里创建新隧道", FluentAvalonia.UI.Controls.InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            (Access.DashBoard as DashBoard)?.OpenSnackbar("失败", ex.Message, FluentAvalonia.UI.Controls.InfoBarSeverity.Error);
        }
    }

    private void OnMenuItemClick(object? sender, RoutedEventArgs e)
    {
        if (e.Source is MenuItem mi && mi.Tag is ValueTuple<string, Proxy> data)
        {
            var (action, proxy) = data;
            switch (action)
            {
                case "start":
                    if (string.IsNullOrWhiteSpace(Global.Config.FrpcPath))
                    {
                        (Access.DashBoard as DashBoard)?.OpenSnackbar("未配置frpc", "请在设置中指定或下载frpc", FluentAvalonia.UI.Controls.InfoBarSeverity.Warning);
                        return;
                    }
                    FrpcProcessManager.StartProxy(proxy.Id, Global.Config.FrpcPath, Global.Config.FrpToken,
                        _ => { UpdateCardVisual(proxy.Id); (Access.DashBoard as DashBoard)?.OpenSnackbar("启动成功", proxy.ProxyName, FluentAvalonia.UI.Controls.InfoBarSeverity.Success); },
                        err => { (Access.DashBoard as DashBoard)?.OpenSnackbar("启动失败", err, FluentAvalonia.UI.Controls.InfoBarSeverity.Error); });
                    break;
                case "stop":
                    if (FrpcProcessManager.StopProxy(proxy.Id))
                    {
                        UpdateCardVisual(proxy.Id);
                        (Access.DashBoard as DashBoard)?.OpenSnackbar("已停止", proxy.ProxyName, FluentAvalonia.UI.Controls.InfoBarSeverity.Informational);
                    }
                    else
                    {
                        (Access.DashBoard as DashBoard)?.OpenSnackbar("未在运行", proxy.ProxyName, FluentAvalonia.UI.Controls.InfoBarSeverity.Warning);
                    }
                    break;
                case "delete":
                    _ = DeleteProxy(proxy);
                    break;
            }
        }
    }

    private Control BuildCard_oldPlaceholderRemoval() { return null; } // sentinel to force tool patch uniqueness
}
