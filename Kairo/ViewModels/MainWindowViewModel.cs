using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Security.Cryptography;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Kairo.Models;
using Kairo.Utils;
using Kairo.Utils.Configuration;
using Kairo.Utils.Logger;
using Kairo.Utils.Serialization;

namespace Kairo.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        private static readonly TimeSpan LoginTimeout = TimeSpan.FromSeconds(30);
        private readonly HttpClient _http = new();
        private CancellationTokenSource? _loginTimeoutCts;
        private bool _isLoggingIn;
        private bool _isLoggedIn;
        private string _tipText = string.Empty;
        private string _snackbarTitle = string.Empty;
        private string _snackbarMessage = string.Empty;
        private InfoBarSeverity _snackbarSeverity = InfoBarSeverity.Informational;
        private bool _isSnackbarOpen;
        private UserInfo? _userInfo;

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

        public event EventHandler<UserInfo>? LoginSucceeded;
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
            var url = $"{Global.APIList.GetTheFUCKINGRefreshToken}?client_id={Global.APPID}&scopes=User,Node,Tunnel,Sign&redirect_uri={Uri.EscapeDataString($"{Global.Dashboard}/auth/oauth/redirect-localhost?port={Global.OAuthPort}&ssl=false&path=/oauth/callback")}&mode=callback";
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                BeginLoginTimeout();
                IsLoggingIn = true;
            }
            catch (Exception ex)
            {
                CancelLoginTimeout();
                ShowSnackbar("启动浏览器失败", ex.Message, InfoBarSeverity.Error);
                IsLoggingIn = false;
            }
        }

        public async Task AcceptOAuthRefreshTokenAsync(string refreshToken)
        {
            CancelLoginTimeout();
            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                ShowSnackbar("无效令牌", "提供的刷新令牌为空", InfoBarSeverity.Warning);
                IsLoggingIn = false;
                return;
            }
            await LoginWithRefreshTokenAsync(refreshToken);
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
                _http.DefaultRequestHeaders.Remove("User-Agent");
                _http.DefaultRequestHeaders.Add("User-Agent", $"Kairo-{Global.Version}");
                var accessUrl = Global.APIList.GetAccessToken;
                var formContent = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("app_id", Global.APPID.ToString()),
                    new KeyValuePair<string, string>("refresh_token", refreshToken)
                });
                var response = await _http.PostAsyncLogged(accessUrl, formContent);
                var accessBody = await response.Content.ReadAsStringAsync();
                var json = JsonNode.Parse(accessBody);
                var accessStatus = json? ["status"]?.GetValue<int>() ?? 0;
                if (accessStatus != 200)
                {
                    var message = json? ["message"]?.GetValue<string>() ?? "未知错误";
                    if (!auto) ShowSnackbar("登录失败", $"API状态: {accessStatus} {message}", InfoBarSeverity.Error);
                    Global.Config.RefreshToken = string.Empty;
                    Global.Config.AccessToken = string.Empty;
                    Global.Config.ID = 0;
                    IsLoggingIn = false;
                    return;
                }
                var dataNode = json? ["data"];
                Global.Config.ID = dataNode? ["user_id"]?.GetValue<int>() ?? 0;
                Global.Config.AccessToken = dataNode? ["access_token"]?.GetValue<string>() ?? string.Empty;
                Global.Config.RefreshToken = refreshToken;

                _http.DefaultRequestHeaders.Remove("Authorization");
                _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {Global.Config.AccessToken}");
                var userUrl = $"{Global.APIList.GetUserInfo}?user_id={Global.Config.ID}";
                var userResp = await _http.GetAsyncLogged(userUrl);
                var userBody = await userResp.Content.ReadAsStringAsync();
                var userJson = JsonNode.Parse(userBody);
                var userNode = userJson? ["data"];
                _userInfo = userNode == null ? null : JsonSerializer.Deserialize(userNode.ToJsonString(), AppJsonContext.Default.UserInfo);
                if (_userInfo == null)
                {
                    ShowSnackbar("错误", "解析用户信息失败", InfoBarSeverity.Error);
                    IsLoggingIn = false;
                    return;
                }

                var frpUrl = $"{Global.APIList.GetFrpToken}?user_id={Global.Config.ID}";
                var frpResp = await _http.GetAsyncLogged(frpUrl);
                var frpBody = await frpResp.Content.ReadAsStringAsync();
                var frpJson = JsonNode.Parse(frpBody);
                _userInfo.FrpToken = frpJson? ["data"]? ["token"]?.GetValue<string>();
                Global.Config.Username = _userInfo.Username;
                Global.Config.FrpToken = _userInfo.FrpToken ?? string.Empty;
                ApplyUserInfoToSession(_userInfo);
                IsLoggedIn = true;
                SessionState.IsLoggedIn = true;
                ConfigManager.Save();
                ShowSnackbar("登录成功", $"欢迎 {_userInfo.Username}", InfoBarSeverity.Success);
                RunOnUi(() => LoginSucceeded?.Invoke(this, _userInfo));
            }
            catch (Exception ex)
            {
                ShowSnackbar("异常", ex.Message, InfoBarSeverity.Error);
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
            SessionState.Inbound = limit?.Inbound ?? userInfo.Inbound;
            SessionState.Outbound = limit?.Outbound ?? userInfo.Outbound;
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

        public void ShowSnackbar(string title, string? message, InfoBarSeverity severity)
        {
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
