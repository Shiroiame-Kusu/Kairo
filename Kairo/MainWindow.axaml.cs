using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Kairo.Components.DashBoard;
using Kairo.Core.Localization;
using Kairo.Models;
using Kairo.Utils;
using Kairo.ViewModels;

namespace Kairo;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;
    private TrayIcon? _trayIcon;
    private NativeMenuItem? _showHideMenuItem;
    private NativeMenuItem? _exitMenuItem;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainWindowViewModel();
        DataContext = _viewModel;
        Access.MainWindow = this;
        ApplyProviderIcon();
        SetupPlatformWindowStyle();
        SetupTrayIcon();
        HookViewModel();
        Opened += async (_, _) => await _viewModel.InitializeAsync();
        Localizer.LanguageChanged += UpdateTrayMenu;
        Closed += (_, _) =>
        {
            Localizer.LanguageChanged -= UpdateTrayMenu;
            DisposeTrayIcon();
        };
    }

    /// <summary>
    /// 根据平台设置窗口样式（边距和阴影）
    /// Windows: 无边距无阴影（避免透明边框问题）
    /// Linux/macOS: 有边距有阴影
    /// </summary>
    private void SetupPlatformWindowStyle()
    {
        var windowBorder = this.FindControl<Border>("WindowBorder");
        if (windowBorder == null) return;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Windows: 不使用边距和阴影，避免透明边框问题
            windowBorder.Margin = new Thickness(0);
            windowBorder.BoxShadow = new BoxShadows();
        }
        else
        {
            // Linux/macOS: 使用边距和阴影
            windowBorder.Margin = new Thickness(8);
            if (Application.Current!.TryFindResource("WindowShadow", out var shadow) && shadow is BoxShadows boxShadows)
            {
                windowBorder.BoxShadow = boxShadows;
            }
        }
    }

    private void HookViewModel()
    {
        _viewModel.LoginSucceeded += OnLoginSucceeded;
        _viewModel.LoginFailed += (_, msg) => _viewModel.ShowSnackbar(L.T("login.failed"), msg, FAInfoBarSeverity.Error);
        _viewModel.ProviderChanged += (_, _) => ApplyProviderIcon();
    }

    private void ApplyProviderIcon()
    {
        try
        {
            Icon = ProviderBranding.LoadIcon(Global.CurrentProvider);
            if (_trayIcon != null)
                _trayIcon.Icon = Icon;
        }
        catch (Exception ex)
        {
            AppLogger.Exception("Unhandled exception in Kairo/MainWindow.axaml.cs:79", ex);
        }
    }

    private async void OnLoginSucceeded(object? sender, UserInfo user)
    {
        EnsureDashboard().Show();
        Hide();
        UpdateTrayMenu();
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    private DashBoard EnsureDashboard()
    {
        if (Access.DashBoard is DashBoard db)
        {
            return db;
        }
        db = new DashBoard();
        Access.DashBoard = db;
        return db;
    }

    public async Task AcceptOAuthRefreshToken(string refreshToken)
    {
        await _viewModel.AcceptOAuthRefreshTokenAsync(refreshToken);
    }

    public async Task AcceptOAuthCode(string code)
    {
        await _viewModel.AcceptOAuthCodeAsync(code);
    }

    private void SetupTrayIcon()
    {
        try
        {
            var menu = new NativeMenu();
            _showHideMenuItem = new NativeMenuItem(L.T("tray.hideWindow"));
            _showHideMenuItem.Click += (_, _) => ToggleWindowVisibility();
            _exitMenuItem = new NativeMenuItem(L.T("tray.exit"));
            _exitMenuItem.Click += (_, _) => ShutdownApplication();
            menu.Items.Add(_showHideMenuItem);
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(_exitMenuItem);
            _trayIcon = new TrayIcon
            {
                Icon = this.Icon,
                ToolTipText = "Kairo",
                IsVisible = true,
                Menu = menu
            };
            _trayIcon.Clicked += (_, _) => ToggleWindowVisibility();
        }
        catch (Exception ex)
        {
            AppLogger.Exception("Unhandled exception in Kairo/MainWindow.axaml.cs:134", ex);
        }
    }

    private void ShutdownApplication()
    {
        DisposeTrayIcon();
        try
        {
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
        catch (Exception ex)
        {
            AppLogger.Exception("Unhandled exception in Kairo/MainWindow.axaml.cs:146", ex);
            Close();
        }
    }

    private void ToggleWindowVisibility()
    {
        if (SessionState.IsLoggedIn)
        {
            if (Access.DashBoard is DashBoard db)
            {
                if (db.IsVisible)
                    db.Hide();
                else
                    db.Show();
            }
            else
            {
                var dbNew = new DashBoard();
                Access.DashBoard = dbNew;
                dbNew.Show();
            }
        }
        else
        {
            if (IsVisible)
            {
                Hide();
            }
            else
            {
                Show();
                Activate();
            }
        }
        UpdateTrayMenu();
    }

    /// <summary>托盘菜单文字随窗口状态和界面语言更新</summary>
    private void UpdateTrayMenu()
    {
        if (_showHideMenuItem != null)
        {
            _showHideMenuItem.Header = SessionState.IsLoggedIn
                ? L.T(Access.DashBoard is { IsVisible: true } ? "tray.hideDashboard" : "tray.showDashboard")
                : L.T(IsVisible ? "tray.hideWindow" : "tray.showWindow");
        }
        if (_exitMenuItem != null)
            _exitMenuItem.Header = L.T("tray.exit");
    }

    private void DisposeTrayIcon()
    {
        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }

    public void PrepareForLogin()
    {
        _viewModel.ResetSession();
        SessionState.Reset();
        Show();
        Activate();
        UpdateTrayMenu();
    }

    /// <summary>
    /// 从面板切换服务商：关闭面板回到登录页，目标服务商已保存登录状态时自动登录
    /// </summary>
    public async Task SwitchProviderAsync(string providerId)
    {
        LogoutCleanup();
        PrepareForLogin();
        await _viewModel.SwitchProviderAsync(providerId);
    }

    public void OnLoggedOut()
    {
        SessionState.IsLoggedIn = false;
        UpdateTrayMenu();
    }

    public static void LogoutCleanup()
    {
        SessionState.Reset();
        DashBoard.Avatar = null;

        if (Access.DashBoard is Window db)
        {
            try { db.Close(); }
            catch (Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/MainWindow.axaml.cs:227", ex);
            }
            Access.DashBoard = null;
        }
        if (Access.MainWindow is MainWindow mw)
            mw.OnLoggedOut();
    }
}
