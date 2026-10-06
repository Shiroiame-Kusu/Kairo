using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Kairo.Core.Localization;
using Kairo.ViewModels;
using Kairo.Utils;
using FluentAvalonia.UI.Controls;

namespace Kairo.Components.DashBoard;

public partial class ProxyListPage : UserControl
{
    public ProxyListPage()
    {
        InitializeComponent();
        DataContext = new ProxyListPageViewModel();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }
    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
    
    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProxyListPageViewModel vm) return;
        vm.OpenCreateWindowRequested += OpenCreateWindow;
        vm.OpenNodePingWindowRequested += OpenNodePingWindow;
        vm.OnLoaded();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ProxyListPageViewModel vm)
        {
            vm.OnUnloaded();
            vm.OpenCreateWindowRequested -= OpenCreateWindow;
            vm.OpenNodePingWindowRequested -= OpenNodePingWindow;
        }
    }

    /// <summary>页面快捷键：F5 刷新，Ctrl/⌘+N 创建隧道</summary>
    public bool HandleShortcut(KeyEventArgs e)
    {
        if (DataContext is not ProxyListPageViewModel vm) return false;
        if (e.Key == Key.F5 && e.KeyModifiers == KeyModifiers.None)
        {
            if (vm.RefreshCommand.CanExecute(null)) vm.RefreshCommand.Execute(null);
            return true;
        }
        if (e.Key == Key.N && (e.KeyModifiers == KeyModifiers.Control || e.KeyModifiers == KeyModifiers.Meta))
        {
            vm.CreateCommand.Execute(null);
            return true;
        }
        return false;
    }

    private void OpenCreateWindow()
    {
        try
        {
            var win = new CreateProxyWindow();
            win.Created += async (id, name) =>
            {
                (Access.DashBoard as DashBoard)?.OpenSnackbar(L.T("tunnels.created"), name, FAInfoBarSeverity.Success);
                if (DataContext is not ProxyListPageViewModel vm) return;
                await vm.RefreshAsync();
                if (vm.SelectProxy(id) is { } created)
                    BringCardIntoView(vm.Proxies.IndexOf(created));
            };
            if (Access.DashBoard is Window owner)
                win.Show(owner);
            else
                win.Show();
        }
        catch (Exception ex)
        {
            AppLogger.Exception("Unhandled exception in Kairo/Components/DashBoard/ProxyListPage.axaml.cs:59", ex);
            (Access.DashBoard as DashBoard)?.OpenSnackbar(L.T("common.openFailed"), ex.Message, FAInfoBarSeverity.Error);
        }
    }

    /// <summary>列表完成布局后滚动到指定位置的隧道卡片</summary>
    private void BringCardIntoView(int index)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var repeater = this.FindControl<FAItemsRepeater>("ProxyRepeater");
            // 创建期间切换到了其他页面时不需要滚动
            if (repeater == null || TopLevel.GetTopLevel(repeater) == null) return;
            if (index < 0 || index >= (repeater.ItemsSourceView?.Count ?? 0)) return;
            try
            {
                // 列表是虚拟化的，视野外的隧道还没有生成卡片，先生成并完成布局再滚动过去
                var element = repeater.GetOrCreateElement(index);
                element.UpdateLayout();
                element.BringIntoView();
            }
            catch (Exception ex)
            {
                AppLogger.Exception("滚动到新建的隧道失败", ex);
            }
        }, DispatcherPriority.Background);
    }

    private void OpenNodePingWindow()
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
            AppLogger.Exception("Unhandled exception in Kairo/Components/DashBoard/ProxyListPage.axaml.cs:75", ex);
            (Access.DashBoard as DashBoard)?.OpenSnackbar(L.T("common.openFailed"), ex.Message, FAInfoBarSeverity.Error);
        }
    }
}
