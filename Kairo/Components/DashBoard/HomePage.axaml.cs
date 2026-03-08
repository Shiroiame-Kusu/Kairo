using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Kairo.ViewModels;

namespace Kairo.Components.DashBoard;

public partial class HomePage : UserControl
{
    private readonly HomePageViewModel _viewModel;

    public HomePage()
    {
        InitializeComponent();
        _viewModel = new HomePageViewModel();
        DataContext = _viewModel;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DashBoard.AvatarChanged -= OnAvatarChanged;
        DashBoard.AvatarChanged += OnAvatarChanged;
        _viewModel.SetAvatar(DashBoard.Avatar);
        await _viewModel.InitializeAsync();
    }

    private void OnUnloaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        DashBoard.AvatarChanged -= OnAvatarChanged;
    }

    private void OnAvatarChanged(Bitmap? avatar)
    {
        _viewModel.SetAvatar(avatar);
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
