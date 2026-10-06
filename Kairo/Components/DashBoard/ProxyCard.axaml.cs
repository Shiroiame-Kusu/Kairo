using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using Kairo.ViewModels;

namespace Kairo.Components.DashBoard;

public partial class ProxyCard : UserControl
{
    public ProxyCard()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not ProxyCardViewModel vm || IsFromButton(e.Source)) return;
        vm.SelectCommand.Execute(null);
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            ShowMenu();
            e.Handled = true;
        }
    }

    private void OnCardDoubleTapped(object? sender, TappedEventArgs e)
    {
        // 双击按钮（如启动/停止）时不重复触发
        if (IsFromButton(e.Source)) return;
        if (DataContext is ProxyCardViewModel vm && vm.ToggleCommand.CanExecute(null))
            vm.ToggleCommand.Execute(null);
    }

    private void OnMoreClick(object? sender, RoutedEventArgs e) => ShowMenu();

    private void ShowMenu()
    {
        var target = this.FindControl<Border>("RootBorder") as Control ?? this;
        FlyoutBase.GetAttachedFlyout(target)?.ShowAt(target);
    }

    private static bool IsFromButton(object? source) =>
        source is Control control && (control is Button || control.FindAncestorOfType<Button>() != null);
}
