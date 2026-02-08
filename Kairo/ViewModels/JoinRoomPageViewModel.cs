using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using HakuuLib.MultiplayerLAN.Minecraft.Java.Forwarding;
using HakuuLib.MultiplayerLAN.Minecraft.Bedrock.Forwarding;
using Kairo.Utils;

namespace Kairo.ViewModels
{
    /// <summary>
    /// ViewModel for a Minecraft room the user has created.
    /// </summary>
    public class RoomViewModel : ViewModelBase
    {
        private readonly JoinRoomPageViewModel _parent;

        public string Code { get; }
        public int ProxyId { get; }
        public string Name { get; }
        public string Type { get; }
        public bool IsUdp => Type?.Equals("UDP", StringComparison.OrdinalIgnoreCase) == true;
        public string EditionDisplay => IsUdp ? "基岩" : "Java";
        public string CodeDisplay => $"房间代码: {Code}";

        public ICommand CopyCodeCommand { get; }
        public ICommand DeleteCommand { get; }

        public RoomViewModel(string code, int proxyId, string name, string type, JoinRoomPageViewModel parent)
        {
            Code = code;
            ProxyId = proxyId;
            Name = name;
            Type = type;
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

    public class JoinRoomPageViewModel : ViewModelBase, IDisposable
    {
        private readonly ApiClient _api = new();
        private JavaLanForwarder? _javaForwarder;
        private BedrockLanForwarder? _bedrockForwarder;

        // Join room input
        private string _joinRoomCode = string.Empty;
        public string JoinRoomCode
        {
            get => _joinRoomCode;
            set => SetProperty(ref _joinRoomCode, value);
        }

        // My rooms
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
        public ICommand JoinRoomCommand { get; }
        public ICommand RefreshMyRoomsCommand { get; }
        public ICommand StopForwarderCommand { get; }

        public JoinRoomPageViewModel()
        {
            JoinRoomCommand = new AsyncRelayCommand(JoinRoomAsync);
            RefreshMyRoomsCommand = new AsyncRelayCommand(RefreshMyRoomsAsync);
            StopForwarderCommand = new AsyncRelayCommand(StopForwarderAsync);
        }

        public void OnLoaded()
        {
            _ = RefreshMyRoomsAsync();
        }

        public void OnUnloaded()
        {
            // Keep forwarder running even when navigating away
        }

        public void Dispose()
        {
            _javaForwarder?.DisposeAsync().AsTask().Wait(1000);
            _bedrockForwarder?.DisposeAsync().AsTask().Wait(1000);
            _api.Dispose();
        }

        public void ShowStatus(string message)
        {
            StatusText = message;
        }

        private void ShowSnackbar(string title, string? message, InfoBarSeverity severity)
        {
            (Access.DashBoard as Components.DashBoard.DashBoard)?.OpenSnackbar(title, message, severity);
        }

        #region Rooms

        private async Task RefreshMyRoomsAsync()
        {
            try
            {
                var url = $"{Global.API}/game/minecraft/games?user_id={Global.Config.ID}";
                var resp = await _api.GetAsync(url);
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
                        var type = item?["type"]?.GetValue<string>() ?? "TCP";
                        MyRooms.Add(new RoomViewModel(code, proxyId, name, type, this));
                    }
                }

                OnPropertyChanged(nameof(NoRooms));
                StatusText = $"已加载 {MyRooms.Count} 个房间";
            }
            catch (Exception ex)
            {
                ShowSnackbar("刷新房间失败", ex.Message, InfoBarSeverity.Error);
            }
        }

        public async Task DeleteRoomAsync(RoomViewModel room)
        {
            try
            {
                var url = $"{Global.API}/game/minecraft/game?user_id={Global.Config.ID}&code={room.Code}";
                var resp = await _api.DeleteAsync(url);
                var body = await resp.Content.ReadAsStringAsync();
                var json = JsonNode.Parse(body);

                if (json?["status"]?.GetValue<int>() == 200)
                {
                    ShowSnackbar("删除成功", $"房间 {room.Name} 及关联隧道已删除", InfoBarSeverity.Success);
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

        #endregion

        #region Join Room

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
                var resp = await _api.GetAsync(url);
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
                var type = json?["data"]?["type"]?.GetValue<string>() ?? "TCP";
                var isUdp = type.Equals("UDP", StringComparison.OrdinalIgnoreCase);

                if (string.IsNullOrEmpty(host) || port == 0)
                {
                    ShowSnackbar("房间信息无效", "无法获取服务器地址", InfoBarSeverity.Error);
                    return;
                }

                // Stop existing forwarders
                await StopForwarderAsync();

                // Start forwarder based on type
                StatusText = $"正在连接到 {host}:{port}...";

                if (isUdp)
                {
                    // Bedrock Edition (UDP)
                    var listenPort = FindAvailableUdpPort();
                    var options = new BedrockLanForwarderOptions
                    {
                        RemoteHost = host,
                        RemotePort = port,
                        ListenPort = listenPort,
                        MotdLine1 = name,
                        MotdLine2 = "Kairo LAN Party"
                    };

                    _bedrockForwarder = new BedrockLanForwarder(options);
                    await _bedrockForwarder.StartAsync();

                    IsForwarderActive = true;
                    ForwarderStatus = $"基岩版转发 localhost:{listenPort} → {host}:{port}\n在 Minecraft 基岩版中打开好友页面即可看到 \"{name}\"";
                    StatusText = "已连接！在 Minecraft 基岩版的好友游戏列表中可以看到房间";
                    ShowSnackbar("加入成功", "在 Minecraft 基岩版好友列表中可以看到房间", InfoBarSeverity.Success);
                }
                else
                {
                    // Java Edition (TCP)
                    var listenPort = FindAvailableTcpPort();
                    var options = new JavaLanForwarderOptions
                    {
                        RemoteHost = host,
                        RemotePort = port,
                        ListenPort = listenPort,
                        Motd = name
                    };

                    _javaForwarder = new JavaLanForwarder(options);
                    await _javaForwarder.StartAsync();

                    IsForwarderActive = true;
                    ForwarderStatus = $"Java 版转发 localhost:{listenPort} → {host}:{port}\n在 Minecraft 中打开多人游戏即可看到 \"{name}\"";
                    StatusText = "已连接！在 Minecraft 多人游戏中可以看到房间";
                    ShowSnackbar("加入成功", "在 Minecraft 多人游戏中可以看到房间", InfoBarSeverity.Success);
                }
            }
            catch (Exception ex)
            {
                ShowSnackbar("加入房间异常", ex.Message, InfoBarSeverity.Error);
                StatusText = $"加入异常: {ex.Message}";
            }
        }

        private async Task StopForwarderAsync()
        {
            // Stop Java forwarder
            if (_javaForwarder != null)
            {
                try
                {
                    await _javaForwarder.StopAsync();
                    await _javaForwarder.DisposeAsync();
                }
                catch
                {
                    // Ignore cleanup errors
                }
                finally
                {
                    _javaForwarder = null;
                }
            }

            // Stop Bedrock forwarder
            if (_bedrockForwarder != null)
            {
                try
                {
                    await _bedrockForwarder.StopAsync();
                    await _bedrockForwarder.DisposeAsync();
                }
                catch
                {
                    // Ignore cleanup errors
                }
                finally
                {
                    _bedrockForwarder = null;
                }
            }

            IsForwarderActive = false;
            ForwarderStatus = string.Empty;
            StatusText = "转发已停止";
        }

        private static int FindAvailableTcpPort()
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

        private static int FindAvailableUdpPort()
        {
            int port = 19132; // Bedrock default port
            var properties = System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties();
            var udpEndPoints = properties.GetActiveUdpListeners();
            var usedPorts = new HashSet<int>();

            foreach (var ep in udpEndPoints)
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
