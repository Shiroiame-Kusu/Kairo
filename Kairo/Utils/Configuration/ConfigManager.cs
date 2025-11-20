using Newtonsoft.Json;
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kairo.Utils.Logger;
using AppLogger = Kairo.Utils.Logger.Logger; // alias added

namespace Kairo.Utils.Configuration
{
    internal class ConfigManager : IDisposable
    {
        private static string _oldSettings = string.Empty;
        private static readonly object _syncRoot = new();
        private static bool _initialized;

        // Debounced save support
        private static readonly object _timerLock = new();
        private static Timer? _saveTimer;
        private static bool _saveScheduled;

        // Paths (initialized in static ctor to allow env override)
        private static readonly string ConfigDirectory;
        private static readonly string SettingsFilePath;
        private static readonly string TempSettingsFilePath;

        private const string EnvConfigDir = "KAIRO_CONFIG_DIR";

        static ConfigManager()
        {
            string? envDir = Environment.GetEnvironmentVariable(EnvConfigDir);
            if (!string.IsNullOrWhiteSpace(envDir))
            {
                try
                {
                    envDir = Path.GetFullPath(envDir);
                }
                catch
                {
                    envDir = null; // fallback
                }
            }
            ConfigDirectory = envDir ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kairo");
            SettingsFilePath = Path.Combine(ConfigDirectory, "Settings.json");
            TempSettingsFilePath = SettingsFilePath + ".tmp";
        }

        // Raised after a successful write when content actually changed
        internal static event Action<Config>? ConfigChanged;

        void IDisposable.Dispose() { /* no unmanaged resources */ }
        public void Dispose() => GC.SuppressFinalize(this);

        public ConfigManager(FileMode fileMode)
        {
            switch (fileMode)
            {
                case FileMode.Open:
                    LoadInternal();
                    break;
                case FileMode.Create or FileMode.CreateNew or FileMode.OpenOrCreate:
                    SaveInternal(force: true);
                    break;
                default:
                    throw new IOException("Unsupported FileMode for ConfigManager");
            }
        }

        // PUBLIC API ---------------------------------------------------------
        public static void Init()
        {
            lock (_syncRoot)
            {
                if (_initialized) return;
                try
                {
                    EnsureDirectory();
                    if (!File.Exists(SettingsFilePath))
                    {
                        Global.Config ??= new();
                        SaveInternal(force: true);
                    }
                    else
                    {
                        LoadInternal();
                    }
                    _initialized = true;
                }
                catch (Exception ex)
                {
                    AppLogger.Output(LogType.Error, "Config initialization failed:", ex);
                    Global.Config ??= new();
                }
            }
        }

        public static void Save()
        {
            lock (_syncRoot)
            {
                EnsureDirectory();
                SaveInternal();
            }
        }

        // Debounced save (e.g., high-frequency UI changes)
        public static void RequestSave(TimeSpan? delay = null)
        {
            delay ??= TimeSpan.FromMilliseconds(400);
            if (delay.Value <= TimeSpan.Zero)
            {
                Save();
                return;
            }

            lock (_timerLock)
            {
                _saveTimer?.Dispose();
                _saveTimer = new Timer(_ =>
                {
                    try { Save(); } finally { CancelScheduledSave(); }
                }, null, delay.Value, Timeout.InfiniteTimeSpan);
                _saveScheduled = true;
            }
        }

        public static void CancelScheduledSave()
        {
            lock (_timerLock)
            {
                _saveTimer?.Dispose();
                _saveTimer = null;
                _saveScheduled = false;
            }
        }

        public static bool IsSaveScheduled
        {
            get { lock (_timerLock) return _saveScheduled; }
        }

        public static Task SaveAsync(CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                Save();
            }, ct);
        }

        public static void Reload()
        {
            lock (_syncRoot)
            {
                LoadInternal();
            }
        }

        public static Task ReloadAsync(CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                Reload();
            }, ct);
        }

        // Mutate helper (ensures atomic compare + optional save)
        public static bool TryUpdate(Func<Config, bool> mutator, bool save = true, bool debounce = false)
        {
            if (mutator == null) throw new ArgumentNullException(nameof(mutator));
            lock (_syncRoot)
            {
                string before = JsonConvert.SerializeObject(Global.Config, Formatting.None);
                bool reportedChanged = mutator(Global.Config);
                string after = JsonConvert.SerializeObject(Global.Config, Formatting.None);
                bool actualChanged = reportedChanged || !string.Equals(before, after, StringComparison.Ordinal);
                if (actualChanged && save)
                {
                    if (debounce)
                        RequestSave();
                    else
                        SaveInternal();
                }
                return actualChanged;
            }
        }

        public static string Export(bool indented = true)
        {
            lock (_syncRoot)
            {
                return JsonConvert.SerializeObject(Global.Config, indented ? Formatting.Indented : Formatting.None);
            }
        }

        public static bool Import(string json, bool save = true)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                var cfg = JsonConvert.DeserializeObject<Config>(json);
                if (cfg == null) return false;
                if (!Validate(cfg, out var _)) return false;
                lock (_syncRoot)
                {
                    Global.Config = cfg;
                    Global.RefreshRuntimeFlags();
                    if (save) SaveInternal(force: true);
                }
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Output(LogType.Warn, "Failed to import config:", ex);
                return false;
            }
        }

        public static IDisposable Subscribe(Action<Config> handler, bool fireImmediately = false)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            ConfigChanged += handler;
            if (fireImmediately)
            {
                handler(Global.Config);
            }
            return new Unsubscriber(() => ConfigChanged -= handler);
        }

        // INTERNAL CORE ------------------------------------------------------
        private static void LoadInternal()
        {
            EnsureDirectory();
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath, Encoding.UTF8);
                    try
                    {
                        var cfg = JsonConvert.DeserializeObject<Config>(json) ?? new();
                        if (!Validate(cfg, out var _))
                        {
                            BackupCorruptFile();
                            Global.Config = new();
                            SaveInternal(force: true);
                        }
                        else
                        {
                            Global.Config = cfg;
                            Global.RefreshRuntimeFlags();
                            _oldSettings = JsonConvert.SerializeObject(Global.Config);
                        }
                    }
                    catch (JsonException jex)
                    {
                        BackupCorruptFile();
                        AppLogger.Output(LogType.Warn, "Config file corrupt. Replacing with defaults:", jex);
                        Global.Config = new();
                        Global.RefreshRuntimeFlags();
                        SaveInternal(force: true);
                    }
                }
                else
                {
                    Global.Config ??= new();
                    Global.RefreshRuntimeFlags();
                    SaveInternal(force: true);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Output(LogType.Error, "Failed to read config:", ex);
                Global.Config ??= new();
                SaveInternal(force: true);
            }
        }

        private static void SaveInternal(bool force = false)
        {
            try
            {
                string newSettings = JsonConvert.SerializeObject(Global.Config, Formatting.None);
                if (!force && newSettings == _oldSettings)
                {
                    return; // no changes
                }

                // Atomic write: write to temp then move
                File.WriteAllText(TempSettingsFilePath, JsonConvert.SerializeObject(Global.Config, Formatting.Indented), Encoding.UTF8);
#if NET6_0_OR_GREATER
                try
                {
                    File.Move(TempSettingsFilePath, SettingsFilePath, true);
                }
                catch (IOException)
                {
                    // Some FS (e.g. network) might not support overwrite flag reliably
                    if (File.Exists(SettingsFilePath)) File.Delete(SettingsFilePath);
                    File.Move(TempSettingsFilePath, SettingsFilePath);
                }
#else
                if (File.Exists(SettingsFilePath)) File.Delete(SettingsFilePath);
                File.Move(TempSettingsFilePath, SettingsFilePath);
#endif
                _oldSettings = newSettings;
                ConfigChanged?.Invoke(Global.Config);
            }
            catch (Exception ex)
            {
                AppLogger.Output(LogType.Error, "Failed to write config:", ex);
                TryDeleteTemp();
            }
        }

        private static void TryDeleteTemp()
        {
            try { if (File.Exists(TempSettingsFilePath)) File.Delete(TempSettingsFilePath); } catch { /* ignore */ }
        }

        private static void BackupCorruptFile()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string backup = SettingsFilePath + ".corrupt_" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".bak";
                    File.Move(SettingsFilePath, backup);
                }
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
                {
                    string backupPath = ConfigDirectory + ".bak_" + DateTime.UtcNow.Ticks;
                    File.Move(ConfigDirectory, backupPath);
                }
                Directory.CreateDirectory(ConfigDirectory);
            }
            catch (Exception ex)
            {
                AppLogger.Output(LogType.Error, "Failed to ensure config directory:", ex);
                throw;
            }
        }

        private static bool Validate(Config cfg, out string? error)
        {
            if (cfg.OAuthPort <= 0 || cfg.OAuthPort > 65535)
            {
                error = "OAuthPort out of range";
                return false;
            }
            error = null;
            return true;
        }

        private sealed class Unsubscriber : IDisposable
        {
            private Action? _dispose;
            public Unsubscriber(Action dispose) { _dispose = dispose; }
            public void Dispose()
            {
                Interlocked.Exchange(ref _dispose, null)?.Invoke();
            }
        }
    }
}
