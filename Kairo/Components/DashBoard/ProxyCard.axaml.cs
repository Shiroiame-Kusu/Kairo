using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Input;
using FluentAvalonia.UI.Controls;
using Avalonia.Controls.Primitives; // for FlyoutBase

namespace Kairo.Components.DashBoard;

public partial class ProxyCard : UserControl
{
    public Proxy? Proxy { get; private set; }

    private MenuItem? _refreshItem;
    private MenuItem? _createItem;
    private MenuItem? _deleteItem;
    private MenuItem? _startItem;
    private MenuItem? _stopItem;

    private Border? _rootCache;
    private Ellipse? _indicatorCache;

    // Action events raised to parent page
    public event Action<ProxyCard, Proxy>? RequestSelect;
    public event Action<ProxyCard, Proxy>? RequestRefresh;
    public event Action<ProxyCard, Proxy>? RequestCreate;
    public event Action<ProxyCard, Proxy>? RequestDelete;
    public event Action<ProxyCard, Proxy>? RequestStart;
    public event Action<ProxyCard, Proxy>? RequestStop;

    public ProxyCard()
    {
        InitializeComponent();
        CacheMenuItemsAndWire();
        AddHandler(PointerPressedEvent, OnPointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        AddHandler(ContextRequestedEvent, OnContextRequested, Avalonia.Interactivity.RoutingStrategies.Bubble);
        this.AttachedToVisualTree += (_, __) =>
        {
            var rb = GetRootBorder();
            if (rb != null)
            {
                rb.DoubleTapped -= RootBorder_DoubleTapped;
                rb.DoubleTapped += RootBorder_DoubleTapped;
            }
        };
    }

    private void CacheMenuItemsAndWire()
    {
        _refreshItem = this.FindControl<MenuItem>("RefreshItem");
        _createItem = this.FindControl<MenuItem>("CreateItem");
        _deleteItem = this.FindControl<MenuItem>("DeleteItem");
        _startItem = this.FindControl<MenuItem>("StartItem");
        _stopItem = this.FindControl<MenuItem>("StopItem");

        if (_refreshItem != null) _refreshItem.Click += (_, __) => { if (Proxy != null) RequestRefresh?.Invoke(this, Proxy); };
        if (_createItem != null) _createItem.Click += (_, __) => { if (Proxy != null) RequestCreate?.Invoke(this, Proxy); };
        if (_deleteItem != null) _deleteItem.Click += (_, __) => { if (Proxy != null) RequestDelete?.Invoke(this, Proxy); };
        if (_startItem != null) _startItem.Click += (_, __) => { if (Proxy != null) RequestStart?.Invoke(this, Proxy); };
        if (_stopItem != null) _stopItem.Click += (_, __) => { if (Proxy != null) RequestStop?.Invoke(this, Proxy); };
    }

    private void RootBorder_DoubleTapped(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (Proxy != null) RequestStart?.Invoke(this, Proxy);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    public Border? GetRootBorder() => _rootCache ??= this.FindControl<Border>("RootBorder");
    private Ellipse? GetIndicator() => _indicatorCache ??= this.FindControl<Ellipse>("Indicator");

    public void Initialize(Proxy proxy)
    {
        Proxy = proxy;
        var nameBlock = this.FindControl<TextBlock>("NameBlock");
        var routeBlock = this.FindControl<TextBlock>("RouteBlock");
        var indicator = GetIndicator();
        if (nameBlock != null) nameBlock.Text = proxy.ProxyName;
        if (routeBlock != null) routeBlock.Text = $"{proxy.LocalIp}:{proxy.LocalPort} -> Node{proxy.Node}:{proxy.RemotePort}";
        if (indicator != null)
        {
            indicator.Stroke = Brushes.Gray;
            indicator.Fill = Brushes.Gray;
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (Proxy == null) return;
        RequestSelect?.Invoke(this, Proxy);
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            var target = GetRootBorder() as Control ?? this;
            var f = FlyoutBase.GetAttachedFlyout(target);
            f?.ShowAt(target);
            e.Handled = true;
        }
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (Proxy == null) return;
        RequestSelect?.Invoke(this, Proxy);
        var target = GetRootBorder() as Control ?? this;
        var f = FlyoutBase.GetAttachedFlyout(target);
        f?.ShowAt(target);
        e.Handled = true;
    }

    public void UpdateRunningState(bool running)
    {
        var rb = GetRootBorder();
        var indicator = GetIndicator();
        if (rb != null)
        {
            if (running) rb.Classes.Add("running"); else rb.Classes.Remove("running");
        }
        if (indicator != null)
        {
            if (running)
            {
                indicator.Stroke = Brushes.LightGreen;
                indicator.Fill = Brushes.LightGreen;
            }
            else
            {
                indicator.Stroke = Brushes.Gray;
                indicator.Fill = Brushes.Gray;
            }
        }
        if (_startItem != null)
            _startItem.Header = running ? "重新启动" : "启动隧道";
    }

    public void HideFlyout()
    {
        var rb = GetRootBorder();
        if (rb == null) return;
        if (FlyoutBase.GetAttachedFlyout(rb) is FlyoutBase f) f.Hide();
    }
}
