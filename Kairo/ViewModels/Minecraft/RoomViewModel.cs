using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Kairo.Utils;
using Kairo.Core.Localization;

namespace Kairo.ViewModels;

public class RoomViewModel : ViewModelBase
{
    private readonly Func<RoomViewModel, Task> _deleteAsync;
    private readonly Action<string> _showStatus;

    public string Code { get; }
    public int ProxyId { get; }
    public string Name { get; }
    public string Type { get; }
    public bool IsUdp => Type.Equals("UDP", StringComparison.OrdinalIgnoreCase);
    public string EditionDisplay => IsUdp ? L.T("lan.edition.bedrock") : "Java";
    public string CodeDisplay => L.T("lan.roomCode", Code);

    public ICommand CopyCodeCommand { get; }
    public ICommand DeleteCommand { get; }

    public RoomViewModel(string code, int proxyId, string name, string type, Func<RoomViewModel, Task> deleteAsync, Action<string> showStatus)
    {
        Code = code;
        ProxyId = proxyId;
        Name = name;
        Type = type;
        _deleteAsync = deleteAsync;
        _showStatus = showStatus;
        CopyCodeCommand = new RelayCommand(CopyCode);
        DeleteCommand = new AsyncRelayCommand(() => _deleteAsync(this));
    }

    private void CopyCode()
    {
        _ = CopyToClipboardAsync(Code);
        _showStatus(L.T("lan.codeCopied"));
    }

    private static async Task CopyToClipboardAsync(string text)
    {
        try
        {
            if (Access.DashBoard == null) return;
            var clipboard = TopLevel.GetTopLevel(Access.DashBoard)?.Clipboard;
            if (clipboard != null)
                await clipboard.SetTextAsync(text);
        }
        catch (Exception ex)
        {
            AppLogger.Exception("Unhandled exception in Kairo/ViewModels/Minecraft/RoomViewModel.cs:52", ex);
        }
    }
}
