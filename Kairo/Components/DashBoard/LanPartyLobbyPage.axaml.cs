using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Kairo.ViewModels;

namespace Kairo.Components.DashBoard;

public partial class LanPartyLobbyPage : UserControl
{
    private readonly LanPartyLobbyPageViewModel _viewModel;

    public LanPartyLobbyPage()
    {
        InitializeComponent();
        _viewModel = new LanPartyLobbyPageViewModel();
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel.OnLoaded();
    }

    private void OnUnloaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        _viewModel.OnUnloaded();
    }

    private void OnServerCardPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border { DataContext: DetectedServerViewModel vm })
        {
            _viewModel.SelectServer(vm);
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}