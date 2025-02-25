using Kairo.Utils;
using Microsoft.AspNetCore.Mvc;
using System;

namespace Kairo.Components.OAuth.Controllers
{
    [ApiController]
    [Route("oauth")]
    public class MainController : Controller
    {   
        [HttpGet("callback")]
        public IActionResult Callback() {
            Access.MainWindow.Login(Request.Query["refresh_token"]);
            Console.WriteLine(Request.Query["refresh_token"]);
            return Ok();
        }
    }
}
