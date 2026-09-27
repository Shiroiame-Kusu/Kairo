using Kairo.Utils;
using Microsoft.AspNetCore.Builder;
using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Kairo.Utils.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Kairo.Core.Localization;

namespace Kairo.Components.OAuth
{
    class OAuthCallbackHandler
    {
        private static bool _started;
        private static readonly object _lock = new();
        private static WebApplication? _application;
        private static Task? _runTask; // store host task
        public static void Init()
        {
            lock (_lock)
            {
                if (_started) return; // prevent multiple starts
                _started = true;
            }
            Task.Run(() =>
            {
                try
                {
                    // Determine starting port from config or default
                    int startPort = Global.Config.OAuthPort > 0 ? Global.Config.OAuthPort : 10000;
                    int port = startPort;
                    while (port <= 65535 && IsPortInUse(port))
                        port++;
                    if (port > 65535)
                        throw new Exception(L.T("network.noHighPort"));
                    Global.OAuthPort = port;
                    Global.Config.OAuthPort = port;
                    ConfigManager.Save();

                    var builder = WebApplication.CreateBuilder();
                    builder.WebHost.UseUrls($"http://127.0.0.1:{Global.OAuthPort}");
                    // 默认的 ConsoleLifetime 会拦截 SIGTERM / Ctrl+C 并只停止这个 Web 服务，
                    // 导致 kill 无法关闭 Kairo；进程信号统一交给 App 处理
                    builder.Services.AddSingleton<IHostLifetime>(new EmbeddedHostLifetime());
                    // Minimal APIs only; avoid MVC which isn't trim/AOT friendly
                    //builder.Services.AddControllers();
                    _application = builder.Build();

                    // Map minimal OAuth callback endpoint
                    _application.MapGet("/oauth/callback", async (HttpContext ctx) =>
                    {
                        var refreshToken = ctx.Request.Query["refresh_token"].ToString();
                        var code = ctx.Request.Query["code"].ToString();
                        if (Access.MainWindow is MainWindow mw)
                        {
                            if (!string.IsNullOrWhiteSpace(refreshToken))
                                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () => await mw.AcceptOAuthRefreshToken(refreshToken));
                            else if (!string.IsNullOrWhiteSpace(code))
                                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () => await mw.AcceptOAuthCode(code));
                        }
                        var html = "<html><head><meta charset=\"utf-8\"><title>OAuth Complete</title></head><body><h3>" +
                                   System.Net.WebUtility.HtmlEncode(L.T("oauth.completePage")) +
                                   "</h3><script>setTimeout(()=>window.close(),1500);</script></body></html>";
                        ctx.Response.ContentType = "text/html; charset=utf-8";
                        await ctx.Response.WriteAsync(html);
                    });

                    // _application.MapControllers();
                    _runTask = _application.RunAsync(); // keep reference
                    
                }
                catch (Exception e)
                {
                    CrashInterception.ShowException(e);
                }
            });
        }

        public static async Task StopAsync()
        {
            try
            {
                if (_application != null)
                {
                    await _application.StopAsync();
                    await _application.DisposeAsync();
                }
            }
            catch (Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/Components/OAuth/OAuthCallbackHandler.cs:85", ex);
            }
        }
        public static void Stop()
        {
            // synchronous wrapper used if async not awaited
            try { StopAsync().GetAwaiter().GetResult(); }
            catch (Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/Components/OAuth/OAuthCallbackHandler.cs:90", ex);
            }
        }
        /// <summary>
        /// 内嵌回调服务使用的空生命周期：不注册任何进程信号处理
        /// </summary>
        private sealed class EmbeddedHostLifetime : IHostLifetime
        {
            public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }

        private static bool IsPortInUse(int port)
        {
            IPGlobalProperties ipProperties = IPGlobalProperties.GetIPGlobalProperties();
            IPEndPoint[] tcpEndPoints = ipProperties.GetActiveTcpListeners();
            foreach (var endPoint in tcpEndPoints)
            {
                if (endPoint.Port == port)
                    return true;
            }
            return false;
        }
    }
}