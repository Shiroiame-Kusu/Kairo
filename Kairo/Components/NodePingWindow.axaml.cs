using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Newtonsoft.Json.Linq;
using Kairo; // for Global

namespace Kairo.Components;

public partial class NodePingWindow : Window
{
    private readonly ObservableCollection<Row> _rows = new();
    private readonly DispatcherTimer _timer;
    private readonly Ping _pinger = new();
    private bool _isPinging;
    private readonly bool _useApi;
    private bool _permissionWarned;

    public NodePingWindow() : this(null, null) { }

    public NodePingWindow(IEnumerable<int>? nodes = null, string? hostPattern = null)
    {
        InitializeComponent();
        _useApi = nodes == null; // if no preset nodes, use API list
        if (Design.IsDesignMode)
        {
            _rows.Add(new Row { Node = 1, Host = "node1.locyanfrp.cn", LatencyMs = 32, Status = "成功" });
            _rows.Add(new Row { Node = 2, Host = "node2.locyanfrp.cn", LatencyMs = null, Status = "超时" });
        }
        else if (!_useApi)
        {
            var pattern = string.IsNullOrWhiteSpace(hostPattern) ? "node{0}.locyanfrp.cn" : hostPattern!;
            var set = new SortedSet<int>((nodes ?? Enumerable.Empty<int>()).Where(n => n > 0));
            foreach (var n in set)
            {
                _rows.Add(new Row { Node = n, Host = string.Format(pattern, n) });
            }
        }
        var grid = this.FindControl<DataGrid>("Grid");
        if (grid != null) grid.ItemsSource = _rows;
        UpdateStatus();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += async (_, __) => await TickAsync();
        Opened += async (_, __) =>
        {
            if (_useApi)
            {
                await LoadNodesFromApiAsync();
            }
            _timer.Start();
        };
        Closing += (_, __) => { try { _timer.Stop(); } catch { } };
        DetachedFromVisualTree += (_, __) => { try { _timer.Stop(); } catch { } };
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private async Task LoadNodesFromApiAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Global.Config.AccessToken) || Global.Config.ID == 0)
            {
                SetStatusText("未登录或令牌缺失");
                return;
            }
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("Authorization", $"Bearer {Global.Config.AccessToken}");
            var url = $"{Global.APIList.GetAllNodes}{Global.Config.ID}";
            var resp = await http.GetAsync(url);
            var content = await resp.Content.ReadAsStringAsync();
            var json = JObject.Parse(content);
            if ((int?)json["status"] != 200)
            {
                SetStatusText($"获取节点失败: {json["message"]?.ToString()}");
                return;
            }
            var list = json["data"]?["list"] as JArray;
            if (list == null || list.Count == 0)
            {
                SetStatusText("没有可用节点");
                return;
            }
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _rows.Clear();
                foreach (var item in list)
                {
                    int id = (int)(item["id"] ?? 0);
                    string ip = item["ip"]?.ToString() ?? string.Empty;
                    string host = item["hostname"]?.ToString() ?? string.Empty;
                    string target = !string.IsNullOrWhiteSpace(ip) ? ip : host;
                    if (string.IsNullOrWhiteSpace(target)) continue;
                    _rows.Add(new Row { Node = id, Host = target, Status = "等待中" });
                }
                UpdateStatus();
            });
        }
        catch (Exception ex)
        {
            SetStatusText($"获取失败: {ex.Message}");
        }
    }

    private async Task TickAsync()
    {
        if (_isPinging || _rows.Count == 0) return;
        _isPinging = true;
        try
        {
            var tasks = _rows.Select(row => PingOneAsync(row)).ToArray();
            await Task.WhenAll(tasks);
            UpdateStatus();
        }
        finally
        {
            _isPinging = false;
        }
    }

    private static bool IsPermissionError(Exception ex)
    {
        // Linux without CAP_NET_RAW/root typically throws SocketException (Permission denied)
        if (ex is PingException pe && pe.InnerException is SocketException se)
            return se.SocketErrorCode == SocketError.AccessDenied;
        if (ex is SocketException se2)
            return se2.SocketErrorCode == SocketError.AccessDenied;
        return ex.Message.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("Operation not permitted", StringComparison.OrdinalIgnoreCase);
    }

    private async Task PingOneAsync(Row row)
    {
        try
        {
            var reply = await _pinger.SendPingAsync(row.Host, 900);
            if (reply.Status == IPStatus.Success)
            {
                Dispatcher.UIThread.Post(() => { row.LatencyMs = reply.RoundtripTime; row.Status = "成功"; });
            }
            else
            {
                Dispatcher.UIThread.Post(() => { row.LatencyMs = null; row.Status = reply.Status == IPStatus.TimedOut ? "超时" : reply.Status.ToString(); });
            }
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                row.LatencyMs = null;
                if (IsPermissionError(ex))
                {
                    row.Status = "权限不足";
                    if (!_permissionWarned)
                    {
                        _permissionWarned = true;
                        SetStatusText("ICMP 权限不足，无法执行 ping（在 Linux 需 root 或给进程授予 CAP_NET_RAW）");
                    }
                }
                else
                {
                    row.Status = ex is PingException ? "失败" : "错误";
                }
            });
        }
    }

    private void UpdateStatus()
    {
        var status = this.FindControl<TextBlock>("StatusText");
        if (status == null) return;
        var ok = _rows.Count(r => r.LatencyMs.HasValue);
        status.Text = $"在线: {ok}/{_rows.Count}";
    }

    private void SetStatusText(string text)
    {
        var status = this.FindControl<TextBlock>("StatusText");
        if (status != null) status.Text = text;
    }

    private void CloseBtn_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        try { Close(); } catch { }
    }

    public class Row : INotifyPropertyChanged
    {
        private int _node;
        private string _host = string.Empty;
        private long? _latencyMs;
        private string _status = "等待中";

        public int Node { get => _node; set => SetField(ref _node, value); }
        public string Host { get => _host; set => SetField(ref _host, value); }
        public long? LatencyMs { get => _latencyMs; set { if (SetField(ref _latencyMs, value)) OnPropertyChanged(nameof(LatencyDisplay)); } }
        public string Status { get => _status; set => SetField(ref _status, value); }
        public string LatencyDisplay => LatencyMs.HasValue ? LatencyMs.Value.ToString() : "-";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }
    }
}
