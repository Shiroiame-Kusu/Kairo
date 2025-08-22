using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kairo.Utils
{
    internal class ConfigManager : IDisposable
    {
        private static string _oldSettings = string.Empty;
        void IDisposable.Dispose() { }
        public void Dispose() {
            GC.SuppressFinalize(this);
        }
        public ConfigManager(FileMode fileMode) {
            switch (fileMode) { 
                case FileMode.Open:
                    ReadConfig();
                    break;
                case FileMode.Create or FileMode.CreateNew or FileMode.OpenOrCreate:
                    WriteConfig(); 
                    break;
                default:
                    throw new IOException();
            }
            
        }
        private void ReadConfig()
        {
            try
            {
                string configPath = Path.Combine("Kairo", "Settings.json");
                if (File.Exists(configPath))
                {
                    string json = File.ReadAllText(configPath, Encoding.UTF8);
                    Global.Config = JsonConvert.DeserializeObject<Config>(json) ?? new();
                }
            }
            catch (Exception ex)
            {
                // Log the error and fallback to default config
                Logger.Output(LogType.Error, "Failed to read config:", ex);
                Global.Config = new();
            }
            WriteConfig();
        }
        private void WriteConfig() {
            try
            {
                string newSettings = JsonConvert.SerializeObject(Global.Config);
                if (newSettings != _oldSettings)
                {
                    if (!Directory.Exists("Kairo"))
                    {
                        Directory.CreateDirectory("Kairo");
                    }
                    _oldSettings = newSettings;
                    File.WriteAllText(Path.Combine("Kairo", "Settings.json"), JsonConvert.SerializeObject(Global.Config, Formatting.Indented));
                }
            }
            catch (Exception ex)
            {
                Logger.Output(LogType.Error, "Failed to write config:", ex);
            }
        }
        public static void Init()
        {
            if (!Directory.Exists("Kairo"))
            {
                Directory.CreateDirectory("Kairo");
                new ConfigManager(FileMode.CreateNew);
                return;
            }
            new ConfigManager(FileMode.Open);

        }
    }
}
