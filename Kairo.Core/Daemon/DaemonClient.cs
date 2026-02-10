using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Kairo.Core.Daemon;

/// <summary>
/// Daemon 客户端 — 连接到 daemon 进程（如不存在则自动启动）
/// GUI 和 CLI 共用此类管理 frpc 隧道
/// </summary>
public sealed class DaemonClient : IAsyncDisposable, IDisposable
{
    private Socket? _socket;
    private NetworkStream? _stream;
    private CancellationTokenSource? _eventCts;
    private Task? _eventLoop;
    private readonly object _sendLock = new();
    private readonly SemaphoreSlim _sendSemaphore = new(1, 1);
    private readonly Dictionary<string, TaskCompletionSource<IpcResponse>> _pendingRequests = new();
    private volatile bool _disposed;

    /// <summary>daemon 进程退出后的隧道状态变化通知</summary>
    public event Action<int>? ProxyExited;

    /// <summary>日志流回调</summary>
    public event Action<LogLine>? LogReceived;

    /// <summary>连接状态</summary>
    public bool IsConnected => _socket is { Connected: true } && !_disposed;

    /// <summary>
    /// 确保 daemon 正在运行，然后连接
    /// </summary>
    /// <param name="daemonPath">daemon 可执行文件路径（为 null 时自动检测同目录）</param>
    /// <param name="ct">取消令牌</param>
    public async Task ConnectAsync(string? daemonPath = null, CancellationToken ct = default)
    {
        if (IsConnected) return;

        // 1. 尝试连接已有 daemon
        if (!await TryConnectExistingAsync(ct).ConfigureAwait(false))
        {
            // 2. 没有正在运行的 daemon，启动一个
            StartDaemonProcess(daemonPath);

            // 3. 等待 daemon 就绪后重新连接
            var deadline = DateTime.UtcNow.AddMilliseconds(DaemonConstants.ConnectTimeoutMs);
            bool connected = false;
            while (DateTime.UtcNow < deadline)
            {
                await Task.Delay(100, ct).ConfigureAwait(false);
                if (await TryConnectExistingAsync(ct).ConfigureAwait(false))
                {
                    connected = true;
                    break;
                }
            }
            if (!connected)
                throw new TimeoutException("Daemon 启动超时，无法连接");
        }

        // 4. 启动事件接收循环
        _eventCts = new CancellationTokenSource();
        _eventLoop = Task.Run(() => EventLoopAsync(_eventCts.Token), _eventCts.Token);
    }

    /// <summary>
    /// 启动隧道
    /// </summary>
    public async Task<ProxyState> StartProxyAsync(int proxyId, string frpcPath, string frpToken, CancellationToken ct = default)
    {
        var resp = await SendRequestAsync(IpcRequestType.StartProxy, new Dictionary<string, object>
        {
            ["proxyId"] = proxyId,
            ["frpcPath"] = frpcPath,
            ["frpToken"] = frpToken
        }, ct).ConfigureAwait(false);

        if (!resp.Success)
            throw new InvalidOperationException(resp.Error ?? "启动隧道失败");

        return DeserializeData<ProxyState>(resp.Data);
    }

    /// <summary>
    /// 停止隧道
    /// </summary>
    public async Task<bool> StopProxyAsync(int proxyId, CancellationToken ct = default)
    {
        var resp = await SendRequestAsync(IpcRequestType.StopProxy, new Dictionary<string, object>
        {
            ["proxyId"] = proxyId
        }, ct).ConfigureAwait(false);
        return resp.Success;
    }

    /// <summary>
    /// 停止所有隧道
    /// </summary>
    public async Task<int> StopAllAsync(CancellationToken ct = default)
    {
        var resp = await SendRequestAsync(IpcRequestType.StopAll, null, ct).ConfigureAwait(false);
        if (resp.Data?.TryGetValue("count", out var countObj) == true)
        {
            return countObj is JsonElement je ? je.GetInt32() : Convert.ToInt32(countObj);
        }
        return 0;
    }

    /// <summary>
    /// 获取所有隧道状态
    /// </summary>
    public async Task<List<ProxyState>> GetStatusAsync(CancellationToken ct = default)
    {
        var resp = await SendRequestAsync(IpcRequestType.GetStatus, null, ct).ConfigureAwait(false);
        if (resp.Data?.TryGetValue("proxies", out var proxiesObj) == true && proxiesObj is JsonElement je)
        {
            return JsonSerializer.Deserialize(je.GetRawText(),
                       DaemonJsonContext.Default.ListProxyState) ?? [];
        }
        return [];
    }

    /// <summary>
    /// 获取历史日志
    /// </summary>
    public async Task<List<LogLine>> GetLogHistoryAsync(int proxyId, int lines = 200, CancellationToken ct = default)
    {
        var resp = await SendRequestAsync(IpcRequestType.GetLogHistory, new Dictionary<string, object>
        {
            ["proxyId"] = proxyId,
            ["lines"] = lines
        }, ct).ConfigureAwait(false);

        if (resp.Data?.TryGetValue("lines", out var linesObj) == true && linesObj is JsonElement je)
        {
            return JsonSerializer.Deserialize(je.GetRawText(),
                       DaemonJsonContext.Default.ListLogLine) ?? [];
        }
        return [];
    }

    /// <summary>
    /// 订阅隧道实时日志（通过 LogReceived 事件推送）
    /// </summary>
    public async Task SubscribeLogsAsync(int proxyId, CancellationToken ct = default)
    {
        await SendRequestAsync(IpcRequestType.SubscribeLogs, new Dictionary<string, object>
        {
            ["proxyId"] = proxyId
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 取消订阅隧道日志
    /// </summary>
    public async Task UnsubscribeLogsAsync(int proxyId, CancellationToken ct = default)
    {
        await SendRequestAsync(IpcRequestType.UnsubscribeLogs, new Dictionary<string, object>
        {
            ["proxyId"] = proxyId
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 检查指定隧道是否正在运行
    /// </summary>
    public async Task<bool> IsRunningAsync(int proxyId, CancellationToken ct = default)
    {
        var proxies = await GetStatusAsync(ct).ConfigureAwait(false);
        return proxies.Any(p => p.ProxyId == proxyId && p.Running);
    }

    /// <summary>
    /// Ping daemon
    /// </summary>
    public async Task<bool> PingAsync(CancellationToken ct = default)
    {
        try
        {
            var resp = await SendRequestAsync(IpcRequestType.Ping, null, ct).ConfigureAwait(false);
            return resp.Success;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 关闭 daemon 进程
    /// </summary>
    public async Task ShutdownDaemonAsync(CancellationToken ct = default)
    {
        try
        {
            await SendRequestAsync(IpcRequestType.Shutdown, null, ct).ConfigureAwait(false);
        }
        catch
        {
            // daemon 可能在收到 shutdown 后立即关闭连接
        }
    }

    // ─── Private ───────────────────────────────────────────────

    private async Task<bool> TryConnectExistingAsync(CancellationToken ct)
    {
        var socketPath = DaemonConstants.GetSocketPath();
        if (!File.Exists(socketPath)) return false;

        try
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            var endpoint = new UnixDomainSocketEndPoint(socketPath);

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(2000);
            await socket.ConnectAsync(endpoint, connectCts.Token).ConfigureAwait(false);

            _socket = socket;
            _stream = new NetworkStream(socket, ownsSocket: false);
            return true;
        }
        catch
        {
            CleanupConnection();
            return false;
        }
    }

    private static void StartDaemonProcess(string? daemonPath)
    {
        var path = daemonPath ?? FindDaemonExecutable();
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            throw new FileNotFoundException($"找不到 daemon 可执行文件: {path ?? "(null)"}");

        // 确保 Unix 上有执行权限
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "/bin/chmod",
                    Arguments = $"+x \"{path}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                })?.WaitForExit(3000);
            }
            catch { }
        }

        var psi = new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = false,
            RedirectStandardError = false,
        };

        // daemon 作为独立后台进程，不跟随父进程退出
        var proc = Process.Start(psi);
        if (proc == null)
            throw new InvalidOperationException("无法启动 daemon 进程");
    }

    private static string? FindDaemonExecutable()
    {
        // 1. 和当前程序同目录
        var baseDir = AppContext.BaseDirectory;
        var candidate = Path.Combine(baseDir, DaemonConstants.DaemonExecutableName);
        if (File.Exists(candidate)) return candidate;

        // 2. PATH 中查找
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        var separator = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':';
        foreach (var dir in pathEnv.Split(separator, StringSplitOptions.RemoveEmptyEntries))
        {
            candidate = Path.Combine(dir, DaemonConstants.DaemonExecutableName);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    private async Task<IpcResponse> SendRequestAsync(
        string type, Dictionary<string, object>? data, CancellationToken ct)
    {
        if (_stream == null)
            throw new InvalidOperationException("未连接到 daemon");

        var requestId = Guid.NewGuid().ToString("N")[..12];
        var request = new IpcRequest { Id = requestId, Type = type, Data = data };

        var tcs = new TaskCompletionSource<IpcResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_pendingRequests)
        {
            _pendingRequests[requestId] = tcs;
        }

        try
        {
            await _sendSemaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await IpcProtocol.WriteFrameAsync(
                    _stream, IpcProtocol.MessageKind.Request, request,
                    DaemonJsonContext.Default.IpcRequest, ct).ConfigureAwait(false);
            }
            finally
            {
                _sendSemaphore.Release();
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(30_000); // 30s 超时

            await using (timeoutCts.Token.Register(() => tcs.TrySetCanceled()))
            {
                return await tcs.Task.ConfigureAwait(false);
            }
        }
        catch
        {
            lock (_pendingRequests)
            {
                _pendingRequests.Remove(requestId);
            }
            throw;
        }
    }

    private async Task EventLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _stream != null)
            {
                var frame = await IpcProtocol.ReadFrameAsync(_stream, ct).ConfigureAwait(false);
                if (frame == null) break; // stream closed

                var (kind, payload) = frame.Value;
                switch (kind)
                {
                    case IpcProtocol.MessageKind.Response:
                        HandleResponse(payload);
                        break;
                    case IpcProtocol.MessageKind.Event:
                        HandleEvent(payload);
                        break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { } // daemon closed connection
        catch (SocketException) { }
    }

    private void HandleResponse(byte[] payload)
    {
        try
        {
            var resp = IpcProtocol.Deserialize(payload, DaemonJsonContext.Default.IpcResponse);
            TaskCompletionSource<IpcResponse>? tcs;
            lock (_pendingRequests)
            {
                _pendingRequests.Remove(resp.Id, out tcs);
            }
            tcs?.TrySetResult(resp);
        }
        catch { }
    }

    private void HandleEvent(byte[] payload)
    {
        try
        {
            var evt = IpcProtocol.Deserialize(payload, DaemonJsonContext.Default.IpcEvent);
            switch (evt.Type)
            {
                case IpcEventType.ProxyExited when evt.Data?.TryGetValue("proxyId", out var pidObj) == true:
                    var proxyId = pidObj is JsonElement je ? je.GetInt32() : Convert.ToInt32(pidObj);
                    ProxyExited?.Invoke(proxyId);
                    break;
                case IpcEventType.LogLine when evt.Data != null:
                    if (evt.Data.TryGetValue("logLine", out var logObj) && logObj is JsonElement logJe)
                    {
                        var line = JsonSerializer.Deserialize(logJe.GetRawText(),
                            DaemonJsonContext.Default.LogLine);
                        if (line != null) LogReceived?.Invoke(line);
                    }
                    break;
            }
        }
        catch { }
    }

    private static T DeserializeData<T>(Dictionary<string, object>? data) where T : class, new()
    {
        if (data == null) return new T();
        var json = JsonSerializer.Serialize(data, DaemonJsonContext.Default.DictionaryStringObject);
        var result = JsonSerializer.Deserialize(json, typeof(T), DaemonJsonContext.Default) as T;
        return result ?? new T();
    }

    private void CleanupConnection()
    {
        try { _stream?.Dispose(); } catch { }
        try { _socket?.Dispose(); } catch { }
        _stream = null;
        _socket = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _eventCts?.Cancel();
        try { _eventLoop?.Wait(2000); } catch { }
        _eventCts?.Dispose();
        CleanupConnection();
        _sendSemaphore.Dispose();

        // 取消所有挂起请求
        lock (_pendingRequests)
        {
            foreach (var tcs in _pendingRequests.Values)
                tcs.TrySetCanceled();
            _pendingRequests.Clear();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _eventCts?.Cancel();
        if (_eventLoop != null)
        {
            try { await _eventLoop.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
        }
        _eventCts?.Dispose();
        CleanupConnection();
        _sendSemaphore.Dispose();

        lock (_pendingRequests)
        {
            foreach (var tcs in _pendingRequests.Values)
                tcs.TrySetCanceled();
            _pendingRequests.Clear();
        }
    }
}
