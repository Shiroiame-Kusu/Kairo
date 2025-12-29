using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Kairo.Components.DashBoard;
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
        SetupPlatformWindowStyle();
        SetupTrayIcon();
        HookViewModel();
        Opened += async (_, _) => await _viewModel.InitializeAsync();
        Closed += (_, _) => DisposeTrayIcon();
    }

    /// <summary>
    /// 根据平台设置窗口样式（边距和阴影）
    /// Windows: 无边距无阴影（避免透明边框问题）
    /// Linux/macOS: 有边距有阴影
    /// </summary>
    private void SetupPlatformWindowStyle()
    {
        var windowBorder = this.FindControl<Avalonia.Controls.Border>("WindowBorder");
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
        _viewModel.LoginFailed += (_, msg) => _viewModel.ShowSnackbar("登录失败", msg, InfoBarSeverity.Error);
    }

    private async void OnLoginSucceeded(object? sender, UserInfo user)
    {
        EnsureDashboard().Show();
        Hide();
        if (_showHideMenuItem != null) _showHideMenuItem.Header = "隐藏窗口";
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
    }

    private DashBoard EnsureDashboard()
    {
        if (Utils.Access.DashBoard is DashBoard db)
        {
            return db;
        }
        db = new DashBoard();
        Utils.Access.DashBoard = db;
        return db;
    }

    public async Task AcceptOAuthRefreshToken(string refreshToken)
    {
        await _viewModel.AcceptOAuthRefreshTokenAsync(refreshToken);
    }

    private void SetupTrayIcon()
    {
        try
        {
            var menu = new NativeMenu();
            _showHideMenuItem = new NativeMenuItem("隐藏窗口");
            _showHideMenuItem.Click += (_, _) => ToggleWindowVisibility();
            _exitMenuItem = new NativeMenuItem("退出");
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
        catch
        {
        }
    }

    private void ShutdownApplication()
    {
        DisposeTrayIcon();
        try
        {
            (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
        catch
        {
            Close();
        }
    }

    private void ToggleWindowVisibility()
    {
        if (SessionState.IsLoggedIn)
        {
            if (Utils.Access.DashBoard is DashBoard db)
            {
                if (db.IsVisible)
                {
                    db.Hide();
                    if (_showHideMenuItem != null) _showHideMenuItem.Header = "显示面板";
                }
                else
                {
                    db.Show();
                    if (_showHideMenuItem != null) _showHideMenuItem.Header = "隐藏面板";
                }
            }
            else
            {
                var dbNew = new DashBoard();
                Utils.Access.DashBoard = dbNew;
                dbNew.Show();
                if (_showHideMenuItem != null) _showHideMenuItem.Header = "隐藏窗口";
            }
        }
        else
        {
            if (IsVisible)
            {
                Hide();
                if (_showHideMenuItem != null) _showHideMenuItem.Header = "显示窗口";
            }
            else
            {
                Show();
                Activate();
                if (_showHideMenuItem != null) _showHideMenuItem.Header = "隐藏窗口";
            }
        }
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
        if (_showHideMenuItem != null)
            _showHideMenuItem.Header = IsVisible ? "隐藏窗口" : "显示窗口";
    }

    public void OnLoggedOut()
    {
        SessionState.IsLoggedIn = false;
        if (_showHideMenuItem != null)
            _showHideMenuItem.Header = IsVisible ? "隐藏窗口" : "显示窗口";
    }

    public static void LogoutCleanup()
    {
        SessionState.Reset();
        Components.DashBoard.DashBoard.Avatar = null;

        if (Access.DashBoard is Window db)
        {
            try { db.Close(); } catch { }
            Access.DashBoard = null;
        }
        if (Access.MainWindow is MainWindow mw)
            mw.OnLoggedOut();
    }
}
