using System;
using Avalonia;
using Avalonia.Styling;
using Kairo.Utils.Configuration;

namespace Kairo.Utils
{
    internal static class ThemeManager
    {
        public static void Apply(bool followSystem, bool darkIfNotFollow)
        {
            var app = Application.Current;
            if (app == null) return;
            if (followSystem)
            {
                // null => follow system (Avalonia picks system theme when not explicitly set)
                app.RequestedThemeVariant = null;
            }
            else
            {
                app.RequestedThemeVariant = darkIfNotFollow ? ThemeVariant.Dark : ThemeVariant.Light;
            }
            Global.isDarkThemeEnabled = app.ActualThemeVariant == ThemeVariant.Dark;
            ConfigManager.Save();
        }
    }
}
