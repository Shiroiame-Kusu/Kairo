using System;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Newtonsoft.Json.Linq;
using Kairo.Utils;

namespace Kairo.Components;

public partial class HomePage : UserControl
{
    private bool _init;
    public HomePage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private async void OnLoaded(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_init) return;
        _init = true;
        WelcomeText.Text += Global.Config.Username;
        BandwidthText.Text = $"上行/下行带宽: {MainWindow.Inbound * 8 / 1024}/{MainWindow.Outbound * 8 / 1024}Mbps";
        TrafficText.Text = $"剩余流量: {MainWindow.Traffic / 1024}GB";
        _ = RefreshAnnouncement();
        _ = CheckSigned();
        _ = RefreshAvatar();
    }

    private async Task RefreshAnnouncement()
    {
        try
        {
            using HttpClient hc = new();
            var resp = await hc.GetAsync(Global.APIList.GetNotice);
            var json = JObject.Parse(await resp.Content.ReadAsStringAsync());
            if ((int)json["status"] == 200)
            {
                string txt = json["data"]?["broadcast"]?.ToString() ?? "暂无公告";
                Dispatcher.UIThread.Post(() => Announcement.Text = txt);
            }
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() => Announcement.Text = "获取公告失败: " + ex.Message);
        }
    }

    private async Task CheckSigned()
    {
        try
        {
            using HttpClient hc = new();
            hc.DefaultRequestHeaders.Add("Authorization", $"Bearer {Global.Config.AccessToken}");
            var resp = await hc.GetAsync($"{Global.APIList.GetSign}{Global.Config.ID}");
            var json = JObject.Parse(await resp.Content.ReadAsStringAsync());
            if ((int)json["status"] == 200 && (bool)(json["data"]?["status"] ?? false))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    SignButton.IsVisible = false;
                    SignedBorder.IsVisible = true;
                });
            }
        }
        catch { }
    }

    private async Task RefreshAvatar()
    {
        try
        {
            if (MainWindow.Avatar == null) return;
            using HttpClient hc = new();
            var bytes = await hc.GetByteArrayAsync(MainWindow.Avatar);
            using var ms = new System.IO.MemoryStream(bytes);
            var bmp = new Avalonia.Media.Imaging.Bitmap(ms);
            Dispatcher.UIThread.Post(() => AvatarImage.Source = bmp);
        }
        catch { }
    }

    private async void SignButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try
        {
            SignButton.IsEnabled = false;
            using HttpClient hc = new();
            hc.DefaultRequestHeaders.Add("Authorization", $"Bearer {Global.Config.AccessToken}");
            var resp = await hc.PostAsync($"{Global.APIList.GetSign}{Global.Config.ID}", null);
            var json = JObject.Parse(await resp.Content.ReadAsStringAsync());
            if ((int)json["status"] == 200)
            {
                int gained = (int)(json["data"]?["get_traffic"] ?? 0);
                Dispatcher.UIThread.Post(() =>
                {
                    TrafficText.Text = $"剩余流量: {(MainWindow.Traffic / 1024) + gained}GB";
                    SignButton.IsVisible = false;
                    SignedBorder.IsVisible = true;
                });
                (Access.DashBoard as DashBoard)?.OpenSnackbar("签到成功", $"获得 {gained}GB 流量", FluentAvalonia.UI.Controls.InfoBarSeverity.Success);
            }
            else if ((int)json["status"] == 403 && json["message"]?.ToString() == "你今天已经签到过了")
            {
                Dispatcher.UIThread.Post(() =>
                {
                    SignButton.IsVisible = false;
                    SignedBorder.IsVisible = true;
                });
                (Access.DashBoard as DashBoard)?.OpenSnackbar("提示", "你今天已经签到过了", FluentAvalonia.UI.Controls.InfoBarSeverity.Informational);
            }
            else
            {
                (Access.DashBoard as DashBoard)?.OpenSnackbar("签到失败", json["message"]?.ToString(), FluentAvalonia.UI.Controls.InfoBarSeverity.Error);
            }
        }
        catch (Exception ex)
        {
            (Access.DashBoard as DashBoard)?.OpenSnackbar("签到异常", ex.Message, FluentAvalonia.UI.Controls.InfoBarSeverity.Error);
        }
        finally
        {
            SignButton.IsEnabled = true;
        }
    }
}

