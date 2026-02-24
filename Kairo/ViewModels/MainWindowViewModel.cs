using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Kairo.Models.Api;
using Kairo.Utils;
using Kairo.Utils.Configuration;
using Kairo.Utils.Logger;

namespace Kairo.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private static readonly TimeSpan LoginTimeout = TimeSpan.FromSeconds(30);
        private CancellationTokenSource? _loginTimeoutCts;
        private bool _isLoggingIn;
        private bool _isLoggedIn;
        private string _tipText = string.Empty;
        private string _snackbarTitle = string.Empty;
        private string _snackbarMessage = string.Empty;
        private InfoBarSeverity _snackbarSeverity = InfoBarSeverity.Informational;
        private bool _isSnackbarOpen;
        private UserInfoData? _userInfo;

        public RelayCommand StartOAuthCommand { get; }

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
                    OnPropertyChanged(nameof(IsLoginFormVisible));
                    OnPropertyChanged(nameof(IsLoginStatusVisible));
                    OnPropertyChanged(nameof(IsLoginEnabled));
                    OnPropertyChanged(nameof(LoginFormOpacity));
                    OnPropertyChanged(nameof(LoginStatusOpacity));
                    StartOAuthCommand.RaiseCanExecuteChanged();
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
        public bool IsLoginEnabled => !IsLoggingIn;
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

        public InfoBarSeverity SnackbarSeverity
        {
            get => _snackbarSeverity;
            private set => SetProperty(ref _snackbarSeverity, value);
        }

        public bool IsSnackbarOpen
        {
            get => _isSnackbarOpen;
            private set => SetProperty(ref _isSnackbarOpen, value);
        }

        public event EventHandler<UserInfoData>? LoginSucceeded;
        public event EventHandler<string>? LoginFailed;

        public MainWindowViewModel()
        {
            StartOAuthCommand = new RelayCommand(StartOAuthFlow, () => !IsLoggingIn);
        }

        public async Task InitializeAsync()
        {
            TipText = PickTip();
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
            var redirectUri = $"http://127.0.0.1:{Global.OAuthPort}/oauth/callback";
            var url = LoliaApiClient.BuildOAuth2AuthorizeUrl(
                Global.ClientId, redirectUri);
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                BeginLoginTimeout();
                IsLoggingIn = true;
            }
            catch (Exception ex)
            {
                CancelLoginTimeout();
                Logger.Output(LogType.Error, "[Login] 启动浏览器失败:", ex);
                ShowSnackbar("启动浏览器失败", ex.Message, InfoBarSeverity.Error);
                IsLoggingIn = false;
            }
        }

        public async Task AcceptOAuthCodeAsync(string code)
        {
            CancelLoginTimeout();
            if (string.IsNullOrWhiteSpace(code))
            {
                Logger.Output(LogType.Warn, "[Login] OAuth 回调提供的授权码为空");
                ShowSnackbar("无效授权码", "提供的授权码为空", InfoBarSeverity.Warning);
                IsLoggingIn = false;
                return;
            }
            await LoginWithCodeAsync(code);
        }

        private async Task LoginWithCodeAsync(string code)
        {
            if (IsLoggedIn) return;
            IsLoggingIn = true;
            try
            {
                var redirectUri = $"http://127.0.0.1:{Global.OAuthPort}/oauth/callback";
                var tokenResult = await LoliaApiClient.ExchangeCodeForTokenAsync(
                    code, redirectUri, Global.ClientId, Global.ClientSecret);
                if (!tokenResult.IsSuccess || tokenResult.Data == null)
                {
                    var msg = tokenResult.Msg ?? "未知错误";
                    Logger.Output(LogType.Error, $"[Login] 换取令牌失败: {msg}");
                    ShowSnackbar("登录失败", msg, InfoBarSeverity.Error);
                    IsLoggingIn = false;
                    return;
                }

                Global.Config.AccessToken = tokenResult.Data.AccessToken;
                Global.Config.RefreshToken = tokenResult.Data.RefreshToken;
                await FetchUserAndFinishLogin();
            }
            catch (Exception ex)
            {
                Logger.Output(LogType.Error, "[Login] 登录异常:", ex);
                ShowSnackbar("异常", ex.Message, InfoBarSeverity.Error);
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
            try
            {
                if (string.IsNullOrWhiteSpace(refreshToken))
                {
                    IsLoggingIn = false;
                    return;
                }

                // Exchange refresh_token for new tokens via the Lolia OAuth2 token endpoint
                var tokenResult = await LoliaApiClient.RefreshTokenAsync(
                    refreshToken, Global.ClientId, Global.ClientSecret);
                if (!tokenResult.IsSuccess || tokenResult.Data == null)
                {
                    var message = tokenResult.Msg ?? "未知错误";
                    Logger.Output(LogType.Error, $"[Login] 刷新令牌失败: {message}");
                    if (!auto) ShowSnackbar("登录失败", message, InfoBarSeverity.Error);
                    Global.Config.RefreshToken = string.Empty;
                    Global.Config.AccessToken = string.Empty;
                    IsLoggingIn = false;
                    return;
                }

                Global.Config.AccessToken = tokenResult.Data.AccessToken;
                if (!string.IsNullOrWhiteSpace(tokenResult.Data.RefreshToken))
                    Global.Config.RefreshToken = tokenResult.Data.RefreshToken;

                await FetchUserAndFinishLogin();
            }
            catch (Exception ex)
            {
                Logger.Output(LogType.Error, "[Login] 登录异常:", ex);
                ShowSnackbar("异常", ex.Message, InfoBarSeverity.Error);
                RunOnUi(() => LoginFailed?.Invoke(this, ex.Message));
            }
            finally
            {
                IsLoggingIn = false;
                CancelLoginTimeout();
            }
        }

        private async Task FetchUserAndFinishLogin()
        {
            // Fetch user info via centralized client
            var userResult = await LoliaApiClient.GetUserInfoAsync();
            if (!userResult.IsSuccess || userResult.Data == null)
            {
                Logger.Output(LogType.Error, $"[Login] 获取用户信息失败: {userResult.Msg}");
                ShowSnackbar("错误", $"获取用户信息失败: {userResult.Msg}", InfoBarSeverity.Error);
                return;
            }
            _userInfo = userResult.Data;

            Global.Config.Username = _userInfo.Username;
            ApplyUserInfoToSession(_userInfo);
            IsLoggedIn = true;
            SessionState.IsLoggedIn = true;
            ConfigManager.Save();
            ShowSnackbar("登录成功", $"欢迎 {_userInfo.Username}", InfoBarSeverity.Success);
            RunOnUi(() => LoginSucceeded?.Invoke(this, _userInfo));
        }

        private static void ApplyUserInfoToSession(UserInfoData userInfo)
        {
            SessionState.AvatarUrl = userInfo.Avatar;
            SessionState.BandwidthLimit = userInfo.BandwidthLimit;
            SessionState.TrafficLimit = userInfo.TrafficLimit;
            SessionState.TrafficUsed = userInfo.TrafficUsed;
            SessionState.Username = userInfo.Username;
            SessionState.Role = userInfo.Role;
            SessionState.TodayChecked = userInfo.TodayChecked;
            SessionState.MaxTunnelCount = userInfo.MaxTunnelCount;
        }

        public void ShowSnackbar(string title, string? message, InfoBarSeverity severity)
        {
            // 记录到日志
            var logType = severity switch
            {
                InfoBarSeverity.Error => LogType.Error,
                InfoBarSeverity.Warning => LogType.Warn,
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
                        ShowSnackbar("登录超时", "OAuth 验证未完成，请重试", InfoBarSeverity.Warning);
                        IsLoggingIn = false;
                    });
                }
                catch (TaskCanceledException)
                {
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
            _userInfo = null;
        }
    }
}
