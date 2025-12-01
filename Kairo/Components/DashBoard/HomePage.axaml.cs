using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Kairo.Utils;
using Avalonia; // for Design.IsDesignMode
using Kairo.Utils.Logger; // add

namespace Kairo.Components.DashBoard;

public partial class HomePage : UserControl
{
    private bool _init;
    private TextBlock? _welcomeText;
    private TextBlock? _bandwidthText;
    private TextBlock? _trafficText;
    private TextBlock? _announcementPlain;
    private Image? _avatarImage;
    private Button? _signButton;
    private Border? _signedBorder;

    public HomePage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _welcomeText = this.FindControl<TextBlock>("WelcomeText");
        _bandwidthText = this.FindControl<TextBlock>("BandwidthText");
        _trafficText = this.FindControl<TextBlock>("TrafficText");
        _announcementPlain = this.FindControl<TextBlock>("AnnouncementPlain");
        _avatarImage = this.FindControl<Image>("AvatarImage");
        _signButton = this.FindControl<Button>("SignButton");
        _signedBorder = this.FindControl<Border>("SignedBorder");
    }

    private void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_init) return;
        _init = true;
        DesignModeHelper.SafeRuntime(
            runtimeAction: () =>
            {
                if (_welcomeText != null)
                    _welcomeText.Text += Global.Config.Username ?? string.Empty;
                if (_bandwidthText != null)
                    _bandwidthText.Text = $"上行/下行带宽: {MainWindow.Inbound * 8 / 1024}/{MainWindow.Outbound * 8 / 1024}Mbps";
                if (_trafficText != null)
                {
                    String temp = (MainWindow.Traffic / 1024).ToString();
                    String temp2 = "";
                    for (int i = 0; i < temp.Length; i++)
                    {
                        temp2 += temp[i];
                        if (temp[i] == '.')
                        {
                            temp2 += temp[i + 1];
                            temp2 += temp[i + 2];
                            break;
                        }
                    }
                    _trafficText.Text = $"剩余流量: {temp2}GB";
                }
                _ = RefreshAnnouncement();
                _ = CheckSigned();
                _ = RefreshAvatar();
            },
            designFallback: () =>
            {
                if (_welcomeText != null)
                    _welcomeText.Text = "欢迎回来，设计预览用户";
                if (_bandwidthText != null)
                    _bandwidthText.Text = "上行/下行带宽: 0/0 Mbps";
                if (_trafficText != null)
                    _trafficText.Text = "剩余流量: 0GB";
                if (_announcementPlain != null)
                    _announcementPlain.Text = "(设计时) 公告示例文本。";
            }
        );
    }

    private async Task RefreshAnnouncement()
    {
        try
        {
            using HttpClient hc = new();
            var resp = await hc.GetAsyncLogged(Global.APIList.GetNotice);
            if (!resp.IsSuccessStatusCode)
            {
                PostAnnouncement($"获取公告失败: HTTP {(int)resp.StatusCode}");
                return;
            }
            var content = await resp.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(content)) { PostAnnouncement("暂无公告"); return; }
            JsonNode? json;
            try { json = JsonNode.Parse(content); } catch { PostAnnouncement("公告格式错误"); return; }
            if (json?["status"]?.GetValue<int>() == 200)
            {
                var raw = json?["data"]?["broadcast"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(raw)) raw = "暂无公告";
                PostAnnouncement(raw);
            }
            else
            {
                PostAnnouncement(json?["message"]?.GetValue<string>() ?? "获取公告失败");
            }
        }
        catch (Exception ex)
        {
            PostAnnouncement("获取公告异常: " + ex.Message);
        }
    }

    private void PostAnnouncement(string text)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_announcementPlain != null)
                _announcementPlain.Text = text;
        });
    }

    private async Task CheckSigned()
    {
        try
        {
            using HttpClient hc = new();
            hc.DefaultRequestHeaders.Add("Authorization", $"Bearer {Global.Config.AccessToken}");
            var url = $"{Global.APIList.GetSign}?user_id={Global.Config.ID}";
            var resp = await hc.GetAsyncLogged(url);
            var body = await resp.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(body)) return;
            JsonNode? json; try { json = JsonNode.Parse(body); } catch { return; }
            int status = json?["status"]?.GetValue<int>() ?? 0;
            bool signed = json?["data"]?["status"]?.GetValue<bool>() ?? false;
            if (status == 200 && signed)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (_signButton != null) _signButton.IsVisible = false;
                    if (_signedBorder != null) _signedBorder.IsVisible = true;
                });
            }
        }
        catch { }
    }

    private async Task RefreshAvatar()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(MainWindow.Avatar) || _avatarImage == null) return;
            using HttpClient hc = new();
            var bytes = await hc.GetByteArrayAsyncLogged(MainWindow.Avatar);
            using var ms = new System.IO.MemoryStream(bytes);
            DashBoard.Avatar = new Avalonia.Media.Imaging.Bitmap(ms);
            Dispatcher.UIThread.Post(() =>
            {
                if (_avatarImage != null)
                    _avatarImage.Source = DashBoard.Avatar;
            });
        }
        catch { }
    }

    private async void SignButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_signButton == null || _trafficText == null || _signedBorder == null) return;
        try
        {
            _signButton.IsEnabled = false;
            using HttpClient hc = new();
            hc.DefaultRequestHeaders.Add("Authorization", $"Bearer {Global.Config.AccessToken}");
            var resp = await hc.PostAsyncLogged($"{Global.APIList.GetSign}", new FormUrlEncodedContent(new[] { new KeyValuePair<string,string>("user_id", Global.Config.ID.ToString()) }));
            var body = await resp.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(body)) { (Access.DashBoard as DashBoard)?.OpenSnackbar("签到失败", "空响应", FluentAvalonia.UI.Controls.InfoBarSeverity.Error); return; }
            JsonNode? json; try { json = JsonNode.Parse(body); } catch { (Access.DashBoard as DashBoard)?.OpenSnackbar("签到失败", "响应格式错误", FluentAvalonia.UI.Controls.InfoBarSeverity.Error); return; }
            int status = json?["status"]?.GetValue<int>() ?? 0;
            string? message = json?["message"]?.GetValue<string>();
            int gained = json?["data"]?["get_traffic"]?.GetValue<int>() ?? 0;
            if (status == 200)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _trafficText.Text = $"剩余流量: {(MainWindow.Traffic / 1024) + gained}GB";
                    _signButton.IsVisible = false;
                    _signedBorder.IsVisible = true;
                });
                (Access.DashBoard as DashBoard)?.OpenSnackbar("签到成功", $"获得 {gained}GB 流量", FluentAvalonia.UI.Controls.InfoBarSeverity.Success);
            }
            else if (status == 403 && message == "你今天已经签到过了")
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _signButton.IsVisible = false;
                    _signedBorder.IsVisible = true;
                });
                (Access.DashBoard as DashBoard)?.OpenSnackbar("提示", "你今天已经签到过了", FluentAvalonia.UI.Controls.InfoBarSeverity.Informational);
            }
            else
            {
                (Access.DashBoard as DashBoard)?.OpenSnackbar("签到失败", message ?? "未知错误", FluentAvalonia.UI.Controls.InfoBarSeverity.Error);
            }
        }
        catch (Exception ex)
        {
            (Access.DashBoard as DashBoard)?.OpenSnackbar("签到异常", ex.Message, FluentAvalonia.UI.Controls.InfoBarSeverity.Error);
        }
        finally
        {
            if (_signButton != null) _signButton.IsEnabled = true;
        }
    }
}
