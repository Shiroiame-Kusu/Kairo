using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
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
                WebApplicationBuilder builder = WebApplication.CreateBuilder(["--urls=http://localhost:16092"]);
                builder.Services.AddControllers();
                WebApplication app = builder.Build();
                app.UseRouting();
                app.MapControllers();
                app.RunAsync();
            });
        }
    }
}
