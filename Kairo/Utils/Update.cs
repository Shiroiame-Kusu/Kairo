using Kairo.Properties;
using Newtonsoft.Json;
using Kairo.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Net.Http;

namespace Kairo.Utils
{
    internal class Update
    {
        public static void Init()
        {
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
                        if(ui.ImportantLevel == 3)
                        {
                            updateInfo = ui; break;
                        }
                        if(ui.Channel == Global.Branch)
                        {
                            updateInfo = ui;
                            break;
                        }
                        
                    }
                    if (updateInfo != null) {
                        if (updateInfo.Version == Global.Version) {
                            if (updateInfo.Subversion != Global.Revision) {
                                ShowUpdateWindow(updateInfo);
                            }
                        }
                        else
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
            var update = new Kairo.Components.Update();
            update.RefreshData(updateInfo);
            update.Show();

        }
        public static void DownloadUpdate(UpdateInfo updateInfo)
        {

        }

        public static void StartUpdater()
        {
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
    public class UpdateInfo
    {
        [JsonProperty("version")]
        public string? Version;
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
