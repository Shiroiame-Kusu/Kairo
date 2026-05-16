using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

namespace Kairo.Core.Logging;

public enum CoreLogLevel
{
    DetailDebug,
    Warn
}

public static class CoreLogger
{
    public static Action<CoreLogLevel, string>? Sink { get; set; }

    public static void Output(CoreLogLevel level, params object?[] objects)
    {
        var sink = Sink;
        if (sink == null) return;

        var builder = new StringBuilder();
        foreach (var obj in objects)
        {
            if (obj == null) continue;
            builder.Append(obj);
            builder.Append(' ');
        }
        sink(level, builder.ToString().TrimEnd());
    }
}

public static class HttpClientLoggingExtensions
{
    private const int MaxBodyPreview = 4096;

    public static async Task<HttpResponseMessage> GetAsyncLogged(this HttpClient client, string requestUri, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, requestUri);
        await LogRequestAsync(client, req, null, ct);
        var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        await LogResponseAsync(resp, ct);
        return resp;
    }

    public static async Task<HttpResponseMessage> DeleteAsyncLogged(this HttpClient client, string requestUri, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Delete, requestUri);
        await LogRequestAsync(client, req, null, ct);
        var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        await LogResponseAsync(resp, ct);
        return resp;
    }

    public static async Task<HttpResponseMessage> PostAsyncLogged(this HttpClient client, string requestUri, HttpContent? content, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = content
        };
        await LogRequestAsync(client, req, content, ct);
        var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        await LogResponseAsync(resp, ct);
        return resp;
    }

    public static async Task<HttpResponseMessage> PutAsyncLogged(this HttpClient client, string requestUri, HttpContent? content, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, requestUri)
        {
            Content = content
        };
        await LogRequestAsync(client, req, content, ct);
        var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        await LogResponseAsync(resp, ct);
        return resp;
    }

    public static async Task<HttpResponseMessage> PatchAsyncLogged(this HttpClient client, string requestUri, HttpContent? content, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Patch, requestUri)
        {
            Content = content
        };
        await LogRequestAsync(client, req, content, ct);
        var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        await LogResponseAsync(resp, ct);
        return resp;
    }

    public static async Task<HttpResponseMessage> GetAsyncLogged(this HttpClient client, string requestUri, HttpCompletionOption completionOption, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, requestUri);
        await LogRequestAsync(client, req, null, ct);
        var resp = await client.SendAsync(req, completionOption, ct);
        await LogResponseAsync(resp, ct, skipBody: completionOption == HttpCompletionOption.ResponseHeadersRead);
        return resp;
    }

    public static async Task<HttpResponseMessage> PostAsJsonAsyncLogged<T>(this HttpClient client, string requestUri, T value, JsonTypeInfo<T> jsonTypeInfo, CancellationToken ct = default)
    {
        using var content = JsonContent.Create(value, jsonTypeInfo);
        return await client.PostAsyncLogged(requestUri, content, ct);
    }

    public static async Task<string> GetStringAsyncLogged(this HttpClient client, string requestUri, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, requestUri);
        await LogRequestAsync(client, req, null, ct);
        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        await LogResponseAsync(resp, ct, previewOnly: true);
        await resp.Content.LoadIntoBufferAsync(ct);
        return await resp.Content.ReadAsStringAsync(ct);
    }

    public static async Task<byte[]> GetByteArrayAsyncLogged(this HttpClient client, string requestUri, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, requestUri);
        await LogRequestAsync(client, req, null, ct);
        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        await resp.Content.LoadIntoBufferAsync(ct);
        var bytes = await resp.Content.ReadAsByteArrayAsync(ct) ?? Array.Empty<byte>();
        CoreLogger.Output(CoreLogLevel.DetailDebug, "[HTTP] <=", (int)resp.StatusCode, resp.ReasonPhrase, "; bytes=", bytes.Length);
        return bytes;
    }

    private static async Task LogRequestAsync(HttpClient client, HttpRequestMessage req, HttpContent? content, CancellationToken ct)
    {
        CoreLogger.Output(CoreLogLevel.DetailDebug, "[HTTP] =>", req.Method, req.RequestUri);
        var auth = req.Headers.Authorization?.ToString() ?? GetHeader(req, "Authorization") ?? GetHeader(client, "Authorization");
        if (!string.IsNullOrWhiteSpace(auth))
            CoreLogger.Output(CoreLogLevel.DetailDebug, "Authorization:", MaskAuth(auth));
        var ua = GetHeader(req, "User-Agent") ?? GetHeader(client, "User-Agent");
        if (!string.IsNullOrWhiteSpace(ua))
            CoreLogger.Output(CoreLogLevel.DetailDebug, "User-Agent:", ua);
        if (content == null) return;

        try
        {
            await content.LoadIntoBufferAsync(ct);
            var body = await content.ReadAsStringAsync(ct);
            CoreLogger.Output(CoreLogLevel.DetailDebug, string.IsNullOrEmpty(body)
                ? "Body(len=0)"
                : $"Body(len={body.Length})\n{TrimForPreview(MaskSensitiveBody(body))}");
        }
        catch (Exception ex)
        {
            CoreLogger.Output(CoreLogLevel.Warn, "[HTTP] request body read failed:", ex.Message);
        }
    }

    private static async Task LogResponseAsync(HttpResponseMessage resp, CancellationToken ct, bool previewOnly = false, bool skipBody = false)
    {
        CoreLogger.Output(CoreLogLevel.DetailDebug, "[HTTP] <=", (int)resp.StatusCode, resp.ReasonPhrase);
        string? ctype = resp.Content.Headers.ContentType?.ToString();
        string? clen = resp.Content.Headers.ContentLength?.ToString();
        if (!string.IsNullOrEmpty(ctype) || !string.IsNullOrEmpty(clen))
            CoreLogger.Output(CoreLogLevel.DetailDebug, "Content-Type:", ctype ?? "-", "; Content-Length:", clen ?? "-");
        if (skipBody)
            return;

        try
        {
            await resp.Content.LoadIntoBufferAsync(ct);
            if (IsTextual(ctype))
            {
                var text = await resp.Content.ReadAsStringAsync(ct);
                CoreLogger.Output(CoreLogLevel.DetailDebug, string.IsNullOrEmpty(text)
                    ? "Body(len=0)"
                    : $"Body(len={text.Length})\n{TrimForPreview(MaskSensitiveBody(text))}");
            }
            else if (previewOnly)
            {
                var text = await resp.Content.ReadAsStringAsync(ct);
                CoreLogger.Output(CoreLogLevel.DetailDebug, $"Body(len={text?.Length ?? 0})\n{TrimForPreview(text ?? string.Empty)}");
            }
            else
            {
                var bytes = await resp.Content.ReadAsByteArrayAsync(ct) ?? Array.Empty<byte>();
                CoreLogger.Output(CoreLogLevel.DetailDebug, "Body(bytes)=", bytes.Length);
            }
        }
        catch (Exception ex)
        {
            CoreLogger.Output(CoreLogLevel.Warn, "[HTTP] response body read failed:", ex.Message);
        }
    }

    private static bool IsTextual(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType)) return false;
        var ct = contentType.ToLowerInvariant();
        return ct.Contains("json") || ct.Contains("xml") || ct.Contains("text") || ct.Contains("html") || ct.Contains("javascript") || ct.Contains("+json");
    }

    private static string TrimForPreview(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        return input.Length <= MaxBodyPreview ? input : input[..MaxBodyPreview] + "\n...<truncated>";
    }

    private static string? GetHeader(HttpRequestMessage req, string name)
    {
        if (req.Headers.TryGetValues(name, out var vals)) return vals.FirstOrDefault();
        if (req.Content != null && req.Content.Headers.TryGetValues(name, out var cvals)) return cvals.FirstOrDefault();
        return null;
    }

    private static string? GetHeader(HttpClient client, string name)
    {
        if (client.DefaultRequestHeaders.TryGetValues(name, out var vals)) return vals.FirstOrDefault();
        return null;
    }

    private static string MaskAuth(string auth)
    {
        try
        {
            const int head = 8;
            const int tail = 6;
            var trimmed = auth.Trim();
            if (trimmed.Length <= head + tail) return trimmed;
            return trimmed[..head] + "..." + trimmed[^tail..] + $" (len={trimmed.Length})";
        }
        catch
        {
            return auth;
        }
    }

    private static string MaskSensitiveBody(string body)
    {
        if (string.IsNullOrEmpty(body)) return body;
        var masked = Regex.Replace(body,
            "(?i)(access_token|refresh_token|client_secret|code|code_verifier|token)=([^&\\s]+)",
            match => $"{match.Groups[1].Value}={MaskValue(Uri.UnescapeDataString(match.Groups[2].Value))}");
        masked = Regex.Replace(masked,
            "(?i)\\\"(access_token|refresh_token|client_secret|code|code_verifier|token)\\\"\\s*:\\s*\\\"([^\\\"]*)\\\"",
            match => $"\\\"{match.Groups[1].Value}\\\":\\\"{MaskValue(match.Groups[2].Value)}\\\"");
        return masked;
    }

    private static string MaskValue(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        const int head = 4;
        const int tail = 4;
        return value.Length <= head + tail ? "***" : value[..head] + "..." + value[^tail..] + $" (len={value.Length})";
    }
}
