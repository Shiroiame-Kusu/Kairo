using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Kairo.Utils.Logger
{
    internal static class HttpClientLoggingExtensions
    {
        private const int MaxBodyPreview = 4096; // chars

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

        public static async Task<string> GetStringAsyncLogged(this HttpClient client, string requestUri, CancellationToken ct = default)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, requestUri);
            await LogRequestAsync(client, req, null, ct);
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            await LogResponseAsync(resp, ct, previewOnly: true);
            // buffer and return
            await resp.Content.LoadIntoBufferAsync();
            return await resp.Content.ReadAsStringAsync(ct);
        }

        public static async Task<byte[]> GetByteArrayAsyncLogged(this HttpClient client, string requestUri, CancellationToken ct = default)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, requestUri);
            await LogRequestAsync(client, req, null, ct);
            using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            await resp.Content.LoadIntoBufferAsync();
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct) ?? Array.Empty<byte>();
            Logger.OutputNetwork(LogType.DetailDebug, "[HTTP] <=", (int)resp.StatusCode, resp.ReasonPhrase, "; bytes=", bytes.Length);
            return bytes;
        }

        private static async Task LogRequestAsync(HttpClient client, HttpRequestMessage req, HttpContent? content, CancellationToken ct)
        {
            Logger.OutputNetwork(LogType.DetailDebug, "[HTTP] =>", req.Method, req.RequestUri);
            // Headers
            var auth = req.Headers.Authorization?.ToString() ?? GetHeader(req, "Authorization") ?? GetHeader(client, "Authorization");
            if (!string.IsNullOrWhiteSpace(auth))
            {
                Logger.OutputNetwork(LogType.DetailDebug, "Authorization:", MaskAuth(auth));
            }
            var ua = GetHeader(req, "User-Agent") ?? GetHeader(client, "User-Agent");
            if (!string.IsNullOrWhiteSpace(ua))
            {
                Logger.OutputNetwork(LogType.DetailDebug, "User-Agent:", ua);
            }
            if (content != null)
            {
                try
                {
                    await content.LoadIntoBufferAsync();
                    var body = await content.ReadAsStringAsync(ct);
                    if (string.IsNullOrEmpty(body))
                        Logger.OutputNetwork(LogType.DetailDebug, "Body(len=0)");
                    else
                        Logger.OutputNetwork(LogType.DetailDebug, "Body(len=", body.Length, ")\n", TrimForPreview(body));
                }
                catch (Exception ex)
                {
                    Logger.OutputNetwork(LogType.Warn, "[HTTP] request body read failed:", ex.Message);
                }
            }
        }

        private static async Task LogResponseAsync(HttpResponseMessage resp, CancellationToken ct, bool previewOnly = false)
        {
            Logger.OutputNetwork(LogType.DetailDebug, "[HTTP] <=", (int)resp.StatusCode, resp.ReasonPhrase);
            string? ctype = resp.Content.Headers.ContentType?.ToString();
            string? clen = resp.Content.Headers.ContentLength?.ToString();
            if (!string.IsNullOrEmpty(ctype) || !string.IsNullOrEmpty(clen))
            {
                Logger.OutputNetwork(LogType.DetailDebug, "Content-Type:", ctype ?? "-", "; Content-Length:", clen ?? "-");
            }
            try
            {
                await resp.Content.LoadIntoBufferAsync();
                if (IsTextual(ctype))
                {
                    var text = await resp.Content.ReadAsStringAsync(ct);
                    if (string.IsNullOrEmpty(text))
                        Logger.OutputNetwork(LogType.DetailDebug, "Body(len=0)");
                    else
                        Logger.OutputNetwork(LogType.DetailDebug, "Body(len=", text.Length, ")\n", TrimForPreview(text));
                }
                else if (previewOnly)
                {
                    // For GetStringAsyncLogged we already know it's textual, but keep path generic
                    var text = await resp.Content.ReadAsStringAsync(ct);
                    Logger.OutputNetwork(LogType.DetailDebug, "Body(len=", text?.Length ?? 0, ")\n", TrimForPreview(text ?? string.Empty));
                }
                else
                {
                    // non-text: don't dump raw bytes, just length
                    var bytes = await resp.Content.ReadAsByteArrayAsync(ct) ?? Array.Empty<byte>();
                    Logger.OutputNetwork(LogType.DetailDebug, "Body(bytes)=", bytes.Length);
                }
            }
            catch (Exception ex)
            {
                Logger.OutputNetwork(LogType.Warn, "[HTTP] response body read failed:", ex.Message);
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
            if (input.Length <= MaxBodyPreview) return input;
            return input.Substring(0, MaxBodyPreview) + "\n...<truncated>";
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
                const int head = 8; const int tail = 6;
                var trimmed = auth.Trim();
                if (trimmed.Length <= head + tail) return trimmed;
                return trimmed.Substring(0, head) + "..." + trimmed.Substring(trimmed.Length - tail) + $" (len={trimmed.Length})";
            }
            catch { return auth; }
        }
    }
}
