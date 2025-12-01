using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Kairo.Utils;
using Kairo.Utils.Configuration;
using Avalonia; // added for Design.IsDesignMode
using System.Net.Http;
using System.Text.Json;
using System.Diagnostics;
using Kairo.Utils.Logger; // add
using System.Linq; // needed for TakeWhile

namespace Kairo.Components.DashBoard
{
    public partial class SettingsPage : UserControl
    {
        // Added explicit control references (FindControl pattern) to avoid reliance on generated fields
        private TextBox? _frpcPathBox;
        private ToggleSwitch? _followSystemSwitch;
        private ToggleSwitch? _darkThemeSwitch;
        private ToggleSwitch? _useMirrorSwitch;
        private ToggleSwitch? _debugModeSwitch;
        private TextBlock? _buildInfoText;
        private TextBlock? _versionText;
        private TextBlock? _developerText;
        private TextBlock? _copyrightText;
        private ComboBox? _updateBranchBox;

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
            _debugModeSwitch = this.FindControl<ToggleSwitch>("DebugModeSwitch");
            _buildInfoText = this.FindControl<TextBlock>("BuildInfoText");
            _versionText = this.FindControl<TextBlock>("VersionText");
            _developerText = this.FindControl<TextBlock>("DeveloperText");
            _copyrightText = this.FindControl<TextBlock>("CopyrightText");
            _updateBranchBox = this.FindControl<ComboBox>("UpdateBranchBox");
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
                if (_debugModeSwitch != null) _debugModeSwitch.IsChecked = false;
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
            if (_debugModeSwitch != null)
                _debugModeSwitch.IsChecked = Global.Config.DebugMode;
            if (_buildInfoText != null) _buildInfoText.Text = Global.BuildInfo?.ToString() ?? string.Empty;
            if (_versionText != null) _versionText.Text = $"版本: {Global.Version} {Global.VersionName}";
            if (_developerText != null) _developerText.Text = $"开发者: {Global.Developer}";
            if (_copyrightText != null) _copyrightText.Text = Global.Copyright;

            // Initialize update branch selector: use stored preference or current branch
            if (_updateBranchBox != null)
            {
                var preferred = string.IsNullOrWhiteSpace(Global.Config.UpdateBranch) ? Global.Branch : Global.Config.UpdateBranch;
                _updateBranchBox.SelectedIndex = BranchToIndex(preferred);
            }
        }

        private static int BranchToIndex(string? branch)
        {
            var b = NormalizeBranch(branch);
            return b switch
            {
                "Release" => 0,
                "ReleaseCandidate" => 1,
                "Beta" => 2,
                "Alpha" => 3,
                _ => 0
            };
        }

        private static string IndexToBranch(int idx) => idx switch
        {
            0 => "Release",
            1 => "ReleaseCandidate",
            2 => "Beta",
            3 => "Alpha",
            _ => "Release"
        };

        private static string? NormalizeBranch(string? b)
        {
            if (string.IsNullOrWhiteSpace(b)) return null;
            b = b.Trim();
            if (b.Equals("alpha", StringComparison.OrdinalIgnoreCase)) return "Alpha";
            if (b.Equals("beta", StringComparison.OrdinalIgnoreCase)) return "Beta";
            if (b.Equals("rc", StringComparison.OrdinalIgnoreCase) || b.Equals("releasecandidate", StringComparison.OrdinalIgnoreCase)) return "ReleaseCandidate";
            if (b.Equals("release", StringComparison.OrdinalIgnoreCase)) return "Release";
            return null;
        }

        private void UpdateBranchBox_OnChanged(object? sender, RoutedEventArgs e)
        {
            if (_updateBranchBox == null) return;
            var idx = _updateBranchBox.SelectedIndex;
            var sel = IndexToBranch(idx);
            Global.Config.UpdateBranch = sel;
            ConfigManager.Save();
            (Access.DashBoard as DashBoard)?.OpenSnackbar("分支已设置", sel);
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

        private void DebugModeSwitch_OnChanged(object? sender, RoutedEventArgs e)
        {
            var toggle = sender as ToggleSwitch;
            bool enabled = toggle?.IsChecked == true;
            Global.SetDebugMode(enabled, persist: true);
            (Access.DashBoard as DashBoard)?.OpenSnackbar("调试模式", enabled ? "已开启" : "已关闭");
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

        private async void CheckUpdateBtn_OnClick(object? sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn != null) btn.IsEnabled = false;
            try
            {
                (Access.DashBoard as DashBoard)?.OpenSnackbar("检查更新", "正在从 GitHub 获取最新版本...");
                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Kairo/UpdateCheck");

                // Determine desired branch
                var desiredPref = NormalizeBranch(Global.Config.UpdateBranch) ?? Global.Branch;
                var desired = NormalizeBranch(desiredPref) ?? "Release";
                bool canSwitchBranch = Global.Branch.Equals("Alpha", StringComparison.OrdinalIgnoreCase) || desired.Equals(Global.Branch, StringComparison.OrdinalIgnoreCase);

                var releasesUrl = "https://api.github.com/repos/Shiroiame-Kusu/Kairo/releases";
                var resp = await http.GetAsyncLogged(releasesUrl);
                resp.EnsureSuccessStatusCode();
                await using var stream = await resp.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(stream);
                JsonElement? chosen = null;
                foreach (var rel in doc.RootElement.EnumerateArray())
                {
                    var tag = rel.GetProperty("tag_name").GetString() ?? string.Empty;
                    if (IsBranchMatch(tag, desired)) { chosen = rel; break; }
                }
                if (chosen == null)
                {
                    (Access.DashBoard as DashBoard)?.OpenSnackbar("未找到版本", $"分支 {desired}");
                    return;
                }
                var tagName = chosen.Value.GetProperty("tag_name").GetString() ?? string.Empty;
                // Parse current and remote as version-branch.revision
                var current = new Version(Global.Version);
                var currentBranch = NormalizeBranch(Global.Branch) ?? "Release";
                var currentRev = Global.Revision;
                var (remoteVer, remoteBranch, remoteRev) = ParseTag(tagName);

                bool updateAvailable;
                if (currentBranch.Equals(remoteBranch, StringComparison.OrdinalIgnoreCase))
                {
                    // Same branch: update if version new or revision higher
                    updateAvailable = remoteVer > current || (remoteVer == current && remoteRev > currentRev);
                }
                else
                {
                    // Different branch: allow only if on Alpha (override) and user selected desired branch
                    updateAvailable = canSwitchBranch && desired.Equals(remoteBranch, StringComparison.OrdinalIgnoreCase) && !(remoteVer == current && remoteRev == currentRev && currentBranch == remoteBranch);
                }

                if (!updateAvailable)
                {
                    (Access.DashBoard as DashBoard)?.OpenSnackbar("已是最新", $"当前 {Global.Version}-{currentBranch}.{currentRev}");
                    return;
                }
                (Access.DashBoard as DashBoard)?.OpenSnackbar("发现新版本", $"将退出并更新到 {remoteVer}-{remoteBranch}.{remoteRev}");

                // Resolve updater path and launch with branch argument
                var baseDir = AppContext.BaseDirectory;
                var updaterDll = Path.Combine(baseDir, "Updater.dll");
                var updaterExe = Path.Combine(baseDir, OperatingSystem.IsWindows() ? "Updater.exe" : "Updater");
                ProcessStartInfo psi;
                if (File.Exists(updaterExe))
                {
                    psi = new ProcessStartInfo(updaterExe)
                    {
                        UseShellExecute = false,
                        WorkingDirectory = baseDir,
                        Arguments = $"{Process.GetCurrentProcess().Id} Shiroiame-Kusu Kairo {remoteBranch}"
                    };
                }
                else if (File.Exists(updaterDll))
                {
                    psi = new ProcessStartInfo("dotnet")
                    {
                        UseShellExecute = false,
                        WorkingDirectory = baseDir,
                        Arguments = $"\"{updaterDll}\" {Process.GetCurrentProcess().Id} Shiroiame-Kusu Kairo {remoteBranch}"
                    };
                }
                else
                {
                    (Access.DashBoard as DashBoard)?.OpenSnackbar("更新失败", "未找到 Updater 组件");
                    return;
                }

                try
                {
                    Process.Start(psi);
                    (Access.DashBoard as DashBoard)?.OpenSnackbar("正在更新", "程序即将退出并更新");
                    Environment.Exit(0);
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

        private static bool IsBranchMatch(string tag, string desired)
        {
            var lower = tag.ToLowerInvariant();
            return desired switch
            {
                "Alpha" => lower.Contains("-alpha."),
                "Beta" => lower.Contains("-beta."),
                "ReleaseCandidate" => lower.Contains("-rc."),
                "Release" => lower.Contains("-release."),
                _ => true
            };
        }

        private static (Version ver, string branch, int revision) ParseTag(string? tag)
        {
            // Expect like v3.1.0-beta.1, v3.1.0-alpha.2, v3.1.0-rc.3, v3.1.0-release.1
            tag ??= string.Empty;
            var nonNullTag = tag ?? string.Empty;
            string t = nonNullTag.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                ? (nonNullTag.Length > 1 ? nonNullTag[1..] : string.Empty)
                : nonNullTag;
            var safeT = t ?? string.Empty;
            string branch = "Release";
            int rev = 0;
            var lower = safeT.ToLowerInvariant();
            string versionPart = safeT;
            if (lower.Contains("-alpha."))
            {
                branch = "Alpha";
                var idx = lower.IndexOf("-alpha.", StringComparison.Ordinal);
                if (idx >= 0 && idx <= safeT.Length)
                {
                    versionPart = idx == 0 ? string.Empty : safeT[..idx];
                    var suffixIndex = idx + "-alpha.".Length;
                    var rstr = suffixIndex < safeT.Length ? safeT[suffixIndex..] : string.Empty;
                    int.TryParse(new string(rstr.TakeWhile(char.IsDigit).ToArray()), out rev);
                }
            }
            else if (lower.Contains("-beta."))
            {
                branch = "Beta";
                var idx = lower.IndexOf("-beta.", StringComparison.Ordinal);
                if (idx >= 0 && idx <= safeT.Length)
                {
                    versionPart = idx == 0 ? string.Empty : safeT[..idx];
                    var suffixIndex = idx + "-beta.".Length;
                    var rstr = suffixIndex < safeT.Length ? safeT[suffixIndex..] : string.Empty;
                    int.TryParse(new string(rstr.TakeWhile(char.IsDigit).ToArray()), out rev);
                }
            }
            else if (lower.Contains("-rc."))
            {
                branch = "ReleaseCandidate";
                var idx = lower.IndexOf("-rc.", StringComparison.Ordinal);
                if (idx >= 0 && idx <= safeT.Length)
                {
                    versionPart = idx == 0 ? string.Empty : safeT[..idx];
                    var suffixIndex = idx + "-rc.".Length;
                    var rstr = suffixIndex < safeT.Length ? safeT[suffixIndex..] : string.Empty;
                    int.TryParse(new string(rstr.TakeWhile(char.IsDigit).ToArray()), out rev);
                }
            }
            else if (lower.Contains("-release."))
            {
                branch = "Release";
                var idx = lower.IndexOf("-release.", StringComparison.Ordinal);
                if (idx >= 0 && idx <= safeT.Length)
                {
                    versionPart = idx == 0 ? string.Empty : safeT[..idx];
                    var suffixIndex = idx + "-release.".Length;
                    var rstr = suffixIndex < safeT.Length ? safeT[suffixIndex..] : string.Empty;
                    int.TryParse(new string(rstr.TakeWhile(char.IsDigit).ToArray()), out rev);
                }
            }
            if (!Version.TryParse(versionPart, out var ver))
            {
                ver = new Version(0, 0, 0);
            }
            return (ver, branch, rev);
        }
    }
}
