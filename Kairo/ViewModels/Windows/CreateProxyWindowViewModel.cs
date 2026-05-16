using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Kairo.Core.Models;
using Kairo.Core.Providers;
using Kairo.Utils;

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

                if (!ApiClient.TryEnsureLoggedIn(out var error))
                {
                    StatusText = error!;
                    return;
                }

                using var api = new ApiClient();
                var result = await api.GetNodesAsync();
                if (!result.Success)
                {
                    StatusText = $"获取节点失败: {result.Message}";
                    return;
                }

                _nodes.Clear();
                foreach (var node in result.Data ?? Array.Empty<FrpNode>())
                {
                    var label = GetNodeLabel(node);
                    var portRangeDisplay = node.PortRanges?.Count > 0 ? string.Join(", ", node.PortRanges) : "";
                    if (node.Id > 0 && !string.IsNullOrWhiteSpace(label))
                    {
                        _nodes.Add(new NodeItem(node.Id, label)
                        {
                            DisplayName = string.IsNullOrWhiteSpace(node.Name) ? label : node.Name,
                            PortRangeDisplay = string.IsNullOrWhiteSpace(portRangeDisplay) ? "—" : portRangeDisplay,
                            DescriptionDisplay = GetNodeDescription(node),
                        });
                    }
                }

                SelectedNode = _nodes.FirstOrDefault();
                CanPing = Global.CurrentProvider.Type != FrpProviderType.Lolia && _nodes.Count > 0;
            }
            catch (Exception ex)
            {
                Kairo.Utils.Logger.Logger.Exception("Unhandled exception in Kairo/ViewModels/Windows/CreateProxyWindowViewModel.cs:209", ex);
                StatusText = $"获取节点失败: {ex.Message}";
                CanPing = false;
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
                        if (Global.CurrentProvider.Type == FrpProviderType.Lolia)
                        {
                            StatusText = "LoliaFRP 暂不支持随机端口，请手动输入远端端口";
                            return;
                        }
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

                if (!ApiClient.TryEnsureLoggedIn(out var err))
                {
                    StatusText = err!;
                    return;
                }

                using var api = new ApiClient();
                var result = await api.CreateTunnelAsync(new CreateFrpTunnelRequest
                {
                    Name = name!,
                    LocalIp = localIp!,
                    Type = type,
                    LocalPort = localPort,
                    NodeId = nodeItem.Id,
                    UseEncryption = useEnc,
                    UseCompression = useComp,
                    RemotePort = needRemote ? remotePort : null,
                    SecretKey = needSecret ? secret ?? string.Empty : string.Empty,
                    Domain = needDomain ? domain ?? string.Empty : string.Empty
                });
                if (result.Success && result.Data != null)
                {
                    ProxyCreated?.Invoke(result.Data.TunnelId, string.IsNullOrWhiteSpace(result.Data.TunnelName) ? name! : result.Data.TunnelName);
                }
                else
                {
                    StatusText = result.Message;
                }
            }
            catch (Exception ex)
            {
                Kairo.Utils.Logger.Logger.Exception("Unhandled exception in Kairo/ViewModels/Windows/CreateProxyWindowViewModel.cs:296", ex);
                StatusText = $"创建失败: {ex.Message}";
            }
        }

        private static string GetNodeLabel(FrpNode node) =>
            FirstNonEmpty(node.Ip, node.Host, node.Name, node.Id > 0 ? $"Node{node.Id}" : string.Empty);

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            return string.Empty;
        }

        private static string GetNodeDescription(FrpNode node)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(node.RegionCode)) parts.Add($"地区: {node.RegionCode}");
            if (!string.IsNullOrWhiteSpace(node.Status)) parts.Add($"状态: {node.Status}");
            if (node.Bandwidth > 0) parts.Add($"带宽: {node.Bandwidth}Mbps");
            if (node.Load > 0) parts.Add($"负载: {node.Load:0.##}%");
            if (!string.IsNullOrWhiteSpace(node.Sponsor)) parts.Add($"赞助: {node.Sponsor}");
            if (node.NeedKyc) parts.Add("需要实名");
            if (node.BeianRequired) parts.Add("需要备案");
            if (!string.IsNullOrWhiteSpace(node.Description)) parts.Add(node.Description);
            return parts.Count == 0 ? "暂无" : string.Join(" · ", parts);
        }

        private async Task<int> TryGetRandomPortAsync(int nodeId)
        {
            try
            {
                if (!ApiClient.IsLoggedIn)
                    return 0;
                using var api = new ApiClient();
                var result = await api.GetRandomPortAsync(nodeId);
                return result.Success ? result.Data : 0;
            }
            catch (System.Exception ex)
            {
                Kairo.Utils.Logger.Logger.Exception("Unhandled exception in Kairo/ViewModels/Windows/CreateProxyWindowViewModel.cs:338", ex);
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
