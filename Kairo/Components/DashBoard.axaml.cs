using System;
using System.IO; // added for File.Exists
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Controls;
using Kairo.Utils;
using Avalonia.Threading; // added for DispatcherTimer

namespace Kairo.Components.DashBoard
{
    public partial class DashBoard : Window
    {
        private HomePage? _homePage;
        private ProxyListPage? _proxyListPage;
        private StatusPage? _statusPage;
        private SettingsPage? _settingsPage;

        private bool _frpcChecked;

        private DispatcherTimer? _snackbarTimer; // auto-dismiss timer

        public DashBoard()
        {
            InitializeComponent();
            Access.DashBoard = this;
            NavView.SelectedItem = HomeNavItem;
            this.Opened += OnDashBoardOpened;
            this.Deactivated += OnDashBoardDeactivated;
        }

        private void OnDashBoardOpened(object? sender, EventArgs e)
        {
            if (_frpcChecked) return;
            _frpcChecked = true;
            try
            {
                var path = Global.Config.FrpcPath;
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    // Show download window
                    var win = new DownloadFrpcWindow();
                    win.Show(this);
                    OpenSnackbar("提示", "检测到未安装 frpc, 正在打开下载窗口", InfoBarSeverity.Informational);
                }
            }
            catch (Exception ex)
            {
                OpenSnackbar("检测异常", ex.Message, InfoBarSeverity.Warning);
                var win = new DownloadFrpcWindow();
                win.Show(this);
                OpenSnackbar("提示", "检测到未安装 frpc, 正在打开下载窗口", InfoBarSeverity.Informational);    
                
            }
        }

        private void OnDashBoardDeactivated(object? sender, EventArgs e)
        {
            CloseTransientUI();
        }

        private void NavView_OnSelectionChanged(object? sender, NavigationViewSelectionChangedEventArgs e)
        {
            if (e.SelectedItem is NavigationViewItem nvi && nvi.Tag is string tag)
            {
                OpenPage(tag);
            }
            else if (e.IsSettingsSelected)
            {
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

        private void CloseTransientUI()
        {
            _proxyListPage?.CloseAllTransientUI();
        }

        public void OpenSnackbar(string title, string? message, InfoBarSeverity severity = InfoBarSeverity.Informational)
        {
            if (Snackbar == null) return;
            Snackbar.Title = title;
            Snackbar.Message = message ?? string.Empty;
            Snackbar.Severity = severity;
            Snackbar.IsOpen = true;

            // Setup / restart auto-dismiss timer (5s)
            _snackbarTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _snackbarTimer.Stop();
            _snackbarTimer.Tick -= SnackbarTimer_Tick; // ensure not duplicated
            _snackbarTimer.Tick += SnackbarTimer_Tick;
            _snackbarTimer.Start();
        }

        private void SnackbarTimer_Tick(object? sender, EventArgs e)
        {
            _snackbarTimer?.Stop();
            if (Snackbar != null)
                Snackbar.IsOpen = false;
        }

        public void DashBoard_OnClosing(object? sender, WindowClosingEventArgs e)
        {
            CloseTransientUI();
            if (MainWindow.IsLoggedIn)
            {
                e.Cancel = true;
                this.Hide();
            }
            else
            {
                // allow close when logged out
            }
        }
    }
}
