using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Kairo.Utils.Logger;
using AppLogger = Kairo.Utils.Logger.Logger; // alias added

namespace Kairo.Utils.Configuration
{
    internal class ConfigManager : IDisposable
    {
        private static string _oldSettings = string.Empty;
        // Centralized config directory & file path (per-user, cross-platform)
        private static readonly string ConfigDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kairo");
        private static readonly string SettingsFilePath = Path.Combine(ConfigDirectory, "Settings.json");
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
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath, Encoding.UTF8);
                    Global.Config = JsonConvert.DeserializeObject<Config>(json) ?? new();
                }
            }
            catch (Exception ex)
            {
                // Log the error and fallback to default config
                AppLogger.Output(LogType.Error, "Failed to read config:", ex);
                Global.Config = new();
            }
            WriteConfig();
        }
        private void WriteConfig() {
            try
            {
                // Ensure directory exists (idempotent)
                Directory.CreateDirectory(ConfigDirectory);
                string newSettings = JsonConvert.SerializeObject(Global.Config);
                if (newSettings != _oldSettings)
                {
                    _oldSettings = newSettings;
                    File.WriteAllText(SettingsFilePath, JsonConvert.SerializeObject(Global.Config, Formatting.Indented));
                }
            }
            catch (Exception ex)
            {
                AppLogger.Output(LogType.Error, "Failed to write config:", ex);
            }
        }
        public static void Init()
        {
            try
            {
                if (File.Exists(ConfigDirectory) && !Directory.Exists(ConfigDirectory))
                {
                    // A file exists where we expect our directory; rename it to avoid collision
                    string backupPath = ConfigDirectory + ".bak_" + DateTime.UtcNow.Ticks;
                    File.Move(ConfigDirectory, backupPath);
                }
                Directory.CreateDirectory(ConfigDirectory); // safe if exists

                if (!File.Exists(SettingsFilePath))
                {
                    new ConfigManager(FileMode.CreateNew);
                }
                else
                {
                    new ConfigManager(FileMode.Open);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Output(LogType.Error, "Config initialization failed:", ex);
            }
        }
        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigDirectory);
                string newSettings = JsonConvert.SerializeObject(Global.Config);
                if (newSettings != _oldSettings)
                {
                    _oldSettings = newSettings;
                    File.WriteAllText(SettingsFilePath, JsonConvert.SerializeObject(Global.Config, Formatting.Indented));
                }
            }
            catch (Exception ex)
            {
                AppLogger.Output(LogType.Error, "Failed to persist config:", ex);
            }
        }
    }
}
