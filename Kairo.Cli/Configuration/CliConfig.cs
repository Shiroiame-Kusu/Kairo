using System.Text.Json.Serialization;
using Kairo.Core.Configuration;

namespace Kairo.Cli.Configuration;

/// <summary>
/// CLI 专用配置（扩展基础配置）
/// </summary>
public class CliConfig : BaseConfig
{
    /// <summary>
    /// 自动启动的隧道 ID 列表
    /// </summary>
    public List<int> AutoLaunch { get; set; } = new();
}

/// <summary>
/// CLI 配置 JSON Source Generator（用于 AOT）
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(CliConfig))]
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
        }
        catch
        {
            _config = new CliConfig();
        }
    }

    public static void Save() => ConfigHelper.Save(_config, CliConfigJsonContext.Default.CliConfig);
}
