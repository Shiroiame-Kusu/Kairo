using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Newtonsoft.Json.Linq;
using Kairo; // Global
using Kairo.Utils.Logger; // add

namespace Kairo.Components;

public partial class CreateProxyWindow : Window
{
    private TextBox? _name;
    private ComboBox? _type;
    private TextBox? _localIp;
    private TextBox? _localPort;
    private StackPanel? _remotePortPanel;
    private TextBox? _remotePort;
    private ComboBox? _node;
    private CheckBox? _encrypt;
    private CheckBox? _compress;
    private StackPanel? _secretPanel;
    private TextBox? _secret;
    private StackPanel? _domainPanel;
    private TextBox? _domain;
    private TextBlock? _status;
    private Button? _createBtn;
    private Button? _pingBtn;

    private readonly List<NodeItem> _nodes = new();

    public event Action<int, string>? Created; // proxy_id, proxy_name

    public CreateProxyWindow()
    {
        InitializeComponent();
        _name = this.FindControl<TextBox>("NameBox");
        _type = this.FindControl<ComboBox>("TypeBox");
        _localIp = this.FindControl<TextBox>("LocalIpBox");
        _localPort = this.FindControl<TextBox>("LocalPortBox");
        _remotePortPanel = this.FindControl<StackPanel>("RemotePortPanel");
        _remotePort = this.FindControl<TextBox>("RemotePortBox");
        _node = this.FindControl<ComboBox>("NodeBox");
        _encrypt = this.FindControl<CheckBox>("EncryptionBox");
        _compress = this.FindControl<CheckBox>("CompressionBox");
        _secretPanel = this.FindControl<StackPanel>("SecretKeyPanel");
        _secret = this.FindControl<TextBox>("SecretKeyBox");
        _domainPanel = this.FindControl<StackPanel>("DomainPanel");
        _domain = this.FindControl<TextBox>("DomainBox");
        _status = this.FindControl<TextBlock>("StatusText");
        _createBtn = this.FindControl<Button>("CreateButton");
        _pingBtn = this.FindControl<Button>("PingButton");

        if (_type != null)
            _type.SelectionChanged += (_, __) => UpdateVisibilityByType();

        if (_pingBtn != null)
            _pingBtn.IsEnabled = false; // enable after nodes loaded

        Opened += async (_, __) =>
        {
            UpdateVisibilityByType();
            await LoadNodesAsync();
        };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private void SetStatus(string text) { if (_status != null) _status.Text = text; }

    private string? GetSelectedType()
    {
        if (_type?.SelectedItem is ComboBoxItem cbi) return cbi.Content?.ToString();
        if (_type?.SelectedItem is string s) return s;
        return null;
    }

    private void UpdateVisibilityByType()
    {
        var t = (GetSelectedType() ?? string.Empty).ToLowerInvariant();
        bool needRemote = t == "tcp" || t == "udp"; // XTCP STCP HTTP HTTPS don't need remote_port
        bool needSecret = t == "xtcp" || t == "stcp";
        bool needDomain = t == "http" || t == "https";
        if (_remotePortPanel != null) _remotePortPanel.IsVisible = needRemote;
        if (_secretPanel != null) _secretPanel.IsVisible = needSecret;
        if (_domainPanel != null) _domainPanel.IsVisible = needDomain;
    }

    private async Task LoadNodesAsync()
    {
        try
        {
            if (Design.IsDesignMode)
            {
                _nodes.Clear();
                _nodes.Add(new NodeItem(1, "node1.locyanfrp.cn"));
                _nodes.Add(new NodeItem(2, "node2.locyanfrp.cn"));
                ApplyNodesToCombo();
                return;
            }
            if (string.IsNullOrWhiteSpace(Global.Config.AccessToken) || Global.Config.ID == 0)
            {
                SetStatus("未登录或令牌缺失");
                return;
            }
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {Global.Config.AccessToken}");
            var url = $"{Global.APIList.GetAllNodes}{Global.Config.ID}";
            var resp = await http.GetAsyncLogged(url);
            var content = await resp.Content.ReadAsStringAsync();
            var json = JObject.Parse(content);
            if ((int?)json["status"] != 200)
            {
                SetStatus($"获取节点失败: {json["message"]}");
                return;
            }
            var list = json["data"]?["list"] as JArray; // v3: data.list
            _nodes.Clear();
            if (list != null)
            {
                foreach (var item in list)
                {
                    int id = (int)(item["id"] ?? 0);
                    string ip = item["ip"]?.ToString() ?? string.Empty;
                    string host = item["host"]?.ToString() ?? string.Empty; // v3 field is host
                    string label = !string.IsNullOrWhiteSpace(ip) ? ip : host;
                    if (id > 0 && !string.IsNullOrWhiteSpace(label))
                        _nodes.Add(new NodeItem(id, label));
                }
            }
            ApplyNodesToCombo();
        }
        catch (Exception ex)
        {
            SetStatus($"获取节点失败: {ex.Message}");
        }
        finally
        {
            if (_pingBtn != null)
                _pingBtn.IsEnabled = true; // allow ping regardless of populated count
        }
    }

    private void ApplyNodesToCombo()
    {
        if (_node == null) return;
        _node.ItemsSource = _nodes;
        _node.SelectedIndex = _nodes.Count > 0 ? 0 : -1;
    }

    private async void CreateBtn_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_createBtn != null) _createBtn.IsEnabled = false;
            var t = (GetSelectedType() ?? string.Empty).ToLowerInvariant();
            var name = _name?.Text?.Trim();
            var localIp = _localIp?.Text?.Trim();
            var localPortStr = _localPort?.Text?.Trim();
            var remotePortStr = _remotePort?.Text?.Trim();
            var nodeItem = _node?.SelectedItem as NodeItem;
            var useEnc = _encrypt?.IsChecked == true;
            var useComp = _compress?.IsChecked == true;
            var secret = _secret?.Text?.Trim();
            var domain = _domain?.Text?.Trim();

            // Basic validation
            if (string.IsNullOrWhiteSpace(name)) { SetStatus("请输入隧道名称"); return; }
            if (string.IsNullOrWhiteSpace(t)) { SetStatus("请选择协议类型"); return; }
            if (string.IsNullOrWhiteSpace(localIp)) { SetStatus("请输入本地 IP"); return; }
            if (!int.TryParse(localPortStr, out var localPort) || localPort <= 0 || localPort > 65535)
            { SetStatus("本地端口非法"); return; }
            if (nodeItem == null) { SetStatus("请选择节点"); return; }

            bool needRemote = t == "tcp" || t == "udp";
            bool needSecret = t == "xtcp" || t == "stcp";
            bool needDomain = t == "http" || t == "https";
            int remotePort = 0;
            if (needRemote)
            {
                if (!int.TryParse(remotePortStr, out remotePort) || remotePort <= 0 || remotePort > 65535)
                { SetStatus("远端端口非法"); return; }
            }
            if (needSecret && string.IsNullOrWhiteSpace(secret)) { SetStatus("请输入访问密钥"); return; }
            if (needDomain && string.IsNullOrWhiteSpace(domain)) { SetStatus("请输入域名"); return; }

            if (string.IsNullOrWhiteSpace(Global.Config.AccessToken) || Global.Config.ID == 0)
            {
                SetStatus("未登录或令牌缺失");
                return;
            }

            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {Global.Config.AccessToken}");
            var form = new List<KeyValuePair<string, string>>
            {
                new("user_id", Global.Config.ID.ToString()),
                new("name", name!),
                new("local_ip", localIp!),
                new("type", t.ToUpperInvariant()),
                new("local_port", localPort.ToString()),
                new("node_id", nodeItem.Id.ToString()),
                new("use_encryption", (useEnc ? "true" : "false")),
                new("use_compression", (useComp ? "true" : "false")),
            };
            if (needRemote)
                form.Add(new("remote_port", remotePort.ToString()));
            if (needSecret)
                form.Add(new("secret_key", secret ?? string.Empty));
            if (needDomain)
                form.Add(new("domain", domain ?? string.Empty));

            var resp = await http.PutAsyncLogged(Global.APIList.Tunnel, new FormUrlEncodedContent(form));
            var content = await resp.Content.ReadAsStringAsync();
            JObject json;
            try { json = JObject.Parse(content); }
            catch { SetStatus("服务器返回异常"); return; }
            if ((int?)json["status"] == 200)
            {
                int proxyId = json["data"]?["tunnel_id"]?.Value<int>() ?? 0;
                string proxyName = name!;
                Created?.Invoke(proxyId, proxyName);
                try { Close(); } catch { }
            }
            else
            {
                var msg = json["message"]?.ToString() ?? "创建失败";
                SetStatus(msg);
            }
        }
        catch (Exception ex)
        {
            SetStatus($"创建失败: {ex.Message}");
        }
        finally
        {
            if (_createBtn != null) _createBtn.IsEnabled = true;
        }
    }

    private void CancelBtn_OnClick(object? sender, RoutedEventArgs e)
    {
        try { Close(); } catch { }
    }

    private void PingBtn_OnClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var win = new NodePingWindow
            {
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            win.Show(this);
        }
        catch (Exception ex)
        {
            SetStatus("打开 Ping 窗口失败: " + ex.Message);
        }
    }

    public record NodeItem(int Id, string Label)
    {
        public override string ToString() => $"[{Id}] {Label}";
    }
}
