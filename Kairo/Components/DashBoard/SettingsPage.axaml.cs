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
using Avalonia; // added for Design.IsDesignMode

namespace Kairo.Components.DashBoard
{
    public partial class SettingsPage : UserControl
    {
        // Added explicit control references (FindControl pattern) to avoid reliance on generated fields
        private TextBox? _frpcPathBox;
        private ToggleSwitch? _followSystemSwitch;
        private ToggleSwitch? _darkThemeSwitch;
        private ToggleSwitch? _useMirrorSwitch;
        private TextBlock? _buildInfoText;
        private TextBlock? _versionText;
        private TextBlock? _developerText;
        private TextBlock? _copyrightText;

        public SettingsPage()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }
        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            // Resolve controls explicitly (mirrors pattern in HomePage.axaml.cs)
            _frpcPathBox = this.FindControl<TextBox>("FrpcPathBox");
            _followSystemSwitch = this.FindControl<ToggleSwitch>("FollowSystemSwitch");
            _darkThemeSwitch = this.FindControl<ToggleSwitch>("DarkThemeSwitch");
            _useMirrorSwitch = this.FindControl<ToggleSwitch>("UseMirrorSwitch");
            _buildInfoText = this.FindControl<TextBlock>("BuildInfoText");
            _versionText = this.FindControl<TextBlock>("VersionText");
            _developerText = this.FindControl<TextBlock>("DeveloperText");
            _copyrightText = this.FindControl<TextBlock>("CopyrightText");
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            // Design mode sample data and early return to avoid accessing runtime-only state
            if (Design.IsDesignMode)
            {
                if (_frpcPathBox != null) _frpcPathBox.Text = "/usr/bin/frpc"; // replaced direct name
                if (_useMirrorSwitch != null) _useMirrorSwitch.IsChecked = true;
                // if (AutoStartSwitch != null) AutoStartSwitch.IsChecked = false;
                if (_followSystemSwitch != null) _followSystemSwitch.IsChecked = true;
                if (_darkThemeSwitch != null) { _darkThemeSwitch.IsChecked = false; _darkThemeSwitch.IsEnabled = false; }
                if (_buildInfoText != null) _buildInfoText.Text = "(设计时) BuildInfo 占位";
                if (_versionText != null) _versionText.Text = "版本: 0.0.0 Design";
                if (_developerText != null) _developerText.Text = "开发者: ---";
                if (_copyrightText != null) _copyrightText.Text = "Copyright ©";
                return;
            }

            if (_frpcPathBox != null) _frpcPathBox.Text = Global.Config.FrpcPath ?? string.Empty; // replaced direct name
            if (_useMirrorSwitch != null) _useMirrorSwitch.IsChecked = Global.Config.UsingDownloadMirror;
            // if (AutoStartSwitch != null) AutoStartSwitch.IsChecked = Global.Config.AutoStartUp;
            if (_followSystemSwitch != null) _followSystemSwitch.IsChecked = Global.Config.FollowSystemTheme;
            if (_darkThemeSwitch != null)
            {
                _darkThemeSwitch.IsChecked = Global.Config.DarkTheme;
                _darkThemeSwitch.IsEnabled = !Global.Config.FollowSystemTheme;
            }
            if (_buildInfoText != null) _buildInfoText.Text = Global.BuildInfo?.ToString() ?? string.Empty;
            if (_versionText != null) _versionText.Text = $"版本: {Global.Version} {Global.VersionName}";
            if (_developerText != null) _developerText.Text = $"开发者: {Global.Developer}";
            if (_copyrightText != null) _copyrightText.Text = Global.Copyright;
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
                if (_frpcPathBox != null) _frpcPathBox.Text = Global.Config.FrpcPath;
                ConfigManager.Save();
                (Access.DashBoard as DashBoard)?.OpenSnackbar("已选择", Global.Config.FrpcPath);
            }
        }

        private void FollowSystemSwitch_OnChanged(object? sender, RoutedEventArgs e)
        {
            var toggle = sender as ToggleSwitch; // null-safe
            bool follow = toggle?.IsChecked == true;
            Global.Config.FollowSystemTheme = follow;
            if (_darkThemeSwitch != null)
                _darkThemeSwitch.IsEnabled = !follow;
            ThemeManager.Apply(follow, Global.Config.DarkTheme);
            ConfigManager.Save();
        }

        private void DarkThemeSwitch_OnChanged(object? sender, RoutedEventArgs e)
        {
            if (Global.Config.FollowSystemTheme) return; // ignore when system theme is followed
            var toggle = sender as ToggleSwitch;
            Global.Config.DarkTheme = toggle?.IsChecked == true;
            ThemeManager.Apply(false, Global.Config.DarkTheme);
            ConfigManager.Save();
        }

        private void AutoStartSwitch_OnChanged(object? sender, RoutedEventArgs e)
        {
            var toggle = sender as ToggleSwitch;
            Global.Config.AutoStartUp = toggle?.IsChecked == true;
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
            if (_frpcPathBox != null)
                _frpcPathBox.Text = Global.Config.FrpcPath; // replaced direct name
        }

        private void UseMirrorSwitch_OnChanged(object? sender, RoutedEventArgs e)
        {
            var toggle = sender as ToggleSwitch;
            Global.Config.UsingDownloadMirror = toggle?.IsChecked == true;
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
