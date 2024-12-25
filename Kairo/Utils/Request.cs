/*using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Kairo.Utils
{
    public static class Request
    {   
        private static HttpClient httpClient = null;
        public static async string HttpRequest(string url, RequestMethods methods)
        {
            if(httpClient == null)
            {
                httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent", $"Kairo-{Global.Version}");
            }

            HttpResponseMessage responseMessage;
            switch (methods) { 
                case RequestMethods.GET:
                    responseMessage = await httpClient.GetAsync(url);
                    break;
                case RequestMethods.POST:
                    // = await httpClient.PostAsync(url, new());
                    break;
            }
        }

    }
    public enum RequestMethods
    {
        GET,POST,DELETE
    }
}
*/
//TODO