using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Kairo.ViewModels;

namespace Kairo.Components;

public partial class NodePingWindow : Window
{
    private readonly NodePingWindowViewModel _viewModel;

    public NodePingWindow() : this(null, null) { }

    public NodePingWindow(IEnumerable<int>? nodes = null, string? hostPattern = null)
    {
        InitializeComponent();
        _viewModel = new NodePingWindowViewModel(nodes, hostPattern);
        DataContext = _viewModel;

        _viewModel.RequestClose += CloseSafe;

        Opened += async (_, _) => await _viewModel.OnOpenedAsync();
        Closed += (_, _) => _viewModel.Dispose();
        DetachedFromVisualTree += (_, _) => _viewModel.OnClosed();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void CloseSafe()
    {
        try { Close(); }
        catch { }
    }
}
