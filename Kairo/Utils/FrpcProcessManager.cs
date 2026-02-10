using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kairo.Core.Daemon;
using Kairo.Utils.Logger;
using AppLogger = Kairo.Utils.Logger.Logger;

namespace Kairo.Utils;

/// <summary>
/// frpc 进程管理器 — 通过 DaemonClient 委托给独立的 daemon 进程
/// 保留原有的同步 API 签名以最小化上层改动
/// </summary>
internal static class FrpcProcessManager
{
    private const LogDestination FrpcLogDestinations = LogDestination.Console | LogDestination.File | LogDestination.Cache | LogDestination.Event;

    private static readonly DaemonClient _daemon = new();
    private static readonly HashSet<int> _runningProxies = new();
    private static readonly object _lock = new();
    private static bool _initialized;

    /// <summary>隧道退出事件</summary>
    public static event Action<int>? ProxyExited;

    /// <summary>日志行事件</summary>
    public static event Action<LogLine>? LogReceived;

    /// <summary>
    /// 初始化连接到 daemon（应在 App 启动时调用一次）
    /// </summary>
    public static async Task InitializeAsync(string? daemonPath = null)
    {
        if (_initialized) return;
        _initialized = true;

        _daemon.ProxyExited += OnProxyExited;
        _daemon.LogReceived += OnLogReceived;

        try
        {
            await _daemon.ConnectAsync(daemonPath).ConfigureAwait(false);
            AppLogger.Output(LogType.Info, FrpcLogDestinations, "[FRPC] 已连接到 daemon");

            // 同步运行状态
            await SyncRunningStateAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            AppLogger.Output(LogType.Error, FrpcLogDestinations, "[FRPC] 连接 daemon 失败", ex);
        }
    }

    /// <summary>
    /// 从 daemon 同步当前运行状态（用于崩溃恢复后获取之前的隧道）
    /// </summary>
    public static async Task SyncRunningStateAsync()
    {
        try
        {
            var proxies = await _daemon.GetStatusAsync().ConfigureAwait(false);
            lock (_lock)
            {
                _runningProxies.Clear();
                foreach (var p in proxies)
                {
                    if (p.Running)
                        _runningProxies.Add(p.ProxyId);
                }
            }
            AppLogger.Output(LogType.Info, FrpcLogDestinations, $"[FRPC] 同步状态：{_runningProxies.Count} 个隧道正在运行");
        }
        catch (Exception ex)
        {
            AppLogger.Output(LogType.Warn, FrpcLogDestinations, "[FRPC] 同步状态失败", ex);
        }
    }

    public static bool IsRunning(int proxyId)
    {
        lock (_lock) return _runningProxies.Contains(proxyId);
    }

    public static bool StartProxy(int proxyId, string frpcPath, string frpToken, Action<string>? onStarted = null, Action<string>? onFailed = null)
    {
        if (IsRunning(proxyId))
        {
            onFailed?.Invoke("该隧道已在运行中");
            return false;
        }

        // 异步发送到 daemon，同步包装
        Task.Run(async () =>
        {
            try
            {
                await EnsureConnectedAsync().ConfigureAwait(false);
                var state = await _daemon.StartProxyAsync(proxyId, frpcPath, frpToken).ConfigureAwait(false);
                lock (_lock)
                {
                    _runningProxies.Add(proxyId);
                }
                AppLogger.Output(LogType.Info, FrpcLogDestinations, $"[FRPC] 已启动隧道 {proxyId}, PID={state.Pid}");

                // 自动订阅日志
                try { await _daemon.SubscribeLogsAsync(proxyId).ConfigureAwait(false); } catch { }

                onStarted?.Invoke("已启动");
            }
            catch (Exception ex)
            {
                AppLogger.Output(LogType.Error, FrpcLogDestinations, "启动 frpc 失败", ex);
                onFailed?.Invoke(ex.Message);
            }
        });

        return true; // 立即返回（异步操作中）
    }

    public static bool StopProxy(int proxyId)
    {
        try
        {
            var result = Task.Run(async () =>
            {
                await EnsureConnectedAsync().ConfigureAwait(false);
                return await _daemon.StopProxyAsync(proxyId).ConfigureAwait(false);
            }).GetAwaiter().GetResult();

            if (result)
            {
                lock (_lock)
                {
                    _runningProxies.Remove(proxyId);
                }
                AppLogger.Output(LogType.Info, FrpcLogDestinations, $"[FRPC] 已停止隧道 {proxyId}");
            }
            return result;
        }
        catch (Exception ex)
        {
            AppLogger.Output(LogType.Error, FrpcLogDestinations, "停止隧道失败", ex);
            return false;
        }
    }

    public static int StopAll()
    {
        try
        {
            var count = Task.Run(async () =>
            {
                await EnsureConnectedAsync().ConfigureAwait(false);
                return await _daemon.StopAllAsync().ConfigureAwait(false);
            }).GetAwaiter().GetResult();

            lock (_lock)
            {
                _runningProxies.Clear();
            }
            AppLogger.Output(LogType.Info, FrpcLogDestinations, $"[FRPC] 已停止 {count} 个隧道");
            return count;
        }
        catch (Exception ex)
        {
            AppLogger.Output(LogType.Error, FrpcLogDestinations, "停止所有隧道失败", ex);
            return 0;
        }
    }

    /// <summary>
    /// 断开与 daemon 的连接（应用退出时调用；不会杀掉 frpc）
    /// </summary>
    public static void Disconnect()
    {
        _daemon.ProxyExited -= OnProxyExited;
        _daemon.LogReceived -= OnLogReceived;
        _daemon.Dispose();
    }

    // ─── Private ────────────────────────────────────────────

    private static async Task EnsureConnectedAsync()
    {
        if (!_daemon.IsConnected)
        {
            await _daemon.ConnectAsync().ConfigureAwait(false);
        }
    }

    private static void OnProxyExited(int proxyId)
    {
        lock (_lock)
        {
            _runningProxies.Remove(proxyId);
        }
        AppLogger.Output(LogType.Info, FrpcLogDestinations, $"[FRPC] 隧道 {proxyId} 已退出");
        try { ProxyExited?.Invoke(proxyId); } catch { }
    }

    private static void OnLogReceived(LogLine line)
    {
        // 将 daemon 日志转发到 GUI 的日志系统
        var logType = line.IsError ? LogType.Error : LogType.Info;
        AppLogger.Output(logType, FrpcLogDestinations, line.Text);
        try { LogReceived?.Invoke(line); } catch { }
    }
}
