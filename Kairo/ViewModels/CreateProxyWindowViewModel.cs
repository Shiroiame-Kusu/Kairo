using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Kairo.Models.Api;
using Kairo.Utils;

namespace Kairo.ViewModels
{
    public class CreateProxyWindowViewModel : ViewModelBase, IDisposable
    {
        private readonly ObservableCollection<NodeItem> _nodes = new();
        private readonly RelayCommand _pingCommand;
        private bool _canPing;
        private string _remark = string.Empty;
        private string _selectedType = "tcp";
        private string _localIp = "127.0.0.1";
        private string _localPort = string.Empty;
        private string _remotePort = string.Empty;
        private NodeItem? _selectedNode;
        private string _domain = string.Empty;
        private string _statusText = string.Empty;

        public IReadOnlyList<string> Types { get; } = new[] { "tcp", "udp", "http", "https" };
        public ObservableCollection<NodeItem> Nodes => _nodes;

        public string Remark
        {
            get => _remark;
            set => SetProperty(ref _remark, value);
        }

        public string SelectedType
        {
            get => _selectedType;
            set
            {
                if (SetProperty(ref _selectedType, value))
                {
                    OnPropertyChanged(nameof(NeedRemotePort));
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
                    _nodes.Add(new NodeItem(1, "node1.lolia.link")
                    {
                        DisplayName = "示例节点 1",
                        DescriptionDisplay = "本地演示节点",
                    });
                    _nodes.Add(new NodeItem(2, "node2.lolia.link")
                    {
                        DisplayName = "示例节点 2",
                        DescriptionDisplay = "备用演示节点",
                    });
                    SelectedNode = _nodes.FirstOrDefault();
                    CanPing = true;
                    return;
                }

                if (string.IsNullOrWhiteSpace(Global.Config.AccessToken))
                {
                    StatusText = "未登录或令牌缺失";
                    return;
                }

                var result = await LoliaApiClient.GetNodeListAsync();
                if (!result.IsSuccess)
                {
                    StatusText = $"获取节点失败: {result.Msg}";
                    return;
                }

                _nodes.Clear();
                if (result.Data?.Nodes is { } nodes)
                {
                    foreach (var n in nodes)
                    {
                        if (n.Id <= 0) continue;
                        string label = !string.IsNullOrWhiteSpace(n.IpAddress)
                            ? n.IpAddress
                            : n.Name;
                        var protocols = n.SupportedProtocols.Count > 0
                            ? string.Join(", ", n.SupportedProtocols)
                            : "—";
                        _nodes.Add(new NodeItem(n.Id, label)
                        {
                            DisplayName = n.Name,
                            DescriptionDisplay = $"{n.Sponsor} | {n.Bandwidth}Mbps | {protocols}",
                        });
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
                CanPing = _nodes.Count > 0;
            }
        }

        private async Task CreateAsync()
        {
            try
            {
                var type = TypeNormalized;
                var remark = Remark?.Trim();
                var localIp = LocalIp?.Trim();
                var localPortStr = LocalPort?.Trim();
                var remotePortStr = RemotePort?.Trim();
                var nodeItem = SelectedNode;
                var domain = Domain?.Trim();

                if (string.IsNullOrWhiteSpace(type)) { StatusText = "请选择协议类型"; return; }
                if (string.IsNullOrWhiteSpace(localIp)) { StatusText = "请输入本地 IP"; return; }
                if (!int.TryParse(localPortStr, out var localPort) || localPort <= 0 || localPort > 65535)
                { StatusText = "本地端口非法"; return; }
                if (nodeItem == null) { StatusText = "请选择节点"; return; }

                bool needRemote = NeedRemotePort;
                bool needDomain = NeedDomain;
                int remotePort = 0;
                if (needRemote)
                {
                    if (!string.IsNullOrWhiteSpace(remotePortStr))
                    {
                        if (!int.TryParse(remotePortStr, out remotePort) || remotePort <= 0 || remotePort > 65535)
                        { StatusText = "远端端口非法"; return; }
                    }
                    // remotePort=0 lets the server auto-assign
                }
                if (needDomain && string.IsNullOrWhiteSpace(domain)) { StatusText = "请输入域名"; return; }

                if (string.IsNullOrWhiteSpace(Global.Config.AccessToken))
                {
                    StatusText = "未登录或令牌缺失";
                    return;
                }

                var request = new CreateTunnelRequest
                {
                    NodeId = nodeItem.Id,
                    Type = type,
                    LocalIp = localIp!,
                    LocalPort = localPort,
                    RemotePort = remotePort,
                    CustomDomain = needDomain ? (domain ?? string.Empty) : string.Empty,
                    Remark = remark ?? string.Empty,
                };

                var result = await LoliaApiClient.CreateTunnelAsync(request);
                if (result.IsSuccess)
                {
                    int proxyId = result.Data?["id"]?.GetValue<int>() ?? 0;
                    string proxyName = !string.IsNullOrWhiteSpace(remark)
                        ? remark
                        : (result.Data?["name"]?.GetValue<string>() ?? string.Empty);
                    ProxyCreated?.Invoke(proxyId, proxyName);
                }
                else
                {
                    StatusText = result.Msg ?? "创建失败";
                }
            }
            catch (Exception ex)
            {
                StatusText = $"创建失败: {ex.Message}";
            }
        }

        private string TypeNormalized => (SelectedType ?? string.Empty).Trim().ToLowerInvariant();

        public record NodeItem(int Id, string Label)
        {
            public string DisplayName { get; init; } = string.Empty;
            public string DescriptionDisplay { get; init; } = string.Empty;
            public string DisplayLabel => string.IsNullOrWhiteSpace(DisplayName) ? $"[{Id}] {Label}" : DisplayName;
            public override string ToString() => $"[{Id}] {Label}";
        }
    }
}
