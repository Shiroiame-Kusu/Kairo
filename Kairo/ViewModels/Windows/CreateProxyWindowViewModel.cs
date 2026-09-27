using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Kairo.Core.Localization;
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
        private bool _isStatusError;
        private bool _isLoadingNodes;

        public IReadOnlyList<string> Types { get; }
        public ObservableCollection<NodeItem> Nodes => _nodes;

        /// <summary>LoliaFRP 创建隧道时不接受加密、压缩等高级配置</summary>
        public bool ShowAdvancedOptions => !IsLolia;

        private static bool IsLolia => Global.CurrentProvider.Type == FrpProviderType.Lolia;

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
            set
            {
                if (SetProperty(ref _statusText, value))
                    OnPropertyChanged(nameof(HasStatus));
            }
        }

        public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);

        /// <summary>状态文本是否为错误（错误显示为红色，进度提示为普通颜色）</summary>
        public bool IsStatusError
        {
            get => _isStatusError;
            private set => SetProperty(ref _isStatusError, value);
        }

        public bool IsLoadingNodes
        {
            get => _isLoadingNodes;
            private set
            {
                if (SetProperty(ref _isLoadingNodes, value))
                    OnPropertyChanged(nameof(NodePlaceholder));
            }
        }

        public string NodePlaceholder => L.T(IsLoadingNodes ? "create.loadingNodes" : _nodes.Count == 0 ? "create.noNodes" : "create.chooseNode");

        public void SetError(string message)
        {
            IsStatusError = true;
            StatusText = message;
        }

        private void SetProgress(string message)
        {
            IsStatusError = false;
            StatusText = message;
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
            // LoliaFRP 只支持 tcp/udp/http/https
            Types = IsLolia
                ? new[] { "tcp", "udp", "http", "https" }
                : new[] { "tcp", "udp", "xtcp", "stcp", "http", "https" };
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
            IsLoadingNodes = true;
            try
            {
                if (Design.IsDesignMode)
                {
                    _nodes.Clear();
                    _nodes.Add(new NodeItem(1, "node1.locyanfrp.cn")
                    {
                        DisplayName = L.T("create.demo.node1"),
                        PortRangeDisplay = "10000-10100",
                        DescriptionDisplay = L.T("create.demo.node1Description"),
                    });
                    _nodes.Add(new NodeItem(2, "node2.locyanfrp.cn")
                    {
                        DisplayName = L.T("create.demo.node2"),
                        PortRangeDisplay = "20000-20100",
                        DescriptionDisplay = L.T("create.demo.node2Description"),
                    });
                    SelectedNode = _nodes.FirstOrDefault();
                    CanPing = true;
                    return;
                }

                if (!ApiClient.TryEnsureLoggedIn(out var error))
                {
                    SetError(error!);
                    return;
                }

                using var api = new ApiClient();
                var result = await api.GetNodesAsync();
                if (!result.Success)
                {
                    SetError(L.T("create.nodesFailed", result.Message));
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
                AppLogger.Exception("Unhandled exception in Kairo/ViewModels/Windows/CreateProxyWindowViewModel.cs:209", ex);
                SetError(L.T("create.nodesFailed", ex.Message));
                CanPing = false;
            }
            finally
            {
                IsLoadingNodes = false;
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

                if (string.IsNullOrWhiteSpace(name)) { SetError(L.T("create.errors.name")); return; }
                if (string.IsNullOrWhiteSpace(type)) { SetError(L.T("create.errors.type")); return; }
                if (string.IsNullOrWhiteSpace(localIp)) { SetError(L.T("create.errors.localIp")); return; }
                if (IsLolia && !IPAddress.TryParse(localIp, out _)) { SetError(L.T("create.errors.loliaLocalIp")); return; }
                if (!int.TryParse(localPortStr, out var localPort) || localPort <= 0 || localPort > 65535)
                { SetError(L.T("create.errors.localPort")); return; }
                if (nodeItem == null) { SetError(L.T("create.errors.node")); return; }

                bool needRemote = NeedRemotePort;
                bool needSecret = NeedSecretKey;
                bool needDomain = NeedDomain;
                int? remotePort = null;
                if (needRemote)
                {
                    if (!string.IsNullOrWhiteSpace(remotePortStr))
                    {
                        if (!int.TryParse(remotePortStr, out var port) || port <= 0 || port > 65535)
                        { SetError(L.T("create.errors.remotePort")); return; }
                        remotePort = port;
                    }
                    else if (!IsLolia)
                    {
                        // LoliaFRP 不传远端端口时由服务端自动分配，LocyanFRP 需要先申请随机端口
                        SetProgress(L.T("create.requestingPort"));
                        var port = await TryGetRandomPortAsync(nodeItem.Id);
                        if (port <= 0)
                        {
                            SetError(L.T("create.errors.randomPort"));
                            return;
                        }
                        remotePort = port;
                        RemotePort = port.ToString();
                    }
                }
                if (needSecret && string.IsNullOrWhiteSpace(secret)) { SetError(L.T("create.errors.secretKey")); return; }
                if (needDomain && string.IsNullOrWhiteSpace(domain)) { SetError(L.T("create.errors.domain")); return; }

                if (!ApiClient.TryEnsureLoggedIn(out var err))
                {
                    SetError(err!);
                    return;
                }

                SetProgress(L.T("create.creating"));
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
                    // 提示使用用户填写的名称（LoliaFRP 的隧道名称是服务端生成的随机字符串）
                    ProxyCreated?.Invoke(result.Data.TunnelId, name!);
                }
                else
                {
                    SetError(result.Message);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/ViewModels/Windows/CreateProxyWindowViewModel.cs:296", ex);
                SetError(L.T("create.errors.failed", ex.Message));
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
            if (!string.IsNullOrWhiteSpace(node.RegionCode)) parts.Add(L.T("create.nodeInfo.region", node.RegionCode));
            if (!string.IsNullOrWhiteSpace(node.Status)) parts.Add(L.T("create.nodeInfo.status", node.Status));
            if (node.Bandwidth > 0) parts.Add(L.T("create.nodeInfo.bandwidth", node.Bandwidth));
            if (node.Load > 0) parts.Add(L.T("create.nodeInfo.load", node.Load));
            if (!string.IsNullOrWhiteSpace(node.Sponsor)) parts.Add(L.T("create.nodeInfo.sponsor", node.Sponsor));
            if (node.NeedKyc) parts.Add(L.T("create.nodeInfo.kyc"));
            if (node.BeianRequired) parts.Add(L.T("create.nodeInfo.beian"));
            if (!string.IsNullOrWhiteSpace(node.Description)) parts.Add(node.Description);
            return parts.Count == 0 ? L.T("create.nodeInfo.none") : string.Join(" · ", parts);
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
            catch (Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/ViewModels/Windows/CreateProxyWindowViewModel.cs:338", ex);
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
