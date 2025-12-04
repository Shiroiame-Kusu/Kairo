using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Kairo.Utils;
using System.Security.Cryptography;
using System.Text;
using System.Numerics;
using FluentAvalonia.UI.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ExtendedNumerics; // added for DispatcherTimer
using Kairo.Components.DashBoard; // added for new namespace
using Kairo.Utils.Logger; // added for HTTP logging
using Kairo.Utils.Serialization;

namespace Kairo;

public partial class MainWindow : Window
{
    private bool _isLoggingIn;
    private static bool _isLoggedIn;
    private UserInfo? _userInfo;

    public static string? Avatar { get; private set; }
    public static int Inbound { get; private set; }
    public static int Outbound { get; private set; }
    public static BigDecimal Traffic { get; private set; }
    public static bool IsLoggedIn => _isLoggedIn;

    private TrayIcon? _trayIcon;
    private NativeMenuItem? _showHideMenuItem;
    private NativeMenuItem? _exitMenuItem;
    private DispatcherTimer? _snackbarTimer; // auto-dismiss timer
    private DispatcherTimer? _loginTimeoutTimer;
    private static readonly TimeSpan LoginTimeout = TimeSpan.FromSeconds(30);

    public MainWindow()
    {
        InitializeComponent();
        Access.MainWindow = this;
        // OAuthCallbackHandler.Init moved to App.OnFrameworkInitializationCompleted
        SetupTrayIcon();
        Init();
        this.Closed += (_, _) => DisposeTrayIcon();
    }

    private void Init()
    {
        // Random tip
        if (Global.Tips.Count > 0)
            Tips.Text = Global.Tips[Random.Shared.Next(0, Global.Tips.Count)];

        ShowLoginForm(false); // start hidden until first layout then fade in
        Opened += async (_, _) =>
        {
            ShowLoginForm(true);
            await TryAutoLogin();
        };
    }

    private async Task TryAutoLogin()
    {
        if (!string.IsNullOrWhiteSpace(Global.Config.RefreshToken))
        {
            await Login(Global.Config.RefreshToken);
        }
        else
        {
            ToggleLoggingIn(false);
        }
    }

    private void ToggleLoggingIn(bool loggingIn)
    {
        if (LoginForm == null || LoginStatusPanel == null) return;
        if (loggingIn)
        {
            LoginStatusPanel.IsVisible = true;
            LoginStatusPanel.Opacity = 1;
            LoginForm.IsVisible = false;
            LoginForm.Opacity = 0;
        }
        else
        {
            LoginStatusPanel.IsVisible = false;
            LoginStatusPanel.Opacity = 0;
            LoginForm.IsVisible = true;
            LoginForm.Opacity = 1;
        }
    }

    private void ShowLoginForm(bool visible)
    {
        if (LoginForm == null) return;
        LoginForm.IsVisible = visible;
        LoginForm.Opacity = visible ? 1 : 0;
    }

    private async Task<bool> Login(string refreshToken)
    {
        if (_isLoggingIn || _isLoggedIn) return false;
        _isLoggingIn = true;
        ToggleLoggingIn(true);
        try
        {
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                ToggleLoggingIn(false);
                return false;
            }
            using HttpClient http = new();
            http.DefaultRequestHeaders.Add("User-Agent", $"Kairo-{Global.Version}");
            var accessUrl = $"{Global.APIList.GetAccessToken}?app_id={Global.APPID}&refresh_token={refreshToken}";
            var response = await http.PostAsyncLogged(accessUrl, null);
            var accessBody = await response.Content.ReadAsStringAsync();
            var json = JsonNode.Parse(accessBody);
            var accessStatus = json?["status"]?.GetValue<int>() ?? 0;
            if (accessStatus != 200)
            {
                var message = json?["message"]?.GetValue<string>() ?? "未知错误";
                OpenSnackbar("登录失败", $"API状态: {accessStatus} {message}", InfoBarSeverity.Error);
                Global.Config.RefreshToken = "";
                Global.Config.AccessToken = "";
                Global.Config.ID = 0;
                ToggleLoggingIn(false);
                return false;
            }
            var dataNode = json?["data"];
            Global.Config.ID = dataNode?["user_id"]?.GetValue<int>() ?? 0;
            Global.Config.AccessToken = dataNode?["access_token"]?.GetValue<string>() ?? string.Empty;
            Global.Config.RefreshToken = refreshToken; // persist
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {Global.Config.AccessToken}");
            var userUrl = $"{Global.APIList.GetUserInfo}?user_id={Global.Config.ID}";
            var userResp = await http.GetAsyncLogged(userUrl);
            var userBody = await userResp.Content.ReadAsStringAsync();
            var userJson = JsonNode.Parse(userBody);
            var userNode = userJson?["data"];
            _userInfo = userNode == null
                ? null
                : JsonSerializer.Deserialize(userNode.ToJsonString(), AppJsonContext.Default.UserInfo);
            if (_userInfo == null)
            {
                OpenSnackbar("错误", "解析用户信息失败", InfoBarSeverity.Error);
                ToggleLoggingIn(false);
                return false;
            }

            var frpUrl = $"{Global.APIList.GetFrpToken}?user_id={Global.Config.ID}";
            var frpResp = await http.GetAsyncLogged(frpUrl);
            var frpBody = await frpResp.Content.ReadAsStringAsync();
            var frpJson = JsonNode.Parse(frpBody);
            _userInfo.FrpToken = frpJson?["data"]?["frp_token"]?.GetValue<string>();
            Global.Config.Username = _userInfo.Username;
            Global.Config.FrpToken = _userInfo.FrpToken ?? string.Empty;
            InitializeInfoForDashboard();
            _isLoggedIn = true;
            Kairo.Utils.Configuration.ConfigManager.Save(); // persist new tokens/user info
            OpenSnackbar("登录成功", $"欢迎 {_userInfo.Username}", InfoBarSeverity.Success);
            if (Utils.Access.DashBoard is not DashBoard db)
            {
                db = new DashBoard();
                Utils.Access.DashBoard = db;
            }
            if (!db.IsVisible)
                db.Show();
            this.Hide();
            if (_showHideMenuItem != null) _showHideMenuItem.Header = "隐藏窗口";
            return true;
        }
        catch (Exception ex)
        {
            OpenSnackbar("异常", ex.Message, InfoBarSeverity.Error);
            ToggleLoggingIn(false);
            return false;
        }
        finally
        {
            _isLoggingIn = false;
            StopLoginTimeout();
        }
    }

    public async Task AcceptOAuthRefreshToken(string refreshToken)
    {
        StopLoginTimeout();
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            OpenSnackbar("无效令牌", "提供的刷新令牌为空", InfoBarSeverity.Warning);
            ToggleLoggingIn(false);
            return;
        }
        await Login(refreshToken);
    }

    private void InitializeInfoForDashboard()
    {
        if (_userInfo == null || string.IsNullOrEmpty(_userInfo.Email)) return;
        Console.WriteLine(_userInfo.Email);
        StringBuilder sb = new();
        foreach (byte b in MD5.HashData(Encoding.UTF8.GetBytes(_userInfo.Email.ToLower())))
            sb.Append(b.ToString("x2"));
        Avatar = $"https://cravatar.cn/avatar/{sb}";
        var limit = _userInfo.Limit;
        Inbound = limit?.Inbound ?? _userInfo.Inbound;
        Outbound = limit?.Outbound ?? _userInfo.Outbound;
        Traffic = _userInfo.Traffic;
    }

    public void OpenSnackbar(string title, string? message, InfoBarSeverity severity)
    {
        if (Snackbar == null) return;
        Snackbar.Title = title;
        Snackbar.Message = message ?? string.Empty;
        Snackbar.Severity = severity;
        Snackbar.IsOpen = true;

        _snackbarTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _snackbarTimer.Stop();
        _snackbarTimer.Tick -= SnackbarTimer_Tick;
        _snackbarTimer.Tick += SnackbarTimer_Tick;
        _snackbarTimer.Start();
    }

    private void SnackbarTimer_Tick(object? sender, EventArgs e)
    {
        _snackbarTimer?.Stop();
        if (Snackbar != null)
            Snackbar.IsOpen = false;
    }

    private void StartLoginTimeout()
    {
        _loginTimeoutTimer ??= new DispatcherTimer { Interval = LoginTimeout };
        _loginTimeoutTimer.Stop();
        _loginTimeoutTimer.Tick -= LoginTimeoutTimer_Tick;
        _loginTimeoutTimer.Tick += LoginTimeoutTimer_Tick;
        _loginTimeoutTimer.Start();
    }

    private void StopLoginTimeout()
    {
        if (_loginTimeoutTimer == null) return;
        _loginTimeoutTimer.Stop();
        _loginTimeoutTimer.Tick -= LoginTimeoutTimer_Tick;
    }

    private void LoginTimeoutTimer_Tick(object? sender, EventArgs e)
    {
        StopLoginTimeout();
        ToggleLoggingIn(false);
        OpenSnackbar("登录超时", "OAuth 验证未完成，请重试", InfoBarSeverity.Warning);
    }

    private async void LoginButton_Click(object? sender, RoutedEventArgs e)
    {
        //var url = $"{Global.APIList.GetTheFUCKINGRefreshToken}?client_id={Global.APPID}&scopes=User.Read,User.Read.FrpToken,Node.Read,Tunnel.Read,Tunnel.Write.Create,Tunnel.Write.Delete,Sign.Read,Sign.Action.Sign&redirect_uri={Uri.EscapeDataString($"{Global.Dashboard}/auth/oauth/redirect-localhost?port={Global.OAuthPort}&ssl=false&path=/oauth/callback")}&mode=callback";
        var url = $"{Global.APIList.GetTheFUCKINGRefreshToken}?client_id={Global.APPID}&scopes=User,Node,Tunnel,Sign&redirect_uri={Uri.EscapeDataString($"{Global.Dashboard}/auth/oauth/redirect-localhost?port={Global.OAuthPort}&ssl=false&path=/oauth/callback")}&mode=callback";

        try
        {
            using var proc = new System.Diagnostics.Process();
            proc.StartInfo = new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true };
            proc.Start();
            ToggleLoggingIn(true);
            StartLoginTimeout();
        }
        catch (Exception ex)
        {
            StopLoginTimeout();
            OpenSnackbar("启动浏览器失败", ex.Message, InfoBarSeverity.Error);
            ToggleLoggingIn(false);
        }
    }

    private void HyperlinkButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is HyperlinkButton hb && hb.Tag is string url && !string.IsNullOrWhiteSpace(url))
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                OpenSnackbar("打开链接失败", ex.Message, InfoBarSeverity.Error);
            }
        }
    }

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && LoginButton.IsVisible && LoginButton.IsEnabled)
        {
            LoginButton_Click(LoginButton, new RoutedEventArgs());
        }
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
        catch { }
    }

    private void ShutdownApplication()
    {
        DisposeTrayIcon();
        try
        {
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Shutdown();
        }
        catch { Close(); }
    }

    private void ToggleWindowVisibility()
    {
        if (IsLoggedIn)
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

    // Represents the user info JSON contract
    public class UserInfo
    {
        [JsonPropertyName("qq")] public long QQ { get; set; }
        [JsonPropertyName("qq_social_id")] public string? QQSocialID { get; set; }
        [JsonPropertyName("reg_time")] public string? RegTime { get; set; }
        [JsonPropertyName("id")] public int ID { get; set; }
        [JsonPropertyName("inbound")] public int Inbound { get; set; }
        [JsonPropertyName("outbound")] public int Outbound { get; set; }
        [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
        [JsonPropertyName("traffic")] public BigDecimal Traffic { get; set; }
        [JsonPropertyName("avatar")] public string? Avatar { get; set; }
        [JsonPropertyName("username")] public string Username { get; set; } = string.Empty;
        [JsonPropertyName("status")] public int Status { get; set; }
        [JsonPropertyName("frp_token")] public string? FrpToken { get; set; }
        [JsonPropertyName("limit")] public LimitInfo? Limit { get; set; }
        public string? Token { get; set; }
        
        public class LimitInfo
        {
            [JsonPropertyName("inbound")] public int Inbound { get; set; }
            [JsonPropertyName("outbound")] public int Outbound { get; set; }
            [JsonPropertyName("tunnel")] public int? Tunnel { get; set; }
        }
    }

    public void PrepareForLogin()
    {
        _isLoggedIn = false;
        Show();
        Activate();
        ToggleLoggingIn(false);
        if (_showHideMenuItem != null)
            _showHideMenuItem.Header = IsVisible ? "隐藏窗口" : "显示窗口";
    }

    public void OnLoggedOut()
    {
        _isLoggedIn = false;
        if (_showHideMenuItem != null)
            _showHideMenuItem.Header = IsVisible ? "隐藏窗口" : "显示窗口";
    }

    public static void LogoutCleanup()
    {
        _isLoggedIn = false;
        // 清理静态用户数据
        Avatar = null;
        Inbound = 0;
        Outbound = 0;
        Traffic = 0;
        // 清理 DashBoard 的静态头像
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
