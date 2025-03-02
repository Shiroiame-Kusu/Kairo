using Kairo.Utils;
using Microsoft.Win32;
using System;
using System.IO;
using System.Reflection;
using System.Windows;
using Wpf.Ui.Controls;

namespace Kairo.Dashboard
{
    /// <summary>
    /// Interaction logic for Settings.xaml
    /// </summary>
    public partial class Settings : UiPage
    {
        private int i = 0;
        public Settings()
        {
            InitializeCustomComponent();
            Access.Settings = this;
            
        }
        private void InitializeCustomComponent()
        {
            InitializeComponent();
            InitializeToggleSwitch();
            _Version.Text = $"版本: Ver {Global.Version}-{Global.Branch}{((Global.Branch == "Alpha" || Global.Branch == "Beta") ? "." : "")}{Global.Revision}  \"{Global.VersionName}\"";
            _BuildInfo.Text = Global.BuildInfo.ToString();
            _Developer.Text = $"开发者: {Global.Developer}";
            _Copyright.Text = Global.Copyright;
            FrpcPath.Text = Global.Config.FrpcPath;
            
        }
        private void InitializeToggleSwitch()
        {
            
            switch (Global.Config.AppliedTheme)
            {
                case 0:
                    FollowSystemThemeSetting.IsChecked = true;
                    UseDarkTheme.IsEnabled = false;
                    break;
                case 1:
                    FollowSystemThemeSetting.IsChecked = false;
                    UseDarkTheme.IsEnabled = true;
                    UseDarkTheme.IsChecked = true;
                    break;
                case 2:
                    FollowSystemThemeSetting.IsChecked = false;
                    UseDarkTheme.IsEnabled = true;
                    UseDarkTheme.IsChecked = false;
                    break;
                default:
                    Global.Config.AppliedTheme = 0;
                    i++;
                    InitializeToggleSwitch();
                    break;
            }
            if (i != 0) {
                throw new IndexOutOfRangeException("Error Config File, Please Check.");
            }

        }
        public void Select_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dialog = new()
            {
                InitialDirectory = Global.PATH,
                Filter = "支持的文件(frpc.exe)|frpc.exe"
            };
            if (dialog.ShowDialog() ?? false)
            {
                FrpcPath.Text = dialog.FileName;
                Global.Config.FrpcPath = FrpcPath.Text;

            }
        }

        private void SignOut_Click(object sender, RoutedEventArgs e)
        {
            if (!Logger.MsgBox("您确定要退出登录吗?", "退出登录", 1, 47, 1))
            {
                return;
            }
            Access.DashBoard.Close();
            
            Global.Config.FrpToken = null;
            Global.Config.RefreshToken = null;
            Global.Config.AccessToken = null;
            Global.Password = null;
            new ConfigManager(FileMode.Create);
            MainWindow.islogin = false;
            Access.DashBoard = null;
            Access.MainWindow.Width = double.NaN;
            Access.MainWindow.VisibilityChange(false);
            Access.MainWindow.Show();
        }

        private void CopyToken_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(Global.Config.FrpToken);
            Logger.MsgBox("Kairo\n已经复制啦~", "Kairo", 0, 48, 1);
        }

        private void EasterEgg_Click(object sender, RoutedEventArgs e)
        {
            if(Random.Shared.Next(0,2) == 0)
            {
                CrashInterception.ShowException(new Exception("不是说让你不要点吗"));
                
            }
            else
            {
                BSODTrigger.Trigger();
            }
        }

        private void FollowSystemThemeSetting_Click(object sender, RoutedEventArgs e)
        {
            if ((bool)FollowSystemThemeSetting.IsChecked)
            {
                Global.Config.AppliedTheme = 0;
                MainWindow.IsDarkThemeEnabled();
                UseDarkTheme.IsChecked = false;
                UseDarkTheme.IsEnabled = false;
            }
            else
            {
                UseDarkTheme.IsEnabled = true;
                if (Global.isDarkThemeEnabled)
                {
                    Global.Config.AppliedTheme = 1;
                    UseDarkTheme.IsChecked = true;
                }
                else
                {
                    UseDarkTheme.IsChecked = false;
                    Global.Config.AppliedTheme = 2;
                }
            }
            Access.DashBoard.ChangeColor();
        }

        private void UseDarkTheme_Click(object sender, RoutedEventArgs e)
        {
            if ((bool)UseDarkTheme.IsChecked)
            {
                Global.isDarkThemeEnabled = true;
                Global.Config.AppliedTheme = 1;
            }
            else {
                Global.isDarkThemeEnabled = false;
                Global.Config.AppliedTheme = 2;
            }
            Access.DashBoard.ChangeColor();
        }

        private void AutoStartUp_Click(object sender, RoutedEventArgs e)
        {
            RegistryKey rk = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
            if ((bool)AutoStartUp.IsChecked)
            {
                
                if (rk != null) {
                    rk.SetValue("Kairo", Environment.ProcessPath);
                }
                Global.Config.AutoStartUp = true;
            }
            else
            {
                rk.DeleteValue("Kairo");
                Global.Config.AutoStartUp = false;
            }
        }

        private void DownloadFrpc_Click(object sender, RoutedEventArgs e)
        {
            Download download = new Download();
            download.Owner = Access.DashBoard;
            download.Show();
        }

        private void UseMirror_Click(object sender, RoutedEventArgs e)
        {
            Global.Config.UsingDownloadMirror = (bool)UseMirror.IsChecked;
        }
    }
}
