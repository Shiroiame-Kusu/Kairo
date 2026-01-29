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
}
