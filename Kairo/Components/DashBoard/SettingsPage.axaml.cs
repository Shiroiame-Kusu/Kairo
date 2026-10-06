using System;
using System.IO;
using System.Text.Json;
using Kairo.Models;
using Kairo.Utils.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Controls.Primitives;
using Avalonia.Platform.Storage;
using Kairo.Core;
using Kairo.Core.Localization;
using Kairo.Utils;
using Kairo.Utils.Configuration;
using Kairo.ViewModels;

namespace Kairo.Components.DashBoard
{
    public partial class SettingsPage : UserControl
    {
        private int _easterCount;
        private ScrollViewer? _contentScrollViewer;
        private Border? _navGeneral;
        private Border? _navAppearance;
        private Border? _navUpdate;
        private Border? _navAccount;
        private Border? _navAbout;
        private Border? _sectionGeneral;
        private Border? _sectionAppearance;
        private Border? _sectionUpdate;
        private Border? _sectionAccount;
        private Border? _sectionAbout;
        private bool _isProgrammaticScrolling;

        public SettingsPage()
        {
            InitializeComponent();
            DataContext = new SettingsPageViewModel();
            Loaded += OnLoaded;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
            _contentScrollViewer = this.FindControl<ScrollViewer>("ContentScrollViewer");
            _navGeneral = this.FindControl<Border>("NavGeneral");
            _navAppearance = this.FindControl<Border>("NavAppearance");
            _navUpdate = this.FindControl<Border>("NavUpdate");
            _navAccount = this.FindControl<Border>("NavAccount");
            _navAbout = this.FindControl<Border>("NavAbout");
            _sectionGeneral = this.FindControl<Border>("SectionGeneral");
            _sectionAppearance = this.FindControl<Border>("SectionAppearance");
            _sectionUpdate = this.FindControl<Border>("SectionUpdate");
            _sectionAccount = this.FindControl<Border>("SectionAccount");
            _sectionAbout = this.FindControl<Border>("SectionAbout");

            if (_contentScrollViewer != null)
            {
                _contentScrollViewer.ScrollChanged += ContentScrollViewer_OnScrollChanged;
            }
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not SettingsPageViewModel vm) return;

            if (Design.IsDesignMode)
            {
                vm.LoadDesignData();
                return;
            }

            vm.LoadFromConfig();
            UpdateActiveNavByScrollPosition();
        }

        private void ContentScrollViewer_OnScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            if (_isProgrammaticScrolling) return;
            UpdateActiveNavByScrollPosition();
        }

        private void NavItem_OnPointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (sender is not Border nav || nav.Tag is not string tag) return;

            switch (tag)
            {
                case "general":
                    ScrollToSection(_sectionGeneral);
                    break;
                case "appearance":
                    ScrollToSection(_sectionAppearance);
                    break;
                case "update":
                    ScrollToSection(_sectionUpdate);
                    break;
                case "account":
                    ScrollToSection(_sectionAccount);
                    break;
                case "about":
                    ScrollToSection(_sectionAbout);
                    break;
            }

            SetActiveNav(tag);
        }

        private void ScrollToSection(Control? section)
        {
            if (_contentScrollViewer == null || section == null) return;

            var point = section.TranslatePoint(new Point(0, 0), _contentScrollViewer);
            if (point == null) return;

            var maxOffset = Math.Max(0, _contentScrollViewer.Extent.Height - _contentScrollViewer.Viewport.Height);
            var targetY = Math.Clamp(_contentScrollViewer.Offset.Y + point.Value.Y - 4, 0, maxOffset);

            _isProgrammaticScrolling = true;
            _contentScrollViewer.Offset = new Vector(_contentScrollViewer.Offset.X, targetY);
            _isProgrammaticScrolling = false;
            UpdateActiveNavByScrollPosition();
        }

        private void UpdateActiveNavByScrollPosition()
        {
            if (_contentScrollViewer == null)
            {
                SetActiveNav("general");
                return;
            }

            var maxOffset = Math.Max(0, _contentScrollViewer.Extent.Height - _contentScrollViewer.Viewport.Height);
            var currentY = _contentScrollViewer.Offset.Y;
            var isBottom = maxOffset > 0 && currentY >= maxOffset - 6;

            if (isBottom)
            {
                SetActiveNav("about");
                return;
            }

            var activeTag = GetNearestSectionTag();
            SetActiveNav(activeTag);
        }

        private string GetNearestSectionTag()
        {
            if (_contentScrollViewer == null) return "general";

            var anchorY = 10.0;
            var bestTag = "general";
            var bestDistance = double.MaxValue;

            TryPickNearest(_sectionGeneral, "general", anchorY, ref bestTag, ref bestDistance);
            TryPickNearest(_sectionAppearance, "appearance", anchorY, ref bestTag, ref bestDistance);
            TryPickNearest(_sectionUpdate, "update", anchorY, ref bestTag, ref bestDistance);
            TryPickNearest(_sectionAccount, "account", anchorY, ref bestTag, ref bestDistance);
            TryPickNearest(_sectionAbout, "about", anchorY, ref bestTag, ref bestDistance);

            return bestTag;
        }

        private void TryPickNearest(Control? section, string tag, double anchorY, ref string bestTag, ref double bestDistance)
        {
            if (_contentScrollViewer == null || section == null) return;

            var point = section.TranslatePoint(new Point(0, 0), _contentScrollViewer);
            if (point == null) return;

            var distance = Math.Abs(point.Value.Y - anchorY);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestTag = tag;
            }
        }

        private void SetActiveNav(string tag)
        {
            SetNavActive(_navGeneral, tag == "general");
            SetNavActive(_navAppearance, tag == "appearance");
            SetNavActive(_navUpdate, tag == "update");
            SetNavActive(_navAccount, tag == "account");
            SetNavActive(_navAbout, tag == "about");
        }

        private static void SetNavActive(Border? border, bool isActive)
        {
            if (border == null) return;
            if (isActive)
            {
                if (!border.Classes.Contains("active")) border.Classes.Add("active");
            }
            else
            {
                border.Classes.Remove("active");
            }
        }

        private async void SelectFile_OnClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not SettingsPageViewModel vm) return;
            if (TopLevel.GetTopLevel(this) is not TopLevel top) return;

            var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                Title = L.T("settings.frpc.pickerTitle")
            });
            var file = files.Count > 0 ? files[0] : null;
            if (file == null) return;

            vm.FrpcPath = file.Path.LocalPath;
            ConfigManager.Save();
            (Access.DashBoard as DashBoard)?.OpenSnackbar(L.T("settings.frpc.selected"), vm.FrpcPath);
        }

        private async void CopyTokenBtn_OnClick(object? sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(Global.Config.FrpToken)) return;
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return;
            await clipboard.SetTextAsync(Global.Config.FrpToken);
            (Access.DashBoard as DashBoard)?.OpenSnackbar(L.T("settings.security.copied"), L.T("settings.security.tokenCopied"));
        }

        private async void OpenConfigDir_OnClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not SettingsPageViewModel vm) return;
            try
            {
                var launcher = TopLevel.GetTopLevel(this)?.Launcher;
                if (launcher == null || !Directory.Exists(vm.ConfigDirectory) ||
                    !await launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(vm.ConfigDirectory)))
                {
                    (Access.DashBoard as DashBoard)?.OpenSnackbar(L.T("settings.dataDir.openFailed"), vm.ConfigDirectory, FluentAvalonia.UI.Controls.FAInfoBarSeverity.Warning);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Exception("打开配置目录失败", ex);
                (Access.DashBoard as DashBoard)?.OpenSnackbar(L.T("settings.dataDir.openFailed"), ex.Message, FluentAvalonia.UI.Controls.FAInfoBarSeverity.Warning);
            }
        }

        private void SwitchProviderBtn_OnClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not SettingsPageViewModel vm || sender is not Control anchor) return;

            var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
            foreach (var provider in vm.OtherProviders)
            {
                var item = new MenuItem { Header = L.T("settings.provider.switchTo", provider.DisplayName) };
                item.Click += async (_, _) => await SwitchProviderAsync(provider);
                flyout.Items.Add(item);
            }
            flyout.ShowAt(anchor);
        }

        private static async System.Threading.Tasks.Task SwitchProviderAsync(Kairo.Core.Providers.IFrpProvider provider)
        {
            var running = FrpcProcessManager.RunningCount;
            var message = running > 0
                ? L.Plural("settings.provider.switchConfirmRunning", running, provider.DisplayName, Global.CurrentProvider.DisplayName, running)
                : L.T("settings.provider.switchConfirm", provider.DisplayName, Global.CurrentProvider.DisplayName);
            if (!await DialogHelper.ConfirmAsync(Access.DashBoard, L.T("settings.provider.switch"), message, L.T("settings.provider.switchButton")))
                return;

            FrpcProcessManager.StopAll();
            if (Access.MainWindow is MainWindow mw)
                await mw.SwitchProviderAsync(provider.Id);
        }

        private async void SignOutBtn_OnClick(object? sender, RoutedEventArgs e)
        {
            var running = FrpcProcessManager.RunningCount;
            var message = running > 0
                ? L.Plural("settings.signOut.confirmRunning", running, Global.CurrentProvider.DisplayName, Global.Config.Username, running)
                : L.T("settings.signOut.confirm", Global.CurrentProvider.DisplayName, Global.Config.Username);
            var signOut = L.T("settings.security.signOut");
            if (!await DialogHelper.ConfirmAsync(Access.DashBoard, signOut, message, signOut, destructive: true))
                return;

            FrpcProcessManager.StopAll();
            ProviderAuth.ClearCurrent(save: false);
            Global.Config.AccessToken = string.Empty;
            Global.Config.RefreshToken = string.Empty;
            Global.Config.Username = string.Empty;
            Global.Config.ID = 0;
            Global.Config.FrpToken = string.Empty;
            ConfigManager.Save();
            AppLogger.ClearCache();
            (Access.DashBoard as DashBoard)?.OpenSnackbar(L.T("settings.signOut.done"), L.T("settings.signOut.doneHint"));

            if (Access.MainWindow is MainWindow mw)
            {
                MainWindow.LogoutCleanup();
                mw.PrepareForLogin();
            }
        }

        private async void DownloadFrpcBtn_OnClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not SettingsPageViewModel vm) return;

            var win = new DownloadFrpcWindow();
            if (Access.DashBoard is Window owner)
                await win.ShowDialog(owner);
            else
                win.Show();

            vm.RefreshFrpcPath();
        }

        private void EasterEggBtn_OnClick(object? sender, RoutedEventArgs e)
        {
            _easterCount++;
            if (_easterCount >= 3)
            {
                (Access.DashBoard as DashBoard)?.OpenSnackbar("???", L.T("settings.easterEgg.stop"));
            }
        }

        private async void CheckUpdateBtn_OnClick(object? sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                (Access.DashBoard as DashBoard)?.OpenSnackbar(L.T("settings.appUpdate.check"), L.T("settings.appUpdate.checking"));
                using var api = new ApiClient();

                // Parse current version using AppVersion
                var currentVersion = AppVersion.FromComponents(Global.Version, Global.Branch, Global.Revision);

                var releasesUrl = "https://api.github.com/repos/Shiroiame-Kusu/Kairo/releases";
                var resp = await api.GetWithoutAuthAsync(releasesUrl);
                resp.EnsureSuccessStatusCode();
                await using var stream = await resp.Content.ReadAsStreamAsync();
                var releases = await JsonSerializer.DeserializeAsync(stream, AppJsonContext.Default.ListGitHubReleaseSummary);

                // Find the latest release matching current channel only
                AppVersion? remoteVersion = null;
                foreach (var rel in releases ?? new())
                {
                    if (!AppVersion.TryParse(rel.TagName, out var parsed)) continue;
                    if (parsed.Channel == currentVersion.Channel)
                    {
                        remoteVersion = parsed;
                        break;
                    }
                }

                if (remoteVersion == null)
                {
                    (Access.DashBoard as DashBoard)?.OpenSnackbar(L.T("settings.appUpdate.notFound"), L.T("settings.appUpdate.channel", currentVersion.ChannelName));
                    return;
                }

                // Compare versions (same channel guaranteed)
                bool updateAvailable = remoteVersion.Value > currentVersion;

                if (!updateAvailable)
                {
                    (Access.DashBoard as DashBoard)?.OpenSnackbar(L.T("settings.appUpdate.upToDate"), L.T("settings.appUpdate.current", currentVersion));
                    return;
                }

                // Check if updater is available
                if (!UpdaterHelper.IsUpdaterAvailable())
                {
                    (Access.DashBoard as DashBoard)?.OpenSnackbar(L.T("settings.appUpdate.failed"), L.T("settings.appUpdate.noUpdater"));
                    return;
                }

                (Access.DashBoard as DashBoard)?.OpenSnackbar(L.T("settings.appUpdate.found"), L.T("settings.appUpdate.willUpdate", remoteVersion.Value));

                // Prepare and launch updater
                if (!UpdaterHelper.PrepareUpdate(remoteVersion.Value))
                {
                    (Access.DashBoard as DashBoard)?.OpenSnackbar(L.T("settings.appUpdate.failed"), L.T("settings.appUpdate.prepareFailed"));
                    return;
                }

                try
                {
                    (Access.DashBoard as DashBoard)?.OpenSnackbar(L.T("settings.appUpdate.updating"), L.T("settings.appUpdate.quitting"));
                    UpdaterHelper.LaunchUpdaterAndExit();
                }
                catch (Exception exLaunch)
                {
                    AppLogger.Exception("Unhandled exception in Kairo/Components/DashBoard/SettingsPage.axaml.cs:343", exLaunch);
                    (Access.DashBoard as DashBoard)?.OpenSnackbar(L.T("settings.appUpdate.launchFailed"), exLaunch.Message);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/Components/DashBoard/SettingsPage.axaml.cs:348", ex);
                (Access.DashBoard as DashBoard)?.OpenSnackbar(L.T("settings.appUpdate.checkFailed"), ex.Message);
            }
            finally
            {
                if (btn != null) btn.IsEnabled = true;
            }
        }
    }
}
