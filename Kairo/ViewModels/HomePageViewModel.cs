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
        private readonly HttpClient _http = new();

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
            set => SetProperty(ref _signButtonVisible, value);
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
            WelcomeText = $"欢迎回来，{Global.Config.Username ?? string.Empty}";
            BandwidthText = $"上行/下行带宽: {SessionState.Inbound * 8 / 1024}/{SessionState.Outbound * 8 / 1024}Mbps";
            var trafficGb = SessionState.Traffic / 1024d;
            TrafficText = $"剩余流量: {trafficGb:0.00}GB";

            _ = RefreshAnnouncementAsync();
            await CheckSignedAsync();
        }

        public async Task RefreshAnnouncementAsync()
        {
            try
            {
                var resp = await _http.GetAsync(Global.APIList.GetNotice);
                if (!resp.IsSuccessStatusCode)
                {
                    Announcement = $"获取公告失败: HTTP {(int)resp.StatusCode}";
                    return;
                }
                var content = await resp.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(content)) { Announcement = "暂无公告"; return; }
                JsonNode? json;
                try { json = JsonNode.Parse(content); }
                catch { Announcement = "公告格式错误"; return; }
                if (json?["status"]?.GetValue<int>() == 200)
                {
                    var raw = json?["data"]?["broadcast"]?.GetValue<string>();
                    Announcement = string.IsNullOrWhiteSpace(raw) ? "暂无公告" : raw;
                }
                else
                {
                    Announcement = json?["message"]?.GetValue<string>() ?? "获取公告失败";
                }
            }
            catch (Exception ex)
            {
                Announcement = "获取公告异常: " + ex.Message;
            }
        }

        private async Task CheckSignedAsync()
        {
            try
            {
                var token = Global.Config.AccessToken;
                if (string.IsNullOrWhiteSpace(token) || Global.Config.ID == 0)
                {
                    SignButtonVisible = false;
                    SignedBorderVisible = false;
                    return;
                }
                using var hc = new HttpClient();
                hc.DefaultRequestHeaders.Add("Authorization", $"Bearer {token}");
                var url = $"{Global.APIList.GetSign}?user_id={Global.Config.ID}";
                var resp = await hc.GetAsync(url);
                var body = await resp.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(body)) return;
                JsonNode? json; try { json = JsonNode.Parse(body); } catch { return; }
                int status = json?["status"]?.GetValue<int>() ?? 0;
                bool signed = json?["data"]?["status"]?.GetValue<bool>() ?? false;
                if (status == 200 && signed)
                {
                    SignButtonVisible = false;
                    SignedBorderVisible = true;
                }
            }
            catch
            {
            }
        }

        private async Task SignAsync()
        {
            if (!SignButtonVisible) return;
            try
            {
                using HttpClient hc = new();
                hc.DefaultRequestHeaders.Add("Authorization", $"Bearer {Global.Config.AccessToken}");
                var resp = await hc.PostAsync(Global.APIList.GetSign, new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("user_id", Global.Config.ID.ToString()) }));
                var body = await resp.Content.ReadAsStringAsync();
                if (string.IsNullOrWhiteSpace(body))
                {
                    (Access.DashBoard as Components.DashBoard.DashBoard)?.OpenSnackbar("签到失败", "空响应", InfoBarSeverity.Error);
                    return;
                }
                JsonNode? json; try { json = JsonNode.Parse(body); } catch { (Access.DashBoard as Components.DashBoard.DashBoard)?.OpenSnackbar("签到失败", "响应格式错误", InfoBarSeverity.Error); return; }
                int status = json?["status"]?.GetValue<int>() ?? 0;
                string? message = json?["message"]?.GetValue<string>();
                int gained = json?["data"]?["get_traffic"]?.GetValue<int>() ?? 0;
                if (status == 200)
                {
                    SessionState.Traffic += gained * 1024; // traffic is MB-like
                    TrafficText = $"剩余流量: {(SessionState.Traffic / 1024):0.00}GB";
                    SignButtonVisible = false;
                    SignedBorderVisible = true;
                    (Access.DashBoard as Components.DashBoard.DashBoard)?.OpenSnackbar("签到成功", $"获得 {gained:0.00}GB 流量", InfoBarSeverity.Success);
                }
                else if (status == 403 && message == "你今天已经签到过了")
                {
                    SignButtonVisible = false;
                    SignedBorderVisible = true;
                    (Access.DashBoard as Components.DashBoard.DashBoard)?.OpenSnackbar("提示", "你今天已经签到过了", InfoBarSeverity.Informational);
                }
                else
                {
                    (Access.DashBoard as Components.DashBoard.DashBoard)?.OpenSnackbar("签到失败", message ?? "未知错误", InfoBarSeverity.Error);
                }
            }
            catch (Exception ex)
            {
                (Access.DashBoard as Components.DashBoard.DashBoard)?.OpenSnackbar("签到异常", ex.Message, InfoBarSeverity.Error);
            }
            finally
            {
                SignCommand.RaiseCanExecuteChanged();
            }
        }
    }
}
