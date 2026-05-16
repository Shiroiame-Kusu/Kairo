using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using Kairo.Core;
using Kairo.Utils.Logger;
using Kairo.Utils.Serialization;

namespace Kairo.Utils
{
    internal static partial class CrashInterception
    {
        private static void LogCrash(CrashReport report)
        {
            Logger.Logger.Output(LogType.Error, BuildCrashLogText(report));
        }

        private static void WriteCrashFiles(CrashReport report, Exception ex)
        {
            try
            {
                var dir = CurrentOptions.CrashDirectory;
                if (string.IsNullOrWhiteSpace(dir)) return;
                var dailyFile = Path.Combine(dir, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                var baseName = Path.Combine(dir, report.Id);
                if (CurrentOptions.AppendDailyLog)
                {
                    lock (FileWriteLock)
                    {
                        File.AppendAllText(dailyFile, BuildLegacyLogLine(report), Encoding.UTF8);
                    }
                }
                if (CurrentOptions.WriteStructuredFile)
                {
                    var json = SerializeCrashReport(report, indented: true);
                    lock (FileWriteLock)
                    {
                        File.WriteAllText(baseName + ".json", json, Encoding.UTF8);
                        File.WriteAllText(baseName + ".txt", BuildHumanReadable(report), Encoding.UTF8);
                    }
                }
            }
            catch (Exception fileEx)
            {
                Kairo.Utils.Logger.Logger.Exception("Unhandled exception in Kairo/Utils/Crash/CrashInterception.Files.cs:45", fileEx);
                Debug.WriteLine(fileEx);
            }
        }

        private static string BuildLegacyLogLine(CrashReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine(DateTime.Now + "  |  " + $"{Global.Version} - {Global.Branch.ToDisplayName()}.{Global.Revision}" + "  |  NET" + Environment.Version);
            sb.AppendLine(report.ExceptionMerged);
            sb.AppendLine();
            return sb.ToString();
        }

        private static string BuildCrashLogText(CrashReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[CRASH] {report.Id} from {report.Source}");
            sb.AppendLine($"Version: {report.Version} - {report.Branch}.{report.Revision}");
            sb.AppendLine($"Time: {report.Time:O}");
            sb.AppendLine($"Exception: {report.ExceptionType}: {report.Message}");
            sb.AppendLine(report.ExceptionMerged);
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

        private static string SerializeCrashReport(CrashReport report, bool indented)
        {
            if (!indented)
            {
                return JsonSerializer.Serialize(report, AppJsonContext.Default.CrashReport);
            }

            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            {
                JsonSerializer.Serialize(writer, report, AppJsonContext.Default.CrashReport);
            }
            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }
    }
}
