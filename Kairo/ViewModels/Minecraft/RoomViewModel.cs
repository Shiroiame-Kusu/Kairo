using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Kairo.Utils;

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
    public string EditionDisplay => IsUdp ? "基岩" : "Java";
    public string CodeDisplay => $"房间代码: {Code}";

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
        _showStatus("房间代码已复制到剪贴板");
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
        catch
        {
        }
    }
}
