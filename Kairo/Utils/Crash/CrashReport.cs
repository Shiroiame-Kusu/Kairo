using System;
using System.Collections.Generic;

namespace Kairo.Utils;

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
