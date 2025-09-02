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

namespace Kairo.Components;

public partial class ProxyListPage : UserControl
{
    private readonly HttpClient _http = new();
    private bool _loaded;
    private readonly Dictionary<int, Border> _cardByProxyId = new();
    public ProxyListPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (_loaded) return; _loaded = true;
        await LoadProxies();
    }

    private async Task LoadProxies()
    {
        try
        {
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
                ListPanel.Children.Clear();
                return;
            }
            var proxies = JsonConvert.DeserializeObject<List<Proxy>>(arrToken.ToString()) ?? new();
            Dispatcher.UIThread.Post(() =>
            {
                ListPanel.Children.Clear();
                int idx = 0;
                foreach (var p in proxies)
                {
                    ListPanel.Children.Add(BuildCard(p, idx++));
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
        var header = new TextBlock { Text = proxy.ProxyName, FontWeight = Avalonia.Media.FontWeight.SemiBold };
        var route = new TextBlock { Text = $"{proxy.LocalIp}:{proxy.LocalPort} -> Node{proxy.Node}:{proxy.RemotePort}", FontSize = 12 };
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(header);
        panel.Children.Add(route);
        var border = new Border
        {
            Child = panel,
            Padding = new Thickness(10),
            Margin = new Thickness(0,0,10,10),
            CornerRadius = new CornerRadius(6),
            Width = 220,
            Tag = proxy,
            Background = this.TryFindResource("SystemControlBackgroundBaseLowBrush", out var b) && b is IBrush brush ? brush : Brushes.Gray
        };
        _cardByProxyId[proxy.Id] = border;
        UpdateCardVisual(proxy.Id);
        var menu = new ContextMenu();
        menu.ItemsSource = new List<MenuItem>
        {
            new MenuItem{ Header = FrpcProcessManager.IsRunning(proxy.Id)?"重新启动":"启动", Tag = ("start", proxy)},
            new MenuItem{ Header = "停止", Tag = ("stop", proxy)},
            new MenuItem{ Header = "删除", Tag = ("delete", proxy)}
        };
        menu.AddHandler(MenuItem.ClickEvent, OnMenuItemClick, RoutingStrategies.Bubble);
        border.ContextMenu = menu;
        return border;
    }

    private void UpdateCardVisual(int proxyId)
    {
        if (_cardByProxyId.TryGetValue(proxyId, out var border))
        {
            bool running = FrpcProcessManager.IsRunning(proxyId);
            var baseBrush = this.TryFindResource("SystemControlBackgroundBaseLowBrush", out var b) && b is IBrush bb ? bb : Brushes.Gray;
            if (!running)
            {
                border.Background = baseBrush;
            }
            else
            {
                border.Background = new SolidColorBrush(Color.FromArgb(180, 60, 130, 60));
            }
        }
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
}
