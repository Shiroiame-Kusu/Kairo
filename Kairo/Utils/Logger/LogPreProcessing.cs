using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Controls; // added for TextBlock
using Avalonia.Controls.Documents; // for Run, LineBreak
using Avalonia.Media;    // added for Brushes
using Avalonia; 
using Kairo; // for Global

namespace Kairo.Utils.Logger
{
    internal static class LogPreProcess
    {
        public static class Process
        {
            public static List<(LogType, string)> Cache = new();
        }

        private static readonly Regex AnsiRegex = new(@"\x1b\[[0-9;]*m", RegexOptions.Compiled);
        private static readonly Regex IpPortRegex = new(@"\b(\d{1,3}\.){3}\d{1,3}:\d+\b", RegexOptions.Compiled);
        private static readonly Regex SplitRegex = new(@"(?<=\[)(i|w|e|\d{1,3}\.\d{1,3}\.\d{1,3}\.\d{1,3}:\d+)(?=\])", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Strip ANSI color codes and control chars
        public static string Filter(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            string result = AnsiRegex.Replace(input, string.Empty);
            var sb = new StringBuilder(result.Length);
            foreach (char c in result)
            {
                int unicode = c;
                if (unicode > 31 && unicode != 127)
                    sb.Append(c);
            }
            return sb.ToString();
        }

        // Legacy compatibility (returns filtered plain string)
        public static string Color(LogType type, string text) => Filter(text);
        public static string Color((LogType, string) line) => Color(line.Item1, line.Item2);

        // New: build a colored TextBlock with runs similar to legacy WPF Paragraph logic
        public static TextBlock ToColoredTextBlock(LogType type, string line)
        {
            var textBlock = new TextBlock
            {
                FontFamily = new FontFamily("Consolas,JetBrains Mono,monospace"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };

            // Multi-line (e.g., exception) support: handle each line separately as runs, keep newline semantics by adding LineBreak runs
            string filtered = Filter(line);
            string[] physicalLines = filtered.Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < physicalLines.Length; i++)
            {
                string logical = physicalLines[i];
                AppendColoredRuns(textBlock, logical);
                if (i < physicalLines.Length - 1)
                    textBlock.Inlines.Add(new LineBreak());
            }
            return textBlock;
        }

        private static void AppendColoredRuns(TextBlock tb, string logical)
        {
            if (string.IsNullOrEmpty(logical))
            {
                tb.Inlines.Add(new Run());
                return;
            }
            foreach (string segment in SplitRegex.Split(logical))
            {
                if (segment.Length == 0) continue;
                var run = new Run(segment);
                run.Foreground = SelectBrush(segment);
                tb.Inlines.Add(run);
            }
        }

        private static IBrush SelectBrush(string token)
        {
            string lower = token.ToLowerInvariant();
            return lower switch
            {
                "i" => Brushes.MediumTurquoise,
                "w" => Brushes.Gold,
                "e" => Brushes.Crimson,
                "error" => Brushes.Crimson,
                "debug" => Brushes.DarkOrchid,
                "true" => Brushes.YellowGreen,
                "false" => Brushes.Tomato,
                _ => IpPortRegex.IsMatch(token) ? Brushes.Teal : (Global.isDarkThemeEnabled ? Brushes.White : Brushes.Black)
            };
        }
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
