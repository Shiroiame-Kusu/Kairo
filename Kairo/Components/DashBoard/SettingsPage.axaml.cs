using System;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Kairo.Core;
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
                vm.FrpcPath = "/usr/bin/frpc";
                vm.UseMirror = true;
                vm.FollowSystem = true;
                vm.DarkTheme = false;
                vm.DebugMode = false;
                vm.UpdateBranchIndex = 0;
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
                    ScrollToBottom();
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

        private void ScrollToBottom()
        {
            if (_contentScrollViewer == null) return;

            var maxOffset = Math.Max(0, _contentScrollViewer.Extent.Height - _contentScrollViewer.Viewport.Height);
            _isProgrammaticScrolling = true;
            _contentScrollViewer.Offset = new Vector(_contentScrollViewer.Offset.X, maxOffset);
            _isProgrammaticScrolling = false;
            UpdateActiveNavByScrollPosition(forceAboutWhenBottom: true);
        }

        private void UpdateActiveNavByScrollPosition(bool forceAboutWhenBottom = false)
        {
            if (_contentScrollViewer == null)
            {
                SetActiveNav("general");
                return;
            }

            var maxOffset = Math.Max(0, _contentScrollViewer.Extent.Height - _contentScrollViewer.Viewport.Height);
            var currentY = _contentScrollViewer.Offset.Y;
            var isBottom = maxOffset > 0 && currentY >= maxOffset - 6;

            if (forceAboutWhenBottom || isBottom)
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
                Title = "选择 frpc 可执行文件"
            });
            var file = files.Count > 0 ? files[0] : null;
            if (file == null) return;

            vm.FrpcPath = file.Path.LocalPath;
            ConfigManager.Save();
            (Access.DashBoard as DashBoard)?.OpenSnackbar("已选择", vm.FrpcPath);
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

            vm.FrpcPath = Global.Config.FrpcPath;
        }

        private void EasterEggBtn_OnClick(object? sender, RoutedEventArgs e)
        {
            _easterCount++;
            if (_easterCount >= 3)
            {
                (Access.DashBoard as DashBoard)?.OpenSnackbar("???", "别点啦");
            }
        }

        private async void CheckUpdateBtn_OnClick(object? sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                (Access.DashBoard as DashBoard)?.OpenSnackbar("检查更新", "正在从 GitHub 获取最新版本...");
                using var api = new ApiClient();

                // Parse current version using AppVersion
                var currentVersion = AppVersion.FromComponents(Global.Version, Global.Branch, Global.Revision);

                var releasesUrl = "https://api.github.com/repos/Shiroiame-Kusu/Kairo/releases";
                var resp = await api.GetWithoutAuthAsync(releasesUrl);
                resp.EnsureSuccessStatusCode();
                await using var stream = await resp.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(stream);
                
                // Find the latest release matching current channel only
                AppVersion? remoteVersion = null;
                foreach (var rel in doc.RootElement.EnumerateArray())
                {
                    var tag = rel.GetProperty("tag_name").GetString() ?? string.Empty;
                    if (!AppVersion.TryParse(tag, out var parsed)) continue;
                    if (parsed.Channel == currentVersion.Channel)
                    {
                        remoteVersion = parsed;
                        break;
                    }
                }

                if (remoteVersion == null)
                {
                    (Access.DashBoard as DashBoard)?.OpenSnackbar("未找到版本", $"分支 {currentVersion.ChannelName}");
                    return;
                }

                // Compare versions (same channel guaranteed)
                bool updateAvailable = remoteVersion.Value > currentVersion;

                if (!updateAvailable)
                {
                    (Access.DashBoard as DashBoard)?.OpenSnackbar("已是最新", $"当前 {currentVersion}");
                    return;
                }

                // Check if updater is available
                if (!UpdaterHelper.IsUpdaterAvailable())
                {
                    (Access.DashBoard as DashBoard)?.OpenSnackbar("更新失败", "未找到 Updater 组件");
                    return;
                }

                (Access.DashBoard as DashBoard)?.OpenSnackbar("发现新版本", $"将退出并更新到 {remoteVersion.Value}");

                // Prepare and launch updater
                if (!UpdaterHelper.PrepareUpdate(remoteVersion.Value))
                {
                    (Access.DashBoard as DashBoard)?.OpenSnackbar("更新失败", "准备更新器失败");
                    return;
                }

                try
                {
                    (Access.DashBoard as DashBoard)?.OpenSnackbar("正在更新", "程序即将退出并更新");
                    UpdaterHelper.LaunchUpdaterAndExit();
                }
                catch (Exception exLaunch)
                {
                    (Access.DashBoard as DashBoard)?.OpenSnackbar("启动更新失败", exLaunch.Message);
                }
            }
            catch (Exception ex)
            {
                (Access.DashBoard as DashBoard)?.OpenSnackbar("检查失败", ex.Message);
            }
            finally
            {
                if (btn != null) btn.IsEnabled = true;
            }
        }
    }
}
