using System.Runtime.InteropServices;

namespace Kairo.Core.Daemon;

/// <summary>
/// Daemon 常量定义
/// </summary>
public static class DaemonConstants
{
    /// <summary>
    /// Unix Domain Socket 文件名
    /// </summary>
    private const string SocketFileName = "kairo-daemon.sock";

    /// <summary>
    /// Daemon PID 文件名  
    /// </summary>
    private const string PidFileName = "kairo-daemon.pid";

    /// <summary>
    /// Daemon 状态文件名
    /// </summary>
    private const string StateFileName = "daemon-state.json";

    /// <summary>
    /// Daemon 日志目录名
    /// </summary>
    private const string LogDirName = "daemon-logs";

    /// <summary>
    /// 获取 daemon 运行时目录（存放 socket、pid 文件等）
    /// </summary>
    public static string GetRuntimeDirectory()
    {
        string baseDir;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // 优先使用 XDG_RUNTIME_DIR（用户级临时目录，systemd 管理）
            var xdgRuntime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            baseDir = !string.IsNullOrEmpty(xdgRuntime)
                ? Path.Combine(xdgRuntime, "kairo")
                : Path.Combine(Path.GetTempPath(), $"kairo-{Environment.UserName}");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            baseDir = Path.Combine(Path.GetTempPath(), $"kairo-{Environment.UserName}");
        }
        else // Windows
        {
            baseDir = Path.Combine(Path.GetTempPath(), "kairo");
        }

        return baseDir;
    }

    /// <summary>
    /// 获取 Unix Domain Socket 路径
    /// </summary>
    public static string GetSocketPath()
        => Path.Combine(GetRuntimeDirectory(), SocketFileName);

    /// <summary>
    /// 获取 PID 文件路径
    /// </summary>
    public static string GetPidFilePath()
        => Path.Combine(GetRuntimeDirectory(), PidFileName);

    /// <summary>
    /// 获取状态文件路径（持久化目录，非临时）
    /// </summary>
    public static string GetStateFilePath()
        => Path.Combine(Configuration.ConfigHelper.GetConfigDirectory(), StateFileName);

    /// <summary>
    /// 获取 daemon 日志目录
    /// </summary>
    public static string GetLogDirectory()
        => Path.Combine(Configuration.ConfigHelper.GetConfigDirectory(), LogDirName);

    /// <summary>
    /// 获取指定隧道的日志文件路径
    /// </summary>
    public static string GetProxyLogPath(int proxyId)
        => Path.Combine(GetLogDirectory(), $"proxy-{proxyId}.log");

    /// <summary>
    /// IPC 协议版本
    /// </summary>
    public const int ProtocolVersion = 1;

    /// <summary>
    /// 最大消息大小 (1MB)
    /// </summary>
    public const int MaxMessageSize = 1024 * 1024;

    /// <summary>
    /// 连接超时 (ms)
    /// </summary>
    public const int ConnectTimeoutMs = 5000;

    /// <summary>
    /// Daemon 可执行文件名
    /// </summary>
    public static string DaemonExecutableName =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "kairo-daemon.exe"
            : "kairo-daemon";
}
