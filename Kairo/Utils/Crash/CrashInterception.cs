using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Kairo.Utils.Logger;

namespace Kairo.Utils
{
    internal static partial class CrashInterception
    {
        public sealed class Options
        {
            public bool ShowDialog { get; init; } = true;
            public int RecentLogLines { get; init; } = 120;
            public int RetainDays { get; init; } = 14;
            public bool WriteStructuredFile { get; init; } = true;
            public string CrashDirectory { get; init; } = Path.Combine("logs", "crash");
            public bool AppendDailyLog { get; init; } = true;
            public bool ShowJsonTab { get; init; } = true;
            public bool ShowLogsTab { get; init; } = true;
            public bool ShowExceptionTab { get; init; } = true;
            public bool IncludeEnvironment { get; init; } = true;
        }

        public static Options CurrentOptions { get; private set; } = new();

        public static void ReplaceOptions(Options? opts) => CurrentOptions = opts ?? new();

        private static int _initialized;
        private static int _handling;
        private static readonly object FileWriteLock = new();
        private static readonly object DirectoryLock = new();
        private static string? _machineHash;
        private static bool _retentionProcessed;

        public static event Action<Exception, string>? CrashLogged;

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
                catch (System.Exception ex)
                {
                    AppLogger.Exception("Unhandled exception in Kairo/Utils/Crash/CrashInterception.cs:52", ex);
                }
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
                    catch (System.Exception ex)
                    {
                        AppLogger.Exception("Unhandled exception in Kairo/Utils/Crash/CrashInterception.cs:85", ex);
                    }
                }
            }
            catch (System.Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/Utils/Crash/CrashInterception.cs:88", ex);
            }
        }

        private static void HandleException(Exception ex, string source, Window? parentWindow)
        {
            try
            {
                EnsureDirectory();
                var crashId = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N")[..6];
                var report = BuildCrashReport(ex, source, crashId);
                LogCrash(report);
                WriteCrashFiles(report, ex);
                CrashLoggedSafe(ex, crashId);
                if (CurrentOptions.ShowDialog && Interlocked.Exchange(ref _handling, 1) == 0)
                {
                    try { ShowCrashDialog(report, parentWindow); }
                    finally { Interlocked.Exchange(ref _handling, 0); }
                }
            }
            catch (Exception handlerEx)
            {
                AppLogger.Exception("Unhandled exception in Kairo/Utils/Crash/CrashInterception.cs:107", handlerEx);
                Debug.WriteLine(handlerEx);
            }
        }

        private static void CrashLoggedSafe(Exception ex, string id)
        {
            try { CrashLogged?.Invoke(ex, id); }
            catch (System.Exception callbackEx)
            {
                AppLogger.Exception("Unhandled exception in Kairo/Utils/Crash/CrashInterception.cs:115", callbackEx);
            }
        }

        public static string MergeException(Exception? e)
        {
            var sb = new System.Text.StringBuilder();
            while (e != null)
            {
                sb.Insert(0, e + Environment.NewLine);
                e = e.InnerException;
            }
            return sb.ToString();
        }

        public static void CreateDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        public static void ShowException(Exception e, Window? parentWindow = null)
        {
            HandleException(e, "Explicit", parentWindow);
        }
    }
}
