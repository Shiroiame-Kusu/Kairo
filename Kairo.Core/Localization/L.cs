using System.Globalization;

namespace Kairo.Core.Localization;

/// <summary>
/// 文本查找的简写：<c>L.T("tunnels.title")</c>、<c>L.T("tunnels.started", name)</c>、<c>L.Plural("tunnels.count", n)</c>
/// </summary>
public static class L
{
    /// <summary>当前语言的文本</summary>
    public static string T(string key) => Localizer.Get(key);

    /// <summary>当前语言的文本，并用 <paramref name="args"/> 填充 {0}、{1} 等占位符</summary>
    public static string T(string key, params object?[] args) => Format(Localizer.Get(key), args);

    /// <summary>
    /// 按数量选择单复数形式：英文等语言可以在键下提供 <c>one</c> 和 <c>other</c> 两种写法，
    /// 没有时使用键本身。未传入 <paramref name="args"/> 时 {0} 为数量
    /// </summary>
    public static string Plural(string key, long count, params object?[] args)
    {
        var form = UsesSingularForm(Localizer.CurrentLanguage, count) ? "one" : "other";
        if (!Localizer.TryGet($"{key}.{form}", out var template) &&
            !Localizer.TryGet($"{key}.other", out template))
            template = Localizer.Get(key);
        return Format(template, args.Length == 0 ? new object?[] { count } : args);
    }

    /// <summary>JSON 数组形式的文本列表（按 <c>key.0</c>、<c>key.1</c> … 依次读取）</summary>
    public static IReadOnlyList<string> List(string key)
    {
        var items = new List<string>();
        while (Localizer.TryGet($"{key}.{items.Count}", out var item))
            items.Add(item);
        return items;
    }

    private static bool UsesSingularForm(string language, long count) =>
        // 中文没有单复数之分
        !language.StartsWith("zh", StringComparison.OrdinalIgnoreCase) && count == 1;

    private static string Format(string template, object?[] args)
    {
        if (args.Length == 0) return template;
        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            // 翻译里的占位符写错时，至少把原文显示出来
            return template;
        }
    }
}
