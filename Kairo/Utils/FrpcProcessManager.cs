using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Kairo.Utils.Logger;
using AppLogger = Kairo.Utils.Logger.Logger;

namespace Kairo.Utils;

internal static class FrpcProcessManager
{
    private const LogDestination FrpcLogDestinations = LogDestination.Console | LogDestination.File | LogDestination.Cache | LogDestination.Event;

    private class ProcInfo
    {
        public int ProxyId { get; init; }
        public Process Process { get; init; } = null!;
        public DateTime StartTime { get; init; } = DateTime.UtcNow;
    }

    private static readonly Dictionary<int, ProcInfo> _processes = new(); // key: proxyId

    public static event Action<int>? ProxyExited; // new event

    public static bool IsRunning(int proxyId) => _processes.ContainsKey(proxyId);

    public static bool StartProxy(int proxyId, string frpcPath, string frpToken, Action<string>? onStarted = null, Action<string>? onFailed = null)
    {
        if (string.IsNullOrWhiteSpace(frpcPath) || !File.Exists(frpcPath))
        {
            onFailed?.Invoke("frpc 路径无效");
            return false;
        }
        
        // 在 Unix-like 系统上检查执行权限
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                var fileInfo = new FileInfo(frpcPath);
                // 尝试检查文件权限，如果无法执行则设置权限
                var testPsi = new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    Arguments = $"-c \"test -x '{frpcPath}'\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var testProc = Process.Start(testPsi))
                {
                    testProc?.WaitForExit(1000);
                    if (testProc?.ExitCode != 0)
                    {
                        // 文件没有执行权限，尝试设置
                        AppLogger.Output(LogType.Warn, FrpcLogDestinations, $"[FRPC] frpc 文件缺少执行权限，正在设置...");
                        var chmodPsi = new ProcessStartInfo
                        {
                            FileName = "/bin/chmod",
                            Arguments = $"+x \"{frpcPath}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var chmodProc = Process.Start(chmodPsi);
                        chmodProc?.WaitForExit(3000);
                        
                        if (chmodProc?.ExitCode != 0)
                        {
                            onFailed?.Invoke("无法设置 frpc 执行权限");
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Output(LogType.Warn, FrpcLogDestinations, "检查/设置执行权限时出错", ex);
                // 继续尝试启动，让系统报告真正的错误
            }
        }
        
        if (IsRunning(proxyId))
        {
            onFailed?.Invoke("该隧道已在运行中");
            return false;
        }
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = frpcPath,
                Arguments = $" -u {frpToken} -t {proxyId}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            
            var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    AppLogger.Output(LogType.Info, FrpcLogDestinations, e.Data);
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    AppLogger.Output(LogType.Error, FrpcLogDestinations, e.Data);
            };
            proc.Exited += (_, _) =>
            {
                lock (_processes)
                {
                    _processes.Remove(proxyId);
                }
                AppLogger.Output(LogType.Info, FrpcLogDestinations, $"[FRPC] Proxy {proxyId} 进程已退出");
                try { ProxyExited?.Invoke(proxyId); } catch { }
            };
            if (!proc.Start())
            {
                onFailed?.Invoke("frpc 启动失败");
                return false;
            }
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            lock (_processes)
            {
                _processes[proxyId] = new ProcInfo { ProxyId = proxyId, Process = proc };
            }
            AppLogger.Output(LogType.Info, FrpcLogDestinations, $"[FRPC] 已启动隧道 {proxyId}, PID={proc.Id}");
            onStarted?.Invoke("已启动");
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Output(LogType.Error, FrpcLogDestinations, "启动 frpc 失败", ex);
            onFailed?.Invoke(ex.Message);
            return false;
        }
    }

    public static bool StopProxy(int proxyId)
    {
        lock (_processes)
        {
            if (_processes.TryGetValue(proxyId, out var info))
            {
                try
                {
                    if (!info.Process.HasExited)
                    {
                        info.Process.Kill(true);
                        info.Process.WaitForExit(3000);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Output(LogType.Error, FrpcLogDestinations, "结束隧道失败", ex);
                    return false;
                }
                _processes.Remove(proxyId);
                AppLogger.Output(LogType.Info, FrpcLogDestinations, $"[FRPC] 已结束隧道 {proxyId}");
                return true;
            }
        }
        return false;
    }

    public static int StopAll()
    {
        List<int> ids;
        lock (_processes)
        {
            ids = new List<int>(_processes.Keys);
        }
        int count = 0;
        foreach (var id in ids)
        {
            if (StopProxy(id)) count++;
        }
        return count;
    }
}
