using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Kairo.Components.OAuth;
using Kairo.Core.Logging;
using Kairo.Utils;
using Kairo.Utils.Configuration; // added for Access
using Kairo.Utils.Logger;

namespace Kairo;

public partial class App : Application
{
    public static readonly AttachedProperty<bool> UseGrayscaleTextRenderingProperty = AvaloniaProperty.RegisterAttached<App, Visual, bool>(
        "UseGrayscaleTextRendering",
        false);

    static App()
    {
        UseGrayscaleTextRenderingProperty.Changed.AddClassHandler<Visual>((visual, e) =>
        {
            if (e.NewValue is true)
                ApplyTextRenderingOptions(visual);
        });
    }

    public static bool GetUseGrayscaleTextRendering(Visual visual) => visual.GetValue(UseGrayscaleTextRenderingProperty);

    public static void SetUseGrayscaleTextRendering(Visual visual, bool value) => visual.SetValue(UseGrayscaleTextRenderingProperty, value);

    public override void Initialize()
    {   
        // Skip heavy initialization in design mode
        if (Design.IsDesignMode)
        {
            AvaloniaXamlLoader.Load(this);
            return;
        }
        CrashInterception.Init(); // already hooks AppDomain + TaskScheduler
        // Ensure frpc child processes are killed on ANY exit path
        // (SIGTERM, Environment.Exit, updater, etc.)
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { FrpcProcessManager.StopAll(); }
            catch (Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/App.axaml.cs:30", ex);
            }
        };
        ConfigManager.Init();
        CoreLogger.Sink = (level, message) => Logger.OutputNetwork(level switch
        {
            CoreLogLevel.Error => LogType.Error,
            CoreLogLevel.Warn => LogType.Warn,
            _ => LogType.DetailDebug
        }, message);
        OAuthCallbackHandler.Init();
        AvaloniaXamlLoader.Load(this); // load XAML BEFORE applying theme so XAML doesn't overwrite our choice
        // Apply persisted theme AFTER XAML so user's preference wins over App.axaml RequestedThemeVariant
        ThemeManager.Apply(Global.Config.FollowSystemTheme, Global.Config.DarkTheme, persist: false);
        // Safety: re-apply once on background priority in case system detection finishes slightly later (prevents transient dark)
        Dispatcher.UIThread.Post(() =>
            ThemeManager.Apply(Global.Config.FollowSystemTheme, Global.Config.DarkTheme, persist: false), DispatcherPriority.Background);
        // Hook UI thread unhandled exceptions (Avalonia dispatcher)
        Dispatcher.UIThread.UnhandledException += OnUiThreadUnhandledException;
    }

    private static void OnUiThreadUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CrashInterception.ShowException(e.Exception);
        e.Handled = true; // mimic legacy swallowing behavior
    }

    public override void OnFrameworkInitializationCompleted()
    {   
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            Access.MainWindow = desktop.MainWindow; // store reference for Logger dialogs
            desktop.Exit += async (_, __) =>
            {
                try { await OAuthCallbackHandler.StopAsync(); }
                catch (Exception ex)
                {
                    AppLogger.Exception("Unhandled exception in Kairo/App.axaml.cs:64", ex);
                }
                try { FrpcProcessManager.StopAll(); }
                catch (Exception ex)
                {
                    AppLogger.Exception("Unhandled exception in Kairo/App.axaml.cs:65", ex);
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ApplyTextRenderingOptions(Visual visual)
    {
        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Antialias);
        TextOptions.SetTextHintingMode(visual, TextHintingMode.Strong);
        TextOptions.SetBaselinePixelAlignment(visual, BaselinePixelAlignment.Aligned);
    }
}