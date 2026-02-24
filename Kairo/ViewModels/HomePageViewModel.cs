using System;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Collections.Generic;
using FluentAvalonia.UI.Controls;
using Kairo.Utils;
using Kairo.Utils.Logger;

namespace Kairo.ViewModels
{
    public class HomePageViewModel : ViewModelBase
    {
        private string _welcomeText = "欢迎回来，";
        private string _bandwidthText = "上行/下行带宽: -/-";
        private string _trafficText = "剩余流量: -";
        private string _announcement = "加载公告中...";
        private bool _signButtonVisible = true;
        private bool _signedBorderVisible;

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
            set
            {
                if (SetProperty(ref _signButtonVisible, value))
                {
                    SignCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool SignedBorderVisible
        {
            get => _signedBorderVisible;
            set => SetProperty(ref _signedBorderVisible, value);
        }

        public AsyncRelayCommand SignCommand { get; }

        public HomePageViewModel()
        {
            SignCommand = new AsyncRelayCommand(SignAsync, () => SignButtonVisible);
        }

        public async Task InitializeAsync()
        {
            WelcomeText = $"欢迎回来，{SessionState.Username}";
            BandwidthText = $"带宽限制: {SessionState.BandwidthLimit}Mbps";
            UpdateTrafficDisplay();

            _ = RefreshAnnouncementAsync();

            // Use today_checked from the user info to determine sign status
            if (SessionState.TodayChecked)
            {
                SignButtonVisible = false;
                SignedBorderVisible = true;
            }
        }

        private void UpdateTrafficDisplay()
        {
            var remainingGb = SessionState.TrafficRemaining / (1024.0 * 1024 * 1024);
            TrafficText = $"剩余流量: {remainingGb:0.00}GB";
        }

        public async Task RefreshAnnouncementAsync()
        {
            try
            {
                // The notice API is not in the new Lolia v1 docs yet.
                // For now, use a direct GET call. When available, switch to LoliaApiClient.
                using var http = new HttpClient();
                http.DefaultRequestHeaders.Add("Authorization", $"Bearer {Global.Config.AccessToken}");
                var resp = await http.GetAsyncLogged($"{Global.LoliaApi}/site/notice");
                var content = await resp.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(content)) { Announcement = "暂无公告"; return; }
                JsonNode? json;
                try { json = JsonNode.Parse(content); }
                catch { Announcement = "公告格式错误"; return; }
                var code = json?["code"]?.GetValue<int>() ?? 0;
                if (code == 200)
                {
                    var raw = json?["data"]?["broadcast"]?.GetValue<string>();
                    Announcement = string.IsNullOrWhiteSpace(raw) ? "暂无公告" : raw;
                }
                else
                {
                    Announcement = json?["msg"]?.GetValue<string>() ?? "获取公告失败";
                }
            }
            catch (Exception ex)
            {
                Announcement = "获取公告异常: " + ex.Message;
            }
        }

        private async Task SignAsync()
        {
            if (!SignButtonVisible) return;
            try
            {
                using var http = new HttpClient();
                http.DefaultRequestHeaders.Add("Authorization", $"Bearer {Global.Config.AccessToken}");
                var resp = await http.PostAsyncLogged($"{Global.LoliaApi}/sign", null);
                var body = await resp.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(body))
                {
                    AccessSnackbar("签到失败", "空响应", InfoBarSeverity.Error);
                    return;
                }
                JsonNode? json;
                try { json = JsonNode.Parse(body); }
                catch { AccessSnackbar("签到失败", "响应格式错误", InfoBarSeverity.Error); return; }
                int code = json?["code"]?.GetValue<int>() ?? 0;
                string? message = json?["msg"]?.GetValue<string>();
                if (code == 200)
                {
                    // Refresh traffic stats after sign
                    var statsResult = await LoliaApiClient.GetTrafficStatsAsync();
                    if (statsResult.IsSuccess && statsResult.Data != null)
                    {
                        SessionState.TrafficLimit = statsResult.Data.TrafficLimit;
                        SessionState.TrafficUsed = statsResult.Data.TrafficUsed;
                    }
                    UpdateTrafficDisplay();
                    SignButtonVisible = false;
                    SignedBorderVisible = true;
                    SessionState.TodayChecked = true;
                    AccessSnackbar("签到成功", message ?? "签到成功", InfoBarSeverity.Success);
                }
                else
                {
                    if (message?.Contains("已经签到") == true)
                    {
                        SignButtonVisible = false;
                        SignedBorderVisible = true;
                        SessionState.TodayChecked = true;
                        AccessSnackbar("提示", message, InfoBarSeverity.Informational);
                    }
                    else
                    {
                        AccessSnackbar("签到失败", message ?? "未知错误", InfoBarSeverity.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                AccessSnackbar("签到异常", ex.Message, InfoBarSeverity.Error);
            }
            finally
            {
                SignCommand.RaiseCanExecuteChanged();
            }
        }

        private static void AccessSnackbar(string title, string? message, InfoBarSeverity severity)
        {
            (Access.DashBoard as Components.DashBoard.DashBoard)?.OpenSnackbar(title, message, severity);
        }
    }
}
