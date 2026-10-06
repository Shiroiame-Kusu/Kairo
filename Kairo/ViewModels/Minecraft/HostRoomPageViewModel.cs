using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using Kairo.Core.Localization;
using Kairo.Core.Models;
using Kairo.Core.Providers;
using HakuuLib.MultiplayerLAN.Minecraft.Java.Discovery;
using HakuuLib.MultiplayerLAN.Minecraft.Bedrock.Discovery;
using Kairo.Utils;
using Kairo.Components.DashBoard;

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
        private string _statusText = L.T("lan.status.ready");
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
            _useEncryption = Global.CurrentProvider.Type == FrpProviderType.Lolia;
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
                StatusText = L.T("lan.status.unsupported", Global.CurrentProvider.DisplayName);
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
            StatusText = L.T("lan.status.detectionPaused");
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

        private void ShowSnackbar(string title, string? message, FAInfoBarSeverity severity)
        {
            (Access.DashBoard as DashBoard)?.OpenSnackbar(title, message, severity);
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
                    ? L.T("lan.status.continueDetecting", DetectedServers.Count)
                    : L.T("lan.status.detecting");

                await _discovery.StartAsync();
                StatusText = L.T("lan.status.detectingHint");
            }
            catch (Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/ViewModels/Minecraft/HostRoomPageViewModel.cs:200", ex);
                StatusText = L.T("lan.status.detectionFailed", ex.Message);
                ShowSnackbar(L.T("lan.detectionFailed"), ex.Message, FAInfoBarSeverity.Error);
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
                StatusText = L.T("lan.status.foundJava", announcement.Motd);
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
                StatusText = L.T("lan.status.foundBedrock", announcement.MotdLine1);
            });
        }

        private void StopDetection()
        {
            if (!IsDetecting) return;

            _discovery.Stop();
            IsDetecting = false;
            StatusText = L.T("lan.status.detectionStopped");
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
                    StatusText = L.T("lan.status.nodesFailed", result.Message);
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

                StatusText = L.Plural("lan.status.nodesLoaded", Nodes.Count);
            }
            catch (Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/ViewModels/Minecraft/HostRoomPageViewModel.cs:306", ex);
                StatusText = L.T("lan.status.nodesFailed", ex.Message);
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
            return parts.Count == 0 ? L.T("lan.noDescription") : string.Join(" · ", parts);
        }

        #endregion

        #region Room Creation

        private async Task CreateRoomAsync()
        {
            if (!Global.CurrentProvider.SupportsMinecraftRooms)
            {
                ShowSnackbar(L.T("lan.unavailable"), L.T("lan.status.unsupported", Global.CurrentProvider.DisplayName), FAInfoBarSeverity.Warning);
                return;
            }

            if (SelectedServer == null)
            {
                ShowSnackbar(L.T("lan.chooseServer"), L.T("lan.chooseServerHint"), FAInfoBarSeverity.Warning);
                return;
            }

            if (SelectedNode == null)
            {
                ShowSnackbar(L.T("lan.chooseNode"), L.T("lan.chooseNodeHint"), FAInfoBarSeverity.Warning);
                return;
            }

            try
            {
                StatusText = L.T("lan.status.creatingTunnel");

                // Step 1: Get random port for the node
                int remotePort = await TryGetRandomPortAsync(SelectedNode.Id);
                if (remotePort <= 0)
                {
                    ShowSnackbar(L.T("lan.portFailed"), L.T("lan.portFailedHint"), FAInfoBarSeverity.Error);
                    return;
                }

                // Step 2: Create tunnel automatically
                string tunnelName = $"{L.T("lan.tunnelNamePrefix")}_{DateTime.Now:yyyyMMdd_HHmmss}";
                string localIp = SelectedServer.Sender.Address.ToString();
                int localPort = SelectedServer.Port;

                int tunnelId = await CreateTunnelAsync(tunnelName, localIp, localPort, SelectedNode.Id, remotePort);
                if (tunnelId <= 0)
                {
                    return; // Error already shown
                }

                StatusText = L.T("lan.status.tunnelCreated", tunnelId);

                var room = await _rooms.CreateRoomAsync(tunnelId);
                if (room?.Status == 200)
                {
                    var code = room.Data?.Code ?? string.Empty;
                    ShowSnackbar(L.T("lan.roomCreated"), L.T("lan.roomCode", code), FAInfoBarSeverity.Success);
                    StatusText = L.T("lan.status.roomCreated", code);

                    if (!string.IsNullOrEmpty(code))
                    {
                        await CopyToClipboardAsync(code);
                        ShowSnackbar(L.T("lan.copied"), L.T("lan.codeCopied"), FAInfoBarSeverity.Informational);
                    }
                }
                else
                {
                    var msg = room?.Message ?? L.T("core.api.unknownError");
                    ShowSnackbar(L.T("lan.createRoomFailed"), msg, FAInfoBarSeverity.Error);
                    StatusText = L.T("lan.status.createFailed", msg);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/ViewModels/Minecraft/HostRoomPageViewModel.cs:407", ex);
                ShowSnackbar(L.T("lan.createRoomError"), ex.Message, FAInfoBarSeverity.Error);
                StatusText = L.T("lan.status.createError", ex.Message);
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
            catch (Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/ViewModels/Minecraft/HostRoomPageViewModel.cs:424", ex);
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

                ShowSnackbar(L.T("lan.createTunnelFailed"), result.Message, FAInfoBarSeverity.Error);
                StatusText = result.Message;
                return 0;
            }
            catch (Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/ViewModels/Minecraft/HostRoomPageViewModel.cs:458", ex);
                ShowSnackbar(L.T("lan.createTunnelError"), ex.Message, FAInfoBarSeverity.Error);
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
            catch (Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/ViewModels/Minecraft/HostRoomPageViewModel.cs:478", ex);
                // Ignore
            }
        }

        #endregion
    }
}
