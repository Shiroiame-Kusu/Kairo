using Kairo.Core.Localization;
using Kairo.Utils.Configuration;

namespace Kairo.Localization;

/// <summary>界面语言设置：保存在 Settings.json 的 language 字段中（与 kairo-cli 共用）</summary>
internal static class LanguageSettings
{
    /// <summary>当前的语言设置；未设置时为默认语言</summary>
    public static string Current => Normalize(Global.Config.Language);

    public static string Normalize(string? setting) =>
        string.IsNullOrWhiteSpace(setting) ? Localizer.DefaultLanguage : setting.Trim();

    /// <summary>启动时按配置应用语言</summary>
    public static void ApplySaved() => Localizer.SetLanguage(Current);

    /// <summary>修改语言设置：保存配置并立即切换界面语言</summary>
    public static void Change(string setting)
    {
        setting = Normalize(setting);
        if (setting.Equals(Current, System.StringComparison.OrdinalIgnoreCase)) return;
        Global.Config.Language = setting;
        ConfigManager.Save();
        Localizer.SetLanguage(setting);
    }
}
