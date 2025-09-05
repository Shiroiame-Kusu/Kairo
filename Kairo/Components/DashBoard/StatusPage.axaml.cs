using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Kairo.Utils.Logger;
using Kairo.Utils; // add DesignModeHelper reference
using Avalonia.Interactivity; // for RoutedEventArgs

namespace Kairo.Components.DashBoard;

public partial class StatusPage : UserControl
{
    private const int MaxVisualLines = 800; // prevent unlimited growth
    private StackPanel? _logPanel;
    private ScrollViewer? _scroll;
    private bool _subscribed;
    private int _lastGlobalIndex; // global index of next line to process (handles cache trimming)

    public StatusPage()
    {
        InitializeComponent();
        _logPanel = this.FindControl<StackPanel>("LogPanel");
        _scroll = this.FindControl<ScrollViewer>("LogScroll");
        if (DesignModeHelper.IsDesign)
        {
            PopulateDesignSample();
            return; // do not hook runtime events
        }
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        ThemeManager.ThemeChanged += OnThemeChanged; // react to theme changes
    }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (DesignModeHelper.IsDesign) return;
        // Populate any missed lines from cache first
        PopulateFromCacheIncremental();
        if (!_subscribed)
        {
            Logger.LineWritten += OnLineWritten;
            _subscribed = true;
        }
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (_subscribed)
        {
            try { Logger.LineWritten -= OnLineWritten; } catch { }
            _subscribed = false;
        }
        // Do not unsubscribe ThemeChanged here so hidden page still updates; if desired, uncomment next line
        // ThemeManager.ThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged()
    {
        // Recolor existing lines to adopt new base color
        if (_logPanel == null) return;
        // Replace each TextBlock with newly colored one using stored original line (in Tag)
        for (int i = 0; i < _logPanel.Children.Count; i++)
        {
            if (_logPanel.Children[i] is TextBlock oldTb && oldTb.Tag is ValueTuple<LogType, string> data)
            {
                var (t, l) = data;
                var newTb = LogPreProcess.ToColoredTextBlock(t, l);
                newTb.Tag = data; // preserve
                _logPanel.Children[i] = newTb;
            }
        }
    }

    private void OnLineWritten(LogType type, string line)
    {
        // When we get a live line, increment global index AFTER adding
        Dispatcher.UIThread.Post(() =>
        {
            AddLineUI(type, line);
            _lastGlobalIndex++; // reflect consumption of one more line
        });
    }

    private void AddLineUI(LogType type, string line)
    {
        if (_logPanel == null) return;
        var tb = LogPreProcess.ToColoredTextBlock(type, line);
        tb.Tag = (type, line); // store original for recolor
        _logPanel.Children.Add(tb);
        if (_logPanel.Children.Count > MaxVisualLines)
        {
            int remove = _logPanel.Children.Count - (MaxVisualLines - 100);
            for (int i = 0; i < remove; i++) _logPanel.Children.RemoveAt(0);
        }
        try
        {
            if (_scroll != null)
            {
                var current = _scroll.Offset;
                _scroll.Offset = new Vector(current.X, double.MaxValue);
            }
        }
        catch { }
    }

    private void PopulateFromCacheIncremental()
    {
        var (snapshot, baseIndex) = Logger.GetCacheSnapshotWithBase();
        if (_logPanel == null || snapshot.Count == 0) return;

        // If our last index is behind trimmed region, jump forward
        if (_lastGlobalIndex < baseIndex) _lastGlobalIndex = baseIndex;

        int startOffset = _lastGlobalIndex - baseIndex; // starting position inside snapshot
        if (startOffset < 0) startOffset = 0; // safety
        if (startOffset >= snapshot.Count) return; // nothing new

        for (int i = startOffset; i < snapshot.Count; i++)
        {
            var (t, l) = snapshot[i];
            AddLineUI(t, l);
        }
        _lastGlobalIndex = baseIndex + snapshot.Count; // consumed all cached lines
    }

    private void PopulateDesignSample()
    {
        if (_logPanel == null) return;
        _logPanel.Children.Clear();
        foreach (var (type, line) in DesignModeHelper.SampleLogs)
        {
            _logPanel.Children.Add(LogPreProcess.ToColoredTextBlock(type, line));
        }
    }

    private void StopAllBtn_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        int stopped = FrpcProcessManager.StopAll();
        (Access.DashBoard as DashBoard)?.OpenSnackbar("已停止", $"结束 {stopped} 个隧道", FluentAvalonia.UI.Controls.InfoBarSeverity.Informational);
    }
}
