using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading.Tasks;

namespace Kairo.Components.OAuth
{
    class OAuthCallbackHandler
    {
        public OAuthCallbackHandler() { 
            
        }
        public static void Init()
        {   
            
            Task.Run(() => {
                try
                {
                    if (Global.Config.OAuthPort != null || Global.Config.OAuthPort != 0) {
                        Global.OAuthPort = Global.Config.OAuthPort;
                    }
                }
                catch (Exception _)
                {
                    while (true)
                    {
                        if (IsPortInUse(Global.OAuthPort))
                        {
                            if (Global.OAuthPort > 65535)
                            {
                                if (Global.Config.OAuthPort != null || Global.Config.OAuthPort != 0)
                                {
                                    Global.OAuthPort = Global.Config.OAuthPort;
                                }
                                throw new Exception("无可用高位端口, 请检查您的网络情况");
                                
                            }
                            else
                            {
                                Global.OAuthPort++;
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                }
                string[] a = { $"--urls=http://localhost:{Global.OAuthPort}" };
                Global.Config.OAuthPort = Global.OAuthPort;
                WebApplicationBuilder builder = WebApplication.CreateBuilder(a);
                builder.Services.AddControllers();
                WebApplication app = builder.Build();
                app.UseRouting();
                app.MapControllers();
                app.RunAsync();
            });
        }
        private static bool IsPortInUse(int port)
        {
            IPGlobalProperties ipProperties = IPGlobalProperties.GetIPGlobalProperties();
            IPEndPoint[] tcpEndPoints = ipProperties.GetActiveTcpListeners();

            foreach (var endPoint in tcpEndPoints)
            {
                if (endPoint.Port == port)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
