using System;
using System.IO; // added for File.Exists
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using FluentAvalonia.UI.Controls;
using Kairo.Controls;
using Kairo.Utils;
using Avalonia.Threading; // added for DispatcherTimer
using Kairo.Utils.Logger;
using Kairo.ViewModels;

namespace Kairo.Components.DashBoard
{
    public partial class DashBoard : Window
    {
        public static Bitmap? Avatar = null;
        private HomePage? _homePage;
        private ProxyListPage? _proxyListPage;
        private StatusPage? _statusPage;
        private SettingsPage? _settingsPage;

        private readonly DashBoardViewModel _viewModel;
        private DispatcherTimer? _snackbarTimer; // auto-dismiss timer
        private CustomTitleBar? _titleBar;

        public DashBoard()
        {
            InitializeComponent();
            _viewModel = new DashBoardViewModel();
            DataContext = _viewModel;
            Access.DashBoard = this;
            SetupPlatformWindowStyle();
            NavView.SelectedItem = HomeNavItem;
            _titleBar = this.FindControl<CustomTitleBar>("TitleBar");
            this.Opened += OnDashBoardOpened;
            this.Deactivated += OnDashBoardDeactivated;
            _ = LoadAvatar();
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

        private void OnDashBoardOpened(object? sender, EventArgs e)
        {
            if (_viewModel.ShouldPromptFrpcDownload())
            {
                try
                {
                    var win = new DownloadFrpcWindow();
                    win.Show(this);
                    OpenSnackbar("提示", "检测到未安装 frpc, 正在打开下载窗口", InfoBarSeverity.Informational);
                }
                catch (Exception ex)
                {
                    OpenSnackbar("检测异常", ex.Message, InfoBarSeverity.Warning);
                }
            }
        }

        private void OnDashBoardDeactivated(object? sender, EventArgs e)
        {
            // No transient UI to close after MVVM refactor
        }

        private async Task LoadAvatar()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(SessionState.AvatarUrl)) return;
                using HttpClient hc = new();
                var bytes = await hc.GetByteArrayAsyncLogged(SessionState.AvatarUrl);
                using var ms = new System.IO.MemoryStream(bytes);
                Avatar = new Bitmap(ms);
                Dispatcher.UIThread.Post(() =>
                {
                    _titleBar?.RefreshAvatar();
                });
            }
            catch { }
        }

        private void NavView_OnSelectionChanged(object? sender, NavigationViewSelectionChangedEventArgs e)
        {
            if (e.SelectedItem is NavigationViewItem nvi && nvi.Tag is string tag)
            {
                _viewModel.SelectedTag = tag;
                OpenPage(tag);
            }
            else if (e.IsSettingsSelected)
            {
                _viewModel.SelectedTag = "settings";
                OpenPage("settings");
            }
        }

        private void OpenPage(string tag)
        {
            switch (tag)
            {
                case "home":
                    ContentHost.Content = _homePage ??= new HomePage();
                    break;
                case "proxylist":
                    ContentHost.Content = _proxyListPage ??= new ProxyListPage();
                    break;
                case "status":
                    ContentHost.Content = _statusPage ??= new StatusPage();
                    break;
                case "settings":
                    ContentHost.Content = _settingsPage ??= new SettingsPage();
                    break;
            }
        }

        public void OpenSnackbar(string title, string? message, InfoBarSeverity severity = InfoBarSeverity.Informational)
        {
            _viewModel.ShowSnackbar(title, message, severity);

            _snackbarTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _snackbarTimer.Stop();
            _snackbarTimer.Tick -= SnackbarTimer_Tick;
            _snackbarTimer.Tick += SnackbarTimer_Tick;
            _snackbarTimer.Start();
        }

        private void SnackbarTimer_Tick(object? sender, EventArgs e)
        {
            _snackbarTimer?.Stop();
            _viewModel.CloseSnackbar();
        }

        public void DashBoard_OnClosing(object? sender, WindowClosingEventArgs e)
        {
            if (SessionState.IsLoggedIn)
            {
                e.Cancel = true;
                Hide();
            }
            else
            {
                // allow close when logged out
            }
        }
    }
}
