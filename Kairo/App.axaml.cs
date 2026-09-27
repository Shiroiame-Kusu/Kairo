using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Kairo.Components.OAuth;
using Kairo.Core.Logging;
using Kairo.Localization;
using Kairo.Utils;
using Kairo.Utils.Configuration; // added for Access
using Kairo.Utils.Logger;

namespace Kairo;

public partial class App : Application
{
    /// <summary>
    /// Avalonia 12（SkiaSharp 3）会真正按 RGB 子像素（LCD）抗锯齿绘制文字，在灰度抗锯齿的桌面、深色背景或非 RGB 排列的屏幕上
    /// 会出现彩色毛边（Avalonia 11 实际是灰度）。样式为每个顶层窗口（含弹出层）设置此属性，改用灰度抗锯齿
    /// </summary>
    public static readonly AttachedProperty<bool> UseGrayscaleTextRenderingProperty = AvaloniaProperty.RegisterAttached<App, Visual, bool>(
        "UseGrayscaleTextRendering",
        false);

    // 保持引用，避免信号注册被回收
    private static readonly List<PosixSignalRegistration> ShutdownSignalRegistrations = new();
    private static int _signalShutdownStarted;

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
        // Ensure frpc child processes are killed on normal exit paths (shutdown, Environment.Exit, updater, etc.).
        // ProcessExit is NOT raised for an unhandled SIGTERM; signals are handled in RegisterShutdownSignals.
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { FrpcProcessManager.StopAll(); }
            catch (Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/App.axaml.cs:30", ex);
            }
        };
        ConfigManager.Init();
        LanguageSettings.ApplySaved();
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
                // 先同步结束 frpc：退出时调度器随即关闭，await 之后的代码不一定还会执行
                try { FrpcProcessManager.StopAll(); }
                catch (Exception ex)
                {
                    AppLogger.Exception("Unhandled exception in Kairo/App.axaml.cs:65", ex);
                }
                try { await OAuthCallbackHandler.StopAsync(); }
                catch (Exception ex)
                {
                    AppLogger.Exception("Unhandled exception in Kairo/App.axaml.cs:64", ex);
                }
            };
            RegisterShutdownSignals(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// 收到 SIGTERM（kill、注销会话）或 SIGINT（终端 Ctrl+C）时，按托盘「退出」相同的流程关闭应用，
    /// 确保 frpc 子进程被结束。界面线程无响应时 5 秒后强制退出
    /// </summary>
    private static void RegisterShutdownSignals(IClassicDesktopStyleApplicationLifetime desktop)
    {
        foreach (var signal in new[] { PosixSignal.SIGTERM, PosixSignal.SIGINT })
        {
            try
            {
                ShutdownSignalRegistrations.Add(PosixSignalRegistration.Create(signal, context =>
                {
                    context.Cancel = true;
                    if (Interlocked.Exchange(ref _signalShutdownStarted, 1) == 1) return;

                    var received = context.Signal;
                    AppLogger.Output(LogType.Info, $"收到 {received}，正在退出 Kairo");
                    Dispatcher.UIThread.Post(() => desktop.Shutdown());
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5));
                        Environment.Exit(received == PosixSignal.SIGINT ? 130 : 143);
                    });
                }));
            }
            catch (Exception ex)
            {
                AppLogger.Exception($"无法注册 {signal} 信号处理", ex);
            }
        }
    }

    private static void ApplyTextRenderingOptions(Visual visual)
    {
        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Antialias);
        TextOptions.SetTextHintingMode(visual, TextHintingMode.Strong);
        TextOptions.SetBaselinePixelAlignment(visual, BaselinePixelAlignment.Aligned);
    }
}