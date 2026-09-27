using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using SkiaSharp;

namespace Kairo.Utils;

/// <summary>
/// 中文字体回退配置。
/// Avalonia 为缺字的字符向系统请求回退字体时，只附带当前 UI 语言（系统语言为英文时是 en-US）。
/// 此时 fontconfig 会把与日文共用的汉字匹配到日文字体（如 MS Gothic），只有简体独有的字才落到中文字体，
/// 同一个词里混用两种字体，字重、字宽和全角标点的间距都不一致。这里为中日韩字符统一指定简体中文字体
/// </summary>
internal static class FontSetup
{
    // 部首、符号与标点、假名、注音、表意文字及扩展区、兼容表意文字、竖排与兼容形式、全角字符
    private static readonly UnicodeRange CjkRange = new(new[]
    {
        new UnicodeRangeSegment(0x2E80, 0x2FFF),
        new UnicodeRangeSegment(0x3000, 0x33FF),
        new UnicodeRangeSegment(0x3400, 0x4DBF),
        new UnicodeRangeSegment(0x4E00, 0x9FFF),
        new UnicodeRangeSegment(0xF900, 0xFAFF),
        new UnicodeRangeSegment(0xFE10, 0xFE1F),
        new UnicodeRangeSegment(0xFE30, 0xFE4F),
        new UnicodeRangeSegment(0xFF00, 0xFFEF),
        new UnicodeRangeSegment(0x20000, 0x323AF)
    });

    // 系统首选字体之后依次尝试的常见简体中文界面字体，未安装的会被跳过
    private static readonly string[] CommonFamilies =
    {
        "Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC", "Hiragino Sans GB",
        "Noto Sans CJK SC", "Noto Sans SC", "Source Han Sans SC", "Source Han Sans CN",
        "HarmonyOS Sans SC", "MiSans", "Sarasa UI SC", "WenQuanYi Micro Hei", "Droid Sans Fallback"
    };

    public static FontManagerOptions CreateOptions()
    {
        var families = new List<string>();
        var preferred = GetSystemPreferredFamily();
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            // 微软雅黑的 UI 版本行高更紧凑，是 Windows 中文界面的默认字体
            if (preferred.Equals("Microsoft YaHei", StringComparison.OrdinalIgnoreCase))
                families.Add("Microsoft YaHei UI");
            families.Add(preferred);
        }
        families.AddRange(CommonFamilies);

        return new FontManagerOptions
        {
            FontFallbacks = families
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(family => new FontFallback { FontFamily = new FontFamily(family), UnicodeRange = CjkRange })
                .ToArray()
        };
    }

    /// <summary>系统为简体中文配置的首选字体（Linux 上即 fontconfig 对 lang=zh-cn 的匹配结果）</summary>
    private static string? GetSystemPreferredFamily()
    {
        try
        {
            using var typeface = SKFontManager.Default.MatchCharacter(null, SKFontStyle.Normal, new[] { "zh-CN" }, '中');
            return typeface?.FamilyName;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
