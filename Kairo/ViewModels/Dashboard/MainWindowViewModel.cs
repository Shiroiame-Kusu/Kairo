using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Security.Cryptography;
using Avalonia.Media;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Kairo.Core;
using Kairo.Core.Providers;
using Kairo.Models;
using Kairo.Utils;
using Kairo.Utils.Configuration;
using Kairo.Utils.Logger;

namespace Kairo.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private static readonly TimeSpan LoginTimeout = TimeSpan.FromSeconds(30);
        private readonly ApiClient _api = new();
        private CancellationTokenSource? _loginTimeoutCts;
        private bool _isLoggingIn;
        private bool _isWaitingForBrowser;
        private bool _isLoggedIn;
        private string _tipText = string.Empty;
        private string _snackbarTitle = string.Empty;
        private string _snackbarMessage = string.Empty;
        private FAInfoBarSeverity _snackbarSeverity = FAInfoBarSeverity.Informational;
        private bool _isSnackbarOpen;
        private string _pkceCodeVerifier = string.Empty;
        private UserInfo? _userInfo;
        private ProviderOption? _selectedProvider;

        public IReadOnlyList<ProviderOption> Providers { get; } = FrpProviderRegistry.All
            .Select(provider => new ProviderOption(provider.Id, provider.DisplayName))
            .ToList();

        public ProviderOption? SelectedProvider
        {
            get => _selectedProvider;
            set
            {
                if (!SetProperty(ref _selectedProvider, value) || value == null) return;
                ProviderAuth.SaveCurrent(save: false);
                Global.Config.ProviderId = value.Id;
                ProviderAuth.ApplyCurrent();
                ConfigManager.Save();
                OnPropertyChanged(nameof(BannerSource));
                OnPropertyChanged(nameof(IconSource));
                OnPropertyChanged(nameof(IsLoginEnabled));
                NotifySavedSessionChanged();
                StartOAuthCommand.RaiseCanExecuteChanged();
                ProviderChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public IImage BannerSource => ProviderBranding.GetBannerImage(Global.CurrentProvider);
        public IImage IconSource => ProviderBranding.GetIconImage(Global.CurrentProvider);

        public RelayCommand StartOAuthCommand { get; }
        public RelayCommand UseOtherAccountCommand { get; }
        public RelayCommand CancelLoginCommand { get; }

        /// <summary>当前服务商是否保存了可用于自动登录的会话</summary>
        public bool HasSavedSession => !string.IsNullOrWhiteSpace(Global.Config.RefreshToken);

        public string LoginButtonText => HasSavedSession && !string.IsNullOrWhiteSpace(Global.Config.Username)
            ? $"以 {Global.Config.Username} 继续"
            : "登录";

        public string LoginHintText => HasSavedSession
            ? $"已保存 {Global.CurrentProvider.DisplayName} 的登录状态"
            : "将在浏览器中完成 OAuth 授权";

        public string LoginStatusText => IsWaitingForBrowser ? "等待浏览器授权…" : "正在登录…";

        public string VersionText => $"v{Global.Version} \"{Global.VersionName}\" · {Global.Branch.ToDisplayName()} {Global.Revision}";

        public bool IsWaitingForBrowser
        {
            get => _isWaitingForBrowser;
            private set
            {
                if (!Dispatcher.UIThread.CheckAccess())
                {
                    Dispatcher.UIThread.Post(() => IsWaitingForBrowser = value);
                    return;
                }
                if (SetProperty(ref _isWaitingForBrowser, value))
                    OnPropertyChanged(nameof(LoginStatusText));
            }
        }

        private static void RunOnUi(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess())
                action();
            else
                Dispatcher.UIThread.Post(action);
        }

        public bool IsLoggingIn
        {
            get => _isLoggingIn;
            private set
            {
                if (!Dispatcher.UIThread.CheckAccess())
                {
                    Dispatcher.UIThread.Post(() => IsLoggingIn = value);
                    return;
                }
                if (SetProperty(ref _isLoggingIn, value))
                {
                    if (!value) IsWaitingForBrowser = false;
                    OnPropertyChanged(nameof(IsLoginFormVisible));
                    OnPropertyChanged(nameof(IsLoginStatusVisible));
                    OnPropertyChanged(nameof(IsLoginEnabled));
                    OnPropertyChanged(nameof(LoginFormOpacity));
                    OnPropertyChanged(nameof(LoginStatusOpacity));
                    StartOAuthCommand.RaiseCanExecuteChanged();
                    UseOtherAccountCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsLoggedIn
        {
            get => _isLoggedIn;
            private set
            {
                if (!Dispatcher.UIThread.CheckAccess())
                {
                    Dispatcher.UIThread.Post(() => IsLoggedIn = value);
                    return;
                }
                SetProperty(ref _isLoggedIn, value);
            }
        }

        public bool IsLoginFormVisible => !IsLoggingIn;
        public bool IsLoginStatusVisible => IsLoggingIn;
        public bool IsLoginEnabled => !IsLoggingIn && Global.CurrentProvider.SupportsOAuthLogin;
        public double LoginFormOpacity => IsLoginFormVisible ? 1d : 0d;
        public double LoginStatusOpacity => IsLoginStatusVisible ? 1d : 0d;

        public string TipText
        {
            get => _tipText;
            private set => SetProperty(ref _tipText, value);
        }

        public string SnackbarTitle
        {
            get => _snackbarTitle;
            private set => SetProperty(ref _snackbarTitle, value);
        }

        public string SnackbarMessage
        {
            get => _snackbarMessage;
            private set => SetProperty(ref _snackbarMessage, value);
        }

        public FAInfoBarSeverity SnackbarSeverity
        {
            get => _snackbarSeverity;
            private set => SetProperty(ref _snackbarSeverity, value);
        }

        public bool IsSnackbarOpen
        {
            get => _isSnackbarOpen;
            private set => SetProperty(ref _isSnackbarOpen, value);
        }

        public event EventHandler<UserInfo>? LoginSucceeded;
        public event EventHandler<string>? LoginFailed;
        public event EventHandler? ProviderChanged;

        public MainWindowViewModel()
        {
            _selectedProvider = Providers.FirstOrDefault(provider => provider.Id.Equals(Global.Config.ProviderId, StringComparison.OrdinalIgnoreCase))
                ?? Providers.FirstOrDefault();
            StartOAuthCommand = new RelayCommand(StartLogin, () => !IsLoggingIn && Global.CurrentProvider.SupportsOAuthLogin);
            UseOtherAccountCommand = new RelayCommand(StartOAuthFlow, () => !IsLoggingIn && Global.CurrentProvider.SupportsOAuthLogin);
            CancelLoginCommand = new RelayCommand(CancelLogin);
        }

        /// <summary>
        /// 登录按钮：已保存会话时直接使用 Refresh Token 登录，否则走浏览器 OAuth
        /// </summary>
        public void StartLogin()
        {
            if (IsLoggingIn) return;
            if (HasSavedSession)
            {
                _ = LoginWithRefreshTokenAsync(Global.Config.RefreshToken);
                return;
            }
            StartOAuthFlow();
        }

        /// <summary>
        /// 取消等待浏览器授权。保留 PKCE 校验码，用户稍后在浏览器完成授权时仍可登录
        /// </summary>
        private void CancelLogin()
        {
            if (!IsWaitingForBrowser) return;
            CancelLoginTimeout();
            IsLoggingIn = false;
            ShowSnackbar("已取消", "可以重新点击登录", FAInfoBarSeverity.Informational);
        }

        public async Task SwitchProviderAsync(string providerId)
        {
            var option = Providers.FirstOrDefault(p => p.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase));
            if (option == null) return;
            SelectedProvider = option;
            if (HasSavedSession)
                await LoginWithRefreshTokenAsync(Global.Config.RefreshToken, auto: true);
        }

        private void NotifySavedSessionChanged()
        {
            OnPropertyChanged(nameof(HasSavedSession));
            OnPropertyChanged(nameof(LoginButtonText));
            OnPropertyChanged(nameof(LoginHintText));
        }

        public async Task InitializeAsync()
        {
            TipText = PickTip();
            ProviderAuth.ApplyCurrent();
            if (!string.IsNullOrWhiteSpace(Global.Config.RefreshToken))
            {
                await LoginWithRefreshTokenAsync(Global.Config.RefreshToken, auto: true);
            }
        }

        private static string PickTip()
        {
            if (Global.Tips.Count == 0) return string.Empty;
            return Global.Tips[Random.Shared.Next(0, Global.Tips.Count)];
        }

        public void StartOAuthFlow()
        {
            if (IsLoggingIn) return;
            if (!Global.CurrentProvider.SupportsOAuthLogin)
            {
                ShowSnackbar("暂不支持登录", $"{Global.CurrentProvider.DisplayName} 未公开 OAuth 登录接口", FAInfoBarSeverity.Warning);
                return;
            }
            var codeChallenge = string.Empty;
            if (Global.CurrentProvider.Type == FrpProviderType.Lolia)
            {
                _pkceCodeVerifier = CreatePkceCodeVerifier();
                codeChallenge = CreatePkceCodeChallenge(_pkceCodeVerifier);
            }
            var url = _api.BuildOAuthUrl("callback", codeChallenge);
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                BeginLoginTimeout();
                IsLoggingIn = true;
                IsWaitingForBrowser = true;
            }
            catch (Exception ex)
            {
                CancelLoginTimeout();
                _pkceCodeVerifier = string.Empty;
                Logger.Output(LogType.Error, "[Login] 启动浏览器失败:", ex);
                ShowSnackbar("启动浏览器失败", ex.Message, FAInfoBarSeverity.Error);
                IsLoggingIn = false;
            }
        }

        public async Task AcceptOAuthRefreshTokenAsync(string refreshToken)
        {
            CancelLoginTimeout();
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                Logger.Output(LogType.Warn, "[Login] OAuth 回调提供的刷新令牌为空");
                ShowSnackbar("无效令牌", "提供的刷新令牌为空", FAInfoBarSeverity.Warning);
                IsLoggingIn = false;
                return;
            }
            await LoginWithRefreshTokenAsync(refreshToken);
        }

        public async Task AcceptOAuthCodeAsync(string code)
        {
            CancelLoginTimeout();
            if (string.IsNullOrWhiteSpace(code))
            {
                Logger.Output(LogType.Warn, "[Login] OAuth 回调提供的授权码为空");
                ShowSnackbar("无效授权码", "提供的授权码为空", FAInfoBarSeverity.Warning);
                IsLoggingIn = false;
                return;
            }
            await LoginWithCodeAsync(code);
        }

        private async Task LoginWithCodeAsync(string code)
        {
            if (IsLoggedIn) return;
            IsLoggingIn = true;
            IsWaitingForBrowser = false;
            try
            {
                var token = await _api.ExchangeCodeForRefreshTokenAsync(code, _pkceCodeVerifier);
                if (!token.Success || string.IsNullOrWhiteSpace(token.Data))
                {
                    Logger.Output(LogType.Error, $"[Login] 换取令牌失败: API状态={token.Code}, 消息={token.Message}");
                    ShowSnackbar("登录失败", $"API状态: {token.Code} {token.Message}", FAInfoBarSeverity.Error);
                    IsLoggingIn = false;
                    return;
                }

                await LoginWithRefreshTokenAsync(token.Data);
            }
            catch (Exception ex)
            {
                Logger.Output(LogType.Error, "[Login] 登录异常:", ex);
                ShowSnackbar("异常", ex.Message, FAInfoBarSeverity.Error);
                RunOnUi(() => LoginFailed?.Invoke(this, ex.Message));
            }
            finally
            {
                IsLoggingIn = false;
                CancelLoginTimeout();
            }
        }

        public async Task LoginWithRefreshTokenAsync(string refreshToken, bool auto = false)
        {
            if (IsLoggedIn) return;
            IsLoggingIn = true;
            IsWaitingForBrowser = false;
            try
            {
                if (string.IsNullOrWhiteSpace(refreshToken))
                {
                    IsLoggingIn = false;
                    return;
                }
                var login = await _api.LoginWithRefreshTokenAsync(refreshToken);
                if (!login.Success || login.Data == null)
                {
                    Logger.Output(LogType.Error, $"[Login] 登录失败: API状态={login.Code}, 消息={login.Message}");
                    if (!auto) ShowSnackbar("登录失败", $"API状态: {login.Code} {login.Message}", FAInfoBarSeverity.Error);
                    ProviderAuth.ClearCurrent(save: false);
                    Global.Config.RefreshToken = string.Empty;
                    Global.Config.AccessToken = string.Empty;
                    Global.Config.Username = string.Empty;
                    Global.Config.ID = 0;
                    Global.Config.FrpToken = string.Empty;
                    ConfigManager.Save();
                    NotifySavedSessionChanged();
                    IsLoggingIn = false;
                    return;
                }
                _userInfo = login.Data.User.ToUserInfo(login.Data.FrpToken);
                ApplyUserInfoToSession(_userInfo);
                IsLoggedIn = true;
                SessionState.IsLoggedIn = true;
                ProviderAuth.SaveCurrent(save: false);
                ConfigManager.Save();
                ShowSnackbar("登录成功", $"欢迎 {_userInfo.Username}", FAInfoBarSeverity.Success);
                RunOnUi(() => LoginSucceeded?.Invoke(this, _userInfo));
            }
            catch (Exception ex)
            {
                Logger.Output(LogType.Error, "[Login] 登录异常:", ex);
                ShowSnackbar("异常", ex.Message, FAInfoBarSeverity.Error);
                RunOnUi(() => LoginFailed?.Invoke(this, ex.Message));
            }
            finally
            {
                IsLoggingIn = false;
                CancelLoginTimeout();
            }
        }

        private static void ApplyUserInfoToSession(UserInfo userInfo)
        {
            var limit = userInfo.Limit;
            SessionState.AvatarUrl = ComputeAvatarUrl(userInfo.Email);
            SessionState.UserEmail = userInfo.Email;
            SessionState.UserQQ = userInfo.QQ;
            SessionState.UserRegTime = userInfo.RegTime;
            var bandwidth = limit?.Inbound ?? userInfo.Inbound;
            SessionState.Inbound = bandwidth;
            SessionState.Outbound = bandwidth;
            SessionState.Traffic = userInfo.Traffic;
        }

        private static string ComputeAvatarUrl(string email)
        {
            if (string.IsNullOrWhiteSpace(email)) return string.Empty;
            var sb = new StringBuilder();
            foreach (byte b in MD5.HashData(Encoding.UTF8.GetBytes(email.ToLower())))
                sb.Append(b.ToString("x2"));
            return $"https://cravatar.cn/avatar/{sb}";
        }

        private static string CreatePkceCodeVerifier()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Base64UrlEncode(bytes);
        }

        private static string CreatePkceCodeChallenge(string codeVerifier)
        {
            var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
            return Base64UrlEncode(hash);
        }

        private static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        public void ShowSnackbar(string title, string? message, FAInfoBarSeverity severity)
        {
            // 记录到日志
            var logType = severity switch
            {
                FAInfoBarSeverity.Error => LogType.Error,
                FAInfoBarSeverity.Warning => LogType.Warn,
                _ => LogType.Info
            };
            var logMessage = string.IsNullOrEmpty(message) ? title : $"{title}: {message}";
            Logger.Output(logType, "[Login]", logMessage);

            RunOnUi(() =>
            {
                SnackbarTitle = title;
                SnackbarMessage = message ?? string.Empty;
                SnackbarSeverity = severity;
                IsSnackbarOpen = true;
            });
        }

        private void BeginLoginTimeout()
        {
            CancelLoginTimeout();
            _loginTimeoutCts = new CancellationTokenSource();
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(LoginTimeout, _loginTimeoutCts.Token);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        ShowSnackbar("登录超时", "OAuth 验证未完成，请重试", FAInfoBarSeverity.Warning);
                        IsLoggingIn = false;
                    });
                }
                catch (TaskCanceledException ex)
                {
                    AppLogger.Exception("Unhandled exception in Kairo/ViewModels/Dashboard/MainWindowViewModel.cs:380", ex);
                }
            });
        }

        private void CancelLoginTimeout()
        {
            if (_loginTimeoutCts == null) return;
            _loginTimeoutCts.Cancel();
            _loginTimeoutCts.Dispose();
            _loginTimeoutCts = null;
        }

        public void ResetSession()
        {
            CancelLoginTimeout();
            IsLoggingIn = false;
            IsLoggedIn = false;
            IsSnackbarOpen = false;
            _pkceCodeVerifier = string.Empty;
            _userInfo = null;
            NotifySavedSessionChanged();
        }
    }

    public sealed record ProviderOption(string Id, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }
}
