using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Kairo.Utils;

/// <summary>
/// 环境检测工具类，用于检测运行环境
/// </summary>
public static class EnvironmentDetector
{
    /// <summary>
    /// 检测是否为Linux非桌面环境（无GUI环境）
    /// </summary>
    public static bool IsLinuxHeadless()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return false;

        // 检查 DISPLAY 环境变量（X11）
        var display = Environment.GetEnvironmentVariable("DISPLAY");
        
        // 检查 WAYLAND_DISPLAY 环境变量（Wayland）
        var waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        
        // 检查 XDG_SESSION_TYPE
        var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        
        // 如果 DISPLAY 和 WAYLAND_DISPLAY 都为空，且 session type 不是 x11/wayland，则认为是无头环境
        bool hasDisplay = !string.IsNullOrEmpty(display) || !string.IsNullOrEmpty(waylandDisplay);
        bool hasGuiSession = sessionType == "x11" || sessionType == "wayland";
        
        // 如果没有显示服务器连接，认为是无头环境
        if (!hasDisplay && !hasGuiSession)
            return true;

        // 额外检查：尝试检测是否在 SSH 会话中且没有 X 转发
        var sshTty = Environment.GetEnvironmentVariable("SSH_TTY");
        var sshConnection = Environment.GetEnvironmentVariable("SSH_CONNECTION");
        if (!string.IsNullOrEmpty(sshTty) || !string.IsNullOrEmpty(sshConnection))
        {
            // SSH 连接中，如果没有 DISPLAY，则是无头环境
            if (string.IsNullOrEmpty(display) && string.IsNullOrEmpty(waylandDisplay))
                return true;
        }

        // 检查是否在容器/Docker中运行
        if (IsRunningInContainer() && string.IsNullOrEmpty(display))
            return true;

        return false;
    }

    /// <summary>
    /// 检测是否在容器中运行
    /// </summary>
    private static bool IsRunningInContainer()
    {
        // 检查 /.dockerenv 文件
        if (File.Exists("/.dockerenv"))
            return true;

        // 检查 /proc/1/cgroup 中是否包含 docker/lxc/containerd
        try
        {
            if (File.Exists("/proc/1/cgroup"))
            {
                var content = File.ReadAllText("/proc/1/cgroup");
                if (content.Contains("docker") || content.Contains("lxc") || 
                    content.Contains("containerd") || content.Contains("kubepods"))
                    return true;
            }
        }
        catch { }

        return false;
    }
}
