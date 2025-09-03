using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using System.Collections.Generic;
using System.Text.Json;
using System.Security.Cryptography;
using Kairo.Utils.Logger; // for recent logs

namespace Kairo.Utils
{
    internal static class CrashInterception
    {
        /// <summary>
        /// Options controlling crash interception behavior.
        /// </summary>
        public sealed class Options
        {
            public bool ShowDialog { get; init; } = true;
            public int RecentLogLines { get; init; } = 120;
            public int RetainDays { get; init; } = 14;
            public bool WriteStructuredFile { get; init; } = true;
            public string CrashDirectory { get; init; } = Path.Combine("logs", "crash");
            public bool AppendDailyLog { get; init; } = true; // keep legacy daily file
            public bool ShowJsonTab { get; init; } = true;
            public bool ShowLogsTab { get; init; } = true;
            public bool ShowExceptionTab { get; init; } = true;
            public bool IncludeEnvironment { get; init; } = true;
        }

        /// <summary>Current global options (mutable only through ReplaceOptions for thread-safety intent).</summary>
        public static Options CurrentOptions { get; private set; } = new();

        public static void ReplaceOptions(Options? opts) => CurrentOptions = opts ?? new();

        private static int _initialized;
        private static int _handling; // 0/1 flag to avoid re-entrancy while building UI.
        private static readonly object FileWriteLock = new();
        private static readonly object DirectoryLock = new();
        private static string? _machineHash;
        private static bool _retentionProcessed;

        public static event Action<Exception, string>? CrashLogged; // (Exception, crashId)

        /// <summary>
        /// Initialize crash interception (idempotent).
        /// </summary>
        public static void Init(Window? parentWindow = null, Options? options = null)
        {
            if (Interlocked.Exchange(ref _initialized, 1) == 1) return;
            if (options != null) ReplaceOptions(options);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => HandleException((Exception)e.ExceptionObject, "AppDomain", parentWindow);
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                try
                {
                    HandleException(e.Exception, "TaskScheduler", parentWindow);
                    e.SetObserved();
                }
                catch { /* swallow */ }
            };
        }

        private static void EnsureDirectory()
        {
            var dir = CurrentOptions.CrashDirectory;
            if (string.IsNullOrWhiteSpace(dir)) return;
            lock (DirectoryLock)
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                if (!_retentionProcessed)
                {
                    _retentionProcessed = true;
                    TryApplyRetention(dir, CurrentOptions.RetainDays);
                }
            }
        }

        private static void TryApplyRetention(string dir, int retainDays)
        {
            if (retainDays <= 0) return;
            try
            {
                var cutoff = DateTime.Now.AddDays(-retainDays);
                foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        if (info.LastWriteTime < cutoff)
                            info.Delete();
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static string GetMachineHash()
        {
            if (_machineHash != null) return _machineHash;
            try
            {
                using var md5 = MD5.Create();
                string raw = (Environment.MachineName + "|" + Environment.UserName + "|" + Environment.OSVersion + "|" + Global.Version);
                var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(raw));
                _machineHash = Convert.ToHexString(bytes)[..12];
            }
            catch { _machineHash = "UNKNOWN"; }
            return _machineHash;
        }

        /// <summary>
        /// Main handling pipeline.
        /// </summary>
        private static void HandleException(Exception ex, string source, Window? parentWindow)
        {
            try
            {
                EnsureDirectory();
                var crashId = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N")[..6];
                var report = BuildCrashReport(ex, source, crashId);
                WriteCrashFiles(report, ex);
                CrashLoggedSafe(ex, crashId);
                if (CurrentOptions.ShowDialog)
                {
                    // Avoid overlapping dialogs for cascades: show only first while still handling.
                    if (Interlocked.Exchange(ref _handling, 1) == 0)
                    {
                        try { ShowCrashDialog(report, parentWindow); }
                        finally { Interlocked.Exchange(ref _handling, 0); }
                    }
                }
            }
            catch (Exception handlerEx)
            {
                Debug.WriteLine(handlerEx);
            }
        }

        private static void CrashLoggedSafe(Exception ex, string id)
        {
            try { CrashLogged?.Invoke(ex, id); } catch { }
        }

        private static CrashReport BuildCrashReport(Exception ex, string source, string crashId)
        {
            List<(LogType, string)> recentLogs = new();
            try
            {
                if (CurrentOptions.RecentLogLines > 0)
                {
                    var cache = LogPreProcess.Process.Cache; // not thread-safe but read-only iteration typical small race acceptable
                    int start = Math.Max(0, cache.Count - CurrentOptions.RecentLogLines);
                    for (int i = start; i < cache.Count; i++)
                        recentLogs.Add(cache[i]);
                }
            }
            catch { }

            var now = DateTime.Now;
            var env = CurrentOptions.IncludeEnvironment ? CollectEnvironmentInfo(now) : new Dictionary<string, string>();

            var report = new CrashReport
            {
                Id = crashId,
                Time = now,
                Uptime = (now - Global.StartTime).ToString(),
                Source = source,
                ExceptionType = ex.GetType().FullName ?? ex.GetType().Name,
                Message = ex.Message,
                ExceptionMerged = MergeException(ex),
                Version = Global.Version,
                Branch = Global.Branch,
                Revision = Global.Revision,
                MachineHash = GetMachineHash(),
                Environment = env,
                RecentLogs = recentLogs.Select(l => new CrashReport.LogLine { Type = l.Item1.ToString(), Text = l.Item2 }).ToList()
            };
            return report;
        }

        private static Dictionary<string, string> CollectEnvironmentInfo(DateTime now)
        {
            var d = new Dictionary<string, string>();
            void Add(string k, string? v)
            {
                if (string.IsNullOrWhiteSpace(v)) return; d[k] = v!; }
            try
            {
                Add("OSDescription", System.Runtime.InteropServices.RuntimeInformation.OSDescription);
                Add("OSArchitecture", System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString());
                Add("ProcessArchitecture", System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString());
                Add("FrameworkDescription", System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
                Add("RuntimeVersion", Environment.Version.ToString());
                Add("CurrentDirectory", Environment.CurrentDirectory);
                Add("CommandLine", Environment.CommandLine);
                Add("64BitProcess", Environment.Is64BitProcess.ToString());
                Add("ProcessorCount", Environment.ProcessorCount.ToString());
                Add("WorkingSetMB", (Process.GetCurrentProcess().WorkingSet64 / 1024d / 1024d).ToString("F1"));
                Add("UptimeSeconds", (now - Global.StartTime).TotalSeconds.ToString("F0"));
                Add("UserInteractive", Environment.UserInteractive.ToString());
            }
            catch { }
            return d;
        }

        private static void WriteCrashFiles(CrashReport report, Exception ex)
        {
            try
            {
                var dir = CurrentOptions.CrashDirectory;
                if (string.IsNullOrWhiteSpace(dir)) return;
                string dailyFile = Path.Combine(dir, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                string baseName = Path.Combine(dir, report.Id);
                if (CurrentOptions.AppendDailyLog)
                {
                    lock (FileWriteLock)
                    {
                        File.AppendAllText(dailyFile, BuildLegacyLogLine(report), Encoding.UTF8);
                    }
                }
                if (CurrentOptions.WriteStructuredFile)
                {
                    string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
                    lock (FileWriteLock)
                    {
                        File.WriteAllText(baseName + ".json", json, Encoding.UTF8);
                        File.WriteAllText(baseName + ".txt", BuildHumanReadable(report), Encoding.UTF8);
                    }
                }
            }
            catch (Exception fileEx)
            {
                // Last resort: write to Debug so at least something surfaces.
                Debug.WriteLine(fileEx);
            }
        }

        private static string BuildLegacyLogLine(CrashReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine(DateTime.Now + "  |  " + $"{Global.Version} - {Global.Branch}.{Global.Revision}" + "  |  NET" + Environment.Version);
            sb.AppendLine(report.ExceptionMerged);
            sb.AppendLine();
            return sb.ToString();
        }

        private static string BuildHumanReadable(CrashReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Crash ID: {report.Id}");
            sb.AppendLine($"Time: {report.Time:O}");
            sb.AppendLine($"Version: {report.Version} - {report.Branch}.{report.Revision}");
            sb.AppendLine($"Machine: {report.MachineHash}");
            sb.AppendLine($"Source: {report.Source}");
            sb.AppendLine($"Uptime: {report.Uptime}");
            sb.AppendLine($"Exception: {report.ExceptionType}: {report.Message}");
            sb.AppendLine("--- Merged Exception ---");
            sb.AppendLine(report.ExceptionMerged);
            if (report.Environment.Count > 0)
            {
                sb.AppendLine("--- Environment ---");
                foreach (var kv in report.Environment) sb.AppendLine(kv.Key + ": " + kv.Value);
            }
            if (report.RecentLogs.Count > 0)
            {
                sb.AppendLine("--- Recent Logs ---");
                foreach (var l in report.RecentLogs) sb.AppendLine(l.Type + " | " + l.Text);
            }
            return sb.ToString();
        }

        private static async void ShowCrashDialog(CrashReport report, Window? parentWindow)
        {
            try
            {
                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var window = BuildCrashWindow(report);
                    try
                    {
                        if (parentWindow != null)
                            await window.ShowDialog(parentWindow);
                        else if (Access.MainWindow != null)
                            await window.ShowDialog(Access.MainWindow);
                        else
                            window.Show();
                    }
                    catch { window.Show(); }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        private static Window BuildCrashWindow(CrashReport report)
        {
            var tabs = new TabControl();
            var items = new List<TabItem>();

            items.Add(new TabItem
            {
                Header = "概览",
                Content = new ScrollViewer
                {
                    Content = new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        Text = BuildOverviewText(report)
                    }
                }
            });

            if (CurrentOptions.ShowExceptionTab)
            {
                items.Add(new TabItem
                {
                    Header = "异常",
                    Content = new ScrollViewer
                    {
                        Content = new TextBlock
                        {
                            FontFamily = new FontFamily("Consolas,JetBrains Mono,monospace"),
                            TextWrapping = TextWrapping.Wrap,
                            Text = report.ExceptionMerged
                        }
                    }
                });
            }

            if (CurrentOptions.ShowLogsTab && report.RecentLogs.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var l in report.RecentLogs)
                    sb.AppendLine(l.Type + " | " + l.Text);
                items.Add(new TabItem
                {
                    Header = "日志",
                    Content = new ScrollViewer
                    {
                        Content = new TextBlock
                        {
                            FontFamily = new FontFamily("Consolas,JetBrains Mono,monospace"),
                            TextWrapping = TextWrapping.Wrap,
                            Text = sb.ToString()
                        }
                    }
                });
            }

            if (CurrentOptions.ShowJsonTab)
            {
                string json;
                try { json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }); }
                catch { json = "<json serialization failed>"; }
                items.Add(new TabItem
                {
                    Header = "JSON",
                    Content = new ScrollViewer
                    {
                        Content = new TextBlock
                        {
                            FontFamily = new FontFamily("Consolas,JetBrains Mono,monospace"),
                            TextWrapping = TextWrapping.Wrap,
                            Text = json
                        }
                    }
                });
            }

            foreach (var it in items) tabs.Items.Add(it);

            var btnCopy = new Button { Content = "复制", Width = 80 };
            var btnFolder = new Button { Content = "打开目录", Width = 90 };
            var btnClose = new Button { Content = "关闭", Width = 80 };

            btnCopy.Click += async (_, _) =>
            {
                try
                {
                    var txt = BuildHumanReadable(report);
                    if (btnCopy.GetVisualRoot() is Window w)
                        await (w.Clipboard?.SetTextAsync(txt) ?? Task.CompletedTask);
                }
                catch (Exception copyEx)
                {
                    Debug.WriteLine(copyEx);
                }
            };

            btnFolder.Click += (_, _) =>
            {
                try
                {
                    var dir = CurrentOptions.CrashDirectory;
                    if (Directory.Exists(dir))
                    {
                        try
                        {
                            using var _ = Process.Start(new ProcessStartInfo
                            {
                                FileName = dir,
                                UseShellExecute = true
                            });
                        }
                        catch (Exception startEx)
                        {
                            Debug.WriteLine(startEx);
                        }
                    }
                }
                catch (Exception folderEx)
                {
                    Debug.WriteLine(folderEx);
                }
            };

            btnClose.Click += (_, _) =>
            {
                if (btnClose.GetVisualRoot() is Window w) w.Close();
            };

            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Spacing = 8,
                Children = { btnCopy, btnFolder, btnClose }
            };

            var grid = new Grid
            {
                RowDefinitions = new RowDefinitions("*,Auto"),
                Margin = new Thickness(10)
            };
            Grid.SetRow(tabs, 0);
            Grid.SetRow(buttonPanel, 1);
            grid.Children.Add(tabs);
            grid.Children.Add(buttonPanel);

            return new Window
            {
                Title = "Kairo - 崩溃报告 (" + report.Id + ")",
                Width = 760,
                Height = 560,
                CanResize = true,
                Content = grid
            };
        }

        private static string BuildOverviewText(CrashReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"发生了未处理的异常，已生成崩溃报告。");
            sb.AppendLine();
            sb.AppendLine($"ID: {report.Id}");
            sb.AppendLine($"时间: {report.Time:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"版本: {report.Version} - {report.Branch}.{report.Revision}");
            sb.AppendLine($"运行时: .NET {Environment.Version}");
            sb.AppendLine($"运行时框架: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
            sb.AppendLine($"系统: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
            sb.AppendLine($"进程架构: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
            sb.AppendLine($"Uptime: {report.Uptime}");
            sb.AppendLine($"异常: {report.ExceptionType}");
            sb.AppendLine($"消息: {report.Message}");
            sb.AppendLine();
            sb.AppendLine($"◦ 崩溃文件保存在 {CurrentOptions.CrashDirectory}");
            sb.AppendLine("◦ 点击 复制 以复制完整报告，可粘贴到 Issue");
            sb.AppendLine("◦ 点击 打开目录 直达日志文件夹");
            sb.AppendLine();
            sb.AppendLine("提交 Issue: https://github.com/Shiroiame-Kusu/Kairo/issues/new?assignees=&labels=%E2%9D%97+%E5%B4%A9%E6%BA%83&template=crash_report.yml&title=崩溃反馈+" + Uri.EscapeDataString(report.ExceptionType));
            return sb.ToString();
        }

        /// <summary>
        /// Merge exception & inner exceptions into a single string (innermost last).
        /// (Legacy API preserved)
        /// </summary>
        public static string MergeException(Exception? e)
        {
            var sb = new StringBuilder();
            while (e != null)
            {
                sb.Insert(0, e + Environment.NewLine);
                e = e.InnerException; // e already not null here
            }
            return sb.ToString();
        }

        // Legacy methods preserved for compatibility (no longer used internally directly)
        public static void CreateDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        /// <summary>
        /// Backwards compatibility: originally displayed exception directly. Now delegates to HandleException pipeline.
        /// </summary>
        public static void ShowException(Exception e, Window? parentWindow = null)
        {
            HandleException(e, "Explicit", parentWindow);
        }

        // ---- Data model ----
        public sealed class CrashReport
        {
            public string Id { get; set; } = string.Empty;
            public DateTime Time { get; set; }
            public string Uptime { get; set; } = string.Empty;
            public string Source { get; set; } = string.Empty;
            public string Version { get; set; } = string.Empty;
            public string Branch { get; set; } = string.Empty;
            public int Revision { get; set; }
            public string ExceptionType { get; set; } = string.Empty;
            public string Message { get; set; } = string.Empty;
            public string ExceptionMerged { get; set; } = string.Empty;
            public string MachineHash { get; set; } = string.Empty;
            public Dictionary<string, string> Environment { get; set; } = new();
            public List<LogLine> RecentLogs { get; set; } = new();

            public sealed class LogLine
            {
                public string Type { get; set; } = string.Empty;
                public string Text { get; set; } = string.Empty;
            }
        }
    }
}
