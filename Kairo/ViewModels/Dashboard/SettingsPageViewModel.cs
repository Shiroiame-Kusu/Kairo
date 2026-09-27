using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kairo.Core;
using Kairo.Core.Providers;
using Kairo.Utils;
using Kairo.Utils.Configuration;

namespace Kairo.ViewModels
{
    public class SettingsPageViewModel : ViewModelBase
    {
        private string _frpcPath = string.Empty;
        private int _themeIndex;
        private bool _useMirror;
        private bool _debugMode;
        private int _updateBranchIndex;
        private bool _isLoading;

        public const string RepositoryUrl = "https://github.com/Shiroiame-Kusu/Kairo";
        public const string IssuesUrl = "https://github.com/Shiroiame-Kusu/Kairo/issues";

        public string FrpcPath
        {
            get => _frpcPath;
            set
            {
                if (SetProperty(ref _frpcPath, value))
                {
                    ProviderFrpcPath.Set(Global.CurrentProvider, value);
                    OnPropertyChanged(nameof(FrpcDownloadButtonText));
                    OnPropertyChanged(nameof(FrpcStatusText));
                }
            }
        }

        /// <summary>主题：0 跟随系统，1 浅色，2 深色</summary>
        public int ThemeIndex
        {
            get => _themeIndex;
            set
            {
                if (!SetProperty(ref _themeIndex, value) || _isLoading) return;
                var followSystem = value == 0;
                var dark = value == 2;
                Global.Config.FollowSystemTheme = followSystem;
                if (!followSystem) Global.Config.DarkTheme = dark;
                ThemeManager.Apply(followSystem, Global.Config.DarkTheme);
            }
        }

        public IReadOnlyList<string> ThemeOptions { get; } = new[] { "跟随系统", "浅色", "深色" };

        public bool UseMirror
        {
            get => _useMirror;
            set
            {
                if (SetProperty(ref _useMirror, value) && !_isLoading)
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
                if (SetProperty(ref _debugMode, value) && !_isLoading)
                {
                    Global.SetDebugMode(value, persist: true);
                }
            }
        }

        public bool FrpcInstalled => !string.IsNullOrWhiteSpace(FrpcPath) && File.Exists(FrpcPath);
        public string FrpcDownloadButtonText => FrpcInstalled ? "更新 FRPC" : "下载 FRPC";
        public string FrpcStatusText => FrpcInstalled
            ? $"{Global.CurrentProvider.DisplayName} 使用的 frpc 可执行文件"
            : $"尚未安装 {Global.CurrentProvider.DisplayName} frpc，点击「下载 FRPC」自动安装";

        public string AccountText => string.IsNullOrWhiteSpace(Global.Config.Username)
            ? $"{Global.CurrentProvider.DisplayName} · 未登录"
            : $"{Global.Config.Username} · {Global.CurrentProvider.DisplayName}";

        public bool CanCopyToken => !string.IsNullOrWhiteSpace(Global.Config.FrpToken);

        /// <summary>可以切换到的其他服务商</summary>
        public IReadOnlyList<IFrpProvider> OtherProviders => FrpProviderRegistry.All
            .Where(p => !p.Id.Equals(Global.CurrentProvider.Id, StringComparison.OrdinalIgnoreCase))
            .ToList();

        public bool CanSwitchProvider => OtherProviders.Count > 0;

        public string ConfigDirectory => Kairo.Core.Configuration.ConfigHelper.GetConfigDirectory();

        public string BuildInfoText => Global.BuildInfo?.ToString() ?? string.Empty;
        public string VersionText => $"版本: {Global.Version} \"{Global.VersionName}\" {Global.Branch.ToDisplayName()} {Global.Revision}";
        public string DeveloperText => $"开发者: {Global.Developer}";
        public string CopyrightText => Global.Copyright;

        public int UpdateBranchIndex
        {
            get => _updateBranchIndex;
            set
            {
                if (SetProperty(ref _updateBranchIndex, value) && !_isLoading)
                {
                    Global.Config.UpdateBranch = IndexToBranch(value);
                    ConfigManager.Save();
                }
            }
        }

        public void LoadFromConfig()
        {
            _isLoading = true;
            try
            {
                _frpcPath = ProviderFrpcPath.Get(Global.CurrentProvider);
                UseMirror = Global.Config.UsingDownloadMirror;
                ThemeIndex = Global.Config.FollowSystemTheme ? 0 : Global.Config.DarkTheme ? 2 : 1;
                DebugMode = Global.Config.DebugMode;
                UpdateBranchIndex = BranchToIndex(string.IsNullOrWhiteSpace(Global.Config.UpdateBranch)
                    ? Global.Branch.ToDisplayName()
                    : Global.Config.UpdateBranch);
            }
            finally
            {
                _isLoading = false;
            }

            OnPropertyChanged(nameof(FrpcPath));
            OnPropertyChanged(nameof(FrpcDownloadButtonText));
            OnPropertyChanged(nameof(FrpcStatusText));
            RefreshAccount();
        }

        /// <summary>设计器预览数据（不写入配置、不切换主题）</summary>
        public void LoadDesignData()
        {
            _isLoading = true;
            _frpcPath = "/usr/bin/frpc";
            UseMirror = true;
            ThemeIndex = 0;
            _isLoading = false;
            OnPropertyChanged(nameof(FrpcPath));
        }

        public void RefreshFrpcPath()
        {
            _frpcPath = ProviderFrpcPath.Get(Global.CurrentProvider);
            OnPropertyChanged(nameof(FrpcPath));
            OnPropertyChanged(nameof(FrpcDownloadButtonText));
            OnPropertyChanged(nameof(FrpcStatusText));
        }

        public void RefreshAccount()
        {
            OnPropertyChanged(nameof(AccountText));
            OnPropertyChanged(nameof(CanCopyToken));
            OnPropertyChanged(nameof(OtherProviders));
            OnPropertyChanged(nameof(CanSwitchProvider));
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
