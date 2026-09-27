using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Kairo.Cli.Utils;

internal sealed record OAuthCallbackResult(string Code, string RefreshToken, string Error);

/// <summary>
/// 在 127.0.0.1 上监听 OAuth 回调（/oauth/callback），浏览器与 CLI 在同一台设备时可自动完成授权。
/// 仅使用 TcpListener 解析请求行，兼容 AOT 发布。
/// </summary>
internal sealed class OAuthLoopbackListener : IDisposable
{
    private const string CallbackPath = "/oauth/callback";
    private readonly TcpListener _listener;

    public int Port { get; }
    public string RedirectUri => $"http://127.0.0.1:{Port}{CallbackPath}";

    private OAuthLoopbackListener(TcpListener listener, int port)
    {
        _listener = listener;
        Port = port;
    }

    /// <summary>从首选端口开始尝试绑定，端口被占用（例如 GUI 正在运行）时顺延</summary>
    public static OAuthLoopbackListener? TryStart(int preferredPort, int attempts = 20)
    {
        var start = preferredPort is > 0 and <= 65535 ? preferredPort : 10000;
        for (var port = start; port < start + attempts && port <= 65535; port++)
        {
            try
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                return new OAuthLoopbackListener(listener, port);
            }
            catch (SocketException ex)
            {
                Logger.Debug($"OAuth 回调端口 {port} 不可用: {ex.SocketErrorCode}");
            }
        }
        return null;
    }

    public async Task<OAuthCallbackResult?> WaitForCallbackAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(ct);
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
            {
                return null;
            }

            using (client)
            {
                var result = await HandleClientAsync(client, ct);
                if (result != null)
                    return result;
            }
        }
        return null;
    }

    private static async Task<OAuthCallbackResult?> HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var stream = client.GetStream();
            var requestLine = await ReadRequestLineAsync(stream, timeout.Token);
            Logger.Debug($"OAuth 回调请求: {requestLine?.Split('?')[0]}");

            // 请求行格式: GET /oauth/callback?code=xxx HTTP/1.1
            var parts = requestLine?.Split(' ');
            var target = parts is { Length: >= 2 } ? parts[1] : string.Empty;
            var queryIndex = target.IndexOf('?');
            var path = queryIndex >= 0 ? target[..queryIndex] : target;
            if (!path.Equals(CallbackPath, StringComparison.OrdinalIgnoreCase))
            {
                await WriteResponseAsync(stream, "404 Not Found", "Not Found", timeout.Token);
                return null;
            }

            var query = ParseQuery(queryIndex >= 0 ? target[(queryIndex + 1)..] : string.Empty);
            var result = new OAuthCallbackResult(
                query.GetValueOrDefault("code", string.Empty),
                query.GetValueOrDefault("refresh_token", string.Empty),
                query.GetValueOrDefault("error_description", query.GetValueOrDefault("error", string.Empty)));

            var success = string.IsNullOrEmpty(result.Error) && (result.Code.Length > 0 || result.RefreshToken.Length > 0);
            await WriteResponseAsync(stream, "200 OK", BuildPage(success, result.Error), timeout.Token);
            return result;
        }
        catch (Exception ex)
        {
            Logger.Debug($"处理 OAuth 回调失败: {ex.Message}");
            return null;
        }
    }

    private static async Task<string?> ReadRequestLineAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), ct);
            if (read == 0) break;
            total += read;
            var text = Encoding.ASCII.GetString(buffer, 0, total);
            var lineEnd = text.IndexOf("\r\n", StringComparison.Ordinal);
            if (lineEnd >= 0) return text[..lineEnd];
        }
        return total > 0 ? Encoding.ASCII.GetString(buffer, 0, total) : null;
    }

    private static async Task WriteResponseAsync(NetworkStream stream, string status, string body, CancellationToken ct)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var header = $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), ct);
        await stream.WriteAsync(bodyBytes, ct);
        await stream.FlushAsync(ct);
    }

    private static string BuildPage(bool success, string error)
    {
        var title = success ? "授权完成" : "授权未完成";
        var message = success
            ? "Kairo CLI 已收到授权，可以关闭此页面并返回终端。"
            : $"授权失败：{WebUtility.HtmlEncode(string.IsNullOrEmpty(error) ? "未收到授权码" : error)}，请返回终端重试。";
        return "<!doctype html><html lang=\"zh-CN\"><head><meta charset=\"utf-8\"><title>Kairo - " + title + "</title>" +
               "<style>body{font-family:system-ui,sans-serif;display:flex;align-items:center;justify-content:center;height:100vh;margin:0;background:#f5f6f8;color:#222}" +
               "main{padding:32px 40px;border-radius:12px;background:#fff;box-shadow:0 8px 30px rgba(0,0,0,.08);text-align:center}" +
               "h1{font-size:20px;margin:0 0 8px}p{margin:0;color:#555}</style></head><body><main><h1>" + title + "</h1><p>" +
               message + "</p></main>" + (success ? "<script>setTimeout(()=>window.close(),1500)</script>" : string.Empty) +
               "</body></html>";
    }

    /// <summary>解析查询字符串（也可用于用户粘贴的完整回调地址）</summary>
    public static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var key = Uri.UnescapeDataString((eq >= 0 ? pair[..eq] : pair).Replace('+', ' '));
            var value = eq >= 0 ? Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' ')) : string.Empty;
            if (key.Length > 0) result[key] = value;
        }
        return result;
    }

    public void Dispose()
    {
        try { _listener.Stop(); }
        catch (Exception ex)
        {
            Logger.Debug($"关闭 OAuth 回调监听失败: {ex.Message}");
        }
    }
}
