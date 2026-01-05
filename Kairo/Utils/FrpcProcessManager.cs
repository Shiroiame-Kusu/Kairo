using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
