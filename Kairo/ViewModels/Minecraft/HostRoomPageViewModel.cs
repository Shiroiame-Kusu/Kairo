using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Kairo.Core.Models;
using Kairo.Core.Providers;
using HakuuLib.MultiplayerLAN.Minecraft.Java.Discovery;
using HakuuLib.MultiplayerLAN.Minecraft.Bedrock.Discovery;
using Kairo.Utils;

namespace Kairo.ViewModels
{
    public class HostRoomPageViewModel : ViewModelBase, IDisposable
    {
        private readonly ApiClient _api = new();
        private readonly MinecraftRoomApiClient _rooms;
        private readonly MinecraftLanDiscoveryService _discovery = new();
        private readonly RelayCommand _pingCommand;
        private bool _canPing;
        private bool _useEncryption;
        private bool _useCompression;

        /// <summary>
        /// Event raised when user requests to open the ping window.
        /// </summary>
        public event Action? RequestPingWindow;

        // Detected servers
        public ObservableCollection<DetectedServerViewModel> DetectedServers { get; } = new();

        private DetectedServerViewModel? _selectedServer;
        public DetectedServerViewModel? SelectedServer
        {
            get => _selectedServer;
            set => SetProperty(ref _selectedServer, value);
        }

        public bool NoServersDetected => DetectedServers.Count == 0;

        private bool _isDetecting;
        public bool IsDetecting
        {
            get => _isDetecting;
            set => SetProperty(ref _isDetecting, value);
        }

        // Nodes
        public ObservableCollection<NodeViewModel> Nodes { get; } = new();

        private NodeViewModel? _selectedNode;
        public NodeViewModel? SelectedNode
        {
            get => _selectedNode;
            set => SetProperty(ref _selectedNode, value);
        }

        public bool NoNodes => Nodes.Count == 0;

        // Advanced options
        public bool CanPing
        {
            get => _canPing;
            set
            {
                if (SetProperty(ref _canPing, value))
                {
                    _pingCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public RelayCommand PingCommand => _pingCommand;

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

        // Status
        private string _statusText = "准备就绪";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        // Commands
        public ICommand StartDetectionCommand { get; }
        public ICommand StopDetectionCommand { get; }
        public ICommand RefreshNodesCommand { get; }
        public ICommand CreateRoomCommand { get; }

        public HostRoomPageViewModel()
        {
            _rooms = new MinecraftRoomApiClient(_api);
            _discovery.JavaAnnouncementReceived += OnJavaAnnouncementReceived;
            _discovery.BedrockServerDiscovered += OnBedrockServerDiscovered;
            _pingCommand = new RelayCommand(() => RequestPingWindow?.Invoke(), () => CanPing);
            StartDetectionCommand = new AsyncRelayCommand(StartDetectionAsync);
            StopDetectionCommand = new RelayCommand(StopDetection);
            RefreshNodesCommand = new AsyncRelayCommand(RefreshNodesAsync);
            CreateRoomCommand = new AsyncRelayCommand(CreateRoomAsync);
        }

        public void OnLoaded()
        {
            if (!Global.CurrentProvider.SupportsMinecraftRooms)
            {
                StatusText = $"{Global.CurrentProvider.DisplayName} 暂不支持 Minecraft 联机房间";
                return;
            }

            // Only refresh nodes if empty (first load or after explicit clear)
            if (Nodes.Count == 0)
            {
                _ = RefreshNodesAsync();
            }
            // Resume detection
            _ = StartDetectionAsync();
        }

        public void OnUnloaded()
        {
            // Pause detection but keep detected servers
            PauseDetection();
        }

        public void Dispose()
        {
            StopDetection();
            _discovery.JavaAnnouncementReceived -= OnJavaAnnouncementReceived;
            _discovery.BedrockServerDiscovered -= OnBedrockServerDiscovered;
            _discovery.Dispose();
            _api.Dispose();
        }

        /// <summary>
        /// Pause detection without clearing detected servers.
        /// </summary>
        private void PauseDetection()
        {
            if (!IsDetecting) return;

            _discovery.Stop();
            IsDetecting = false;
            StatusText = "探测已暂停";
        }

        public void SelectServer(DetectedServerViewModel server)
        {
            foreach (var s in DetectedServers)
            {
                s.IsSelected = s == server;
            }
            SelectedServer = server;
        }

        public void SelectNode(NodeViewModel node)
        {
            foreach (var n in Nodes)
            {
                n.IsSelected = n == node;
            }
            SelectedNode = node;
        }

        private void ShowSnackbar(string title, string? message, InfoBarSeverity severity)
        {
            (Access.DashBoard as Components.DashBoard.DashBoard)?.OpenSnackbar(title, message, severity);
        }

        #region Detection

        private async Task StartDetectionAsync()
        {
            if (IsDetecting) return;

            try
            {
                IsDetecting = true;
                OnPropertyChanged(nameof(NoServersDetected));
                StatusText = DetectedServers.Count > 0
                    ? $"继续探测服务器... (已发现 {DetectedServers.Count} 个)"
                    : "正在探测本地 Minecraft 服务器...";

                await _discovery.StartAsync();
                StatusText = "探测中... 请在 Minecraft 中对局域网开放";
            }
            catch (Exception ex)
            {
                Kairo.Utils.Logger.Logger.Exception("Unhandled exception in Kairo/ViewModels/Minecraft/HostRoomPageViewModel.cs:200", ex);
                StatusText = $"探测失败: {ex.Message}";
                ShowSnackbar("探测失败", ex.Message, InfoBarSeverity.Error);
                IsDetecting = false;
            }
        }

        private void OnJavaAnnouncementReceived(object? sender, JavaLanAnnouncement announcement)
        {
            Dispatcher.UIThread.Post(() =>
            {
                // Check if already exists
                foreach (var existing in DetectedServers)
                {
                    if (existing.Sender.Equals(announcement.Sender) && existing.Port == announcement.Port)
                    {
                        return; // Already detected
                    }
                }

                var vm = new DetectedServerViewModel(announcement);
                DetectedServers.Add(vm);
                OnPropertyChanged(nameof(NoServersDetected));
                StatusText = $"探测到 Java 服务器: {announcement.Motd}";
            });
        }

        private void OnBedrockServerDiscovered(object? sender, BedrockLanAnnouncement announcement)
        {
            Dispatcher.UIThread.Post(() =>
            {
                // Check if already exists by ServerUniqueId or endpoint
                foreach (var existing in DetectedServers)
                {
                    if (existing.Sender.Equals(announcement.Sender) && existing.Port == announcement.PortV4)
                    {
                        return; // Already detected
                    }
                }

                var vm = new DetectedServerViewModel(announcement);
                DetectedServers.Add(vm);
                OnPropertyChanged(nameof(NoServersDetected));
                StatusText = $"探测到基岩版服务器: {announcement.MotdLine1}";
            });
        }

        private void StopDetection()
        {
            if (!IsDetecting) return;

            _discovery.Stop();
            IsDetecting = false;
            StatusText = "探测已停止";
        }

        #endregion

        #region Nodes

        private async Task RefreshNodesAsync()
        {
            try
            {
                if (!ApiClient.TryEnsureLoggedIn(out var error))
                {
                    StatusText = error!;
                    return;
                }

                var result = await _api.GetNodesAsync();
                if (!result.Success)
                {
                    StatusText = $"获取节点失败: {result.Message}";
                    return;
                }

                Nodes.Clear();
                foreach (var node in result.Data ?? Array.Empty<FrpNode>())
                {
                    var label = GetNodeLabel(node);
                    var portRangeDisplay = node.PortRanges?.Count > 0 ? string.Join(", ", node.PortRanges) : "—";

                    if (node.Id > 0 && !string.IsNullOrWhiteSpace(label))
                    {
                        Nodes.Add(new NodeViewModel(
                            node.Id,
                            string.IsNullOrWhiteSpace(node.Name) ? label : node.Name,
                            label,
                            portRangeDisplay,
                            GetNodeDescription(node)
                        ));
                    }
                }

                OnPropertyChanged(nameof(NoNodes));
                if (Nodes.Count > 0 && SelectedNode == null)
                {
                    SelectedNode = Nodes[0];
                }
                
                CanPing = Global.CurrentProvider.Type != FrpProviderType.Lolia && Nodes.Count > 0;

                StatusText = $"已加载 {Nodes.Count} 个节点";
            }
            catch (Exception ex)
            {
                Kairo.Utils.Logger.Logger.Exception("Unhandled exception in Kairo/ViewModels/Minecraft/HostRoomPageViewModel.cs:306", ex);
                StatusText = $"获取节点失败: {ex.Message}";
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
            return parts.Count == 0 ? "暂无描述" : string.Join(" · ", parts);
        }

        #endregion

        #region Room Creation

        private async Task CreateRoomAsync()
        {
            if (!Global.CurrentProvider.SupportsMinecraftRooms)
            {
                ShowSnackbar("功能不可用", $"{Global.CurrentProvider.DisplayName} 暂不支持 Minecraft 联机房间", InfoBarSeverity.Warning);
                return;
            }

            if (SelectedServer == null)
            {
                ShowSnackbar("请选择服务器", "请先选择一个探测到的本地服务器", InfoBarSeverity.Warning);
                return;
            }

            if (SelectedNode == null)
            {
                ShowSnackbar("请选择节点", "请先选择用于映射的节点", InfoBarSeverity.Warning);
                return;
            }

            try
            {
                StatusText = "正在创建隧道...";

                // Step 1: Get random port for the node
                int remotePort = await TryGetRandomPortAsync(SelectedNode.Id);
                if (remotePort <= 0)
                {
                    ShowSnackbar("获取端口失败", "无法获取节点随机端口", InfoBarSeverity.Error);
                    return;
                }

                // Step 2: Create tunnel automatically
                string tunnelName = $"MC联机_{DateTime.Now:yyyyMMdd_HHmmss}";
                string localIp = SelectedServer.Sender.Address.ToString();
                int localPort = SelectedServer.Port;

                int tunnelId = await CreateTunnelAsync(tunnelName, localIp, localPort, SelectedNode.Id, remotePort);
                if (tunnelId <= 0)
                {
                    return; // Error already shown
                }

                StatusText = $"隧道创建成功 (ID: {tunnelId})，正在创建房间...";

                var room = await _rooms.CreateRoomAsync(tunnelId);
                if (room?.Status == 200)
                {
                    var code = room.Data?.Code ?? string.Empty;
                    ShowSnackbar("房间创建成功", $"房间代码: {code}", InfoBarSeverity.Success);
                    StatusText = $"房间已创建，代码: {code}";

                    if (!string.IsNullOrEmpty(code))
                    {
                        await CopyToClipboardAsync(code);
                        ShowSnackbar("已复制", "房间代码已复制到剪贴板", InfoBarSeverity.Informational);
                    }
                }
                else
                {
                    var msg = room?.Message ?? "未知错误";
                    ShowSnackbar("创建房间失败", msg, InfoBarSeverity.Error);
                    StatusText = $"创建失败: {msg}";
                }
            }
            catch (Exception ex)
            {
                Kairo.Utils.Logger.Logger.Exception("Unhandled exception in Kairo/ViewModels/Minecraft/HostRoomPageViewModel.cs:407", ex);
                ShowSnackbar("创建房间异常", ex.Message, InfoBarSeverity.Error);
                StatusText = $"创建异常: {ex.Message}";
            }
        }

        private async Task<int> TryGetRandomPortAsync(int nodeId)
        {
            try
            {
                if (!ApiClient.IsLoggedIn)
                    return 0;

                var result = await _api.GetRandomPortAsync(nodeId);
                return result.Success ? result.Data : 0;
            }
            catch (System.Exception ex)
            {
                Kairo.Utils.Logger.Logger.Exception("Unhandled exception in Kairo/ViewModels/Minecraft/HostRoomPageViewModel.cs:424", ex);
                return 0;
            }
        }

        private async Task<int> CreateTunnelAsync(string name, string localIp, int localPort, int nodeId, int remotePort)
        {
            try
            {
                // Use UDP for Bedrock, TCP for Java - determine from selected server
                var tunnelType = SelectedServer?.Edition == MinecraftEdition.Bedrock ? "udp" : "tcp";

                var result = await _api.CreateTunnelAsync(new CreateFrpTunnelRequest
                {
                    Name = name,
                    LocalIp = localIp,
                    Type = tunnelType,
                    LocalPort = localPort,
                    NodeId = nodeId,
                    RemotePort = remotePort,
                    UseEncryption = UseEncryption,
                    UseCompression = UseCompression
                });

                if (result.Success && result.Data != null)
                {
                    return result.Data.TunnelId;
                }

                ShowSnackbar("创建隧道失败", result.Message, InfoBarSeverity.Error);
                StatusText = result.Message;
                return 0;
            }
            catch (Exception ex)
            {
                Kairo.Utils.Logger.Logger.Exception("Unhandled exception in Kairo/ViewModels/Minecraft/HostRoomPageViewModel.cs:458", ex);
                ShowSnackbar("创建隧道异常", ex.Message, InfoBarSeverity.Error);
                return 0;
            }
        }

        private static async Task CopyToClipboardAsync(string text)
        {
            try
            {
                if (Access.DashBoard != null)
                {
                    var clipboard = TopLevel.GetTopLevel(Access.DashBoard)?.Clipboard;
                    if (clipboard != null)
                    {
                        await clipboard.SetTextAsync(text);
                    }
                }
            }
            catch (System.Exception ex)
            {
                Kairo.Utils.Logger.Logger.Exception("Unhandled exception in Kairo/ViewModels/Minecraft/HostRoomPageViewModel.cs:478", ex);
                // Ignore
            }
        }

        #endregion
    }
}
