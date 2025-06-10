using Kairo.Properties;
using Newtonsoft.Json;
using Kairo.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Net.Http;
using System.Runtime.CompilerServices;
using Downloader;
using System.ComponentModel;
using System.Net;
using System.Security.Policy;
using System.Windows.Shapes;
using Path = System.IO.Path;
using System.Windows.Forms;

namespace Kairo.Utils
{
    internal class Update
    {
        private static DownloadService DownloadService;

        public Update() {
            InitDownloader();
            Access.Updater = this;
        }
        public static void Init()
        {
            
            Task.Run(() =>
            {
                new Update();
            });
            Task.Run(CheckVersion);

        }
        public static void CheckVersion()
        {
            using (var hc = new HttpClient()) {
                try
                {
                    var json = JsonConvert.DeserializeObject<List<UpdateInfo>>(hc.GetAsync($"{Global.UpdateCheckerAPI}").Await().Content.ReadAsStringAsync().Await());
                    UpdateInfo? updateInfo = null;
                    foreach (var ui in json)
                    {
                        if (ui.ImportantLevel == 3 && Global.Branch.Equals("Release"))
                        {
                            updateInfo = ui;
                            break;
                        }
                        if (ui.Channel == Global.Branch)
                        {
                            updateInfo = ui;
                            break;
                        }
                        

                    }
                    if (updateInfo != null) {
                        string[] s1 = updateInfo.Version.Split(".");
                        string[] s2 = Global.Version.Split(".");
                        int major1 = int.Parse(s1[0]);
                        int minor1 = int.Parse(s1[1]);
                        int patch1 = int.Parse(s1[2]);

                        int major2 = int.Parse(s2[0]);
                        int minor2 = int.Parse(s2[1]);
                        int patch2 = int.Parse(s2[2]);

                        if (major1 > major2 ||
                            (major1 == major2 && minor1 > minor2) ||
                            (major1 == major2 && minor1 == minor2 && patch1 > patch2) ||
                            (major1 == major2 && minor1 == minor2 && patch1 == patch2 && updateInfo.Subversion > Global.Revision))
                        {
                            ShowUpdateWindow(updateInfo);
                        }

                    }


                }
                catch (Exception ex) { 
                    Logger.Output(LogType.Error, ex);
                }
            }
            
        }
        public static void ShowUpdateWindow(UpdateInfo updateInfo)
        {
            
            App.Current.Dispatcher.Invoke(new Action(() => {
                var update = new Kairo.Components.Update();
                update.RefreshData(updateInfo);
                update.Show();
            }));
            

        }
        public static async Task<IDownloadService> DownloadUpdate(UpdateInfo updateInfo)
        {
            Logger.Output(LogType.DetailDebug, updateInfo);
            try
            {
                await DownloadService.DownloadFileTaskAsync($"{Global.GithubMirror}https://github.com/Shiroiame-Kusu/Kairo/releases/download/v{updateInfo.Version}-{updateInfo.Channel}.{updateInfo.Subversion}/Kairo.exe", Path.Combine("Kairo","update.temp"));
            }catch(Exception e)
            {
                Logger.Output(LogType.Error, e);
                Access.Update.Close();
            }
            return DownloadService;
        }
        public static void StartUpdater()
        {
            if (!File.Exists(Path.Combine(Global.PATH, "Kairo", "Updater.exe")));
            {
                using FileStream fileStream = new(Path.Combine(Global.PATH, "Kairo", "Updater.exe"), FileMode.Create);
                fileStream.Write(Resources.Updater, 0, Resources.Updater.Length);

            }
            Process.Start(new ProcessStartInfo(Path.Combine(Global.PATH, "Kairo", "Updater.exe"))
            {
                WorkingDirectory = Path.Combine(Global.PATH,"Kairo"),
                Arguments = $"{Process.GetCurrentProcess().Id}",
                UseShellExecute = true,
            });
            Environment.Exit(0);
        }
        private void InitDownloader()
        {
            var DownloadOption = new DownloadConfiguration()
            {
                BufferBlockSize = 10240,
                // file parts to download, the default value is 1
                ChunkCount = 16,
                // download speed limited to 2MB/s, default values is zero or unlimited
                // the maximum number of times to fail
                MaxTryAgainOnFailover = 3,
                // release memory buffer after each 50 MB
                MaximumMemoryBufferBytes = 1024 * 1024 * 50,
                // download parts of the file as parallel or not. The default value is false
                ParallelDownload = true,
                // number of parallel downloads. The default value is the same as the chunk count
                ParallelCount = 16,
                // timeout (millisecond) per stream block reader, default values is 1000
                Timeout = 3000,
                // clear package chunks data when download completed with failure, default value is false
                ClearPackageOnCompletionWithFailure = true,
                ReserveStorageSpaceBeforeStartingDownload = true,
                RequestConfiguration = {
                    Accept = "*/*",
                    //CookieContainer = cookies,
                    Headers = new WebHeaderCollection()
                    {

                    }, // { your custom headers }
                    KeepAlive = true, // default value is false
                    ProtocolVersion = HttpVersion.Version11, // default value is HTTP 1.1
                    UseDefaultCredentials = false,
                    // your custom user agent or your_app_name/app_version.
                    UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36 Edg/124.0.0.0",
                }
            };
            DownloadService = new DownloadService(DownloadOption);

            // Provide any information about chunker downloads, 
            // like progress percentage per chunk, speed, 
            // total received bytes and received bytes array to live streaming.
            //DownloadService.ChunkDownloadProgressChanged += OnChunkDownloadProgressChanged;

            // Provide any information about download progress, 
            // like progress percentage of sum of chunks, total speed, 
            // average speed, total received bytes and received bytes array 
            // to live streaming.


            // Download completed event that can include occurred errors or 
            // cancelled or download completed successfully.

            DownloadService.DownloadStarted += OnDownloadStarted;
            DownloadService.DownloadProgressChanged += OnDownloadProgressChanged;
            DownloadService.DownloadFileCompleted += OnDownloadFileCompleted;
        }
        private void OnDownloadStarted(object? sender, DownloadStartedEventArgs e)
        {


        }

        private void OnDownloadProgressChanged(object? sender, Downloader.DownloadProgressChangedEventArgs e)
        {

        }

        private void OnDownloadFileCompleted(object? sender, AsyncCompletedEventArgs e)
        {
            App.Current.Dispatcher.BeginInvoke(() =>
            {
                Access.Update.Close();
            });
            if (e.Cancelled || e.Error != null)
            {
                if(Access.DashBoard == null)
                {
                    Logger.MsgBox("更新失败", "错误", 0, 48, 0);
                }
                else
                {
                    Logger.MsgBox("更新失败", "错误", 0, 48, 1);
                }
            }
            StartUpdater();
        }
    }
    public class UpdateInfo
    {
        [JsonProperty("version")]
        public string? Version;
        [JsonProperty("versionCode")]
        public string? VersionCode;
        [JsonProperty("channel")]
        public string? Channel;
        [JsonProperty("subversion")]
        public int? Subversion;
        [JsonProperty("updatedWhat")]
        public List<string> UpdatedWhat = [];
        [JsonProperty("importantLevel")]
        public int ImportantLevel;
    }
}
