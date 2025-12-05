using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Kairo.Utils.Logger;

namespace Kairo.ViewModels
{
    public class CreateProxyWindowViewModel : ViewModelBase, IDisposable
    {
        private readonly ObservableCollection<NodeItem> _nodes = new();
        private readonly RelayCommand _pingCommand;
        private bool _canPing;
        private string _name = string.Empty;
        private string _selectedType = "tcp";
        private string _localIp = "127.0.0.1";
        private string _localPort = string.Empty;
        private string _remotePort = string.Empty;
        private NodeItem? _selectedNode;
        private bool _useEncryption;
        private bool _useCompression;
        private string _secretKey = string.Empty;
        private string _domain = string.Empty;
        private string _statusText = string.Empty;

        public IReadOnlyList<string> Types { get; } = new[] { "tcp", "udp", "xtcp", "stcp", "http", "https" };
        public ObservableCollection<NodeItem> Nodes => _nodes;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string SelectedType
        {
            get => _selectedType;
            set
            {
                if (SetProperty(ref _selectedType, value))
                {
                    OnPropertyChanged(nameof(NeedRemotePort));
                    OnPropertyChanged(nameof(NeedSecretKey));
                    OnPropertyChanged(nameof(NeedDomain));
                }
            }
        }

        public string LocalIp
        {
            get => _localIp;
            set => SetProperty(ref _localIp, value);
        }

        public string LocalPort
        {
            get => _localPort;
            set => SetProperty(ref _localPort, value);
        }

        public string RemotePort
        {
            get => _remotePort;
            set => SetProperty(ref _remotePort, value);
        }

        public NodeItem? SelectedNode
        {
            get => _selectedNode;
            set => SetProperty(ref _selectedNode, value);
        }

        public bool UseEncryption
        {
            get => _useEncryption;
            set => SetProperty(ref _useEncryption, value);
        }

        public bool UseCompression
        {
            get => _useCompression;
            set => SetProperty(ref _useCompression, value);
        }

        public string SecretKey
        {
            get => _secretKey;
            set => SetProperty(ref _secretKey, value);
        }

        public string Domain
        {
            get => _domain;
            set => SetProperty(ref _domain, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public bool NeedRemotePort => TypeNormalized is "tcp" or "udp";
        public bool NeedSecretKey => TypeNormalized is "xtcp" or "stcp";
        public bool NeedDomain => TypeNormalized is "http" or "https";

        public bool CanPing
        {
            get => _canPing;
            private set
            {
                if (SetProperty(ref _canPing, value))
                {
                    _pingCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public AsyncRelayCommand CreateCommand { get; }
        public RelayCommand CancelCommand { get; }
        public RelayCommand PingCommand => _pingCommand;

        public event Action? RequestClose;
        public event Action? RequestPingWindow;
        public event Action<int, string>? ProxyCreated;

        public CreateProxyWindowViewModel()
        {
            CreateCommand = new AsyncRelayCommand(CreateAsync);
            CancelCommand = new RelayCommand(() => RequestClose?.Invoke());
            _pingCommand = new RelayCommand(() => RequestPingWindow?.Invoke(), () => CanPing);
        }

        public void Dispose()
        {
            // no unmanaged resources
        }

        public async Task OnOpenedAsync()
        {
            OnPropertyChanged(nameof(NeedRemotePort));
            OnPropertyChanged(nameof(NeedSecretKey));
            OnPropertyChanged(nameof(NeedDomain));
            await LoadNodesAsync();
        }

        private async Task LoadNodesAsync()
        {
            try
            {
                if (Design.IsDesignMode)
                {
                    _nodes.Clear();
                    _nodes.Add(new NodeItem(1, "node1.locyanfrp.cn")
                    {
                        DisplayName = "示例节点 1",
                        PortRangeDisplay = "10000-10100",
                        DescriptionDisplay = "本地演示节点",
                    });
                    _nodes.Add(new NodeItem(2, "node2.locyanfrp.cn")
                    {
                        DisplayName = "示例节点 2",
                        PortRangeDisplay = "20000-20100",
                        DescriptionDisplay = "备用演示节点",
                    });
                    SelectedNode = _nodes.FirstOrDefault();
                    CanPing = true;
                    return;
                }

                if (string.IsNullOrWhiteSpace(Global.Config.AccessToken) || Global.Config.ID == 0)
                {
                    StatusText = "未登录或令牌缺失";
                    return;
                }

                using var http = new HttpClient();
                http.DefaultRequestHeaders.Add("Authorization", $"Bearer {Global.Config.AccessToken}");
                var url = $"{Global.APIList.GetAllNodes}{Global.Config.ID}";
                var resp = await http.GetAsyncLogged(url);
                var content = await resp.Content.ReadAsStringAsync();
                var json = JsonNode.Parse(content);
                if (json?["status"]?.GetValue<int>() != 200)
                {
                    StatusText = $"获取节点失败: {json?["message"]?.GetValue<string>()}";
                    return;
                }

                var list = json?["data"]?["list"] as JsonArray;
                _nodes.Clear();
                if (list != null)
                {
                    foreach (var item in list)
                    {
                        int id = item?["id"]?.GetValue<int>() ?? 0;
                        string ip = item?["ip"]?.GetValue<string>() ?? string.Empty;
                        string host = item?["host"]?.GetValue<string>() ?? string.Empty;
                        string label = !string.IsNullOrWhiteSpace(ip) ? ip : host;
                        var additional = item?["additional"] as JsonObject;
                        string name = item?["name"]?.GetValue<string>() ?? label;
                        string? description = additional?["description"]?.GetValue<string>();
                        if (string.IsNullOrWhiteSpace(description))
                            description = item?["description"]?.GetValue<string>();
                        var portRangeArray = item?["port_range"] as JsonArray;
                        var portRanges = new List<string>();
                        if (portRangeArray != null)
                        {
                            foreach (var token in portRangeArray)
                            {
                                if (token is JsonNode tk)
                                {
                                    var range = tk.GetValue<string>();
                                    if (!string.IsNullOrWhiteSpace(range))
                                        portRanges.Add(range);
                                }
                            }
                        }

                        string portRangeDisplay = portRanges.Count > 0 ? string.Join(", ", portRanges) : "";
                        if (id > 0 && !string.IsNullOrWhiteSpace(label))
                        {
                            _nodes.Add(new NodeItem(id, label)
                            {
                                DisplayName = string.IsNullOrWhiteSpace(name) ? label : name,
                                PortRangeDisplay = string.IsNullOrWhiteSpace(portRangeDisplay) ? "—" : portRangeDisplay,
                                DescriptionDisplay = string.IsNullOrWhiteSpace(description) ? "暂无" : description!,
                            });
                        }
                    }
                }

                SelectedNode = _nodes.FirstOrDefault();
            }
            catch (Exception ex)
            {
                StatusText = $"获取节点失败: {ex.Message}";
            }
            finally
            {
                CanPing = true;
            }
        }

        private async Task CreateAsync()
        {
            try
            {
                var type = TypeNormalized;
                var name = Name?.Trim();
                var localIp = LocalIp?.Trim();
                var localPortStr = LocalPort?.Trim();
                var remotePortStr = RemotePort?.Trim();
                var nodeItem = SelectedNode;
                var useEnc = UseEncryption;
                var useComp = UseCompression;
                var secret = SecretKey?.Trim();
                var domain = Domain?.Trim();

                if (string.IsNullOrWhiteSpace(name)) { StatusText = "请输入隧道名称"; return; }
                if (string.IsNullOrWhiteSpace(type)) { StatusText = "请选择协议类型"; return; }
                if (string.IsNullOrWhiteSpace(localIp)) { StatusText = "请输入本地 IP"; return; }
                if (!int.TryParse(localPortStr, out var localPort) || localPort <= 0 || localPort > 65535)
                { StatusText = "本地端口非法"; return; }
                if (nodeItem == null) { StatusText = "请选择节点"; return; }

                bool needRemote = NeedRemotePort;
                bool needSecret = NeedSecretKey;
                bool needDomain = NeedDomain;
                int remotePort = 0;
                if (needRemote)
                {
                    if (string.IsNullOrWhiteSpace(remotePortStr))
                    {
                        StatusText = "正在请求随机远端端口…";
                        var port = await TryGetRandomPortAsync(nodeItem.Id);
                        if (port <= 0)
                        {
                            StatusText = "获取随机端口失败，请手动输入远端端口";
                            return;
                        }
                        remotePort = port;
                        RemotePort = port.ToString();
                    }
                    else if (!int.TryParse(remotePortStr, out remotePort) || remotePort <= 0 || remotePort > 65535)
                    { StatusText = "远端端口非法"; return; }
                }
                if (needSecret && string.IsNullOrWhiteSpace(secret)) { StatusText = "请输入访问密钥"; return; }
                if (needDomain && string.IsNullOrWhiteSpace(domain)) { StatusText = "请输入域名"; return; }

                if (string.IsNullOrWhiteSpace(Global.Config.AccessToken) || Global.Config.ID == 0)
                {
                    StatusText = "未登录或令牌缺失";
                    return;
                }

                using var http = new HttpClient();
                http.DefaultRequestHeaders.Add("Authorization", $"Bearer {Global.Config.AccessToken}");
                var form = new List<KeyValuePair<string, string>>
                {
                    new("user_id", Global.Config.ID.ToString()),
                    new("name", name!),
                    new("local_ip", localIp!),
                    new("type", type.ToUpperInvariant()),
                    new("local_port", localPort.ToString()),
                    new("node_id", nodeItem.Id.ToString()),
                    new("use_encryption", useEnc ? "true" : "false"),
                    new("use_compression", useComp ? "true" : "false"),
                };
                if (needRemote)
                    form.Add(new("remote_port", remotePort.ToString()));
                if (needSecret)
                    form.Add(new("secret_key", secret ?? string.Empty));
                if (needDomain)
                    form.Add(new("domain", domain ?? string.Empty));

                var resp = await http.PutAsyncLogged(Global.APIList.Tunnel, new FormUrlEncodedContent(form));
                var content = await resp.Content.ReadAsStringAsync();
                JsonNode? json;
                try { json = JsonNode.Parse(content); }
                catch { StatusText = "服务器返回异常"; return; }
                if (json?["status"]?.GetValue<int>() == 200)
                {
                    int proxyId = json?["data"]?["tunnel_id"]?.GetValue<int>() ?? 0;
                    string proxyName = name!;
                    ProxyCreated?.Invoke(proxyId, proxyName);
                }
                else
                {
                    var msg = json?["message"]?.GetValue<string>() ?? "创建失败";
                    StatusText = msg;
                }
            }
            catch (Exception ex)
            {
                StatusText = $"创建失败: {ex.Message}";
            }
        }

        private async Task<int> TryGetRandomPortAsync(int nodeId)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(Global.Config.AccessToken) || Global.Config.ID == 0)
                    return 0;
                using var http = new HttpClient();
                http.DefaultRequestHeaders.Add("Authorization", $"Bearer {Global.Config.AccessToken}");
                var url = $"{Global.APIList.GetRandomPort}?user_id={Global.Config.ID}&node_id={nodeId}";
                var resp = await http.GetAsyncLogged(url);
                var content = await resp.Content.ReadAsStringAsync();
                var json = JsonNode.Parse(content);
                if (json?["status"]?.GetValue<int>() != 200)
                    return 0;
                var port = json?["data"]?["port"]?.GetValue<int>() ?? 0;
                return port;
            }
            catch
            {
                return 0;
            }
        }

        private string TypeNormalized => (SelectedType ?? string.Empty).Trim().ToLowerInvariant();

        public record NodeItem(int Id, string Label)
        {
            public string DisplayName { get; init; } = string.Empty;
            public string PortRangeDisplay { get; init; } = string.Empty;
            public string DescriptionDisplay { get; init; } = string.Empty;
            public string DisplayLabel => string.IsNullOrWhiteSpace(DisplayName) ? $"[{Id}] {Label}" : DisplayName;
            public override string ToString() => $"[{Id}] {Label}";
        }
    }
}
