using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Kairo.Utils;
using Kairo.Utils.Configuration;

namespace Kairo.Components
{
    public partial class SettingsPage : UserControl
    {
        public SettingsPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }
        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            FrpcPathBox.Text = Global.Config.FrpcPath;
            UseMirrorSwitch.IsChecked = Global.Config.UsingDownloadMirror;
            AutoStartSwitch.IsChecked = Global.Config.AutoStartUp;
            FollowSystemSwitch.IsChecked = Global.Config.FollowSystemTheme;
            DarkThemeSwitch.IsChecked = Global.Config.DarkTheme;
            DarkThemeSwitch.IsEnabled = !Global.Config.FollowSystemTheme;
            BuildInfoText.Text = Global.BuildInfo.ToString();
            VersionText.Text = $"版本: {Global.Version} {Global.VersionName}";
            DeveloperText.Text = $"开发者: {Global.Developer}";
            CopyrightText.Text = Global.Copyright;
        }

        private async void SelectFile_OnClick(object? sender, RoutedEventArgs e)
        {
            if (TopLevel.GetTopLevel(this) is not TopLevel top) return;
            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                Title = "选择 frpc 可执行文件"
            });
            var file = files.Count > 0 ? files[0] : null;
            if (file != null)
            {
                Global.Config.FrpcPath = file.Path.LocalPath;
                FrpcPathBox.Text = Global.Config.FrpcPath;
                ConfigManager.Save();
                (Access.DashBoard as DashBoard)?.OpenSnackbar("已选择", Global.Config.FrpcPath);
            }
        }

        private void FollowSystemSwitch_OnChanged(object? sender, RoutedEventArgs e)
        {
            Global.Config.FollowSystemTheme = FollowSystemSwitch.IsChecked == true;
            DarkThemeSwitch.IsEnabled = !Global.Config.FollowSystemTheme;
            ThemeManager.Apply(Global.Config.FollowSystemTheme, Global.Config.DarkTheme);
        }

        private void DarkThemeSwitch_OnChanged(object? sender, RoutedEventArgs e)
        {
            if (Global.Config.FollowSystemTheme) return;
            Global.Config.DarkTheme = DarkThemeSwitch.IsChecked == true;
            ThemeManager.Apply(false, Global.Config.DarkTheme);
        }

        private void AutoStartSwitch_OnChanged(object? sender, RoutedEventArgs e)
        {
            Global.Config.AutoStartUp = AutoStartSwitch.IsChecked == true;
            ConfigManager.Save();
        }

        private void CopyTokenBtn_OnClick(object? sender, RoutedEventArgs e)
        {
            TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(Global.Config.FrpToken ?? string.Empty);
            (Access.DashBoard as DashBoard)?.OpenSnackbar("已复制", "Frp Token 已复制");
        }

        private void SignOutBtn_OnClick(object? sender, RoutedEventArgs e)
        {
            Global.Config.AccessToken = string.Empty;
            Global.Config.RefreshToken = string.Empty;
            ConfigManager.Save();
            (Access.DashBoard as DashBoard)?.OpenSnackbar("已退出", "请重新登录");
            // show main window login again
            if (Access.MainWindow is MainWindow mw)
            {
                MainWindow.LogoutCleanup();
                mw.PrepareForLogin();
            }
        }

        private async void DownloadFrpcBtn_OnClick(object? sender, RoutedEventArgs e)
        {
            var win = new DownloadFrpcWindow();
            if (Access.DashBoard is Window owner)
                await win.ShowDialog(owner);
            else
                win.Show();
            FrpcPathBox.Text = Global.Config.FrpcPath;
        }

        private void UseMirrorSwitch_OnChanged(object? sender, RoutedEventArgs e)
        {
            Global.Config.UsingDownloadMirror = UseMirrorSwitch.IsChecked == true;
            ConfigManager.Save();
        }

        private int _easterCount;
        private void EasterEggBtn_OnClick(object? sender, RoutedEventArgs e)
        {
            _easterCount++;
            if (_easterCount >= 3)
            {
                (Access.DashBoard as DashBoard)?.OpenSnackbar("???", "别点啦");
            }
        }
        
    }
}
