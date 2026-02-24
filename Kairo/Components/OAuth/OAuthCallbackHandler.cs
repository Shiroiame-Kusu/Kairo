using Kairo.Utils;
using Microsoft.AspNetCore.Builder;
using System;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Kairo.Utils.Logger;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using AppLogger = Kairo.Utils.Logger.Logger;

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
                    // Fixed port – kill any process occupying it
                    KillProcessOnPort(Global.OAuthPort);

                    var builder = WebApplication.CreateBuilder();
                    builder.WebHost.UseUrls($"http://127.0.0.1:{Global.OAuthPort}");
                    // Minimal APIs only; avoid MVC which isn't trim/AOT friendly
                    //builder.Services.AddControllers();
                    _application = builder.Build();

                    // Map minimal OAuth callback endpoint
                    _application.MapGet("/oauth/callback", async (HttpContext ctx) =>
                    {
                        var code = ctx.Request.Query["code"].ToString();
                        if (!string.IsNullOrWhiteSpace(code) && Kairo.Utils.Access.MainWindow is Kairo.MainWindow mw)
                        {
                            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () => await mw.AcceptOAuthCode(code));
                        }
                        const string html = "<html><head><title>OAuth Complete</title></head><body><h3>授权完成，可以返回 Kairo 应用。</h3><script>setTimeout(()=>window.close(),1500);</script></body></html>";
                        ctx.Response.ContentType = "text/html; charset=utf-8";
                        await ctx.Response.WriteAsync(html);
                    });

                    // _application.MapControllers();
                    _runTask = _application.RunAsync(); // keep reference
                    
                }
                catch (Exception e)
                {
                    lock (_lock)
                    {
                        _started = false;
                    }
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
                    _application = null;
                    _runTask = null;
                    lock (_lock)
                    {
                        _started = false;
                    }
                }
            }
            catch { }
        }
        public static void Stop()
        {
            // synchronous wrapper used if async not awaited
            try { StopAsync().GetAwaiter().GetResult(); } catch { }
        }
        /// <summary>Kill any process that is listening on the given TCP port.</summary>
        private static void KillProcessOnPort(int port)
        {
            try
            {
                var endpoints = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
                bool occupied = false;
                foreach (var ep in endpoints)
                {
                    if (ep.Port == port) { occupied = true; break; }
                }
                if (!occupied) return;

                AppLogger.Output(LogType.Info, $"端口 {port} 被占用，正在尝试释放...");

                // Use platform-specific commands to find and kill the owning process
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // netstat -ano | findstr :<port>  → last column is PID
                    var psi = new ProcessStartInfo("cmd.exe", $"/c netstat -ano | findstr :{port}")
                    {
                        RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    var output = proc?.StandardOutput.ReadToEnd() ?? "";
                    proc?.WaitForExit();
                    foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 5 && parts[1].Contains($":{port}") && parts[3] == "LISTENING")
                        {
                            if (int.TryParse(parts[4], out int pid) && pid > 0)
                            {
                                try { Process.GetProcessById(pid).Kill(); AppLogger.Output(LogType.Info, $"已终止占用端口 {port} 的进程 PID={pid}"); }
                                catch { /* already exited */ }
                            }
                        }
                    }
                }
                else
                {
                    // Linux / macOS: lsof or ss
                    var psi = new ProcessStartInfo("sh", $"-c \"lsof -ti tcp:{port} 2>/dev/null || ss -tlnp 'sport = :{port}' | awk 'NR>1{{match($0,/pid=([0-9]+)/,a);print a[1]}}'\"")
                    {
                        RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    var output = proc?.StandardOutput.ReadToEnd() ?? "";
                    proc?.WaitForExit();
                    foreach (var pidStr in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (int.TryParse(pidStr.Trim(), out int pid) && pid > 0)
                        {
                            try { Process.GetProcessById(pid).Kill(); AppLogger.Output(LogType.Info, $"已终止占用端口 {port} 的进程 PID={pid}"); }
                            catch { /* already exited */ }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Output(LogType.Warn, $"释放端口 {port} 失败: {ex.Message}");
            }
        }
    }
}