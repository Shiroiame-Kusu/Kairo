using System;
using System.Buffers;
using System.IO;
using System.Text;
using System.Text.Json;
using Kairo.Utils.Logger;
using Kairo.Utils.Serialization;
using AppLogger = Kairo.Utils.Logger.Logger;

namespace Kairo.Utils.Configuration
{
    internal partial class ConfigManager
    {
        private static void LoadInternal()
        {
            EnsureDirectory();
            try
            {
                if (File.Exists(SettingsFilePath))
                    LoadExistingFile();
                else
                    ResetAndSaveDefaults();
            }
            catch (Exception ex)
            {
                AppLogger.Output(LogType.Error, "Failed to read config:", ex);
                Global.Config ??= new();
                SaveInternal(force: true);
            }
        }

        private static void LoadExistingFile()
        {
            var json = File.ReadAllText(SettingsFilePath, Encoding.UTF8);
            try
            {
                var cfg = JsonSerializer.Deserialize(json, AppJsonContext.Default.Config) ?? new();
                if (!Validate(cfg, out _))
                {
                    BackupCorruptFile();
                    ResetAndSaveDefaults();
                    return;
                }

                Global.Config = cfg;
                Global.RefreshRuntimeFlags();
                _oldSettings = SerializeConfig();
            }
            catch (JsonException ex)
            {
                BackupCorruptFile();
                AppLogger.Output(LogType.Warn, "Config file corrupt. Replacing with defaults:", ex);
                ResetAndSaveDefaults();
            }
        }

        private static void ResetAndSaveDefaults()
        {
            Global.Config ??= new();
            Global.RefreshRuntimeFlags();
            SaveInternal(force: true);
        }

        private static void SaveInternal(bool force = false)
        {
            try
            {
                var newSettings = SerializeConfig();
                if (!force && newSettings == _oldSettings)
                    return;

                File.WriteAllText(TempSettingsFilePath, SerializeConfig(indented: true), Encoding.UTF8);
                ReplaceSettingsFile();
                _oldSettings = newSettings;
                ConfigChanged?.Invoke(Global.Config);
            }
            catch (Exception ex)
            {
                AppLogger.Output(LogType.Error, "Failed to write config:", ex);
                TryDeleteTemp();
            }
        }

        private static void ReplaceSettingsFile()
        {
#if NET6_0_OR_GREATER
            try
            {
                File.Move(TempSettingsFilePath, SettingsFilePath, true);
            }
            catch (IOException ex)
            {
                Kairo.Utils.Logger.Logger.Exception("Unhandled exception in Kairo/Utils/Configuration/ConfigManager.Storage.cs:91", ex);
                if (File.Exists(SettingsFilePath)) File.Delete(SettingsFilePath);
                File.Move(TempSettingsFilePath, SettingsFilePath);
            }
#else
            if (File.Exists(SettingsFilePath)) File.Delete(SettingsFilePath);
            File.Move(TempSettingsFilePath, SettingsFilePath);
#endif
        }

        private static void TryDeleteTemp()
        {
            try { if (File.Exists(TempSettingsFilePath)) File.Delete(TempSettingsFilePath); }
            catch (System.Exception ex)
            {
                Kairo.Utils.Logger.Logger.Exception("Unhandled exception in Kairo/Utils/Configuration/ConfigManager.Storage.cs:104", ex);
            }
        }

        private static void BackupCorruptFile()
        {
            try
            {
                if (!File.Exists(SettingsFilePath)) return;
                var backup = SettingsFilePath + ".corrupt_" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".bak";
                File.Move(SettingsFilePath, backup);
            }
            catch (Exception ex)
            {
                AppLogger.Output(LogType.Warn, "Failed to backup corrupt config:", ex);
            }
        }

        private static void EnsureDirectory()
        {
            try
            {
                if (File.Exists(ConfigDirectory) && !Directory.Exists(ConfigDirectory))
                    File.Move(ConfigDirectory, ConfigDirectory + ".bak_" + DateTime.UtcNow.Ticks);
                Directory.CreateDirectory(ConfigDirectory);
            }
            catch (Exception ex)
            {
                AppLogger.Output(LogType.Error, "Failed to ensure config directory:", ex);
                throw;
            }
        }

        private static string SerializeConfig(bool indented = false)
        {
            if (!indented)
                return JsonSerializer.Serialize(Global.Config, AppJsonContext.Default.Config);

            var buffer = new ArrayBufferWriter<byte>();
            using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true });
            JsonSerializer.Serialize(writer, Global.Config, AppJsonContext.Default.Config);
            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }
    }
}
