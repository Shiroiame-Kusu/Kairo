using System.Text.Json;
using System.Text.Json.Serialization;
using Kairo.Core.Configuration;

namespace Kairo.Cli.Configuration;

/// <summary>
/// CLI 专用配置（扩展基础配置）
/// </summary>
public class CliConfig : BaseConfig
{
    /// <summary>
    /// 自动启动的隧道 ID 列表（与 GUI 的 autoLaunch 结构不同，使用独立的键名避免互相覆盖）
    /// </summary>
    [JsonPropertyName("cliAutoLaunch")]
    public List<int> AutoLaunch { get; set; } = new();

    /// <summary>
    /// 是否默认启用调试日志
    /// </summary>
    public bool DebugMode { get; set; } = false;

    /// <summary>
    /// 是否将日志写入文件
    /// </summary>
    public bool LogToFile { get; set; } = true;

    /// <summary>
    /// 界面语言：语言代码（zh-CN、en-US）或 system；为空时使用简体中文。与 GUI 共用
    /// </summary>
    public string Language { get; set; } = "";

    /// <summary>
    /// 保留 CLI 不认识的配置项（例如 GUI 的主题设置），保存时原样写回
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}

/// <summary>
/// CLI 配置 JSON Source Generator（用于 AOT）。
/// 与 GUI 共用 Settings.json，因此使用相同的 camelCase 键名；读取时忽略大小写以兼容旧版 CLI 写入的配置
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(CliConfig))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(ProviderAuthState))]
[JsonSerializable(typeof(Dictionary<string, ProviderAuthState>))]
public partial class CliConfigJsonContext : JsonSerializerContext { }

/// <summary>
/// CLI 配置管理器
/// </summary>
public static class CliConfigManager
{
    private static CliConfig _config = new();

    public static CliConfig Config => _config;

    public static void Init()
    {
        try
        {
            ConfigHelper.EnsureConfigDirectoryExists();
            _config = ConfigHelper.Load(CliConfigJsonContext.Default.CliConfig);
            NormalizeDictionaries();
            if (HasLegacyKeys())
                Save();
        }
        catch (System.Exception ex)
        {
            Kairo.Cli.Utils.Logger.Exception(ex, "Unhandled exception in Kairo.Cli/Configuration/CliConfig.cs:53");
            _config = new CliConfig();
        }
    }

    public static void Save() => ConfigHelper.Save(_config, CliConfigJsonContext.Default.CliConfig);

    /// <summary>
    /// 反序列化得到的字典默认区分大小写，这里恢复为忽略大小写（服务商 Id 可能被写成不同大小写）
    /// </summary>
    private static void NormalizeDictionaries()
    {
        _config.ProviderAuth = new Dictionary<string, ProviderAuthState>(_config.ProviderAuth ?? new(), StringComparer.OrdinalIgnoreCase);
        _config.FrpcPaths = new Dictionary<string, string>(_config.FrpcPaths ?? new(), StringComparer.OrdinalIgnoreCase);
        _config.AutoLaunch ??= new List<int>();
    }

    /// <summary>
    /// 旧版 CLI 以 PascalCase 保存配置，GUI 无法读取；检测到后立即以新格式重写一次
    /// </summary>
    private static bool HasLegacyKeys()
    {
        try
        {
            var path = ConfigHelper.GetSettingsFilePath();
            if (!File.Exists(path)) return false;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Name is "ProviderId" or "AccessToken" or "RefreshToken" or "ProviderAuth" or "FrpcPaths")
                    return true;
            }
        }
        catch (System.Exception ex)
        {
            Kairo.Cli.Utils.Logger.Debug($"检查旧版配置格式失败: {ex.Message}");
        }
        return false;
    }
}
