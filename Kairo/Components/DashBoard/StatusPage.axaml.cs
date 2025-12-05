using System;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Kairo.ViewModels;

namespace Kairo.Components.DashBoard;

public partial class StatusPage : UserControl
{
    private ScrollViewer? _scroll;
    private INotifyCollectionChanged? _linesCollection;

    public StatusPage()
    {
        InitializeComponent();
        _scroll = this.FindControl<ScrollViewer>("LogScroll");
        DataContext = new StatusPageViewModel();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not StatusPageViewModel vm) return;
        vm.Attach();
        HookCollection(vm);
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is StatusPageViewModel vm)
        {
            UnhookCollection();
            vm.Detach();
            if (vm is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private void HookCollection(StatusPageViewModel vm)
    {
        UnhookCollection();
        _linesCollection = vm.Lines;
        if (_linesCollection != null)
        {
            _linesCollection.CollectionChanged += OnLinesChanged;
        }
    }

    private void UnhookCollection()
    {
        if (_linesCollection != null)
        {
            _linesCollection.CollectionChanged -= OnLinesChanged;
            _linesCollection = null;
        }
    }

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_scroll == null) return;
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                var current = _scroll.Offset;
                _scroll.Offset = new Avalonia.Vector(current.X, double.MaxValue);
            }
            catch { }
        });
    }
}
