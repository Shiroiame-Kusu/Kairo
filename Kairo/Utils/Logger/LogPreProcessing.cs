using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Controls; // for TextBlock
using Avalonia.Controls.Documents; // for Run, LineBreak
using Avalonia.Media;    // for Brushes

namespace Kairo.Utils.Logger
{
    internal static class LogPreProcess
    {
        public static class Process
        {
            public static List<(LogType, string)> Cache = new();
        }

        // ANSI removal (unchanged)
        private static readonly Regex AnsiRegex = new(@"\x1b\[[0-9;]*m", RegexOptions.Compiled);

        // Highlight regex rules (ordered by priority to avoid overlaps). More specific first.
        private static readonly Regex IpPortRegex = new(@"\b(?:(?:\d{1,3}\.){3}\d{1,3})(?::\d{1,5})\b", RegexOptions.Compiled);
        private static readonly Regex TimestampRegex = new(@"\b\d{4}[-/]\d{2}[-/]\d{2}[ T]\d{2}:\d{2}:\d{2}(?:\.\d{3,6})?\b", RegexOptions.Compiled);
        private static readonly Regex FileLineRegex = new(@"\b[a-zA-Z0-9_.-]+\.go:\d+\b", RegexOptions.Compiled);
        private static readonly Regex DurationRegex = new(@"\b\d+(?:ms|s|m)\b", RegexOptions.Compiled);
        private static readonly Regex BooleanRegex = new(@"\b(true|false)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex LevelBracketRegex = new(@"\[(?:I|W|E|D|T|INFO|WARN|ERROR|DEBUG|TRACE)\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SuccessWordRegex = new(@"\b(success|connected|login ok|started)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ErrorWordRegex = new(@"\b(error|fail|failed|timeout|refused)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex NumberRegex = new(@"\b\d+\b", RegexOptions.Compiled); // fallback numeric (placed last)

        private record TokenRule(Regex Regex, Func<Match, IBrush> BrushPicker, int Priority);
        private record Span(int Start, int Length, IBrush Brush);

        // Pre-created brushes (light & dark aware where needed)
        private static IBrush BrushTimestamp => Global.isDarkThemeEnabled ? new SolidColorBrush(Color.FromRgb(140, 140, 148)) : new SolidColorBrush(Color.FromRgb(96, 96, 96));
        private static IBrush BrushIp => new SolidColorBrush(Color.FromRgb(79, 193, 255));        // light blue
        private static IBrush BrushFile => new SolidColorBrush(Color.FromRgb(116, 198, 157));     // green
        private static IBrush BrushDuration => new SolidColorBrush(Color.FromRgb(156, 204, 101)); // lime-ish
        private static IBrush BrushTrue => new SolidColorBrush(Color.FromRgb(139, 195, 74));
        private static IBrush BrushFalse => new SolidColorBrush(Color.FromRgb(239, 83, 80));
        private static IBrush BrushWarn => new SolidColorBrush(Color.FromRgb(255, 193, 7));
        private static IBrush BrushError => new SolidColorBrush(Color.FromRgb(244, 67, 54));
        private static IBrush BrushInfo => new SolidColorBrush(Color.FromRgb(3, 169, 244));
        private static IBrush BrushDebug => new SolidColorBrush(Color.FromRgb(171, 71, 188));
        private static IBrush BrushSuccess => new SolidColorBrush(Color.FromRgb(102, 187, 106));
        private static IBrush BrushNumber => new SolidColorBrush(Color.FromRgb(176, 190, 197));
        private static IBrush BrushDefault => Global.isDarkThemeEnabled ? Brushes.White : Brushes.Black;

        private static readonly List<TokenRule> Rules = new()
        {
            new TokenRule(IpPortRegex, _ => BrushIp, 10),
            new TokenRule(TimestampRegex, _ => BrushTimestamp, 9),
            new TokenRule(FileLineRegex, _ => BrushFile, 8),
            new TokenRule(LevelBracketRegex, m => LevelBrush(m.Value), 7),
            new TokenRule(DurationRegex, _ => BrushDuration, 6),
            new TokenRule(BooleanRegex, m => m.Value.Equals("true", StringComparison.OrdinalIgnoreCase) ? BrushTrue : BrushFalse, 5),
            new TokenRule(ErrorWordRegex, _ => BrushError, 4),
            new TokenRule(SuccessWordRegex, _ => BrushSuccess, 3),
            new TokenRule(NumberRegex, _ => BrushNumber, 1) // low priority
        };

        private static IBrush LevelBrush(string token)
        {
            string t = token.Trim('[', ']').ToUpperInvariant();
            return t switch
            {
                "I" or "INFO" => BrushInfo,
                "W" or "WARN" => BrushWarn,
                "E" or "ERROR" => BrushError,
                "D" or "DEBUG" => BrushDebug,
                "T" or "TRACE" => BrushDebug,
                _ => BrushDefault
            };
        }

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

        // New highlighting engine
        public static TextBlock ToColoredTextBlock(LogType type, string line)
        {
            var tb = new TextBlock
            {
                FontFamily = new FontFamily("Consolas,JetBrains Mono,monospace"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };

            var inlines = tb.Inlines;
            if (inlines == null)
                return tb;

            if (string.IsNullOrEmpty(line))
            {
                inlines.Add(new Run());
                return tb;
            }

            string filtered = Filter(line);
            string[] physicalLines = filtered.Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < physicalLines.Length; i++)
            {
                BuildLineRuns(inlines, physicalLines[i], type);
                if (i < physicalLines.Length - 1)
                    inlines.Add(new LineBreak());
            }
            return tb;
        }

        private static void BuildLineRuns(InlineCollection inlines, string logical, LogType type)
        {   
            
            if (string.IsNullOrEmpty(logical))
            {
                inlines.Add(new Run());
                return;
            }

            var spans = CollectSpans(logical);
            // Sort by start
            spans.Sort((a, b) => a.Start.CompareTo(b.Start));
            int pos = 0;
            foreach (var s in spans)
            {
                if (s.Start > pos)
                {
                    inlines.Add(new Run(logical[pos..s.Start]) { Foreground = BaseBrush(type) });
                }
                inlines.Add(new Run(logical.Substring(s.Start, s.Length)) { Foreground = s.Brush });
                pos = s.Start + s.Length;
            }
            if (pos < logical.Length)
            {
                inlines.Add(new Run(logical[pos..]) { Foreground = BaseBrush(type) });
            }
        }

        private static List<Span> CollectSpans(string text)
        {
            var list = new List<Span>();
            foreach (var rule in Rules)
            {
                foreach (Match m in rule.Regex.Matches(text))
                {
                    if (!m.Success || m.Length == 0) continue;
                    if (Overlaps(list, m.Index, m.Length)) continue; // keep first (higher priority due to rule order)
                    list.Add(new Span(m.Index, m.Length, rule.BrushPicker(m)));
                }
            }
            return list;
        }

        private static bool Overlaps(List<Span> spans, int start, int length)
        {
            int end = start + length;
            foreach (var s in spans)
            {
                int sEnd = s.Start + s.Length;
                if (start < sEnd && end > s.Start) return true;
            }
            return false;
        }

        private static IBrush BaseBrush(LogType type)
        {
            // Always use plain default (white/black) for non-token text per request
            return BrushDefault;
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
