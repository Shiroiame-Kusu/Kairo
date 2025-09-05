using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Kairo.Components.OAuth;
using Kairo.Utils;
using Kairo.Utils.Configuration; // added for Access

namespace Kairo;

public partial class App : Application
{
    public override void Initialize()
    {   
        // Skip heavy initialization in design mode
        if (Design.IsDesignMode)
        {
            AvaloniaXamlLoader.Load(this);
            return;
        }
        CrashInterception.Init(); // already hooks AppDomain + TaskScheduler
        ConfigManager.Init();
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
                try { await Components.OAuth.OAuthCallbackHandler.StopAsync(); } catch { }
                try { FrpcProcessManager.StopAll(); } catch { }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}