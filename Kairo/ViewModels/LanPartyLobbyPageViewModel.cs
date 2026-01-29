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
using HakuuLib.Minecraft.Discovery;
using HakuuLib.Minecraft.Forwarding;
using Kairo.Components;
using Kairo.Utils;
using Kairo.Utils.Logger;

namespace Kairo.ViewModels
{
    /// <summary>
    /// ViewModel for a detected local Minecraft LAN server.
    /// </summary>
    public class DetectedServerViewModel : ViewModelBase
    {
        public string Motd { get; }
        public int Port { get; }
        public IPEndPoint Sender { get; }
        public string AddressDisplay => $"{Sender.Address}:{Port}";

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public DetectedServerViewModel(LanAnnouncement announcement)
        {
            Motd = announcement.Motd;
            Port = announcement.Port;
            Sender = announcement.Sender;
        }
    }

    /// <summary>
    /// ViewModel for a tunnel that can be used for hosting.
    /// </summary>
    public class TunnelViewModel : ViewModelBase
    {
        public int Id { get; }
        public string Name { get; }
        public string DisplayName => $"{Name} (ID: {Id})";

        public TunnelViewModel(int id, string name)
        {
            Id = id;
            Name = name;
        }
    }

    /// <summary>
    /// ViewModel for a Minecraft room the user has created.
    /// </summary>
    public class RoomViewModel : ViewModelBase
    {
        private readonly LanPartyLobbyPageViewModel _parent;

        public string Code { get; }
        public int ProxyId { get; }
        public string Name { get; }
        public string CodeDisplay => $"房间代码: {Code}";

        public ICommand CopyCodeCommand { get; }
        public ICommand DeleteCommand { get; }

        public RoomViewModel(string code, int proxyId, string name, LanPartyLobbyPageViewModel parent)
        {
            Code = code;
            ProxyId = proxyId;
            Name = name;
            _parent = parent;
            CopyCodeCommand = new RelayCommand(CopyCode);
            DeleteCommand = new AsyncRelayCommand(DeleteAsync);
        }

        private void CopyCode()
        {
            try
            {
                CopyToClipboardAsync(Code);
                _parent.ShowStatus("房间代码已复制到剪贴板");
            }
            catch
            {
                // Ignore clipboard errors
            }
        }

        private static async void CopyToClipboardAsync(string text)
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

        private async Task DeleteAsync()
        {
            await _parent.DeleteRoomAsync(this);
        }
    }

    public class LanPartyLobbyPageViewModel : ViewModelBase, IDisposable
    {
        private readonly HttpClient _http = new();
        private LanDiscoveryListener? _discoveryListener;
        private MinecraftLanForwarder? _activeForwarder;
        private CancellationTokenSource? _detectionCts;

        // Mode toggles
        private bool _isHostMode = true;
        public bool IsHostMode
        {
            get => _isHostMode;
            set
            {
                if (SetProperty(ref _isHostMode, value) && value)
                {
                    IsJoinMode = false;
                }
            }
        }

        private bool _isJoinMode;
        public bool IsJoinMode
        {
            get => _isJoinMode;
            set
            {
                if (SetProperty(ref _isJoinMode, value) && value)
                {
                    IsHostMode = false;
                }
            }
        }

        // Host mode properties
        public ObservableCollection<DetectedServerViewModel> DetectedServers { get; } = new();
        public ObservableCollection<TunnelViewModel> AvailableTunnels { get; } = new();

        private TunnelViewModel? _selectedTunnel;
        public TunnelViewModel? SelectedTunnel
        {
            get => _selectedTunnel;
            set => SetProperty(ref _selectedTunnel, value);
        }

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

        // Join mode properties
        private string _joinRoomCode = string.Empty;
        public string JoinRoomCode
        {
            get => _joinRoomCode;
            set => SetProperty(ref _joinRoomCode, value);
        }

        public ObservableCollection<RoomViewModel> MyRooms { get; } = new();
        public bool NoRooms => MyRooms.Count == 0;

        // Forwarder status
        private bool _isForwarderActive;
        public bool IsForwarderActive
        {
            get => _isForwarderActive;
            set => SetProperty(ref _isForwarderActive, value);
        }

        private string _forwarderStatus = string.Empty;
        public string ForwarderStatus
        {
            get => _forwarderStatus;
            set => SetProperty(ref _forwarderStatus, value);
        }

        // Status text
        private string _statusText = "准备就绪";
        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        // Commands
        public ICommand StartDetectionCommand { get; }
        public ICommand StopDetectionCommand { get; }
        public ICommand CreateRoomCommand { get; }
        public ICommand RefreshTunnelsCommand { get; }
        public ICommand JoinRoomCommand { get; }
        public ICommand RefreshMyRoomsCommand { get; }
        public ICommand StopForwarderCommand { get; }

        public LanPartyLobbyPageViewModel()
        {
            StartDetectionCommand = new AsyncRelayCommand(StartDetectionAsync);
            StopDetectionCommand = new RelayCommand(StopDetection);
            CreateRoomCommand = new AsyncRelayCommand(CreateRoomAsync);
            RefreshTunnelsCommand = new AsyncRelayCommand(RefreshTunnelsAsync);
            JoinRoomCommand = new AsyncRelayCommand(JoinRoomAsync);
            RefreshMyRoomsCommand = new AsyncRelayCommand(RefreshMyRoomsAsync);
            StopForwarderCommand = new AsyncRelayCommand(StopForwarderAsync);
        }

        public void OnLoaded()
        {
            _ = RefreshTunnelsAsync();
            _ = RefreshMyRoomsAsync();
        }

        public void OnUnloaded()
        {
            StopDetection();
        }

        public void Dispose()
        {
            StopDetection();
            _activeForwarder?.DisposeAsync().AsTask().Wait(1000);
            _http.Dispose();
        }

        public void SelectServer(DetectedServerViewModel server)
        {
            foreach (var s in DetectedServers)
            {
                s.IsSelected = s == server;
            }
            SelectedServer = server;
        }

        public void ShowStatus(string message)
        {
            StatusText = message;
            ShowSnackbar("提示", message, InfoBarSeverity.Informational);
        }

        private void ShowSnackbar(string title, string? message, InfoBarSeverity severity)
        {
            (Access.DashBoard as Components.DashBoard.DashBoard)?.OpenSnackbar(title, message, severity);
        }

        private static async Task CopyToClipboardInternalAsync(string text)
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

        #region Host Mode - Detection

        private async Task StartDetectionAsync()
        {
            if (IsDetecting) return;

            try
            {
                IsDetecting = true;
                DetectedServers.Clear();
                OnPropertyChanged(nameof(NoServersDetected));
                StatusText = "正在探测本地 Minecraft 服务器...";

                _detectionCts = new CancellationTokenSource();
                _discoveryListener = new LanDiscoveryListener();
                _discoveryListener.AnnouncementReceived += OnAnnouncementReceived;

                await _discoveryListener.StartAsync(_detectionCts.Token);
                StatusText = "探测中... 请在 Minecraft 中对局域网开放";
            }
            catch (Exception ex)
            {
                StatusText = $"探测失败: {ex.Message}";
                ShowSnackbar("探测失败", ex.Message, InfoBarSeverity.Error);
                IsDetecting = false;
            }
        }

        private void OnAnnouncementReceived(object? sender, LanAnnouncement announcement)
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
                StatusText = $"探测到服务器: {announcement.Motd}";
            });
        }

        private void StopDetection()
        {
            if (!IsDetecting) return;

            try
            {
                _detectionCts?.Cancel();
                if (_discoveryListener != null)
                {
                    _discoveryListener.AnnouncementReceived -= OnAnnouncementReceived;
                    _ = _discoveryListener.StopAsync();
                    _ = _discoveryListener.DisposeAsync();
                    _discoveryListener = null;
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

        #region Host Mode - Tunnels and Room Creation

        private async Task RefreshTunnelsAsync()
        {
            try
            {
                _http.DefaultRequestHeaders.Remove("Authorization");
                _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {Global.Config.AccessToken}");

                var url = $"{Global.APIList.GetAllProxy}{Global.Config.ID}";
                var resp = await _http.GetAsyncLogged(url);
                var body = await resp.Content.ReadAsStringAsync();
                var json = JsonNode.Parse(body);

                if (json?["status"]?.GetValue<int>() != 200)
                {
                    ShowSnackbar("获取隧道失败", json?["message"]?.GetValue<string>(), InfoBarSeverity.Error);
                    return;
                }

                var list = json?["data"]?["list"]?.AsArray();
                AvailableTunnels.Clear();

                if (list != null)
                {
                    foreach (var item in list)
                    {
                        var id = item?["id"]?.GetValue<int>() ?? 0;
                        var name = item?["name"]?.GetValue<string>() ?? "未命名";
                        var type = item?["type"]?.GetValue<string>() ?? "";
                        
                        // Only TCP tunnels can be used for Minecraft
                        if (type.Equals("tcp", StringComparison.OrdinalIgnoreCase))
                        {
                            AvailableTunnels.Add(new TunnelViewModel(id, name));
                        }
                    }
                }

                StatusText = $"已加载 {AvailableTunnels.Count} 个可用隧道";
            }
            catch (Exception ex)
            {
                ShowSnackbar("刷新隧道失败", ex.Message, InfoBarSeverity.Error);
            }
        }

        private async Task CreateRoomAsync()
        {
            if (SelectedServer == null)
            {
                ShowSnackbar("请选择服务器", "请先选择一个探测到的本地服务器", InfoBarSeverity.Warning);
                return;
            }

            if (SelectedTunnel == null)
            {
                ShowSnackbar("请选择隧道", "请先选择用于映射的隧道", InfoBarSeverity.Warning);
                return;
            }

            try
            {
                StatusText = "正在创建房间...";

                // Update tunnel's local port to match the detected server
                await UpdateTunnelLocalPortAsync(SelectedTunnel.Id, SelectedServer.Port);

                // Create room via API
                using var hc = new HttpClient();
                hc.DefaultRequestHeaders.Add("Authorization", $"Bearer {Global.Config.AccessToken}");

                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("user_id", Global.Config.ID.ToString()),
                    new KeyValuePair<string, string>("tunnel_id", SelectedTunnel.Id.ToString())
                });

                var resp = await hc.PutAsyncLogged($"{Global.API}/game/minecraft/game", content);
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
                        await CopyToClipboardInternalAsync(code);
                    }

                    await RefreshMyRoomsAsync();
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

        private async Task UpdateTunnelLocalPortAsync(int tunnelId, int localPort)
        {
            try
            {
                using var hc = new HttpClient();
                hc.DefaultRequestHeaders.Add("Authorization", $"Bearer {Global.Config.AccessToken}");

                var content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("user_id", Global.Config.ID.ToString()),
                    new KeyValuePair<string, string>("tunnel_id", tunnelId.ToString()),
                    new KeyValuePair<string, string>("local_port", localPort.ToString())
                });

                await hc.PatchAsyncLogged(Global.APIList.Tunnel, content);
            }
            catch
            {
                // Ignore update errors, the room creation will fail if needed
            }
        }

        #endregion

        #region Join Mode

        private async Task RefreshMyRoomsAsync()
        {
            try
            {
                using var hc = new HttpClient();
                hc.DefaultRequestHeaders.Add("Authorization", $"Bearer {Global.Config.AccessToken}");

                var url = $"{Global.API}/game/minecraft/games?user_id={Global.Config.ID}";
                var resp = await hc.GetAsyncLogged(url);
                var body = await resp.Content.ReadAsStringAsync();
                var json = JsonNode.Parse(body);

                if (json?["status"]?.GetValue<int>() != 200)
                {
                    return;
                }

                var list = json?["data"]?["list"]?.AsArray();
                MyRooms.Clear();

                if (list != null)
                {
                    foreach (var item in list)
                    {
                        var code = item?["code"]?.GetValue<string>() ?? "";
                        var proxyId = item?["proxy_id"]?.GetValue<int>() ?? 0;
                        var name = item?["name"]?.GetValue<string>() ?? "未命名房间";
                        MyRooms.Add(new RoomViewModel(code, proxyId, name, this));
                    }
                }

                OnPropertyChanged(nameof(NoRooms));
            }
            catch (Exception ex)
            {
                ShowSnackbar("刷新房间失败", ex.Message, InfoBarSeverity.Error);
            }
        }

        private async Task JoinRoomAsync()
        {
            if (string.IsNullOrWhiteSpace(JoinRoomCode))
            {
                ShowSnackbar("请输入房间代码", null, InfoBarSeverity.Warning);
                return;
            }

            try
            {
                StatusText = "正在获取房间信息...";

                // Get room info from API
                var url = $"{Global.API}/game/minecraft/game?code={JoinRoomCode.Trim()}";
                var resp = await _http.GetAsyncLogged(url);
                var body = await resp.Content.ReadAsStringAsync();
                var json = JsonNode.Parse(body);

                if (json?["status"]?.GetValue<int>() != 200)
                {
                    var msg = json?["message"]?.GetValue<string>() ?? "房间不存在";
                    ShowSnackbar("加入失败", msg, InfoBarSeverity.Error);
                    StatusText = msg;
                    return;
                }

                var host = json?["data"]?["host"]?.GetValue<string>();
                var port = json?["data"]?["port"]?.GetValue<int>() ?? 0;
                var name = json?["data"]?["name"]?.GetValue<string>() ?? "远程服务器";

                if (string.IsNullOrEmpty(host) || port == 0)
                {
                    ShowSnackbar("房间信息无效", "无法获取服务器地址", InfoBarSeverity.Error);
                    return;
                }

                // Stop existing forwarder
                await StopForwarderAsync();

                // Start forwarder
                StatusText = $"正在连接到 {host}:{port}...";

                var options = new MinecraftLanForwarderOptions
                {
                    RemoteHost = host,
                    RemotePort = port,
                    ListenPort = FindAvailablePort(),
                    Motd = name
                };

                _activeForwarder = new MinecraftLanForwarder(options);
                await _activeForwarder.StartAsync();

                IsForwarderActive = true;
                ForwarderStatus = $"转发 localhost:{options.ListenPort} → {host}:{port}\n在 Minecraft 中打开多人游戏即可看到 \"{name}\"";
                StatusText = "已连接！在 Minecraft 多人游戏中可以看到房间";
                ShowSnackbar("加入成功", "在 Minecraft 多人游戏中可以看到房间", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowSnackbar("加入房间异常", ex.Message, InfoBarSeverity.Error);
                StatusText = $"加入异常: {ex.Message}";
            }
        }

        public async Task DeleteRoomAsync(RoomViewModel room)
        {
            try
            {
                using var hc = new HttpClient();
                hc.DefaultRequestHeaders.Add("Authorization", $"Bearer {Global.Config.AccessToken}");

                var url = $"{Global.API}/game/minecraft/game?user_id={Global.Config.ID}&code={room.Code}";
                var resp = await hc.DeleteAsyncLogged(url);
                var body = await resp.Content.ReadAsStringAsync();
                var json = JsonNode.Parse(body);

                if (json?["status"]?.GetValue<int>() == 200)
                {
                    ShowSnackbar("删除成功", $"房间 {room.Name} 已删除", InfoBarSeverity.Success);
                    await RefreshMyRoomsAsync();
                }
                else
                {
                    ShowSnackbar("删除失败", json?["message"]?.GetValue<string>(), InfoBarSeverity.Error);
                }
            }
            catch (Exception ex)
            {
                ShowSnackbar("删除异常", ex.Message, InfoBarSeverity.Error);
            }
        }

        private async Task StopForwarderAsync()
        {
            if (_activeForwarder != null)
            {
                try
                {
                    await _activeForwarder.StopAsync();
                    await _activeForwarder.DisposeAsync();
                }
                catch
                {
                    // Ignore cleanup errors
                }
                finally
                {
                    _activeForwarder = null;
                    IsForwarderActive = false;
                    ForwarderStatus = string.Empty;
                    StatusText = "转发已停止";
                }
            }
        }

        private static int FindAvailablePort()
        {
            int port = 25565;
            var properties = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties();
            var tcpEndPoints = properties.GetActiveTcpListeners();
            var usedPorts = new HashSet<int>();

            foreach (var ep in tcpEndPoints)
            {
                usedPorts.Add(ep.Port);
            }

            while (usedPorts.Contains(port) && port < 65535)
            {
                port++;
            }

            return port;
        }

        #endregion
    }
}

