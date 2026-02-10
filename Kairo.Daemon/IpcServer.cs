using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Kairo.Core.Daemon;

namespace Kairo.Daemon;

/// <summary>
/// IPC 服务端 — 通过 Unix Domain Socket 接受客户端连接
/// </summary>
internal sealed class IpcServer : IDisposable
{
    private readonly string _socketPath;
    private Socket? _listenSocket;
    private readonly ConcurrentDictionary<int, ClientSession> _clients = new();
    private int _nextClientId;
    private bool _disposed;

    /// <summary>收到请求的回调</summary>
    public Func<int, IpcRequest, Task<IpcResponse>>? OnRequest { get; set; }

    public IpcServer()
    {
        _socketPath = DaemonConstants.GetSocketPath();
    }

    /// <summary>
    /// 启动监听
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        // 确保运行时目录存在
        var dir = Path.GetDirectoryName(_socketPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // 删除残留的 socket 文件
        if (File.Exists(_socketPath))
            File.Delete(_socketPath);

        _listenSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        _listenSocket.Bind(new UnixDomainSocketEndPoint(_socketPath));
        _listenSocket.Listen(8);

        Console.WriteLine($"[ipc-server] 监听中: {_socketPath}");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var clientSocket = await _listenSocket.AcceptAsync(ct).ConfigureAwait(false);
                var clientId = Interlocked.Increment(ref _nextClientId);
                var session = new ClientSession(clientId, clientSocket, this);
                _clients[clientId] = session;
                _ = session.RunAsync(ct); // fire-and-forget
                Console.WriteLine($"[ipc-server] 客户端 {clientId} 已连接");
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        finally
        {
            Cleanup();
        }
    }

    /// <summary>
    /// 向所有已连接的客户端广播事件
    /// </summary>
    public async Task BroadcastEventAsync(IpcEvent evt)
    {
        foreach (var (_, session) in _clients)
        {
            await session.SendEventAsync(evt).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 向订阅了指定隧道日志的客户端发送日志事件
    /// </summary>
    public async Task SendLogToSubscribersAsync(int proxyId, LogLine line)
    {
        foreach (var (_, session) in _clients)
        {
            if (session.IsSubscribedToLogs(proxyId))
            {
                var evt = new IpcEvent
                {
                    Type = IpcEventType.LogLine,
                    Data = new Dictionary<string, object>
                    {
                        ["logLine"] = line
                    }
                };
                await session.SendEventAsync(evt).ConfigureAwait(false);
            }
        }
    }

    internal void RemoveClient(int clientId)
    {
        _clients.TryRemove(clientId, out _);
        Console.WriteLine($"[ipc-server] 客户端 {clientId} 已断开");
    }

    private void Cleanup()
    {
        foreach (var (_, session) in _clients)
        {
            session.Dispose();
        }
        _clients.Clear();
        _listenSocket?.Dispose();
        try { File.Delete(_socketPath); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Cleanup();
    }
}

/// <summary>
/// 单个客户端连接会话
/// </summary>
internal sealed class ClientSession : IDisposable
{
    private readonly int _clientId;
    private readonly Socket _socket;
    private readonly NetworkStream _stream;
    private readonly IpcServer _server;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly HashSet<int> _logSubscriptions = new();
    private bool _disposed;

    public ClientSession(int clientId, Socket socket, IpcServer server)
    {
        _clientId = clientId;
        _socket = socket;
        _stream = new NetworkStream(socket, ownsSocket: false);
        _server = server;
    }

    public bool IsSubscribedToLogs(int proxyId)
    {
        lock (_logSubscriptions)
            return _logSubscriptions.Contains(proxyId);
    }

    public void SubscribeLogs(int proxyId)
    {
        lock (_logSubscriptions)
            _logSubscriptions.Add(proxyId);
    }

    public void UnsubscribeLogs(int proxyId)
    {
        lock (_logSubscriptions)
            _logSubscriptions.Remove(proxyId);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && !_disposed)
            {
                var frame = await IpcProtocol.ReadFrameAsync(_stream, ct).ConfigureAwait(false);
                if (frame == null) break;

                var (kind, payload) = frame.Value;
                if (kind != IpcProtocol.MessageKind.Request) continue;

                var request = IpcProtocol.Deserialize(payload, DaemonJsonContext.Default.IpcRequest);

                // 处理订阅/取消订阅（在 session 层面处理）
                if (request.Type == IpcRequestType.SubscribeLogs && request.Data?.TryGetValue("proxyId", out var pidObj) == true)
                {
                    var pid = pidObj is JsonElement je ? je.GetInt32() : Convert.ToInt32(pidObj);
                    SubscribeLogs(pid);
                }
                else if (request.Type == IpcRequestType.UnsubscribeLogs && request.Data?.TryGetValue("proxyId", out var upidObj) == true)
                {
                    var upid = upidObj is JsonElement je2 ? je2.GetInt32() : Convert.ToInt32(upidObj);
                    UnsubscribeLogs(upid);
                }

                IpcResponse response;
                if (_server.OnRequest != null)
                {
                    response = await _server.OnRequest(_clientId, request).ConfigureAwait(false);
                }
                else
                {
                    response = new IpcResponse { Id = request.Id, Success = false, Error = "No handler" };
                }

                response.Id = request.Id;
                await SendResponseAsync(response).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        catch (SocketException) { }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ipc-server] 客户端 {_clientId} 错误: {ex.Message}");
        }
        finally
        {
            _server.RemoveClient(_clientId);
            Dispose();
        }
    }

    public async Task SendResponseAsync(IpcResponse response)
    {
        if (_disposed) return;
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await IpcProtocol.WriteFrameAsync(
                _stream, IpcProtocol.MessageKind.Response,
                response, DaemonJsonContext.Default.IpcResponse).ConfigureAwait(false);
        }
        catch { }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task SendEventAsync(IpcEvent evt)
    {
        if (_disposed) return;
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await IpcProtocol.WriteFrameAsync(
                _stream, IpcProtocol.MessageKind.Event,
                evt, DaemonJsonContext.Default.IpcEvent).ConfigureAwait(false);
        }
        catch { }
        finally
        {
            _writeLock.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _stream.Dispose(); } catch { }
        try { _socket.Dispose(); } catch { }
        _writeLock.Dispose();
    }
}
