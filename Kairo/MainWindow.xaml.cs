using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Net.Http;
using Newtonsoft.Json;
using System.IO;
using System.Diagnostics;
using System.Security;
using Wpf.Ui.Common;
using Wpf.Ui.Controls;
using Wpf.Ui.Appearance;
using System.ComponentModel;
using Microsoft.Win32;
using Kairo.Utils;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Text;
using Kairo.Extensions;
using System.Windows.Media.Animation;
using System.Numerics;

namespace Kairo
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : UiWindow
    {
        public static bool DarkThemeEnabled;
        private UserInfo UserInfo;
        public static bool islogin = false;
        public static DashBoard DashBoard;
        public static string Avatar;
        public static int Inbound;
        public static int Outbound;
        public static BigInteger Traffic;
        private static Storyboard fadeIn;
        private static Storyboard fadeOut;
        public MainWindow()
        {
            Init(App.TokenMode);

            if (App.TokenMode)
            {
                this.Hide();
            }

        }
        private void InitializeAllComponent()
        {
            InitializeComponent();
            _TitleBar.Opacity = 0;
            LoginForm.Opacity = 0;
            LoginStatus.Opacity = 0;
            fadeIn = (Storyboard)FindResource("FadeInStoryboard");
            fadeOut = (Storyboard)FindResource("FadeOutStoryboard");
            fadeIn.Begin(LoginStatus);
            LoginForm.Visibility = Visibility.Collapsed;
        }
        private void Init(bool TokenMode)
        {
            InitializeAllComponent();
            if ( new Random().Next(0, 100) == new Random().Next(0, 100))
            {
                CrashInterception.ShowException(new Exception("这是一个彩蛋，万分之一的机会"));
            }
            Tips.Text = Global.Tips[Random.Shared.Next(0, Global.Tips.Count - 1)];
            //if (!TokenMode) CheckNetworkAvailability();
            _Login.IsEnabled = true;
            DataContext = this;
            Access.MainWindow = this;
            if (!TokenMode) InitializeAutoLogin();
            if (!TokenMode) Update.Init();
            if (!TokenMode) ScheduledTask.Init();
            if (!TokenMode) ProtocolHandler.Init();
        }

        public void OpenSnackbar(string title, string message, SymbolRegular icon)
        {
            Dispatcher.Invoke(() =>
                {

                    Snackbar.Show(title, message, icon);
                });
        }

        private async void InitializeAutoLogin()
        {
            await CheckTokenAvailableAndLogin();
            
        }

        private async Task<bool> CheckTokenAvailableAndLogin()
        {
            if (!string.IsNullOrEmpty(Global.Config.RefreshToken))
            {
                return await Login(Global.Config.RefreshToken);
            }
            else
            {
                VisibilityChange(false);
                return false;
            }
        }

        public async void VisibilityChange(bool logining)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (logining)
                {
                    fadeOut.Begin(LoginForm);
                    fadeOut.Begin(_TitleBar);
                    fadeIn.Begin(LoginStatus);
                    LoginForm.Visibility = Visibility.Hidden;
                    LoginStatus.Visibility = Visibility.Visible;
                }
                else
                {
                    fadeOut.Begin(LoginStatus);
                    fadeIn.Begin(_TitleBar);
                    fadeIn.Begin(LoginForm);
                    LoginForm.Visibility = Visibility.Visible;
                    LoginStatus.Visibility = Visibility.Hidden;

                }
            });
            

        }
        

        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            var url = $"{Global.APIList.GetTheFUCKINGRefreshToken}{Global.Config.ID}&app_id={Global.APPID}&redirect_url=http://localhost:{Global.OAuthPort}/oauth/callback&request_permission_ids=User,Proxy,Sign";
            Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
            VisibilityChange(true);
            e.Handled = true;


        }
        public async Task<bool> Login(string RefreshToken)
        {
            if (islogin) return false;
            Global.Config.RefreshToken = RefreshToken;
            VisibilityChange(true);
            if (!string.IsNullOrEmpty(RefreshToken))
            {
                using (HttpClient httpClient = new())
                {
                    httpClient.DefaultRequestHeaders.Add("User-Agent", $"Kairo-{Global.Version}");
                    Console.WriteLine($"{Global.APIList.GetAccessToken}?app_id={Global.APPID}&refresh_token={Global.Config.RefreshToken}");
                    HttpResponseMessage response = await httpClient.PostAsync($"{Global.APIList.GetAccessToken}?app_id={Global.APPID}&refresh_token={Global.Config.RefreshToken}", null);
                    JObject json = JObject.Parse(await response.Content.ReadAsStringAsync());
                    if (int.Parse(json["status"].ToString()) == 200)
                    {
                        Global.Config.ID = int.Parse(json["data"]["user_id"].ToString());
                        Global.Config.AccessToken = json["data"]["access_token"].ToString();
                        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {Global.Config.AccessToken}");
                        Console.WriteLine($"{Global.APIList.GetUserInfo}?user_id={json["data"]["user_id"].ToString()}");
                        response = httpClient.GetAsync($"{Global.APIList.GetUserInfo}?user_id={json["data"]["user_id"].ToString()}").Await();
                        json = JObject.Parse(response.Content.ReadAsStringAsync().Await());
                        UserInfo = JsonConvert.DeserializeObject<UserInfo>(json["data"].ToString());
                        Logger.MsgBox($"登录成功\n获取到登录Token: {UserInfo.Token}", "提示", 0, 48, 0);
                        InitializeInfoForDashboard();
                        response = await httpClient.GetAsync($"{Global.APIList.GetFrpToken}?user_id={Global.Config.ID}");
                        json = JObject.Parse(response.Content.ReadAsStringAsync().Await());
                        UserInfo.FrpToken = json["data"]["frp_token"].ToString();
                        Global.Config.Username = UserInfo.Username;
                        Global.Config.FrpToken = UserInfo.FrpToken;
                        islogin = true;
                        Dispatcher.BeginInvoke(() =>
                        {

                            DashBoard = new DashBoard();
                            DashBoard.Show();
                            Close();
                            Access.DashBoard.CheckIfFrpcInstalled();
                        });
                        return true;
                    }
                    else
                    {
                        Logger.MsgBox($"请求API的过程中出错 \n 状态: {int.Parse(json["status"].ToString())} {json["message"].ToString()}", "错误", 0, 48, 0);

                    }

                }

            }
            else
            {
                
            }
            VisibilityChange(false);
            return false;



        }
        private void InitializeInfoForDashboard()
        {

            StringBuilder sb = new StringBuilder();
            foreach (byte b in MD5.HashData(Encoding.UTF8.GetBytes(UserInfo.Email.ToLower())))
            {
                sb.Append(b.ToString("x2"));
            }
            Console.WriteLine(sb.ToString());
            Avatar = $"https://cravatar.cn/avatar/{sb.ToString()}";
            Inbound = UserInfo.Inbound;
            Outbound = UserInfo.Outbound;
            Traffic = UserInfo.Traffic;
        }

        private void Register_Navigate(object sender, RequestNavigateEventArgs e)
        {
            var url = e.Uri.ToString();
            Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
            e.Handled = true;
        }
        private void ForgetPassword_Navigate(object sender, RequestNavigateEventArgs e)
        {
            var url = e.Uri.ToString();
            Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
            e.Handled = true;
        }


        public static void IsDarkThemeEnabled()
        {
            const string RegistryKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

            // 如果值为0，则深色主题已启用
            Global.isDarkThemeEnabled = ((int)Registry.GetValue(RegistryKey, "AppsUseLightTheme", 1) == 0);
        }
        public void UiWindow_Loaded(object sender, RoutedEventArgs e)
        { /*
            Catalog.Notification ??= new();
            if (Global.Settings.Serein.ThemeFollowSystem)
            {
                Watcher.Watch(this, BackgroundType.Tabbed, true);
            }
            Theme.Apply(Global.Settings.Serein.UseDarkTheme ? ThemeType.Dark : ThemeType.Light);*/
            //DarkThemeEnabled = IsDarkThemeEnabled();
            //DarkThemeEnabled = false;
            switch (Global.Config.AppliedTheme)
            {
                case 0:
                    IsDarkThemeEnabled();
                    break;
                case 1:
                    Global.isDarkThemeEnabled = true;
                    break;
                case 2:
                    Global.isDarkThemeEnabled = false;
                    break;
                default:
                    IsDarkThemeEnabled();
                    Global.Config.AppliedTheme = 0;
                    break;
            }
            Theme.Apply(Global.isDarkThemeEnabled ? ThemeType.Dark : ThemeType.Light, WindowBackdropType = BackgroundType.Mica);

            Color newColor = Global.isDarkThemeEnabled ? Colors.White : Colors.LightGray;
            Resources["ShadowColor"] = newColor;

        }


        public void UiWindow_Closing(object sender, CancelEventArgs e)
        {
            if (islogin)
            {
                e.Cancel = true;
                ShowInTaskbar = true;
                Hide();
            }
            else
            {
                Exit_Click(sender, null);
            }

        }
        public void UiWindow_StateChanged(object sender, EventArgs e)
        {/*
            MaxHeight = SystemParameters.MaximizedPrimaryScreenHeight;
            MaxWidth = SystemParameters.MaximizedPrimaryScreenWidth; */
        }
        public void UiWindow_ContentRendered(object sender, EventArgs e)
        {

        }
        public void UiWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
            => ShowInTaskbar = IsVisible;
        public void Hide_Click(object sender, RoutedEventArgs e)
        {
            ShowInTaskbar = false;
            DashBoard.Hide();
            Hide();
        }

        public void Exit_Click(object sender, RoutedEventArgs e)
        {
            Environment.Exit(0);
        }

        private void NotifyIcon_LeftClick(NotifyIcon sender, RoutedEventArgs e)
        {
            if (islogin)
            {
                DashBoard.Show();
            }
            else
            {
                Show();
            }
        }

        private void UiWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Login_Click(sender, e);
            }
        }
    }
    public class UserInfo
    {
        [JsonProperty("qq")]
        public long QQ { get; set; }

        [JsonProperty("qq_social_id")]
        public string QQSocialID { get; set; }

        [JsonProperty("reg_time")]
        public string RegTime { get; set; }

        [JsonProperty("id")]
        public int ID { get; set; }

        [JsonProperty("inbound")]
        public int Inbound { get; set; }

        [JsonProperty("outbound")]
        public int Outbound { get; set; }

        [JsonProperty("email")]
        public string Email { get; set; }

        [JsonProperty("traffic")]
        public BigInteger Traffic { get; set; }

        [JsonProperty("avatar")]
        public string Avatar { get; set; }

        [JsonProperty("username")]
        public string Username { get; set; }

        [JsonProperty("status")]
        public int Status { get; set; }
        
        public string Token { get; set; }
        [JsonProperty("frp_token")]
        public string FrpToken { get; set; }
    }


}