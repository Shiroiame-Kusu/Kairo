using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Kairo.Core.Configuration;

/// <summary>
/// 共享配置基类
/// </summary>
public class BaseConfig
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public string Username { get; set; } = "";
    public int ID { get; set; } = 0;
    public string FrpToken { get; set; } = "";
    public string FrpcPath { get; set; } = "";
    public bool UsingDownloadMirror { get; set; } = true;
}

/// <summary>
/// 配置管理器（提供配置目录和基础 IO 逻辑）
/// </summary>
public static class ConfigHelper
{
    private static string? _configDirectory;

    /// <summary>
    /// 获取配置目录（支持 KAIRO_CONFIG_DIR 环境变量）
    /// </summary>
    public static string GetConfigDirectory()
    {
        if (_configDirectory != null) return _configDirectory;

        string? envDir = Environment.GetEnvironmentVariable("KAIRO_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(envDir))
        {
            try { envDir = Path.GetFullPath(envDir); }
            catch { envDir = null; }
        }
        _configDirectory = envDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kairo");
        return _configDirectory;
    }

    /// <summary>
    /// 获取配置文件路径
    /// </summary>
    public static string GetSettingsFilePath()
        => Path.Combine(GetConfigDirectory(), "Settings.json");

    /// <summary>
    /// 确保配置目录存在
    /// </summary>
    public static void EnsureConfigDirectoryExists()
        => Directory.CreateDirectory(GetConfigDirectory());

    /// <summary>
    /// 加载配置（使用 JsonTypeInfo 用于 AOT）
    /// </summary>
    public static T Load<T>(JsonTypeInfo<T> typeInfo) where T : new()
    {
        try
        {
            var path = GetSettingsFilePath();
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize(json, typeInfo) ?? new T();
            }
        }
        catch { }
        return new T();
    }

    /// <summary>
    /// 保存配置（使用 JsonTypeInfo 用于 AOT）
    /// </summary>
    public static void Save<T>(T config, JsonTypeInfo<T> typeInfo)
    {
        try
        {
            EnsureConfigDirectoryExists();
            var json = JsonSerializer.Serialize(config, typeInfo);
            File.WriteAllText(GetSettingsFilePath(), json);
        }
        catch { }
    }
}
