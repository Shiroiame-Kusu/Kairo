using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Kairo.ViewModels;

namespace Kairo.Components.DashBoard;

public partial class ProxyCard : UserControl
{
    private ProxyCardViewModel? _vm;
    private Border? _rootBorder;
    private Ellipse? _indicator;

    public ProxyCard()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm != null)
        {
            _vm.PropertyChanged -= VmOnPropertyChanged;
        }

        _vm = DataContext as ProxyCardViewModel;
        if (_vm != null)
        {
            _vm.PropertyChanged += VmOnPropertyChanged;
            UpdateVisual();
        }
    }

    private void VmOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProxyCardViewModel.IsRunning) || e.PropertyName == nameof(ProxyCardViewModel.IsSelected))
        {
            UpdateVisual();
        }
    }

    private void UpdateVisual()
    {
        if (_vm == null) return;
        _rootBorder ??= this.FindControl<Border>("RootBorder");
        _indicator ??= this.FindControl<Ellipse>("Indicator");

        if (_rootBorder != null)
        {
            if (_vm.IsRunning) _rootBorder.Classes.Add("running"); else _rootBorder.Classes.Remove("running");
            if (_vm.IsSelected) _rootBorder.Classes.Add("selected"); else _rootBorder.Classes.Remove("selected");
        }

        if (_indicator != null)
        {
            var brush = _vm.IsRunning ? Brushes.LightGreen : Brushes.Gray;
            _indicator.Stroke = brush;
            _indicator.Fill = brush;
        }
    }

    private void OnCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not ProxyCardViewModel vm) return;
        vm.SelectCommand.Execute(null);
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            var target = this.FindControl<Border>("RootBorder") as Control ?? this;
            FlyoutBase.GetAttachedFlyout(target)?.ShowAt(target);
            e.Handled = true;
        }
    }

    private void OnCardDoubleTapped(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ProxyCardViewModel vm)
        {
            vm.StartCommand.Execute(null);
        }
    }
}
