using System.Collections.Generic;
using System.Linq;
using Kairo.Core.Localization;
using Kairo.ViewModels;

namespace Kairo.Localization;

/// <summary>语言选择列表中的一项：跟随系统或某种具体语言</summary>
public sealed class LanguageOption : ViewModelBase
{
    private readonly string? _nativeName;

    private LanguageOption(string setting, string? nativeName)
    {
        Setting = setting;
        _nativeName = nativeName;
    }

    /// <summary>保存到配置中的值：语言代码或 <see cref="Localizer.SystemLanguage"/></summary>
    public string Setting { get; }

    /// <summary>具体语言始终用该语言自己的名称显示，方便看不懂当前语言的人找到自己的语言</summary>
    public string DisplayName => _nativeName ?? L.T("language.system");

    public static IReadOnlyList<LanguageOption> All { get; } =
        new[] { new LanguageOption(Localizer.SystemLanguage, null) }
            .Concat(Localizer.Languages.Select(language => new LanguageOption(language.Code, language.NativeName)))
            .ToArray();

    public static LanguageOption Find(string? setting) =>
        All.FirstOrDefault(option => option.Setting.Equals(LanguageSettings.Normalize(setting), System.StringComparison.OrdinalIgnoreCase))
        ?? All[1];
}
