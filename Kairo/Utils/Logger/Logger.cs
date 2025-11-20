using System;
using System.IO;
using System.Text;
using Avalonia.Controls;
using Kairo.Utils; // CrashInterception & Access
using Avalonia.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.VisualTree;
using FluentAvalonia.UI.Controls; // for InfoBarSeverity
using Kairo.Components.DashBoard; // for DashBoard cast

namespace Kairo.Utils.Logger
{
    [System.Flags]
    internal enum LogDestination
    {
        None = 0,
        Console = 1 << 0,
        Cache = 1 << 1,
        File = 1 << 2,
        Event = 1 << 3,
        All = Console | Cache | File | Event
    }

    internal static class Logger
    {
        private static readonly object _cacheLock = new();
        private static readonly object _fileLock = new();
        private static bool _logDirEnsured;
        private static int _totalLines; // monotonically increasing count of all lines ever logged (since process start)

        public static event Action<LogType, string>? LineWritten; // UI or other subscribers can hook

        public static bool EnableFileLogging { get; set; } = true;
        public static string LogDirectory { get; set; } = Path.Combine("logs", "app");
        public static int MaxCacheSize { get; set; } = 500;
        public static int CacheTrimTo { get; set; } = 400;

        private const LogDestination DefaultDestinations = LogDestination.All;
        private const LogDestination NetworkDestinations = LogDestination.Console | LogDestination.File;
        private static bool ShouldEmitToConsole(LogType type) => Global.DebugMode || type is LogType.Warn or LogType.Error;

        // New: expose a snapshot of cached lines for late subscribers / page reloads
        public static System.Collections.Generic.List<(LogType, string)> GetCacheSnapshot()
        {
            lock (_cacheLock)
            {
                return new System.Collections.Generic.List<(LogType, string)>(LogPreProcess.Process.Cache);
            }
        }

        // Newer: snapshot returning base global index so UI can recover after trimming
        public static (System.Collections.Generic.List<(LogType, string)> snapshot, int baseIndex) GetCacheSnapshotWithBase()
        {
            lock (_cacheLock)
            {
                int baseIndex = _totalLines - LogPreProcess.Process.Cache.Count; // global index of first cached item
                return (new System.Collections.Generic.List<(LogType, string)>(LogPreProcess.Process.Cache), baseIndex);
            }
        }

        public static void Output(LogType type, params object?[] objects) => OutputInternal(type, DefaultDestinations, objects);

        public static void Output(LogType type, LogDestination destinations, params object?[] objects) => OutputInternal(type, destinations, objects);

        public static void OutputNetwork(LogType type, params object?[] objects) => OutputInternal(type, GetNetworkDestinations(type), objects);

        private static void OutputInternal(LogType type, LogDestination destinations, object?[] objects)
        {
            if (destinations == LogDestination.None)
                return;
            string line = BuildLine(type, objects);
            if (destinations.HasFlag(LogDestination.Console) && ShouldEmitToConsole(type))
                WriteConsole(type, line);
            if (destinations.HasFlag(LogDestination.Cache))
            {
                lock (_cacheLock)
                {
                    LogPreProcess.Process.Cache.Add(new(type, line));
                    _totalLines++;
                    if (LogPreProcess.Process.Cache.Count > MaxCacheSize)
                    {
                        int remove = LogPreProcess.Process.Cache.Count - CacheTrimTo;
                        if (remove > 0)
                            LogPreProcess.Process.Cache.RemoveRange(0, remove);
                    }
                }
            }
            if (destinations.HasFlag(LogDestination.File) && EnableFileLogging)
                TryWriteFile(type, line);
            if (destinations.HasFlag(LogDestination.Event))
            {
                try { LineWritten?.Invoke(type, line); } catch { }
            }
        }

        private static string BuildLine(LogType type, object?[] objects)
        {
            var bld = new StringBuilder();
            foreach (var o in objects)
            {
                if (o == null)
                {
                    if (type is LogType.Debug or LogType.DetailDebug) bld.Append("null ");
                    continue;
                }
                if (o is Exception ex)
                {
                    bld.AppendLine(CrashInterception.MergeException(ex));
                }
                else
                {
                    bld.Append(o);
                    bld.Append(' ');
                }
            }
            string line = bld.ToString();
            if (type != LogType.Info) line = line.TrimEnd();
            return line;
        }

        private static void WriteConsole(LogType type, string line)
        {
            string prefix = type switch
            {
                LogType.Info => "[INFO] ",
                LogType.Warn => "[WARN] ",
                LogType.Error => "[ERROR] ",
                LogType.Debug => "[DEBUG] ",
                LogType.DetailDebug => "[TRACE] ",
                _ => string.Empty
            };
            Console.WriteLine(prefix + line);
        }

        private static void TryWriteFile(LogType type, string line)
        {
            try
            {
                if (!_logDirEnsured)
                {
                    Directory.CreateDirectory(LogDirectory);
                    _logDirEnsured = true;
                }
                string file = Path.Combine(LogDirectory, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                string formatted = $"{DateTime.Now:HH:mm:ss.fff} | {type,-11} | {line}{Environment.NewLine}";
                lock (_fileLock)
                {
                    File.AppendAllText(file, formatted, Encoding.UTF8);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[LOGGER ERROR] " + ex.Message);
            }
        }

        /// <summary>
        /// Displays a message: if buttons == 0 show snackbar (legacy behavior), else modal dialog. Returns true if confirmed.
        /// buttons: 0 => snackbar only; 1 => OK/Cancel; >1 => Yes/No.
        /// icon: 48 => Warning, else Error/Informational.
        /// className currently unused (kept for legacy signature compatibility).
        /// </summary>
        public static bool MsgBox(string text, string caption, int buttons, int icon, int className)
        {
            // Snackbar path
            if (buttons == 0)
            {
                try
                {
                    string title;
                    string body;
                    if (text.Contains('\n'))
                    {
                        int idx = text.IndexOf('\n');
                        title = text[..idx];
                        body = text[(idx + 1)..].TrimStart('\n');
                    }
                    else
                    {
                        title = "执行失败";
                        body = text;
                    }
                    var severity = icon == 48 ? InfoBarSeverity.Warning : InfoBarSeverity.Error;
                    (Access.DashBoard as DashBoard)?.OpenSnackbar(title, body, severity);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[MSGBOX SNACKBAR ERROR] " + ex.Message);
                }
                return true;
            }

            try
            {
                bool confirmed = false;
                var tcs = new TaskCompletionSource();
                Dispatcher.UIThread.Post(async () =>
                {
                    var dialog = new Window
                    {
                        Title = caption,
                        Width = 420,
                        Height = 260,
                        CanResize = false,
                        Content = BuildDialogContent(text, buttons, () => confirmed = true, () => confirmed = false, tcs)
                    };
                    if (Access.MainWindow != null)
                        await dialog.ShowDialog(Access.MainWindow);
                    else
                        dialog.Show();
                });
                tcs.Task.GetAwaiter().GetResult();
                return confirmed;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[LOGGER MSGBOX ERROR] " + ex);
                return false;
            }
        }

        private static Control BuildDialogContent(string text, int buttons, Action confirm, Action cancel, TaskCompletionSource tcs)
        {
            var grid = new Grid
            {
                RowDefinitions = new RowDefinitions("*,Auto"),
                Margin = new Thickness(12)
            };
            grid.Children.Add(new ScrollViewer
            {
                Content = new TextBlock
                {
                    Text = text,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                }
            });
            var panel = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Spacing = 8
            };
            var okContent = buttons <= 1 ? "确定" : "是";
            var cancelContent = buttons <= 1 ? "取消" : "否";
            var btnOk = new Button { Content = okContent, Width = 80 };
            btnOk.Click += (_, _) => { confirm(); CloseOwner(btnOk); tcs.TrySetResult(); };
            panel.Children.Add(btnOk);
            if (buttons != 0) // show second button for any modal dialog (legacy behavior)
            {
                var btnCancel = new Button { Content = cancelContent, Width = 80 };
                btnCancel.Click += (_, _) => { cancel(); CloseOwner(btnCancel); tcs.TrySetResult(); };
                panel.Children.Add(btnCancel);
            }
            panel.AttachedToVisualTree += (_, _) => btnOk.Focus();
            Grid.SetRow(panel, 1);
            grid.Children.Add(panel);
            return grid;
        }

        private static void CloseOwner(Control control)
        {
            if (control.GetVisualRoot() is Window w)
                w.Close();
        }

        private static LogDestination GetNetworkDestinations(LogType type)
        {
            if (type == LogType.DetailDebug && !Global.DebugMode)
                return LogDestination.File | LogDestination.Cache;
            return NetworkDestinations;
        }
    }
}
