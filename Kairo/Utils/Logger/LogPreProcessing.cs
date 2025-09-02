using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Kairo.Utils.Logger
{
    internal static class LogPreProcess
    {
        public static class Process
        {
            public static List<(LogType, string)> Cache = new();
        }

        // Strip ANSI color codes and control chars
        public static string Filter(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            string result = Regex.Replace(input, @"\x1b\[[0-9;]*m", string.Empty);
            var sb = new StringBuilder(result.Length);
            foreach (char c in result)
            {
                int unicode = c;
                if (unicode > 31 && unicode != 127)
                    sb.Append(c);
            }
            return sb.ToString();
        }

        // Previously returned a WPF Paragraph, now just return filtered string.
        public static string Color(LogType type, string text) => Filter(text);
        public static string Color((LogType, string) line) => Color(line.Item1, line.Item2);
    }

    internal enum LogType
    {
        Undefined,
        Info,
        Warn,
        Error,
        Debug,
        DetailDebug
    }
}
