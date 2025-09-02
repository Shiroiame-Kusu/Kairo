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
        // Apply persisted theme
        ThemeManager.Apply(Global.Config.FollowSystemTheme, Global.Config.DarkTheme);
        // Hook UI thread unhandled exceptions (Avalonia dispatcher)
        Dispatcher.UIThread.UnhandledException += OnUiThreadUnhandledException;
        AvaloniaXamlLoader.Load(this);
    }

    private static void OnUiThreadUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CrashInterception.ShowException(e.Exception);
        e.Handled = true; // mimic legacy swallowing behavior
    }

    public override void OnFrameworkInitializationCompleted()
    {   
        // Start OAuth callback server early
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            Access.MainWindow = desktop.MainWindow; // store reference for Logger dialogs
        }

        base.OnFrameworkInitializationCompleted();
    }
}