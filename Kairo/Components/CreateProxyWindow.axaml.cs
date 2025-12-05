using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Kairo.ViewModels;

namespace Kairo.Components;

public partial class CreateProxyWindow : Window
{
    private readonly CreateProxyWindowViewModel _viewModel;

    public event Action<int, string>? Created;

    public CreateProxyWindow()
    {
        InitializeComponent();
        _viewModel = new CreateProxyWindowViewModel();
        DataContext = _viewModel;

        _viewModel.ProxyCreated += OnProxyCreated;
        _viewModel.RequestClose += CloseSafe;
        _viewModel.RequestPingWindow += OpenPingWindow;

        Opened += async (_, _) => await _viewModel.OnOpenedAsync();
        Closed += (_, _) => _viewModel.Dispose();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnProxyCreated(int id, string name)
    {
        Created?.Invoke(id, name);
        CloseSafe();
    }

    private void OpenPingWindow()
    {
        try
        {
            var win = new NodePingWindow { WindowStartupLocation = WindowStartupLocation.CenterOwner };
            win.Show(this);
        }
        catch (Exception ex)
        {
            _viewModel.StatusText = "打开 Ping 窗口失败: " + ex.Message;
        }
    }

    private void CloseSafe()
    {
        try { Close(); }
        catch { }
    }
}
