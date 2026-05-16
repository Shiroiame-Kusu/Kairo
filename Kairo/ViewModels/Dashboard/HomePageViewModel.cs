using System;
using System.Threading.Tasks;
using Avalonia.Media;
using FluentAvalonia.UI.Controls;
using Kairo.Utils;
using Kairo.Utils.Logger;

namespace Kairo.ViewModels
{
    public class HomePageViewModel : ViewModelBase
    {
        private readonly ApiClient _api = new();

        private string _welcomeText = "欢迎回来，";
        private string _bandwidthText = "上行/下行带宽: -/-";
        private string _trafficText = "剩余流量: -";
        private string _announcement = "加载公告中...";
        private bool _signButtonVisible = true;
        private bool _signedBorderVisible;
        private IImage? _avatarImage;

        public string WelcomeText
        {
            get => _welcomeText;
            set => SetProperty(ref _welcomeText, value);
        }

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

        public string Announcement
        {
            get => _announcement;
            set => SetProperty(ref _announcement, value);
        }

        public bool SignButtonVisible
        {
            get => _signButtonVisible;
            set => SetProperty(ref _signButtonVisible, value);
        }

        public bool SignedBorderVisible
        {
            get => _signedBorderVisible;
            set => SetProperty(ref _signedBorderVisible, value);
        }

        public string UserNameText => string.IsNullOrWhiteSpace(Global.Config.Username) ? "未登录" : Global.Config.Username;
        public string UserIdText => Global.Config.ID > 0 ? Global.Config.ID.ToString() : "-";
        public string LoginStatusText => SessionState.IsLoggedIn ? "已登录" : "未登录";
        public string UserEmailText => string.IsNullOrWhiteSpace(SessionState.UserEmail) ? "-" : SessionState.UserEmail;
        public string UserQQText => SessionState.UserQQ > 0 ? SessionState.UserQQ.ToString() : "-";
        public string UserRegTimeText => string.IsNullOrWhiteSpace(SessionState.UserRegTime) ? "-" : SessionState.UserRegTime;

        public IImage? AvatarImage
        {
            get => _avatarImage;
            set => SetProperty(ref _avatarImage, value);
        }

        public AsyncRelayCommand SignCommand { get; }

        public HomePageViewModel()
        {
            SignCommand = new AsyncRelayCommand(SignAsync, () => SignButtonVisible);
        }

        public async Task InitializeAsync()
        {
            WelcomeText = $"欢迎回来，{Global.Config.Username ?? string.Empty}";
            BandwidthText = $"上行/下行带宽: {SessionState.Inbound * 8 / 1024}/{SessionState.Outbound * 8 / 1024}Mbps";
            var trafficGb = SessionState.Traffic / 1024d;
            TrafficText = $"剩余流量: {trafficGb:0.00}GB";
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
            try
            {
                var result = await _api.GetAnnouncementAsync();
                Announcement = result.Success
                    ? string.IsNullOrWhiteSpace(result.Data) ? "暂无公告" : result.Data
                    : result.Message;
            }
            catch (Exception ex)
            {
                Kairo.Utils.Logger.Logger.Exception("Unhandled exception in Kairo/ViewModels/Dashboard/HomePageViewModel.cs:109", ex);
                Announcement = "获取公告异常: " + ex.Message;
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
            catch (System.Exception ex)
            {
                Kairo.Utils.Logger.Logger.Exception("Unhandled exception in Kairo/ViewModels/Dashboard/HomePageViewModel.cs:132", ex);
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
                    TrafficText = $"剩余流量: {(SessionState.Traffic / 1024):0.00}GB";
                    SignButtonVisible = false;
                    SignedBorderVisible = true;
                    (Access.DashBoard as Components.DashBoard.DashBoard)?.OpenSnackbar("签到成功", $"获得 {gained:0.00}GB 流量", InfoBarSeverity.Success);
                }
                else if (result.Code == 403 && result.Message == "你今天已经签到过了")
                {
                    SignButtonVisible = false;
                    SignedBorderVisible = true;
                    (Access.DashBoard as Components.DashBoard.DashBoard)?.OpenSnackbar("提示", "你今天已经签到过了", InfoBarSeverity.Informational);
                }
                else
                {
                    (Access.DashBoard as Components.DashBoard.DashBoard)?.OpenSnackbar("签到失败", result.Message, InfoBarSeverity.Error);
                }
            }
            catch (Exception ex)
            {
                Kairo.Utils.Logger.Logger.Exception("Unhandled exception in Kairo/ViewModels/Dashboard/HomePageViewModel.cs:163", ex);
                (Access.DashBoard as Components.DashBoard.DashBoard)?.OpenSnackbar("签到异常", ex.Message, InfoBarSeverity.Error);
            }
            finally
            {
                SignCommand.RaiseCanExecuteChanged();
            }
        }
    }
}
