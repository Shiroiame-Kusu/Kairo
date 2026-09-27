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
    private const double BottomThreshold = 24;
    private ScrollViewer? _scroll;
    private Button? _jumpButton;
    private INotifyCollectionChanged? _linesCollection;

    /// <summary>用户停留在底部时才自动滚动，向上翻看日志时不打断</summary>
    private bool _stickToBottom = true;

    public StatusPage()
    {
        InitializeComponent();
        _scroll = this.FindControl<ScrollViewer>("LogScroll");
        _jumpButton = this.FindControl<Button>("JumpToBottomBtn");
        if (_scroll != null)
            _scroll.ScrollChanged += OnScrollChanged;
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
        _stickToBottom = true;
        ScrollToEndLater();
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (DataContext is StatusPageViewModel vm)
        {
            UnhookCollection();
            vm.Detach();
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

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        // 仅在用户滚动或视口变化时更新；新日志只会让内容变高，不应解除"贴底"
        if (e.OffsetDelta.Y != 0 || e.ViewportDelta.Y != 0)
            _stickToBottom = IsNearBottom();
        if (_jumpButton != null)
            _jumpButton.IsVisible = !_stickToBottom && _scroll != null && _scroll.Extent.Height > _scroll.Viewport.Height;
    }

    private bool IsNearBottom() =>
        _scroll == null || _scroll.Offset.Y >= _scroll.Extent.Height - _scroll.Viewport.Height - BottomThreshold;

    private void OnLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_stickToBottom)
            ScrollToEndLater();
    }

    private void JumpToBottom_OnClick(object? sender, RoutedEventArgs e)
    {
        _stickToBottom = true;
        ScrollToEndLater();
    }

    private void ScrollToEndLater()
    {
        if (_scroll == null) return;
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                _scroll.ScrollToEnd();
            }
            catch (Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/Components/DashBoard/StatusPage.axaml.cs:75", ex);
            }
        }, DispatcherPriority.Background);
    }
}
