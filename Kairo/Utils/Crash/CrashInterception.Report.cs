using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Kairo.Core;
using Kairo.Utils.Logger;

namespace Kairo.Utils
{
    internal static partial class CrashInterception
    {
        private static CrashReport BuildCrashReport(Exception ex, string source, string crashId)
        {
            List<(LogType, string)> recentLogs = new();
            try
            {
                if (CurrentOptions.RecentLogLines > 0)
                {
                    var cache = LogPreProcess.Process.Cache;
                    var start = Math.Max(0, cache.Count - CurrentOptions.RecentLogLines);
                    for (var i = start; i < cache.Count; i++)
                        recentLogs.Add(cache[i]);
                }
            }
            catch (System.Exception logEx)
            {
                Kairo.Utils.Logger.Logger.Exception("Unhandled exception in Kairo/Utils/Crash/CrashInterception.Report.cs:27", logEx);
            }

            var now = DateTime.Now;
            var env = CurrentOptions.IncludeEnvironment ? CollectEnvironmentInfo(now) : new Dictionary<string, string>();

            return new CrashReport
            {
                Id = crashId,
                Time = now,
                Uptime = (now - Global.StartTime).ToString(),
                Source = source,
                ExceptionType = ex.GetType().FullName ?? ex.GetType().Name,
                Message = ex.Message,
                ExceptionMerged = MergeException(ex),
                Version = Global.Version,
                Branch = Global.Branch.ToDisplayName(),
                Revision = Global.Revision,
                MachineHash = GetMachineHash(),
                Environment = env,
                RecentLogs = recentLogs.Select(l => new CrashReport.LogLine { Type = l.Item1.ToString(), Text = l.Item2 }).ToList()
            };
        }

        private static Dictionary<string, string> CollectEnvironmentInfo(DateTime now)
        {
            var d = new Dictionary<string, string>();
            void Add(string k, string? v)
            {
                if (!string.IsNullOrWhiteSpace(v)) d[k] = v;
            }

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
            catch (System.Exception ex)
            {
                Kairo.Utils.Logger.Logger.Exception("Unhandled exception in Kairo/Utils/Crash/CrashInterception.Report.cs:73", ex);
            }
            return d;
        }

        private static string GetMachineHash()
        {
            if (_machineHash != null) return _machineHash;
            try
            {
                using var md5 = MD5.Create();
                var raw = Environment.MachineName + "|" + Environment.UserName + "|" + Environment.OSVersion + "|" + Global.Version;
                var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(raw));
                _machineHash = Convert.ToHexString(bytes)[..12];
            }
            catch (System.Exception ex)
            {
                Kairo.Utils.Logger.Logger.Exception("Unhandled exception in Kairo/Utils/Crash/CrashInterception.Report.cs:87", ex);
                _machineHash = "UNKNOWN";
            }
            return _machineHash;
        }
    }
}
