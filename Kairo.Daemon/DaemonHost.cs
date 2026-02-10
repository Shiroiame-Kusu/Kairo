using System.Text.Json;
using Kairo.Core.Daemon;

namespace Kairo.Daemon;

/// <summary>
/// Daemon 主控 — 协调 IPC 服务器、frpc 管理器和状态持久化
/// </summary>
internal sealed class DaemonHost : IDisposable
{
    private readonly FrpcManager _frpcManager = new();
    private readonly StateManager _stateManager = new();
    private readonly IpcServer _ipcServer = new();
    private bool _disposed;

    public async Task RunAsync(CancellationToken ct)
    {
        Console.WriteLine("[daemon] 正在启动...");

        // 1. 恢复之前的 frpc 进程状态
        RecoverState();

        // 2. 注册事件
        _frpcManager.ProxyExited += OnProxyExited;
        _frpcManager.LogEmitted += OnLogEmitted;
        _ipcServer.OnRequest = HandleRequestAsync;

        // 3. 启动自动保存状态
        _stateManager.StartAutoSave(BuildCurrentState);

        // 4. 保存初始状态
        _stateManager.Save(BuildCurrentState());

        Console.WriteLine("[daemon] 启动完成，等待客户端连接...");

        // 5. 运行 IPC 服务器（阻塞直到取消）
        await _ipcServer.RunAsync(ct).ConfigureAwait(false);

        // 6. 退出时保存最终状态（但不停止 frpc！让它们继续运行）
        Console.WriteLine("[daemon] 正在保存状态...");
        _stateManager.Save(BuildCurrentState());
        _stateManager.StopAutoSave();
    }

    // ─── 状态恢复 ────────────────────────────────────────────

    private void RecoverState()
    {
        var state = _stateManager.Load();
        if (state == null || state.Proxies.Count == 0)
        {
            Console.WriteLine("[daemon] 无需恢复的状态");
            return;
        }

        Console.WriteLine($"[daemon] 发现 {state.Proxies.Count} 个历史隧道，尝试接管...");

        int adopted = 0;
        foreach (var proxy in state.Proxies)
        {
            if (proxy.Running && _frpcManager.TryAdoptProcess(proxy))
                adopted++;
        }

        Console.WriteLine($"[daemon] 成功接管 {adopted}/{state.Proxies.Count} 个隧道");
    }

    // ─── IPC 请求处理 ──────────────────────────────────────────

    private Task<IpcResponse> HandleRequestAsync(int clientId, IpcRequest request)
    {
        try
        {
            var response = request.Type switch
            {
                IpcRequestType.Ping => HandlePing(request),
                IpcRequestType.StartProxy => HandleStartProxy(request),
                IpcRequestType.StopProxy => HandleStopProxy(request),
                IpcRequestType.StopAll => HandleStopAll(request),
                IpcRequestType.GetStatus => HandleGetStatus(request),
                IpcRequestType.GetLogHistory => HandleGetLogHistory(request),
                IpcRequestType.SubscribeLogs => HandleSubscribeLogs(request),
                IpcRequestType.UnsubscribeLogs => HandleUnsubscribeLogs(request),
                IpcRequestType.Shutdown => HandleShutdown(request),
                _ => new IpcResponse { Success = false, Error = $"Unknown request type: {request.Type}" }
            };
            return Task.FromResult(response);
        }
        catch (Exception ex)
        {
            return Task.FromResult(new IpcResponse
            {
                Success = false,
                Error = ex.Message
            });
        }
    }

    private static IpcResponse HandlePing(IpcRequest req)
        => new() { Type = "Pong", Success = true };

    private IpcResponse HandleStartProxy(IpcRequest req)
    {
        var proxyId = GetInt(req.Data, "proxyId");
        var frpcPath = GetString(req.Data, "frpcPath");
        var frpToken = GetString(req.Data, "frpToken");

        var state = _frpcManager.StartProxy(proxyId, frpcPath, frpToken);

        // 立即保存状态
        _stateManager.Save(BuildCurrentState());

        // 广播事件
        _ = _ipcServer.BroadcastEventAsync(new IpcEvent
        {
            Type = IpcEventType.ProxyStarted,
            Data = new Dictionary<string, object>
            {
                ["proxyId"] = proxyId,
                ["pid"] = state.Pid
            }
        });

        return new IpcResponse
        {
            Success = true,
            Data = new Dictionary<string, object>
            {
                ["proxyId"] = state.ProxyId,
                ["pid"] = state.Pid,
                ["startTime"] = state.StartTime.ToString("O"),
                ["running"] = state.Running,
                ["frpcPath"] = state.FrpcPath,
                ["logFile"] = state.LogFile
            }
        };
    }

    private IpcResponse HandleStopProxy(IpcRequest req)
    {
        var proxyId = GetInt(req.Data, "proxyId");
        var success = _frpcManager.StopProxy(proxyId);
        _stateManager.Save(BuildCurrentState());
        return new IpcResponse { Success = success };
    }

    private IpcResponse HandleStopAll(IpcRequest req)
    {
        var count = _frpcManager.StopAll();
        _stateManager.Save(BuildCurrentState());
        return new IpcResponse
        {
            Success = true,
            Data = new Dictionary<string, object> { ["count"] = count }
        };
    }

    private IpcResponse HandleGetStatus(IpcRequest req)
    {
        var proxies = _frpcManager.GetAllStatus();

        // 序列化 List<ProxyState> 为 JsonElement 以保留正确类型
        var json = JsonSerializer.Serialize(proxies, DaemonJsonContext.Default.ListProxyState);
        var je = JsonSerializer.Deserialize<JsonElement>(json);

        return new IpcResponse
        {
            Success = true,
            Data = new Dictionary<string, object> { ["proxies"] = je }
        };
    }

    private IpcResponse HandleGetLogHistory(IpcRequest req)
    {
        var proxyId = GetInt(req.Data, "proxyId");
        var maxLines = req.Data?.ContainsKey("lines") == true ? GetInt(req.Data, "lines") : 200;
        var lines = _frpcManager.GetLogHistory(proxyId, maxLines);

        var json = JsonSerializer.Serialize(lines, DaemonJsonContext.Default.ListLogLine);
        var je = JsonSerializer.Deserialize<JsonElement>(json);

        return new IpcResponse
        {
            Success = true,
            Data = new Dictionary<string, object> { ["lines"] = je }
        };
    }

    private static IpcResponse HandleSubscribeLogs(IpcRequest req)
    {
        // 实际的订阅操作在 ClientSession 中已处理
        return new IpcResponse { Success = true };
    }

    private static IpcResponse HandleUnsubscribeLogs(IpcRequest req)
    {
        return new IpcResponse { Success = true };
    }

    private IpcResponse HandleShutdown(IpcRequest req)
    {
        Console.WriteLine("[daemon] 收到 Shutdown 请求");
        // 不停止 frpc，只退出 daemon
        // 状态会保存，下次 daemon 启动时可以接管
        _ = Task.Run(async () =>
        {
            await Task.Delay(100); // 让响应先发出去
            Environment.Exit(0);
        });
        return new IpcResponse { Success = true };
    }

    // ─── 事件处理 ────────────────────────────────────────────

    private void OnProxyExited(int proxyId)
    {
        _stateManager.Save(BuildCurrentState());
        _ = _ipcServer.BroadcastEventAsync(new IpcEvent
        {
            Type = IpcEventType.ProxyExited,
            Data = new Dictionary<string, object> { ["proxyId"] = proxyId }
        });
    }

    private void OnLogEmitted(LogLine line)
    {
        _ = _ipcServer.SendLogToSubscribersAsync(line.ProxyId, line);
    }

    // ─── 工具方法 ────────────────────────────────────────────

    private DaemonState BuildCurrentState()
    {
        return new DaemonState
        {
            DaemonPid = Environment.ProcessId,
            ProtocolVersion = DaemonConstants.ProtocolVersion,
            StartTime = DateTime.UtcNow,
            SocketPath = DaemonConstants.GetSocketPath(),
            Proxies = _frpcManager.GetAllStatus()
        };
    }

    private static int GetInt(Dictionary<string, object>? data, string key)
    {
        if (data == null || !data.TryGetValue(key, out var val))
            throw new ArgumentException($"缺少参数: {key}");
        return val is JsonElement je ? je.GetInt32() : Convert.ToInt32(val);
    }

    private static string GetString(Dictionary<string, object>? data, string key)
    {
        if (data == null || !data.TryGetValue(key, out var val))
            throw new ArgumentException($"缺少参数: {key}");
        return val is JsonElement je ? je.GetString() ?? "" : val?.ToString() ?? "";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _frpcManager.ProxyExited -= OnProxyExited;
        _frpcManager.LogEmitted -= OnLogEmitted;

        _stateManager.Save(BuildCurrentState());
        _stateManager.StopAutoSave();

        _ipcServer.Dispose();
        // 注意：我们 *不* dispose FrpcManager（即不杀 frpc 进程）
        // 让 frpc 继续运行，daemon 下次启动时可以接管
    }
}
