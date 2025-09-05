using Kairo.Utils;
using Microsoft.AspNetCore.Mvc;
using System;
using Avalonia.Threading;

namespace Kairo.Components.OAuth.Controllers
{
    [ApiController]
    [Route("oauth")]
    public class MainController : Controller
    {   
        [HttpGet("callback")]
        public IActionResult Callback()
        {
            var refreshToken = Request.Query["refresh_token"].ToString();
            if (!string.IsNullOrWhiteSpace(refreshToken) && Access.MainWindow is Kairo.MainWindow mw)
            {
                Dispatcher.UIThread.Post(async () => await mw.AcceptOAuthRefreshToken(refreshToken));
            }
            const string html = "<html><head><title>OAuth Complete</title></head><body><h3>授权完成，可以返回 Kairo 应用。</h3><script>setTimeout(()=>window.close(),1500);</script></body></html>";
            return Content(html, "text/html; charset=utf-8");
        }
    }
}