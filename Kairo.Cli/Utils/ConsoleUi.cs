using System.Globalization;
using System.Text;
using Kairo.Core.Localization;

namespace Kairo.Cli.Utils;

/// <summary>
/// 终端输出辅助：统一的状态标签、颜色、提示输入与按显示宽度对齐（中日韩字符占两列）
/// </summary>
internal static class ConsoleUi
{
    private static readonly object WriteLock = new();

    /// <summary>是否输出颜色（遵循 NO_COLOR、--no-color 与输出重定向）</summary>
    public static bool UseColor { get; private set; } = true;

    /// <summary>安静模式：只输出警告与错误（表格、帮助等主动请求的内容不受影响）</summary>
    public static bool Quiet { get; set; }

    /// <summary>标准输入是否为可交互终端</summary>
    public static bool CanPrompt => !Console.IsInputRedirected;

    public static void Configure(bool noColor)
    {
        UseColor = !noColor
                   && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"))
                   && !string.Equals(Environment.GetEnvironmentVariable("TERM"), "dumb", StringComparison.OrdinalIgnoreCase)
                   && !Console.IsOutputRedirected;
    }

    // ── 状态行 ───────────────────────────────────────────────

    public static void Success(string message) => Tagged(L.T("cli.tag.success"), ConsoleColor.Green, message);
    public static void Info(string message) => Tagged(L.T("cli.tag.info"), ConsoleColor.Cyan, message);
    public static void Warn(string message) => Tagged(L.T("cli.tag.warn"), ConsoleColor.Yellow, message);
    public static void Error(string message) => Tagged(L.T("cli.tag.error"), ConsoleColor.Red, message);
    public static void Hint(string message) => Tagged(L.T("cli.tag.hint"), ConsoleColor.Magenta, message);
    public static void Step(string tag, string message) => Tagged(tag, ConsoleColor.Blue, message);

    private static void Tagged(string tag, ConsoleColor color, string message)
    {
        if (Quiet && color is not (ConsoleColor.Yellow or ConsoleColor.Red)) return;
        lock (WriteLock)
        {
            Write($"[{tag}]", color);
            Console.WriteLine(" " + message);
        }
    }

    /// <summary>章节标题，例如 ── 登录 LoCyanFrp ──────</summary>
    public static void Section(string title)
    {
        var line = $"── {title} ";
        var fill = Math.Max(4, Math.Min(GetWindowWidth(), 72) - DisplayWidth(line));
        Console.WriteLine();
        WriteLine(line + new string('─', fill), ConsoleColor.DarkCyan);
    }

    /// <summary>键值对输出，键按显示宽度对齐</summary>
    public static void KeyValue(string key, string value, int keyWidth = 10, ConsoleColor? valueColor = null)
    {
        lock (WriteLock)
        {
            Write("  " + PadRight(key, keyWidth), ConsoleColor.DarkGray);
            Console.Write(" ");
            if (valueColor.HasValue) WriteLine(value, valueColor.Value);
            else Console.WriteLine(value);
        }
    }

    /// <summary>命令示例：  $ kairo-cli list     # 说明</summary>
    public static void Command(string command, string? comment = null, int commandWidth = 34)
    {
        lock (WriteLock)
        {
            Console.Write("  ");
            if (string.IsNullOrEmpty(comment))
            {
                WriteLine(command, ConsoleColor.Green);
                return;
            }
            Write(PadRight(command, commandWidth), ConsoleColor.Green);
            WriteLine(" " + comment, ConsoleColor.DarkGray);
        }
    }

    public static void Write(string text, ConsoleColor color)
    {
        if (!UseColor)
        {
            Console.Write(text);
            return;
        }

        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = previous;
    }

    public static void WriteLine(string text, ConsoleColor color)
    {
        Write(text, color);
        Console.WriteLine();
    }

    public static void Dim(string text)
    {
        if (!Quiet) WriteLine(text, ConsoleColor.DarkGray);
    }

    // ── 交互输入 ─────────────────────────────────────────────

    /// <summary>读取一行输入；标准输入关闭时返回 null</summary>
    public static string? Prompt(string label, string? defaultValue = null)
    {
        lock (WriteLock)
        {
            Write(label, ConsoleColor.White);
            if (!string.IsNullOrEmpty(defaultValue))
                Write($" [{defaultValue}]", ConsoleColor.DarkGray);
            Console.Write(": ");
        }

        var input = Console.ReadLine();
        if (input == null) return null;
        input = input.Trim();
        return input.Length == 0 && defaultValue != null ? defaultValue : input;
    }

    /// <summary>
    /// 读取一行输入，同时等待 <paramref name="competitor"/>；后者先完成时放弃输入并返回 null。
    /// 通过轮询按键实现，避免残留一个阻塞中的 ReadLine 吞掉后续输入
    /// </summary>
    public static async Task<string?> ReadLineUntilAsync(string label, Task competitor)
    {
        lock (WriteLock)
        {
            Write(label, ConsoleColor.White);
            Console.Write(": ");
        }

        if (Console.IsInputRedirected)
        {
            var readTask = Task.Run(Console.ReadLine);
            var finished = await Task.WhenAny(readTask, competitor);
            return finished == readTask ? readTask.Result?.Trim() : null;
        }

        var buffer = new StringBuilder();
        while (!competitor.IsCompleted)
        {
            while (Console.KeyAvailable)
            {
                var key = Console.ReadKey(intercept: true);
                switch (key.Key)
                {
                    case ConsoleKey.Enter:
                        Console.WriteLine();
                        return buffer.ToString().Trim();
                    case ConsoleKey.Backspace:
                        if (buffer.Length > 0)
                        {
                            buffer.Length--;
                            Console.Write("\b \b");
                        }
                        break;
                    default:
                        if (!char.IsControl(key.KeyChar))
                        {
                            buffer.Append(key.KeyChar);
                            Console.Write(key.KeyChar);
                        }
                        break;
                }
            }
            await Task.Delay(40);
        }

        Console.WriteLine();
        return null;
    }

    /// <summary>是/否确认，直接回车使用默认值</summary>
    public static bool Confirm(string question, bool defaultYes = true)
    {
        if (!CanPrompt) return defaultYes;
        lock (WriteLock)
        {
            Write(question, ConsoleColor.White);
            Write(defaultYes ? " [Y/n]" : " [y/N]", ConsoleColor.DarkGray);
            Console.Write(": ");
        }

        var input = Console.ReadLine()?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(input)) return defaultYes;
        return input is "y" or "yes" or "是" or "好";
    }

    /// <summary>
    /// 从编号列表中选择一项；返回选中项下标，取消或输入结束时返回 null
    /// </summary>
    public static int? Choose(string title, IReadOnlyList<string> options, int defaultIndex = 0)
    {
        if (options.Count == 0) return null;
        Console.WriteLine();
        Console.WriteLine(title);
        for (var i = 0; i < options.Count; i++)
        {
            Write($"  {i + 1}) ", i == defaultIndex ? ConsoleColor.Green : ConsoleColor.DarkGray);
            Console.WriteLine(options[i]);
        }

        while (true)
        {
            var input = Prompt(L.T("cli.prompt.chooseNumber"), (defaultIndex + 1).ToString(CultureInfo.InvariantCulture));
            if (input == null || input.Equals("q", StringComparison.OrdinalIgnoreCase))
                return null;
            if (int.TryParse(input, out var number) && number >= 1 && number <= options.Count)
                return number - 1;
            Warn(L.T("cli.prompt.invalidNumber", options.Count));
        }
    }

    // ── 显示宽度 ─────────────────────────────────────────────

    public static int GetWindowWidth()
    {
        try
        {
            if (!Console.IsOutputRedirected && Console.WindowWidth > 20)
                return Console.WindowWidth;
        }
        catch (IOException)
        {
            // 无终端时使用默认宽度
        }
        return 100;
    }

    public static int DisplayWidth(string? text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var width = 0;
        foreach (var rune in text.EnumerateRunes())
            width += RuneWidth(rune);
        return width;
    }

    public static string PadRight(string? text, int width)
    {
        text ??= string.Empty;
        var padding = width - DisplayWidth(text);
        return padding > 0 ? text + new string(' ', padding) : text;
    }

    /// <summary>按显示宽度截断，超出部分以 … 结尾</summary>
    public static string Truncate(string? text, int maxWidth)
    {
        text ??= string.Empty;
        if (maxWidth <= 0) return string.Empty;
        if (DisplayWidth(text) <= maxWidth) return text;

        var builder = new StringBuilder();
        var width = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            var w = RuneWidth(rune);
            if (width + w > maxWidth - 1) break;
            builder.Append(rune.ToString());
            width += w;
        }
        return builder.Append('…').ToString();
    }

    private static int RuneWidth(Rune rune)
    {
        var category = Rune.GetUnicodeCategory(rune);
        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format)
            return 0;
        if (Rune.IsControl(rune)) return 0;

        var v = rune.Value;
        var wide = v is >= 0x1100 and <= 0x115F
                   || v is >= 0x2E80 and <= 0x303E
                   || v is >= 0x3041 and <= 0x33FF
                   || v is >= 0x3400 and <= 0x4DBF
                   || v is >= 0x4E00 and <= 0x9FFF
                   || v is >= 0xA000 and <= 0xA4CF
                   || v is >= 0xAC00 and <= 0xD7A3
                   || v is >= 0xF900 and <= 0xFAFF
                   || v is >= 0xFE30 and <= 0xFE4F
                   || v is >= 0xFF00 and <= 0xFF60
                   || v is >= 0xFFE0 and <= 0xFFE6
                   || v is >= 0x1F300 and <= 0x1F64F
                   || v is >= 0x1F900 and <= 0x1F9FF
                   || v is >= 0x20000 and <= 0x3FFFD;
        return wide ? 2 : 1;
    }
}
