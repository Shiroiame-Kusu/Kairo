using System;
using Avalonia;
using Avalonia.Styling;
using Kairo.Utils.Configuration;

namespace Kairo.Utils
{
    internal static class ThemeManager
    {
        public static event Action? ThemeChanged;

        private static bool _initialized;

        // persist: whether to write config (avoid redundant disk I/O on transient re-applies)
        public static void Apply(bool followSystem, bool darkIfNotFollow, bool persist = true)
        {
            var app = Application.Current;
            if (app == null) return;

            if (followSystem)
            {
                // Explicitly set Default (OS-follow) variant instead of null to prevent temporary dark fallback before system theme detection finishes.
                app.RequestedThemeVariant = ThemeVariant.Default;
            }
            else
            {
                app.RequestedThemeVariant = darkIfNotFollow ? ThemeVariant.Dark : ThemeVariant.Light;
            }

            void UpdateFlag()
            {
                bool dark = app.ActualThemeVariant == ThemeVariant.Dark;
                if (dark != Global.isDarkThemeEnabled)
                {
                    Global.isDarkThemeEnabled = dark;
                    try { ThemeChanged?.Invoke(); } catch { }
                }
            }

            // For explicit light/dark we can update immediately; for followSystem skip immediate read to avoid transient fallback (often dark) before platform detection completes.
            if (!followSystem)
                UpdateFlag();

            // Always schedule a deferred update; ensures system-determined variant (or final applied) is captured.
            Avalonia.Threading.Dispatcher.UIThread.Post(UpdateFlag, Avalonia.Threading.DispatcherPriority.Background);

            if (!_initialized)
            {
                _initialized = true;
                app.ActualThemeVariantChanged += (_, __) => UpdateFlag();
            }

            if (persist)
                ConfigManager.Save();
        }
    }
}
