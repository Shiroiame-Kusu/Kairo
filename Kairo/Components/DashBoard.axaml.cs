using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Controls;
using Kairo.Utils;

namespace Kairo.Components
{
    public partial class DashBoard : Window
    {
        private HomePage? _homePage;
        private ProxyListPage? _proxyListPage;
        private StatusPage? _statusPage;
        private SettingsPage? _settingsPage;

        public DashBoard()
        {
            InitializeComponent();
            Access.DashBoard = this;
            OpenPage("home");
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

        public void OpenSnackbar(string title, string? message, InfoBarSeverity severity = InfoBarSeverity.Informational)
        {
            if (Snackbar == null) return;
            Snackbar.Title = title;
            Snackbar.Message = message ?? string.Empty;
            Snackbar.Severity = severity;
            Snackbar.IsOpen = true;
        }

        public void DashBoard_OnClosing(object? sender, WindowClosingEventArgs e)
        {
            e.Cancel = true;
            this.Hide();
            if (Utils.Access.MainWindow is { IsVisible: false } mw)
            {
                mw.Show();
            }
        }
    }
}
