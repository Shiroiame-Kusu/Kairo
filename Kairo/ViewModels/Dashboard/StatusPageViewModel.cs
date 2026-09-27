using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Kairo.Components.DashBoard;
using Kairo.Core.Localization;
using Kairo.Utils;
using Kairo.Utils.Logger;

namespace Kairo.ViewModels
{
    public class StatusPageViewModel : ViewModelBase, IDisposable
    {
        private const int MaxVisualLines = 800;
        private bool _subscribed;
        private int _lastGlobalIndex;
        private bool? _darkThemeWhenDetached;

        public ObservableCollection<LogEntry> Lines { get; } = new();

        public RelayCommand StopAllCommand { get; }
        public RelayCommand ClearCommand { get; }
        public RelayCommand CopyCommand { get; }

        public int RunningCount => DesignModeHelper.IsDesign ? 2 : FrpcProcessManager.RunningCount;
        public bool HasRunning => RunningCount > 0;
        public string RunningText => HasRunning ? L.Plural("status.running", RunningCount) : L.T("status.noneRunning");
        public bool HasLines => Lines.Count > 0;

        public StatusPageViewModel()
        {
            StopAllCommand = new RelayCommand(StopAllTunnels, () => HasRunning);
            ClearCommand = new RelayCommand(ClearLogs, () => HasLines);
            CopyCommand = new RelayCommand(CopyLogs, () => HasLines);
            Lines.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(HasLines));
                ClearCommand.RaiseCanExecuteChanged();
                CopyCommand.RaiseCanExecuteChanged();
            };
        }

        private void OnRunningChanged()
        {
            Dispatcher.UIThread.Post(() =>
            {
                OnPropertyChanged(nameof(RunningCount));
                OnPropertyChanged(nameof(HasRunning));
                OnPropertyChanged(nameof(RunningText));
                StopAllCommand.RaiseCanExecuteChanged();
            });
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
                // 页面会被缓存复用，订阅需要随 Attach/Detach 成对进行
                Logger.LineWritten += OnLineWritten;
                Logger.Cleared += OnLogsCleared;
                ThemeManager.ThemeChanged += OnThemeChanged;
                FrpcProcessManager.RunningChanged += OnRunningChanged;
                _subscribed = true;
            }
            // 离开页面期间切换过主题时，已有日志需要按新主题重新着色
            if (_darkThemeWhenDetached.HasValue && _darkThemeWhenDetached != Global.isDarkThemeEnabled)
                OnThemeChanged();
            _darkThemeWhenDetached = null;
            OnRunningChanged();
        }

        public void Detach()
        {
            _darkThemeWhenDetached ??= Global.isDarkThemeEnabled;
            if (_subscribed)
            {
                try
                {
                    Logger.LineWritten -= OnLineWritten;
                    Logger.Cleared -= OnLogsCleared;
                    ThemeManager.ThemeChanged -= OnThemeChanged;
                    FrpcProcessManager.RunningChanged -= OnRunningChanged;
                }
                catch (Exception ex)
                {
                    AppLogger.Exception("Unhandled exception in Kairo/ViewModels/Dashboard/StatusPageViewModel.cs:51", ex);
                }
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

        private void OnLogsCleared()
        {
            Dispatcher.UIThread.Post(() =>
            {
                Lines.Clear();
                _lastGlobalIndex = 0;
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

        private async void StopAllTunnels()
        {
            var running = FrpcProcessManager.RunningCount;
            if (running == 0) return;
            if (!await DialogHelper.ConfirmAsync(Access.DashBoard, L.T("status.stopAllTitle"), L.Plural("status.stopAllConfirm", running), L.T("status.stopAllButton"), destructive: true))
                return;

            int stopped = FrpcProcessManager.StopAll();
            (Access.DashBoard as DashBoard)?.OpenSnackbar(L.T("status.stopped"), L.Plural("status.stoppedCount", stopped), FAInfoBarSeverity.Informational);
        }

        private void ClearLogs()
        {
            // 清空日志缓存，Cleared 事件会同步清空界面
            Logger.ClearCache();
            Lines.Clear();
        }

        private async void CopyLogs()
        {
            try
            {
                var text = string.Join(Environment.NewLine, Lines.Select(l => l.Line));
                var clipboard = TopLevel.GetTopLevel(Access.DashBoard)?.Clipboard;
                if (clipboard == null) return;
                await clipboard.SetTextAsync(text);
                (Access.DashBoard as DashBoard)?.OpenSnackbar(L.T("status.copied"), L.Plural("status.copiedLines", Lines.Count), FAInfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                AppLogger.Exception("复制日志失败", ex);
            }
        }

        public void Dispose()
        {
            Detach();
        }
    }

    public record LogEntry(LogType Type, string Line);
}
