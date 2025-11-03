using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Kairo.Utils;
using System.Security.Cryptography;
using System.Text;
using System.Numerics;
using Newtonsoft.Json;
using FluentAvalonia.UI.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading; // added for DispatcherTimer
using Kairo.Components.DashBoard; // added for new namespace

namespace Kairo;

public partial class MainWindow : Window
{
    private bool _isLoggingIn;
    private static bool _isLoggedIn;
    private UserInfo? _userInfo;

    public static string? Avatar { get; private set; }
    public static int Inbound { get; private set; }
    public static int Outbound { get; private set; }
    public static BigInteger Traffic { get; private set; }
    public static bool IsLoggedIn => _isLoggedIn;

    private TrayIcon? _trayIcon;
    private NativeMenuItem? _showHideMenuItem;
    private NativeMenuItem? _exitMenuItem;
    private DispatcherTimer? _snackbarTimer; // auto-dismiss timer

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
            var response = await http.PostAsync(accessUrl, null);
            var json = JObject.Parse(await response.Content.ReadAsStringAsync());
            if ((int)json["status"] != 200)
            {
                OpenSnackbar("登录失败", $"API状态: {json["status"]} {json["message"]}", InfoBarSeverity.Error);
                ToggleLoggingIn(false);
                return false;
            }
            Global.Config.ID = (int)json["data"]["user_id"];
            Global.Config.AccessToken = json["data"]["access_token"]!.ToString();
            Global.Config.RefreshToken = refreshToken; // persist
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {Global.Config.AccessToken}");
            Console.WriteLine(Global.Config.AccessToken);
            var userResp = await http.GetAsync($"{Global.APIList.GetUserInfo}?user_id={Global.Config.ID}");
            var userJson = JObject.Parse(await userResp.Content.ReadAsStringAsync());
            _userInfo = JsonConvert.DeserializeObject<UserInfo>(userJson["data"]!.ToString());
            if (_userInfo == null)
            {
                OpenSnackbar("错误", "解析用户信息失败", InfoBarSeverity.Error);
                ToggleLoggingIn(false);
                return false;
            }
            // Frp token
            var frpResp = await http.GetAsync($"{Global.APIList.GetFrpToken}?user_id={Global.Config.ID}");
            var frpJson = JObject.Parse(await frpResp.Content.ReadAsStringAsync());
            _userInfo.FrpToken = frpJson["data"]["frp_token"]?.ToString();
            Global.Config.Username = _userInfo.Username;
            Global.Config.FrpToken = _userInfo.FrpToken ?? string.Empty;
            InitializeInfoForDashboard();
            _isLoggedIn = true;
            Kairo.Utils.Configuration.ConfigManager.Save(); // persist new tokens/user info
            OpenSnackbar("登录成功", $"欢迎 {_userInfo.Username}", InfoBarSeverity.Success);
            // Open dashboard window
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
        }
    }

    public async Task AcceptOAuthRefreshToken(string refreshToken)
    {
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
        if (_userInfo == null) return;
        StringBuilder sb = new();
        foreach (byte b in MD5.HashData(Encoding.UTF8.GetBytes(_userInfo.Email.ToLower())))
            sb.Append(b.ToString("x2"));
        Avatar = $"https://cravatar.cn/avatar/{sb}";
        Inbound = _userInfo.Inbound;
        Outbound = _userInfo.Outbound;
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

    private async void LoginButton_Click(object? sender, RoutedEventArgs e)
    {
        // OAuth authorize (v2). Must URL-encode nested redirect (dashboard relay -> local listener) so its query params aren't parsed as outer ones.
        var nested = $"https://dashboard.locyanfrp.cn/callback/auth/oauth/localhost?port={Global.OAuthPort}&ssl=false&path=/oauth/callback";
        var encoded = Uri.EscapeDataString(nested);
        var url = $"{Global.APIList.GetTheFUCKINGRefreshToken}?app_id={Global.APPID}&scopes=User.Read,User.Read.FrpToken,Node.Read,Tunnel.Read,Tunnel.Write.Create,Tunnel.Write.Delete,Sign.Read,Sign.Action.Sign&redirect_url={encoded}";
        try
        {
            using var proc = new System.Diagnostics.Process();
            proc.StartInfo = new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true };
            proc.Start();
            ToggleLoggingIn(true);
        }
        catch (Exception ex)
        {
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
        [JsonProperty("qq")] public long QQ { get; set; }
        [JsonProperty("qq_social_id")] public string? QQSocialID { get; set; }
        [JsonProperty("reg_time")] public string? RegTime { get; set; }
        [JsonProperty("id")] public int ID { get; set; }
        [JsonProperty("inbound")] public int Inbound { get; set; }
        [JsonProperty("outbound")] public int Outbound { get; set; }
        [JsonProperty("email")] public string Email { get; set; } = string.Empty;
        [JsonProperty("traffic")] public BigInteger Traffic { get; set; }
        [JsonProperty("avatar")] public string? Avatar { get; set; }
        [JsonProperty("username")] public string Username { get; set; } = string.Empty;
        [JsonProperty("status")] public int Status { get; set; }
        [JsonProperty("frp_token")] public string? FrpToken { get; set; }
        public string? Token { get; set; }
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
        if (Utils.Access.DashBoard is Window db)
        {
            try { db.Close(); } catch { }
            Utils.Access.DashBoard = null;
        }
        if (Utils.Access.MainWindow is MainWindow mw)
            mw.OnLoggedOut();
    }
}