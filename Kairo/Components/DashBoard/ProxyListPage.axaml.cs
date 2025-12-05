using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
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

    private void OpenCreateWindow()
    {
        try
        {
            var win = new CreateProxyWindow();
            win.Created += async (id, name) =>
            {
                (Access.DashBoard as DashBoard)?.OpenSnackbar("创建成功", name, FluentAvalonia.UI.Controls.InfoBarSeverity.Success);
                if (DataContext is ProxyListPageViewModel vm)
                    await vm.RefreshAsync();
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
            (Access.DashBoard as DashBoard)?.OpenSnackbar("打开失败", ex.Message, FluentAvalonia.UI.Controls.InfoBarSeverity.Error);
        }
    }
}
