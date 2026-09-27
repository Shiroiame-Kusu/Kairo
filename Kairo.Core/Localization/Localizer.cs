using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Kairo.Core.Logging;

namespace Kairo.Core.Localization;

/// <summary>界面语言</summary>
/// <param name="Code">语言代码，如 zh-CN</param>
/// <param name="NativeName">该语言自身的名称，用于语言选择列表</param>
public sealed record LanguageInfo(string Code, string NativeName);

/// <summary>
/// 多语言文本管理。每个程序集把 <c>Languages/&lt;语言代码&gt;.json</c> 作为嵌入资源，并通过 <see cref="Register"/> 注册；
/// JSON 可以按模块嵌套分组，嵌套的键用点号连接（如 <c>tunnels.create.title</c>）。
/// 查找顺序：当前语言 → 默认语言（简体中文）→ 键名本身
/// </summary>
public static class Localizer
{
    /// <summary>默认语言，也是所有文本的原始语言</summary>
    public const string DefaultLanguage = "zh-CN";

    /// <summary>配置中表示“跟随系统语言”的值</summary>
    public const string SystemLanguage = "system";

    public static IReadOnlyList<LanguageInfo> Languages { get; } = new[]
    {
        new LanguageInfo("zh-CN", "简体中文"),
        new LanguageInfo("en-US", "English")
    };

    private static readonly object Gate = new();
    private static readonly Dictionary<string, Dictionary<string, string>> Catalogs = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> RegisteredSources = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte> ReportedMissing = new(StringComparer.Ordinal);

    // 启动时的系统界面语言；切换语言会修改 CurrentUICulture，因此需要提前记录
    private static readonly string SystemLanguageTag = DetectSystemLanguageTag();

    private static volatile Dictionary<string, string> _current = new(StringComparer.Ordinal);
    private static volatile Dictionary<string, string> _fallback = new(StringComparer.Ordinal);

    static Localizer()
    {
        Register(typeof(Localizer).Assembly, "Kairo.Core.Lang.");
    }

    /// <summary>当前界面语言代码</summary>
    public static string CurrentLanguage { get; private set; } = DefaultLanguage;

    /// <summary>界面语言切换后触发（在调用 <see cref="SetLanguage"/> 的线程上）</summary>
    public static event Action? LanguageChanged;

    /// <summary>
    /// 注册程序集中名称为 <c>{resourcePrefix}{语言代码}.json</c> 的嵌入资源。同一程序集和前缀只会加载一次
    /// </summary>
    public static void Register(Assembly assembly, string resourcePrefix)
    {
        var sourceKey = assembly.FullName + "|" + resourcePrefix;
        lock (Gate)
        {
            if (!RegisteredSources.Add(sourceKey)) return;

            foreach (var name in assembly.GetManifestResourceNames())
            {
                if (!name.StartsWith(resourcePrefix, StringComparison.Ordinal) ||
                    !name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    continue;

                var code = name[resourcePrefix.Length..^".json".Length];
                using var stream = assembly.GetManifestResourceStream(name);
                if (stream == null) continue;

                try
                {
                    if (!Catalogs.TryGetValue(code, out var catalog))
                        Catalogs[code] = catalog = new Dictionary<string, string>(StringComparer.Ordinal);
                    using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
                    {
                        CommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    });
                    Flatten(document.RootElement, string.Empty, catalog);
                }
                catch (JsonException ex)
                {
                    CoreLogger.Output(CoreLogLevel.Error, $"语言文件 {name} 格式错误", ex);
                }
            }

            RebuildSnapshots();
        }
    }

    /// <summary>把配置值（语言代码、system 或空）解析为支持的语言代码</summary>
    public static string Resolve(string? setting)
    {
        var value = setting?.Trim();
        if (string.IsNullOrEmpty(value)) return DefaultLanguage;
        if (value.Equals(SystemLanguage, StringComparison.OrdinalIgnoreCase))
            // 系统语言不是中文时，英文比中文更可能看得懂
            return Match(SystemLanguageTag) ?? "en-US";
        return Match(value) ?? DefaultLanguage;
    }

    /// <summary>配置值能否匹配到支持的语言（system 也算支持），用于校验命令行等外部输入</summary>
    public static bool IsSupported(string? setting)
    {
        var value = setting?.Trim();
        if (string.IsNullOrEmpty(value)) return false;
        return value.Equals(SystemLanguage, StringComparison.OrdinalIgnoreCase) || Match(value) != null;
    }

    /// <summary>切换界面语言；语言实际发生变化时返回 true 并触发 <see cref="LanguageChanged"/></summary>
    public static bool SetLanguage(string? setting)
    {
        var code = Resolve(setting);
        // 语言不变时也同步区域信息：启动时默认语言与配置相同，但 CurrentUICulture 仍是系统语言
        ApplyCulture(code);
        lock (Gate)
        {
            if (code.Equals(CurrentLanguage, StringComparison.OrdinalIgnoreCase)) return false;
            CurrentLanguage = code;
            RebuildSnapshots();
        }

        LanguageChanged?.Invoke();
        return true;
    }

    /// <summary>获取文本，找不到时返回键名</summary>
    public static string Get(string key)
    {
        if (TryGet(key, out var value)) return value;
        ReportMissing(key, "*");
        return key;
    }

    /// <summary>获取文本；当前语言缺失时回退到默认语言</summary>
    public static bool TryGet(string key, out string value)
    {
        if (_current.TryGetValue(key, out value!)) return true;
        if (_fallback.TryGetValue(key, out value!))
        {
            ReportMissing(key, CurrentLanguage);
            return true;
        }

        value = key;
        return false;
    }

    /// <summary>判断键是否存在（任何语言），用于区分可选的复数形式等</summary>
    public static bool Contains(string key) => _current.ContainsKey(key) || _fallback.ContainsKey(key);

    /// <summary>已加载的全部键（按语言），用于检查翻译是否完整</summary>
    public static IReadOnlyDictionary<string, IReadOnlyCollection<string>> GetKeysByLanguage()
    {
        lock (Gate)
            return Catalogs.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyCollection<string>)pair.Value.Keys.ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    private static void RebuildSnapshots()
    {
        _fallback = Catalogs.TryGetValue(DefaultLanguage, out var fallback)
            ? new Dictionary<string, string>(fallback, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        _current = Catalogs.TryGetValue(CurrentLanguage, out var current)
            ? new Dictionary<string, string>(current, StringComparer.Ordinal)
            : _fallback;
    }

    private static void Flatten(JsonElement element, string prefix, Dictionary<string, string> target)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    Flatten(property.Value, prefix.Length == 0 ? property.Name : prefix + "." + property.Name, target);
                break;
            case JsonValueKind.Array:
                // 列表按下标展开：tips.0、tips.1 …
                var index = 0;
                foreach (var item in element.EnumerateArray())
                    Flatten(item, prefix + "." + index++, target);
                break;
            case JsonValueKind.String:
                target[prefix] = element.GetString() ?? string.Empty;
                break;
        }
    }

    /// <summary>把 zh_CN.UTF-8、zh-Hans-CN、en-GB 等语言标记匹配到支持的语言</summary>
    private static string? Match(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var normalized = tag.Split('.', '@')[0].Replace('_', '-');

        foreach (var language in Languages)
            if (language.Code.Equals(normalized, StringComparison.OrdinalIgnoreCase))
                return language.Code;

        var primary = normalized.Split('-')[0];
        foreach (var language in Languages)
            if (language.Code.Split('-')[0].Equals(primary, StringComparison.OrdinalIgnoreCase))
                return language.Code;
        return null;
    }

    private static string DetectSystemLanguageTag()
    {
        var culture = CultureInfo.CurrentUICulture.Name;
        if (!string.IsNullOrEmpty(culture)) return culture;

        // 启用了 InvariantGlobalization（如 kairo-cli）时区域信息为空，改读 POSIX 语言环境变量
        foreach (var variable in new[] { "LC_ALL", "LC_MESSAGES", "LANG", "LANGUAGE" })
        {
            var value = Environment.GetEnvironmentVariable(variable)?.Split(':')[0];
            if (!string.IsNullOrWhiteSpace(value) && value != "C" && value != "POSIX")
                return value;
        }
        return string.Empty;
    }

    private static void ApplyCulture(string code)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(code);
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
        catch (CultureNotFoundException)
        {
            // InvariantGlobalization 下无法创建区域信息，不影响文本查找
        }
    }

    private static void ReportMissing(string key, string language)
    {
        if (ReportedMissing.TryAdd(language + "|" + key, 0))
            CoreLogger.Output(CoreLogLevel.Warn, $"缺少 {language} 翻译: {key}");
    }
}
