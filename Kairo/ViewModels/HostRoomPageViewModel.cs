using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Threading;
using FluentAvalonia.UI.Controls;
using HakuuLib.MultiplayerLAN.Minecraft.Java.Discovery;
using HakuuLib.MultiplayerLAN.Minecraft.Bedrock.Discovery;
using Kairo.Utils;

namespace Kairo.ViewModels
{
    /// <summary>
    /// Minecraft edition type.
    /// </summary>
    public enum MinecraftEdition
    {
        Java,
        Bedrock
    }

    /// <summary>
    /// ViewModel for a detected local Minecraft LAN server (supports both Java and Bedrock).
    /// </summary>
    public class DetectedServerViewModel : ViewModelBase
    {
        public string Motd { get; }
        public int Port { get; }
        public IPEndPoint Sender { get; }
        public MinecraftEdition Edition { get; }
        public string EditionDisplay => Edition == MinecraftEdition.Java ? "Java" : "基岩";
        public string AddressDisplay => $"{Sender.Address}:{Port}";

        // Bedrock-specific properties
        public string? VersionName { get; }
        public int? PlayerCount { get; }
        public int? MaxPlayerCount { get; }
        public string? GameModeName { get; }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        /// <summary>
        /// Constructor for Java Edition server.
        /// </summary>
        public DetectedServerViewModel(JavaLanAnnouncement announcement)
        {
            Edition = MinecraftEdition.Java;
            Motd = announcement.Motd;
            Port = announcement.Port;
            Sender = announcement.Sender;
        }

        /// <summary>
        /// Constructor for Bedrock Edition server.
        /// </summary>
        public DetectedServerViewModel(BedrockLanAnnouncement announcement)
        {
            Edition = MinecraftEdition.Bedrock;
            Motd = announcement.MotdLine1;
            Port = announcement.PortV4;
            Sender = announcement.Sender;
            VersionName = announcement.VersionName;
            PlayerCount = announcement.PlayerCount;
            MaxPlayerCount = announcement.MaxPlayerCount;
            GameModeName = announcement.GameModeName;
        }
    }

    /// <summary>
    /// ViewModel for a node that can be used for hosting.
    /// </summary>
    public class NodeViewModel : ViewModelBase
    {
        public int Id { get; }
        public string Name { get; }
        public string Host { get; }
        public string PortRangeDisplay { get; }
        public string Description { get; }
        public string DisplayLabel => $"{Name} ({Host})";

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public NodeViewModel(int id, string name, string host, string portRangeDisplay, string description)
        {
            Id = id;
            Name = name;
            Host = host;
            PortRangeDisplay = portRangeDisplay;
            Description = description;
        }
    }

    public class HostRoomPageViewModel : ViewModelBase, IDisposable
    {
        private readonly ApiClient _api = new();
        private readonly RelayCommand _pingCommand;
        private JavaLanDiscoveryListener? _javaDiscoveryListener;
        private BedrockLanDiscoveryListener? _bedrockDiscoveryListener;
        private CancellationTokenSource? _detectionCts;
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
            _pingCommand = new RelayCommand(() => RequestPingWindow?.Invoke(), () => CanPing);
            StartDetectionCommand = new AsyncRelayCommand(StartDetectionAsync);
            StopDetectionCommand = new RelayCommand(StopDetection);
            RefreshNodesCommand = new AsyncRelayCommand(RefreshNodesAsync);
            CreateRoomCommand = new AsyncRelayCommand(CreateRoomAsync);
        }

        public void OnLoaded()
        {
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
            _api.Dispose();
        }

        /// <summary>
        /// Pause detection without clearing detected servers.
        /// </summary>
        private void PauseDetection()
        {
            if (!IsDetecting) return;

            try
            {
                _detectionCts?.Cancel();
                
                // Stop Java listener
                if (_javaDiscoveryListener != null)
                {
                    _javaDiscoveryListener.AnnouncementReceived -= OnJavaAnnouncementReceived;
                    _ = _javaDiscoveryListener.StopAsync();
                    _ = _javaDiscoveryListener.DisposeAsync();
                    _javaDiscoveryListener = null;
                }
                
                // Stop Bedrock listener
                if (_bedrockDiscoveryListener != null)
                {
                    _bedrockDiscoveryListener.ServerDiscovered -= OnBedrockServerDiscovered;
                    _ = _bedrockDiscoveryListener.StopAsync();
                    _ = _bedrockDiscoveryListener.DisposeAsync();
                    _bedrockDiscoveryListener = null;
                }
                
                _detectionCts?.Dispose();
                _detectionCts = null;
            }
            finally
            {
                IsDetecting = false;
                StatusText = "探测已暂停";
            }
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
                // Don't clear detected servers when resuming - keep existing discoveries
                OnPropertyChanged(nameof(NoServersDetected));
                StatusText = DetectedServers.Count > 0 
                    ? $"继续探测服务器... (已发现 {DetectedServers.Count} 个)" 
                    : "正在探测本地 Minecraft 服务器...";

                _detectionCts = new CancellationTokenSource();

                // Start Java listener
                _javaDiscoveryListener = new JavaLanDiscoveryListener();
                _javaDiscoveryListener.AnnouncementReceived += OnJavaAnnouncementReceived;
                await _javaDiscoveryListener.StartAsync(_detectionCts.Token);
                
                // Start Bedrock listener
                _bedrockDiscoveryListener = new BedrockLanDiscoveryListener();
                _bedrockDiscoveryListener.ServerDiscovered += OnBedrockServerDiscovered;
                await _bedrockDiscoveryListener.StartAsync(_detectionCts.Token);
                
                StatusText = "探测中... 请在 Minecraft 中对局域网开放";
            }
            catch (Exception ex)
            {
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

            try
            {
                _detectionCts?.Cancel();
                
                if (_javaDiscoveryListener != null)
                {
                    _javaDiscoveryListener.AnnouncementReceived -= OnJavaAnnouncementReceived;
                    _ = _javaDiscoveryListener.StopAsync();
                    _ = _javaDiscoveryListener.DisposeAsync();
                    _javaDiscoveryListener = null;
                }
                
                if (_bedrockDiscoveryListener != null)
                {
                    _bedrockDiscoveryListener.ServerDiscovered -= OnBedrockServerDiscovered;
                    _ = _bedrockDiscoveryListener.StopAsync();
                    _ = _bedrockDiscoveryListener.DisposeAsync();
                    _bedrockDiscoveryListener = null;
                }
                
                _detectionCts?.Dispose();
                _detectionCts = null;
            }
            finally
            {
                IsDetecting = false;
                StatusText = "探测已停止";
            }
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

                var url = ApiClient.GetNodesUrl();
                var resp = await _api.GetAsync(url);
                var content = await resp.Content.ReadAsStringAsync();
                var json = JsonNode.Parse(content);

                if (json?["status"]?.GetValue<int>() != 200)
                {
                    StatusText = $"获取节点失败: {json?["message"]?.GetValue<string>()}";
                    return;
                }

                var list = json?["data"]?["list"] as JsonArray;
                Nodes.Clear();

                if (list != null)
                {
                    foreach (var item in list)
                    {
                        int id = item?["id"]?.GetValue<int>() ?? 0;
                        string ip = item?["ip"]?.GetValue<string>() ?? string.Empty;
                        string host = item?["host"]?.GetValue<string>() ?? string.Empty;
                        string label = !string.IsNullOrWhiteSpace(host) ? host : ip;
                        string name = item?["name"]?.GetValue<string>() ?? label;

                        var additional = item?["additional"] as JsonObject;
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

                        string portRangeDisplay = portRanges.Count > 0 ? string.Join(", ", portRanges) : "—";

                        if (id > 0 && !string.IsNullOrWhiteSpace(label))
                        {
                            Nodes.Add(new NodeViewModel(
                                id,
                                string.IsNullOrWhiteSpace(name) ? label : name,
                                label,
                                portRangeDisplay,
                                string.IsNullOrWhiteSpace(description) ? "暂无描述" : description!
                            ));
                        }
                    }
                }

                OnPropertyChanged(nameof(NoNodes));
                if (Nodes.Count > 0 && SelectedNode == null)
                {
                    SelectedNode = Nodes[0];
                }
                
                CanPing = Nodes.Count > 0;

                StatusText = $"已加载 {Nodes.Count} 个节点";
            }
            catch (Exception ex)
            {
                StatusText = $"获取节点失败: {ex.Message}";
            }
        }

        #endregion

        #region Room Creation

        private async Task CreateRoomAsync()
        {
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

                // Step 3: Create room via API
                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("user_id", Global.Config.ID.ToString()),
                    new KeyValuePair<string, string>("tunnel_id", tunnelId.ToString())
                });

                var resp = await _api.PutAsync($"{Global.API}/game/minecraft/game", content);
                var body = await resp.Content.ReadAsStringAsync();
                var json = JsonNode.Parse(body);

                if (json?["status"]?.GetValue<int>() == 200)
                {
                    var code = json?["data"]?["code"]?.GetValue<string>();
                    ShowSnackbar("房间创建成功", $"房间代码: {code}", InfoBarSeverity.Success);
                    StatusText = $"房间已创建，代码: {code}";

                    // Copy to clipboard
                    if (!string.IsNullOrEmpty(code))
                    {
                        await CopyToClipboardAsync(code);
                        ShowSnackbar("已复制", "房间代码已复制到剪贴板", InfoBarSeverity.Informational);
                    }
                }
                else
                {
                    var msg = json?["message"]?.GetValue<string>() ?? "未知错误";
                    ShowSnackbar("创建房间失败", msg, InfoBarSeverity.Error);
                    StatusText = $"创建失败: {msg}";
                }
            }
            catch (Exception ex)
            {
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

                var url = ApiClient.GetRandomPortUrl(nodeId);
                var resp = await _api.GetAsync(url);
                var content = await resp.Content.ReadAsStringAsync();
                var json = JsonNode.Parse(content);

                if (json?["status"]?.GetValue<int>() != 200)
                    return 0;

                return json?["data"]?["port"]?.GetValue<int>() ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        private async Task<int> CreateTunnelAsync(string name, string localIp, int localPort, int nodeId, int remotePort)
        {
            try
            {
                // Use UDP for Bedrock, TCP for Java - determine from selected server
                var tunnelType = SelectedServer?.Edition == MinecraftEdition.Bedrock ? "UDP" : "TCP";

                var form = new List<KeyValuePair<string, string>>
                {
                    new("user_id", Global.Config.ID.ToString()),
                    new("name", name),
                    new("local_ip", localIp),
                    new("type", tunnelType),
                    new("local_port", localPort.ToString()),
                    new("node_id", nodeId.ToString()),
                    new("remote_port", remotePort.ToString()),
                    new("use_encryption", UseEncryption.ToString().ToLowerInvariant()),
                    new("use_compression", UseCompression.ToString().ToLowerInvariant()),
                };

                var resp = await _api.PutAsync(Global.APIList.Tunnel, new FormUrlEncodedContent(form));
                var content = await resp.Content.ReadAsStringAsync();
                var json = JsonNode.Parse(content);

                if (json?["status"]?.GetValue<int>() == 200)
                {
                    return json?["data"]?["tunnel_id"]?.GetValue<int>() ?? 0;
                }
                else
                {
                    var msg = json?["message"]?.GetValue<string>() ?? "创建隧道失败";
                    ShowSnackbar("创建隧道失败", msg, InfoBarSeverity.Error);
                    StatusText = msg;
                    return 0;
                }
            }
            catch (Exception ex)
            {
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
            catch
            {
                // Ignore
            }
        }

        #endregion
    }
}
