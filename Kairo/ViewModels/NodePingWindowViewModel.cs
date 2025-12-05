using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using Avalonia.Threading;
using Kairo.Utils.Logger;

namespace Kairo.ViewModels
{
    public class NodePingWindowViewModel : ViewModelBase, IDisposable
    {
        private readonly ObservableCollection<NodePingRow> _rows = new();
        private readonly DispatcherTimer _timer;
        private readonly bool _useApi;
        private readonly IEnumerable<int>? _presetNodes;
        private readonly string? _hostPattern;
        private readonly HttpClient _http = new();
        private bool _isPinging;
        private bool _permissionWarned;
        private string _statusText = string.Empty;

        public ObservableCollection<NodePingRow> Rows => _rows;

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public RelayCommand CloseCommand { get; }

        public event Action? RequestClose;

        public NodePingWindowViewModel(IEnumerable<int>? nodes = null, string? hostPattern = null)
        {
            _useApi = nodes == null;
            _presetNodes = nodes;
            _hostPattern = hostPattern;
            CloseCommand = new RelayCommand(() => RequestClose?.Invoke());
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += async (_, _) => await TickAsync();
        }

        public void Dispose()
        {
            try
            {
                _timer.Stop();
            }
            catch
            {
            }

            _http.Dispose();
        }

        public async Task OnOpenedAsync()
        {
            if (_useApi)
            {
                await LoadNodesFromApiAsync();
            }
            else
            {
                PopulatePresetNodes();
            }

            _timer.Start();
        }

        public void OnClosed()
        {
            try
            {
                _timer.Stop();
            }
            catch
            {
            }
        }

        private void PopulatePresetNodes()
        {
            _rows.Clear();
            var pattern = string.IsNullOrWhiteSpace(_hostPattern) ? "node{0}.locyanfrp.cn" : _hostPattern;
            var set = new SortedSet<int>((_presetNodes ?? Enumerable.Empty<int>()).Where(n => n > 0));
            foreach (var n in set)
            {
                _rows.Add(new NodePingRow { Node = n, Host = string.Format(pattern, n), Status = "等待中" });
            }
            UpdateStatus();
        }

        private async Task LoadNodesFromApiAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(Global.Config.AccessToken) || Global.Config.ID == 0)
                {
                    StatusText = "未登录或令牌缺失";
                    return;
                }

                _http.DefaultRequestHeaders.Remove("Authorization");
                _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {Global.Config.AccessToken}");
                var url = $"{Global.APIList.GetAllNodes}{Global.Config.ID}";
                var resp = await _http.GetAsyncLogged(url);
                var content = await resp.Content.ReadAsStringAsync();
                var json = System.Text.Json.Nodes.JsonNode.Parse(content);
                if (json?["status"]?.GetValue<int>() != 200)
                {
                    StatusText = $"获取节点失败: {json?["message"]?.GetValue<string>()}";
                    return;
                }

                var list = json?["data"]?["list"] as System.Text.Json.Nodes.JsonArray;
                if (list == null || list.Count == 0)
                {
                    StatusText = "没有可用节点";
                    return;
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _rows.Clear();
                    foreach (var item in list)
                    {
                        int id = item?["id"]?.GetValue<int>() ?? 0;
                        string ip = item?["ip"]?.GetValue<string>() ?? string.Empty;
                        string host = item?["host"]?.GetValue<string>() ?? string.Empty;
                        string target = !string.IsNullOrWhiteSpace(ip) ? ip : host;
                        if (string.IsNullOrWhiteSpace(target)) continue;
                        _rows.Add(new NodePingRow { Node = id, Host = target, Status = "等待中" });
                    }
                    UpdateStatus();
                });
            }
            catch (Exception ex)
            {
                StatusText = $"获取失败: {ex.Message}";
            }
        }

        private async Task TickAsync()
        {
            if (_isPinging || _rows.Count == 0) return;
            _isPinging = true;
            try
            {
                await Task.WhenAll(_rows.Select(row => PingOneAsync(row)));
                UpdateStatus();
            }
            finally
            {
                _isPinging = false;
            }
        }

        private async Task PingOneAsync(NodePingRow row)
        {
            try
            {
                using var ping = new Ping();
                var timeout = 900;
                var reply = await ping.SendPingAsync(row.Host, timeout);
                if (reply.Status == IPStatus.Success)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        row.LatencyMs = reply.RoundtripTime;
                        row.Status = "成功";
                    });
                }
                else
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        row.LatencyMs = null;
                        row.Status = reply.Status == IPStatus.TimedOut ? "超时" : reply.Status.ToString();
                    });
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
                            StatusText = "ICMP 权限不足，无法执行 ping（在 Linux 需 root 或给进程授予 CAP_NET_RAW）";
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
            var ok = _rows.Count(r => r.LatencyMs.HasValue);
            StatusText = $"在线: {ok}/{_rows.Count}";
        }

        private static bool IsPermissionError(Exception ex)
        {
            if (ex is PingException pe && pe.InnerException is SocketException se)
                return se.SocketErrorCode == SocketError.AccessDenied;
            if (ex is SocketException se2)
                return se2.SocketErrorCode == SocketError.AccessDenied;
            return ex.Message.Contains("Permission denied", StringComparison.OrdinalIgnoreCase) ||
                   ex.Message.Contains("Operation not permitted", StringComparison.OrdinalIgnoreCase);
        }
    }

    public class NodePingRow : ViewModelBase
    {
        private int _node;
        private string _host = string.Empty;
        private long? _latencyMs;
        private string _status = string.Empty;

        public int Node
        {
            get => _node;
            set => SetProperty(ref _node, value);
        }

        public string Host
        {
            get => _host;
            set => SetProperty(ref _host, value);
        }

        public long? LatencyMs
        {
            get => _latencyMs;
            set
            {
                if (SetProperty(ref _latencyMs, value))
                {
                    OnPropertyChanged(nameof(LatencyDisplay));
                }
            }
        }

        public string Status
        {
            get => _status;
            set
            {
                if (SetProperty(ref _status, value))
                {
                    OnPropertyChanged(nameof(LatencyDisplay));
                }
            }
        }

        public string LatencyDisplay => LatencyMs.HasValue ? LatencyMs.Value.ToString() : Status;
    }
}
