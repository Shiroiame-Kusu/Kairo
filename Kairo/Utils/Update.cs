using Kairo.Properties;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Kairo.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Windows.Shapes;
using System.Timers;
using JToken = Newtonsoft.Json.Linq.JToken;

namespace Kairo.Utils
{
    internal static class Update
    {
        /// <summary>
        /// 检查更新计时器
        /// </summary>
        //private static readonly Timer _checkTimer = new(200000) { AutoReset = true };

        /// <summary>
        /// 更新初始化
        /// </summary>
        public static void Init()
        {
            Task.Run(CheckVersion);
            AppDomain.CurrentDomain.ProcessExit += (_, _) => StartUpdater();

        }

        /// <summary>
        /// 更新准备
        /// </summary>
        public static bool IsReadyToUpdate { get; private set; }

        /// <summary>
        /// 上一个获取到的版本
        /// </summary>
        public static string? LastVersion { get; private set; }
        public static string? CurrentVersion;
        private static HttpClient _httpClient = new();
        public static async Task<HttpResponseMessage> Get(string url, string? accept = null, string userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/102.0.5005.63 Safari/537.36 Edg/102.0.1245.33")
        {
            if (!string.IsNullOrEmpty(accept))
            {
                _httpClient.DefaultRequestHeaders.Accept.Clear();
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
            }
            _httpClient.DefaultRequestHeaders.AcceptCharset.Clear();
            _httpClient.DefaultRequestHeaders.AcceptCharset.Add(new StringWithQualityHeaderValue("UTF-8"));
            _httpClient.DefaultRequestHeaders.Remove("user-agent");
            _httpClient.DefaultRequestHeaders.Add("user-agent", userAgent);
            HttpResponseMessage response = await _httpClient.GetAsync(url);
            Logger.Output(LogType.DetailDebug, "Response Headers\n", response.Headers.ToString());
            Logger.Output(LogType.DetailDebug, "Content\n", await response.Content.ReadAsStringAsync());
            return response;
        }
        /// <summary>
        /// 检查更新
        /// </summary>
        public static void CheckVersion()
        {
            var InterruptReason = "";
            var isProgramHasNewerVersion = false;
            if (!Global.Branch.Equals("Release") && !Global.Branch.Equals("RC"))
            {
                InterruptReason = "You Are On Preview Branch!!!";
                return;
            }
            
            
            try
            {   

                JObject? jsonObject = JsonConvert.DeserializeObject<JObject>(Net.Get("https://api.github.com/repos/LoCyan-Team/Kairo/releases/latest", "application/vnd.github.v3+json", "Kairo").Await().Content.ReadAsStringAsync().Await());
                if (jsonObject is null)
                {
                    InterruptReason = "无法检测更新, 请检查网络连接";
                    return;
                }

                string? version = jsonObject["tag_name"]?.ToString();
                if (LastVersion != version && !string.IsNullOrEmpty(version))
                {
                    LastVersion = version;
                    CurrentVersion = "v" + Global.Version + "-" + Global.Branch + Global.Revision;
                    if (version != CurrentVersion)
                    {       
                            isProgramHasNewerVersion = true;
                            
                                Console.WriteLine("Downloading...");
                                DownloadNewVersion(jsonObject);
                           
                            
                        
                    }
                }
            }
            catch (Exception e)
            {
                InterruptReason = "请带着崩溃日志和截图去见开发者";
                CrashInterception.ShowException(e);
                Logger.Output(LogType.Error, e.Message);
                Logger.Output(LogType.Debug, e);

                }
            }
        

        /// <summary>
        /// 下载新版本
        /// </summary>
        private static void DownloadNewVersion(JObject jobject)
        {
            if (!Directory.Exists("update"))
            {
                Directory.CreateDirectory("update");
            }
            foreach (string file in Directory.GetFiles("update", "*.*", SearchOption.AllDirectories))
            {
                if (System.IO.Path.GetExtension(file.ToLowerInvariant()) != ".zip")
                {
                    File.Delete(file);
                }
            }
            foreach (JToken asset in jobject["assets"]!)
            {
                string? filename = asset["name"]?.ToString();
                string? url = asset["browser_download_url"]?.ToString();

                if (string.IsNullOrEmpty(filename) ||
                    //!IdentifyFile(filename?.ToLowerInvariant()) ||
                    string.IsNullOrEmpty(url))
                {
                    continue;
                }

                try
                {
                    if (File.Exists($"update/{filename}"))
                    {
                        IsReadyToUpdate = false;
                        Logger.Output(LogType.Debug, "文件已存在，自动跳过下载");
                    }
                    else if (IsReadyToUpdate)
                    {
                        break;
                    }
                    else
                    {
                        IsReadyToUpdate = false;
                        Logger.Output(LogType.Info, url);
                        Logger.Output(LogType.Debug, $"正在从[{url}]下载[{asset["name"]}]");
                        using (Stream stream = Net.Get(url!).Await().Content.ReadAsStreamAsync().Await())
                        using (FileStream fileStream = new($"update/{filename}", FileMode.Create))
                        {
                            byte[] bytes = new byte[stream.Length];
                            _ = stream.Read(bytes, 0, bytes.Length);
                            fileStream.Write(bytes, 0, bytes.Length);
                        }
                        Logger.Output(LogType.Debug, "下载成功");
                    }

                    ZipFile.ExtractToDirectory($"update/{filename}", "update");
                    Logger.Output(LogType.Debug, "解压成功");
                    Console.WriteLine("解压成功");
                    IsReadyToUpdate = true;
                    Logger.Output(LogType.Debug, "新版本已下载完毕\n" + (Environment.OSVersion.Platform == PlatformID.Win32NT ? "重启即可自动更新" : "你可以自行打开“update”文件夹复制替换"));
                    Console.WriteLine("新版本已下载完毕\n" + (Environment.OSVersion.Platform == PlatformID.Win32NT ? "重启即可自动更新" : "你可以自行打开“update”文件夹复制替换"));
                }
                catch (Exception e)
                {
                    Logger.Output(LogType.Error, e.Message);
                    Logger.Output(LogType.Debug, e);
                }
                break;
            }
        }

        /// <summary>
        /// 识别文件
        /// </summary>
        /*private static bool IdentifyFile(string? name)
        {
            if (name?.Contains("wpf") ?? false)
            {
                string netVer = Environment.Version.Major.ToString();
                return
                    !(Environment.OSVersion.Platform == PlatformID.Unix ^ name.Contains("unix")) &&
                    !(netVer == "4" ^ name.Contains("dotnetframework472")) &&
                    !(netVer == "6" ^ name.Contains("dotnet6"));
            }
            return false;
        }*/

        /// <summary>
        /// 启动 Updater.exe
        /// </summary>
        public static void StartUpdater()
        {
            if ( !IsReadyToUpdate || Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                return;
            }
            if (!File.Exists("Updater.exe"))
            {
                using FileStream fileStream = new("Updater.exe", FileMode.Create);
                fileStream.Write(Resources.Updater, 0, Resources.Updater.Length);

            }
            Process.Start(new ProcessStartInfo("Updater.exe")
            {
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                UseShellExecute = true,
            });
        }
    }
    internal static class Net
    {
        /// <summary>
        /// Http客户端
        /// </summary>
        private static HttpClient _httpClient = new();

        /// <summary>
        /// 异步Get
        /// </summary>
        /// <param name="url">链接</param>
        /// <param name="accept">Header - Accept</param>
        /// <param name="userAgent">Header - UserAgent</param>
        /// <returns>正文</returns>
        public static async Task<HttpResponseMessage> Get(string url, string? accept = null, string userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/102.0.5005.63 Safari/537.36 Edg/102.0.1245.33")
        {
            if (!string.IsNullOrEmpty(accept))
            {
                _httpClient.DefaultRequestHeaders.Accept.Clear();
                _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
            }
            _httpClient.DefaultRequestHeaders.AcceptCharset.Clear();
            _httpClient.DefaultRequestHeaders.AcceptCharset.Add(new StringWithQualityHeaderValue("UTF-8"));
            _httpClient.DefaultRequestHeaders.Remove("user-agent");
            _httpClient.DefaultRequestHeaders.Add("user-agent", userAgent);;
            HttpResponseMessage response = await _httpClient.GetAsync(url);
            Logger.Output(LogType.DetailDebug, "Response Headers\n", response.Headers.ToString());
            var content = await response.Content.ReadAsStringAsync();
            //Logger.Output(LogType.DetailDebug, "Content\n", content);
            return response;
        }
    }
}
