using System.Runtime.InteropServices;

namespace Kairo.Core;

/// <summary>
/// 运行环境检测
/// </summary>
public static class EnvironmentDetector
{
    /// <summary>
    /// 检测是否为 Linux 无头模式（无桌面环境）
    /// </summary>
    public static bool IsLinuxHeadless()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return false;

        // 检查 DISPLAY 和 WAYLAND_DISPLAY 环境变量
        var display = Environment.GetEnvironmentVariable("DISPLAY");
        var waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");

        // 两者都为空则认为是无头环境
        return string.IsNullOrEmpty(display) && string.IsNullOrEmpty(waylandDisplay);
    }

    /// <summary>
    /// 检测是否应该使用 CLI 模式
    /// </summary>
    public static bool ShouldUseCli()
    {
        // Linux 无头环境强制使用 CLI
        if (IsLinuxHeadless())
            return true;

        return false;
    }

    /// <summary>
    /// 获取应用程序数据目录（平台相关）
    /// 在 macOS 上修复可能返回 /var/root 的问题
    /// </summary>
    public static string GetApplicationDataPath()
    {
        // 在 macOS 上，明确使用用户的主目录来避免权限问题
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // 首先尝试从环境变量获取 HOME
            string? home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrWhiteSpace(home) && home != "/var/root")
            {
                return Path.Combine(home, "Library", "Application Support");
            }
            
            // 备选方案：使用 UserProfile
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile) && userProfile != "/var/root")
            {
                return Path.Combine(userProfile, "Library", "Application Support");
            }

            // 如果两种方法都失败，尝试使用标准 ApplicationData
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            // 如果还是 /var/root，则抛出异常让用户知道存在问题
            if (appData.StartsWith("/var/root", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "无法确定用户的主目录。HOME 环境变量和 UserProfile 都指向 /var/root。" +
                    "请检查应用程序权限或尝试使用 KAIRO_CONFIG_DIR 环境变量指定配置目录。");
            }
            return appData;
        }

        // 对于其他平台，使用标准的 ApplicationData
        return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    }
}
