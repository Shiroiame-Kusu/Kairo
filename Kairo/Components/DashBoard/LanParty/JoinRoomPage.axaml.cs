using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Kairo.ViewModels;

namespace Kairo.Components.DashBoard.LanParty;

public partial class JoinRoomPage : UserControl
{
    private readonly JoinRoomPageViewModel _viewModel;

    public JoinRoomPage()
    {
        InitializeComponent();
        _viewModel = new JoinRoomPageViewModel();
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

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
