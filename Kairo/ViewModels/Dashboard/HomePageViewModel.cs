using System;
using System.Threading.Tasks;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using Kairo.Core.Localization;
using Kairo.Utils;
using Kairo.Utils.Logger;
using Kairo.Components.DashBoard;

namespace Kairo.ViewModels
{
    public class HomePageViewModel : ViewModelBase
    {
        private readonly ApiClient _api = new();

        private string _bandwidthText = "- / - Mbps";
        private string _trafficText = "- GB";
        // 公告内容：null 表示尚未加载；_announcementError 不为空表示加载异常
        private string? _announcementContent;
        private string? _announcementError;
        private bool _isAnnouncementLoading;
        private bool _signButtonVisible = true;
        private bool _signedBorderVisible;
        private IImage? _avatarImage;

        public string WelcomeText => string.IsNullOrWhiteSpace(Global.Config.Username)
            ? L.T("home.welcomeAnonymous")
            : L.T("home.welcome", Global.Config.Username);

        public string BandwidthText
        {
            get => _bandwidthText;
            set => SetProperty(ref _bandwidthText, value);
        }

        public string TrafficText
        {
            get => _trafficText;
            set => SetProperty(ref _trafficText, value);
        }

        public string Announcement =>
            _announcementError != null ? L.T("home.announcementError", _announcementError)
            : _announcementContent == null ? L.T("home.announcementLoading")
            : string.IsNullOrWhiteSpace(_announcementContent) ? L.T("home.noAnnouncement")
            : _announcementContent;

        public bool SignButtonVisible
        {
            get => _signButtonVisible;
            set
            {
                if (SetProperty(ref _signButtonVisible, value))
                    OnPropertyChanged(nameof(SignUnavailable));
            }
        }

        public bool SignedBorderVisible
        {
            get => _signedBorderVisible;
            set
            {
                if (SetProperty(ref _signedBorderVisible, value))
                    OnPropertyChanged(nameof(SignUnavailable));
            }
        }

        /// <summary>当前服务商或账号不支持签到</summary>
        public bool SignUnavailable => !SignButtonVisible && !SignedBorderVisible;

        public bool IsAnnouncementLoading
        {
            get => _isAnnouncementLoading;
            private set => SetProperty(ref _isAnnouncementLoading, value);
        }

        public string ProviderName => Global.CurrentProvider.DisplayName;
        public bool SignSupported => Global.CurrentProvider.SupportsSign;
        public string UserNameText => string.IsNullOrWhiteSpace(Global.Config.Username) ? L.T("home.notSignedIn") : Global.Config.Username;
        public string UserIdText => Global.Config.ID > 0 ? Global.Config.ID.ToString() : "-";
        public string LoginStatusText => SessionState.IsLoggedIn ? L.T("home.signedIn") : L.T("home.notSignedIn");
        public string UserEmailText => string.IsNullOrWhiteSpace(SessionState.UserEmail) ? "-" : SessionState.UserEmail;
        public string UserQQText => SessionState.UserQQ > 0 ? SessionState.UserQQ.ToString() : "-";
        public string UserRegTimeText => string.IsNullOrWhiteSpace(SessionState.UserRegTime) ? "-" : SessionState.UserRegTime;

        public IImage? AvatarImage
        {
            get => _avatarImage;
            set => SetProperty(ref _avatarImage, value);
        }

        public AsyncRelayCommand SignCommand { get; }
        public AsyncRelayCommand RefreshAnnouncementCommand { get; }

        public HomePageViewModel()
        {
            SignCommand = new AsyncRelayCommand(SignAsync, () => SignButtonVisible);
            RefreshAnnouncementCommand = new AsyncRelayCommand(RefreshAnnouncementAsync);
        }

        public async Task InitializeAsync()
        {
            OnPropertyChanged(nameof(WelcomeText));
            BandwidthText = $"{SessionState.Inbound * 8 / 1024} / {SessionState.Outbound * 8 / 1024} Mbps";
            var trafficGb = SessionState.Traffic / 1024d;
            TrafficText = $"{trafficGb:0.00} GB";
            OnPropertyChanged(nameof(UserNameText));
            OnPropertyChanged(nameof(UserIdText));
            OnPropertyChanged(nameof(LoginStatusText));
            OnPropertyChanged(nameof(UserEmailText));
            OnPropertyChanged(nameof(UserQQText));
            OnPropertyChanged(nameof(UserRegTimeText));

            _ = RefreshAnnouncementAsync();
            await CheckSignedAsync();
        }

        public void SetAvatar(IImage? avatar)
        {
            AvatarImage = avatar;
        }

        public async Task RefreshAnnouncementAsync()
        {
            IsAnnouncementLoading = true;
            try
            {
                var result = await _api.GetAnnouncementAsync();
                _announcementError = null;
                _announcementContent = result.Success ? result.Data ?? string.Empty : result.Message;
            }
            catch (Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/ViewModels/Dashboard/HomePageViewModel.cs:109", ex);
                _announcementError = ex.Message;
            }
            finally
            {
                OnPropertyChanged(nameof(Announcement));
                IsAnnouncementLoading = false;
            }
        }

        private async Task CheckSignedAsync()
        {
            try
            {
                if (!Global.CurrentProvider.SupportsSign || string.IsNullOrWhiteSpace(Global.Config.AccessToken) || Global.Config.ID == 0)
                {
                    SignButtonVisible = false;
                    SignedBorderVisible = false;
                    return;
                }
                var result = await _api.GetSignStatusAsync();
                if (result.Success && result.Data?.Signed == true)
                {
                    SignButtonVisible = false;
                    SignedBorderVisible = true;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/ViewModels/Dashboard/HomePageViewModel.cs:132", ex);
            }
        }

        private async Task SignAsync()
        {
            if (!SignButtonVisible) return;
            try
            {
                var result = await _api.SignAsync();
                if (result.Success)
                {
                    var gained = result.Data?.GainedTrafficGb ?? 0;
                    SessionState.Traffic += gained * 1024;
                    TrafficText = $"{(SessionState.Traffic / 1024):0.00} GB";
                    SignButtonVisible = false;
                    SignedBorderVisible = true;
                    (Access.DashBoard as DashBoard)?.OpenSnackbar(L.T("home.checkInSucceeded"), L.T("home.checkInGained", gained), FAInfoBarSeverity.Success);
                }
                else if (result.Code == 403 && result.Message == "你今天已经签到过了")
                {
                    SignButtonVisible = false;
                    SignedBorderVisible = true;
                    (Access.DashBoard as DashBoard)?.OpenSnackbar(L.T("dashboard.notice"), L.T("home.alreadyCheckedIn"), FAInfoBarSeverity.Informational);
                }
                else
                {
                    (Access.DashBoard as DashBoard)?.OpenSnackbar(L.T("home.checkInFailed"), result.Message, FAInfoBarSeverity.Error);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/ViewModels/Dashboard/HomePageViewModel.cs:163", ex);
                (Access.DashBoard as DashBoard)?.OpenSnackbar(L.T("home.checkInError"), ex.Message, FAInfoBarSeverity.Error);
            }
            finally
            {
                SignCommand.RaiseCanExecuteChanged();
            }
        }
    }
}
