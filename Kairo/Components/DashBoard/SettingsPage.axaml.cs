using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Kairo.Utils;
using Kairo.Utils.Configuration;
using Kairo.Utils.Logger;
using Kairo.ViewModels;

namespace Kairo.Components.DashBoard
{
    public partial class SettingsPage : UserControl
    {
        private int _easterCount;

        public SettingsPage()
        {
            InitializeComponent();
            DataContext = new SettingsPageViewModel();
            Loaded += OnLoaded;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
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
                using var http = new HttpClient();
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Kairo/UpdateCheck");

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
                var current = new Version(Global.Version);
                var currentBranch = NormalizeBranch(Global.Branch) ?? "Release";
                var currentRev = Global.Revision;
                var (remoteVer, remoteBranch, remoteRev) = ParseTag(tagName);

                bool updateAvailable;
                if (currentBranch.Equals(remoteBranch, StringComparison.OrdinalIgnoreCase))
                {
                    updateAvailable = remoteVer > current || (remoteVer == current && remoteRev > currentRev);
                }
                else
                {
                    updateAvailable = canSwitchBranch && desired.Equals(remoteBranch, StringComparison.OrdinalIgnoreCase) && !(remoteVer == current && remoteRev == currentRev && currentBranch == remoteBranch);
                }

                if (!updateAvailable)
                {
                    (Access.DashBoard as DashBoard)?.OpenSnackbar("已是最新", $"当前 {Global.Version}-{currentBranch}.{currentRev}");
                    return;
                }
                (Access.DashBoard as DashBoard)?.OpenSnackbar("发现新版本", $"将退出并更新到 {remoteVer}-{remoteBranch}.{remoteRev}");

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

        private static (Version ver, string branch, int revision) ParseTag(string? tag)
        {
            tag ??= string.Empty;
            string t = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                ? (tag.Length > 1 ? tag[1..] : string.Empty)
                : tag;
            string branch = "Release";
            int rev = 0;
            var lower = t.ToLowerInvariant();
            string versionPart = t;
            if (lower.Contains("-alpha."))
            {
                branch = "Alpha";
                var idx = lower.IndexOf("-alpha.", StringComparison.Ordinal);
                if (idx >= 0 && idx <= t.Length)
                {
                    versionPart = idx == 0 ? string.Empty : t[..idx];
                    var suffixIndex = idx + "-alpha.".Length;
                    var rstr = suffixIndex < t.Length ? t[suffixIndex..] : string.Empty;
                    int.TryParse(new string(rstr.TakeWhile(char.IsDigit).ToArray()), out rev);
                }
            }
            else if (lower.Contains("-beta."))
            {
                branch = "Beta";
                var idx = lower.IndexOf("-beta.", StringComparison.Ordinal);
                if (idx >= 0 && idx <= t.Length)
                {
                    versionPart = idx == 0 ? string.Empty : t[..idx];
                    var suffixIndex = idx + "-beta.".Length;
                    var rstr = suffixIndex < t.Length ? t[suffixIndex..] : string.Empty;
                    int.TryParse(new string(rstr.TakeWhile(char.IsDigit).ToArray()), out rev);
                }
            }
            else if (lower.Contains("-rc."))
            {
                branch = "ReleaseCandidate";
                var idx = lower.IndexOf("-rc.", StringComparison.Ordinal);
                if (idx >= 0 && idx <= t.Length)
                {
                    versionPart = idx == 0 ? string.Empty : t[..idx];
                    var suffixIndex = idx + "-rc.".Length;
                    var rstr = suffixIndex < t.Length ? t[suffixIndex..] : string.Empty;
                    int.TryParse(new string(rstr.TakeWhile(char.IsDigit).ToArray()), out rev);
                }
            }
            else if (lower.Contains("-release."))
            {
                branch = "Release";
                var idx = lower.IndexOf("-release.", StringComparison.Ordinal);
                if (idx >= 0 && idx <= t.Length)
                {
                    versionPart = idx == 0 ? string.Empty : t[..idx];
                    var suffixIndex = idx + "-release.".Length;
                    var rstr = suffixIndex < t.Length ? t[suffixIndex..] : string.Empty;
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
