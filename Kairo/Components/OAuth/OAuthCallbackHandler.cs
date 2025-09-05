using Kairo.Utils;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using Kairo.Utils.Configuration;
using Microsoft.AspNetCore.Hosting;

namespace Kairo.Components.OAuth
{
    class OAuthCallbackHandler
    {
        private static bool _started;
        private static readonly object _lock = new();
        private static WebApplication _application;
        private static Task _runTask; // store host task
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
                        throw new Exception("无可用高位端口, 请检查您的网络情况");
                    Global.OAuthPort = port;
                    Global.Config.OAuthPort = port;
                    ConfigManager.Save();

                    var builder = WebApplication.CreateBuilder();
                    builder.WebHost.UseUrls($"http://127.0.0.1:{Global.OAuthPort}");
                    builder.Services.AddControllers();
                    _application = builder.Build();
                    _application.MapControllers();
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
            catch { }
        }
        public static void Stop()
        {
            // synchronous wrapper used if async not awaited
            try { StopAsync().GetAwaiter().GetResult(); } catch { }
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
