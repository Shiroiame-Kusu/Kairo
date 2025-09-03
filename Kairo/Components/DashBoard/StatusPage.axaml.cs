using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Kairo.Utils.Logger;
using Kairo.Utils; // add DesignModeHelper reference

namespace Kairo.Components;

public partial class StatusPage : UserControl
{
    private const int MaxVisualLines = 800; // prevent unlimited growth
    private StackPanel? _logPanel;
    private ScrollViewer? _scroll;

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
        Logger.LineWritten += OnLineWritten;
        Unloaded += (_, _) => Logger.LineWritten -= OnLineWritten;
    }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnLineWritten(LogType type, string line)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_logPanel == null) return;
            var tb = LogPreProcess.ToColoredTextBlock(type, line);
            _logPanel.Children.Add(tb);
            // Trim old
            if (_logPanel.Children.Count > MaxVisualLines)
            {
                int remove = _logPanel.Children.Count - (MaxVisualLines - 100);
                for (int i = 0; i < remove; i++)
                {
                    _logPanel.Children.RemoveAt(0);
                }
            }
            // Auto scroll to bottom
            try
            {
                if (_scroll != null)
                {
                    var current = _scroll.Offset;
                    _scroll.Offset = new Vector(current.X, double.MaxValue);
                }
            }
            catch { }
        });
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
