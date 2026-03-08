using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Kairo.Utils;
using Kairo.ViewModels;

namespace Kairo.Components.DashBoard.LanParty;

public partial class HostRoomPage : UserControl
{
    private readonly HostRoomPageViewModel _viewModel;

    public HostRoomPage()
    {
        InitializeComponent();
        _viewModel = new HostRoomPageViewModel();
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel.RequestPingWindow += OpenPingWindow;
        _viewModel.OnLoaded();
    }

    private void OnUnloaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel.RequestPingWindow -= OpenPingWindow;
        _viewModel.OnUnloaded();
    }

    private void OnServerCardPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: DetectedServerViewModel vm })
        {
            _viewModel.SelectServer(vm);
        }
    }

    private void OpenPingWindow()
    {
        try
        {
            var parentWindow = TopLevel.GetTopLevel(this) as Window;
            var win = new NodePingWindow { WindowStartupLocation = WindowStartupLocation.CenterOwner };
            if (parentWindow != null)
            {
                win.Show(parentWindow);
            }
            else
            {
                win.Show();
            }
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = "打开 Ping 窗口失败: " + ex.Message;
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
