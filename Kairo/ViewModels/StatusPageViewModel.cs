using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Kairo.Components.DashBoard;
using Kairo.Utils;
using Kairo.Utils.Logger;

namespace Kairo.ViewModels
{
    public class StatusPageViewModel : ViewModelBase, IDisposable
    {
        private const int MaxVisualLines = 800;
        private bool _subscribed;
        private int _lastGlobalIndex;

        public ObservableCollection<LogEntry> Lines { get; } = new();

        public RelayCommand StopAllCommand { get; }

        public StatusPageViewModel()
        {
            StopAllCommand = new RelayCommand(StopAllTunnels);
            ThemeManager.ThemeChanged += OnThemeChanged;
        }

        public void Attach()
        {
            if (DesignModeHelper.IsDesign)
            {
                Lines.Clear();
                foreach (var (type, line) in DesignModeHelper.SampleLogs)
                {
                    Lines.Add(new LogEntry(type, line));
                }
                return;
            }

            PopulateFromCacheIncremental();
            if (!_subscribed)
            {
                Logger.LineWritten += OnLineWritten;
                _subscribed = true;
            }
        }

        public void Detach()
        {
            if (_subscribed)
            {
                try { Logger.LineWritten -= OnLineWritten; } catch { }
                _subscribed = false;
            }
        }

        private void OnLineWritten(LogType type, string line)
        {
            Dispatcher.UIThread.Post(() =>
            {
                AddLine(type, line);
                _lastGlobalIndex++;
            });
        }

        private void PopulateFromCacheIncremental()
        {
            var (snapshot, baseIndex) = Logger.GetCacheSnapshotWithBase();
            if (snapshot.Count == 0) return;

            if (_lastGlobalIndex < baseIndex) _lastGlobalIndex = baseIndex;
            int startOffset = _lastGlobalIndex - baseIndex;
            if (startOffset < 0) startOffset = 0;
            if (startOffset >= snapshot.Count) return;

            for (int i = startOffset; i < snapshot.Count; i++)
            {
                var (t, l) = snapshot[i];
                AddLine(t, l);
            }
            _lastGlobalIndex = baseIndex + snapshot.Count;
        }

        private void AddLine(LogType type, string line)
        {
            Lines.Add(new LogEntry(type, line));
            if (Lines.Count > MaxVisualLines)
            {
                int remove = Lines.Count - (MaxVisualLines - 100);
                for (int i = 0; i < remove && Lines.Count > 0; i++)
                {
                    Lines.RemoveAt(0);
                }
            }
        }

        private void OnThemeChanged()
        {
            for (int i = 0; i < Lines.Count; i++)
            {
                var entry = Lines[i];
                Lines[i] = new LogEntry(entry.Type, entry.Line);
            }
        }

        private void StopAllTunnels()
        {
            int stopped = FrpcProcessManager.StopAll();
            (Access.DashBoard as DashBoard)?.OpenSnackbar("已停止", $"结束 {stopped} 个隧道", InfoBarSeverity.Informational);
        }

        public void Dispose()
        {
            Detach();
            ThemeManager.ThemeChanged -= OnThemeChanged;
        }
    }

    public record LogEntry(LogType Type, string Line);
}
