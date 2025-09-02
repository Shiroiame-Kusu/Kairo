using System;
using System.Text;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Kairo.Utils.Logger;
using Kairo.Utils;

namespace Kairo.Components;

public partial class StatusPage : UserControl
{
    private StringBuilder _log = new();
    public StatusPage()
    {
        InitializeComponent();
        Logger.LineWritten += OnLineWritten;
        Unloaded += (_, _) => Logger.LineWritten -= OnLineWritten;
    }
    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void OnLineWritten(LogType type, string line)
    {
        if (_log.Length > 100_000)
            _log.Clear();
        _log.AppendLine(line);
        Dispatcher.UIThread.Post(() => LogText.Text = _log.ToString());
    }

    private void StopAllBtn_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        int stopped = FrpcProcessManager.StopAll();
        (Access.DashBoard as DashBoard)?.OpenSnackbar("已停止", $"结束 {stopped} 个隧道", FluentAvalonia.UI.Controls.InfoBarSeverity.Informational);
    }
}
