using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using FluentAvalonia.UI.Controls;
using Kairo.Utils;
using Kairo.Components.DashBoard;
using Kairo.Core.Localization;

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
        private string _statusText = L.T("lan.status.ready");
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
                StatusText = L.T("lan.status.unsupported", Global.CurrentProvider.DisplayName);
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

        private void ShowSnackbar(string title, string? message, FAInfoBarSeverity severity)
        {
            (Access.DashBoard as DashBoard)?.OpenSnackbar(title, message, severity);
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
                        string.IsNullOrWhiteSpace(item.Name) ? L.T("lan.unnamedRoom") : item.Name,
                        string.IsNullOrWhiteSpace(item.Type) ? "TCP" : item.Type,
                        DeleteRoomAsync,
                        ShowStatus));
                }

                OnPropertyChanged(nameof(NoRooms));
                StatusText = L.Plural("lan.status.roomsLoaded", MyRooms.Count);
            }
            catch (Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/ViewModels/Minecraft/JoinRoomPageViewModel.cs:124", ex);
                ShowSnackbar(L.T("lan.refreshRoomsFailed"), ex.Message, FAInfoBarSeverity.Error);
            }
        }

        public async Task DeleteRoomAsync(RoomViewModel room)
        {
            try
            {
                if (!Global.CurrentProvider.SupportsMinecraftRooms)
                {
                    ShowSnackbar(L.T("lan.unavailable"), L.T("lan.status.unsupported", Global.CurrentProvider.DisplayName), FAInfoBarSeverity.Warning);
                    return;
                }

                var result = await _rooms.DeleteRoomAsync(room.Code);
                if (result?.Status == 200)
                {
                    ShowSnackbar(L.T("lan.deleted"), L.T("lan.deletedMessage", room.Name), FAInfoBarSeverity.Success);
                    await RefreshMyRoomsAsync();
                }
                else
                {
                    ShowSnackbar(L.T("lan.deleteFailed"), result?.Message, FAInfoBarSeverity.Error);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/ViewModels/Minecraft/JoinRoomPageViewModel.cs:151", ex);
                ShowSnackbar(L.T("lan.deleteError"), ex.Message, FAInfoBarSeverity.Error);
            }
        }

        #endregion

        #region Join Room

        private async Task JoinRoomAsync()
        {
            if (!Global.CurrentProvider.SupportsMinecraftRooms)
            {
                ShowSnackbar(L.T("lan.unavailable"), L.T("lan.status.unsupported", Global.CurrentProvider.DisplayName), FAInfoBarSeverity.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(JoinRoomCode))
            {
                ShowSnackbar(L.T("lan.enterCode"), null, FAInfoBarSeverity.Warning);
                return;
            }

            try
            {
                StatusText = L.T("lan.status.fetchingRoom");

                var room = await _rooms.GetRoomAsync(JoinRoomCode.Trim());
                if (room?.Status != 200)
                {
                    var msg = room?.Message ?? L.T("lan.roomNotFound");
                    ShowSnackbar(L.T("lan.joinFailed"), msg, FAInfoBarSeverity.Error);
                    StatusText = msg;
                    return;
                }

                var data = room.Data;
                var host = data?.Host;
                var port = data?.Port ?? 0;
                var name = string.IsNullOrWhiteSpace(data?.Name) ? L.T("lan.remoteServer") : data.Name;
                var type = string.IsNullOrWhiteSpace(data?.Type) ? "TCP" : data.Type;
                var isUdp = type.Equals("UDP", StringComparison.OrdinalIgnoreCase);

                if (string.IsNullOrEmpty(host) || port == 0)
                {
                    ShowSnackbar(L.T("lan.invalidRoom"), L.T("lan.noServerAddress"), FAInfoBarSeverity.Error);
                    return;
                }

                StatusText = L.T("lan.status.connecting", host, port);

                var forwarding = await _forwarding.StartAsync(host, port, name, isUdp);
                IsForwarderActive = _forwarding.IsActive;
                ForwarderStatus = forwarding.ForwarderStatus;
                StatusText = forwarding.StatusText;
                ShowSnackbar(L.T("lan.joined"), forwarding.SuccessMessage, FAInfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/ViewModels/Minecraft/JoinRoomPageViewModel.cs:209", ex);
                ShowSnackbar(L.T("lan.joinError"), ex.Message, FAInfoBarSeverity.Error);
                StatusText = L.T("lan.status.joinError", ex.Message);
            }
        }

        private async Task StopForwarderAsync()
        {
            await _forwarding.StopAsync();
            IsForwarderActive = false;
            ForwarderStatus = string.Empty;
            StatusText = L.T("lan.status.forwardingStopped");
        }

        #endregion
    }
}
