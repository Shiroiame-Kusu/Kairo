using Avalonia.Controls;
using Avalonia.Markup.Xaml;
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
        Loaded += async (_, _) => await _viewModel.InitializeAsync();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
