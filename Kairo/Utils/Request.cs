using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace Kairo.Utils
{
    public static class Request
    {
        private static readonly HttpClient httpClient = new();

        public static async Task<string?> HttpRequestAsync(string url, RequestMethods method, HttpContent? content = null)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                Logger.Output(LogType.Error, "Request URL is null or empty.");
                return null;
            }
            try
            {
                HttpResponseMessage response = method switch
                {
                    RequestMethods.GET => await httpClient.GetAsync(url),
                    RequestMethods.POST => await httpClient.PostAsync(url, content ?? new StringContent("")),
                    RequestMethods.DELETE => await httpClient.DeleteAsync(url),
                    _ => throw new ArgumentOutOfRangeException(nameof(method), method, null)
                };
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                Logger.Output(LogType.Error, $"HTTP request failed for {url}", ex);
                return null;
            }
        }
    }
    public enum RequestMethods
    {
        GET, POST, DELETE
    }
}
//TODO: Add more methods and headers as needed.
