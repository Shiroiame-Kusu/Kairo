using System;
using Kairo.Utils;
using Kairo.Utils.Configuration;

namespace Kairo.ViewModels
{
    public class SettingsPageViewModel : ViewModelBase
    {
        private string _frpcPath = string.Empty;
        private bool _followSystem;
        private bool _darkTheme;
        private bool _useMirror;
        private bool _debugMode;
        private int _updateBranchIndex;

        public string FrpcPath
        {
            get => _frpcPath;
            set
            {
                if (SetProperty(ref _frpcPath, value))
                {
                    Global.Config.FrpcPath = value;
                    ConfigManager.Save();
                }
            }
        }

        public bool FollowSystem
        {
            get => _followSystem;
            set
            {
                if (SetProperty(ref _followSystem, value))
                {
                    Global.Config.FollowSystemTheme = value;
                    if (value)
                    {
                        // When following system, disable manual dark toggle
                        DarkTheme = Global.Config.DarkTheme;
                    }
                    ThemeManager.Apply(value, Global.Config.DarkTheme);
                    ConfigManager.Save();
                    OnPropertyChanged(nameof(CanToggleDarkTheme));
                }
            }
        }

        public bool DarkTheme
        {
            get => _darkTheme;
            set
            {
                if (!CanToggleDarkTheme) return;
                if (SetProperty(ref _darkTheme, value))
                {
                    Global.Config.DarkTheme = value;
                    ThemeManager.Apply(false, value);
                    ConfigManager.Save();
                }
            }
        }

        public bool UseMirror
        {
            get => _useMirror;
            set
            {
                if (SetProperty(ref _useMirror, value))
                {
                    Global.Config.UsingDownloadMirror = value;
                    ConfigManager.Save();
                }
            }
        }

        public bool DebugMode
        {
            get => _debugMode;
            set
            {
                if (SetProperty(ref _debugMode, value))
                {
                    Global.SetDebugMode(value, persist: true);
                }
            }
        }

        public bool CanToggleDarkTheme => !FollowSystem;

        public string BuildInfoText => Global.BuildInfo?.ToString() ?? string.Empty;
        public string VersionText => $"版本: {Global.Version} {Global.VersionName} Rev {Global.Revision}";
        public string DeveloperText => $"开发者: {Global.Developer}";
        public string CopyrightText => Global.Copyright;

        public int UpdateBranchIndex
        {
            get => _updateBranchIndex;
            set
            {
                if (SetProperty(ref _updateBranchIndex, value))
                {
                    Global.Config.UpdateBranch = IndexToBranch(value);
                    ConfigManager.Save();
                }
            }
        }

        public void LoadFromConfig()
        {
            FrpcPath = Global.Config.FrpcPath ?? string.Empty;
            UseMirror = Global.Config.UsingDownloadMirror;
            FollowSystem = Global.Config.FollowSystemTheme;
            DarkTheme = Global.Config.DarkTheme;
            DebugMode = Global.Config.DebugMode;
            UpdateBranchIndex = BranchToIndex(string.IsNullOrWhiteSpace(Global.Config.UpdateBranch) ? Global.Branch : Global.Config.UpdateBranch);
            OnPropertyChanged(nameof(CanToggleDarkTheme));
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
    }
}
