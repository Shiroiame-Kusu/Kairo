using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using FluentAvalonia.UI.Controls;
using Kairo.Utils;

namespace Kairo.ViewModels
{
    public class JoinRoomPageViewModel : ViewModelBase, IDisposable
    {
        private readonly ApiClient _api = new();
        private readonly MinecraftRoomApiClient _rooms;
        private readonly MinecraftLanForwardingService _forwarding = new();

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
            _rooms = new MinecraftRoomApiClient(_api);
            JoinRoomCommand = new AsyncRelayCommand(JoinRoomAsync);
            RefreshMyRoomsCommand = new AsyncRelayCommand(RefreshMyRoomsAsync);
            StopForwarderCommand = new AsyncRelayCommand(StopForwarderAsync);
        }

        public void OnLoaded()
        {
            if (!Global.CurrentProvider.SupportsMinecraftRooms)
            {
                StatusText = $"{Global.CurrentProvider.DisplayName} 暂不支持 Minecraft 联机房间";
                return;
            }

            _ = RefreshMyRoomsAsync();
        }

        public void OnUnloaded()
        {
            // Keep forwarder running even when navigating away
        }

        public void Dispose()
        {
            _forwarding.Dispose();
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
                if (!Global.CurrentProvider.SupportsMinecraftRooms)
                    return;

                var rooms = await _rooms.GetRoomsAsync();
                if (rooms?.Status != 200)
                    return;

                MyRooms.Clear();
                foreach (var item in rooms.Data?.List ?? new())
                {
                    MyRooms.Add(new RoomViewModel(
                        item.Code,
                        item.ProxyId,
                        string.IsNullOrWhiteSpace(item.Name) ? "未命名房间" : item.Name,
                        string.IsNullOrWhiteSpace(item.Type) ? "TCP" : item.Type,
                        DeleteRoomAsync,
                        ShowStatus));
                }

                OnPropertyChanged(nameof(NoRooms));
                StatusText = $"已加载 {MyRooms.Count} 个房间";
            }
            catch (Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/ViewModels/Minecraft/JoinRoomPageViewModel.cs:124", ex);
                ShowSnackbar("刷新房间失败", ex.Message, InfoBarSeverity.Error);
            }
        }

        public async Task DeleteRoomAsync(RoomViewModel room)
        {
            try
            {
                if (!Global.CurrentProvider.SupportsMinecraftRooms)
                {
                    ShowSnackbar("功能不可用", $"{Global.CurrentProvider.DisplayName} 暂不支持 Minecraft 联机房间", InfoBarSeverity.Warning);
                    return;
                }

                var result = await _rooms.DeleteRoomAsync(room.Code);
                if (result?.Status == 200)
                {
                    ShowSnackbar("删除成功", $"房间 {room.Name} 及关联隧道已删除", InfoBarSeverity.Success);
                    await RefreshMyRoomsAsync();
                }
                else
                {
                    ShowSnackbar("删除失败", result?.Message, InfoBarSeverity.Error);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/ViewModels/Minecraft/JoinRoomPageViewModel.cs:151", ex);
                ShowSnackbar("删除异常", ex.Message, InfoBarSeverity.Error);
            }
        }

        #endregion

        #region Join Room

        private async Task JoinRoomAsync()
        {
            if (!Global.CurrentProvider.SupportsMinecraftRooms)
            {
                ShowSnackbar("功能不可用", $"{Global.CurrentProvider.DisplayName} 暂不支持 Minecraft 联机房间", InfoBarSeverity.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(JoinRoomCode))
            {
                ShowSnackbar("请输入房间代码", null, InfoBarSeverity.Warning);
                return;
            }

            try
            {
                StatusText = "正在获取房间信息...";

                var room = await _rooms.GetRoomAsync(JoinRoomCode.Trim());
                if (room?.Status != 200)
                {
                    var msg = room?.Message ?? "房间不存在";
                    ShowSnackbar("加入失败", msg, InfoBarSeverity.Error);
                    StatusText = msg;
                    return;
                }

                var data = room.Data;
                var host = data?.Host;
                var port = data?.Port ?? 0;
                var name = string.IsNullOrWhiteSpace(data?.Name) ? "远程服务器" : data.Name;
                var type = string.IsNullOrWhiteSpace(data?.Type) ? "TCP" : data.Type;
                var isUdp = type.Equals("UDP", StringComparison.OrdinalIgnoreCase);

                if (string.IsNullOrEmpty(host) || port == 0)
                {
                    ShowSnackbar("房间信息无效", "无法获取服务器地址", InfoBarSeverity.Error);
                    return;
                }

                StatusText = $"正在连接到 {host}:{port}...";

                var forwarding = await _forwarding.StartAsync(host, port, name, isUdp);
                IsForwarderActive = _forwarding.IsActive;
                ForwarderStatus = forwarding.ForwarderStatus;
                StatusText = forwarding.StatusText;
                ShowSnackbar("加入成功", forwarding.SuccessMessage, InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/ViewModels/Minecraft/JoinRoomPageViewModel.cs:209", ex);
                ShowSnackbar("加入房间异常", ex.Message, InfoBarSeverity.Error);
                StatusText = $"加入异常: {ex.Message}";
            }
        }

        private async Task StopForwarderAsync()
        {
            await _forwarding.StopAsync();
            IsForwarderActive = false;
            ForwarderStatus = string.Empty;
            StatusText = "转发已停止";
        }

        #endregion
    }
}
