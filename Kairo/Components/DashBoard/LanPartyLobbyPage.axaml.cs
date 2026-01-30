using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using FluentAvalonia.UI.Controls;
using Kairo.Components.DashBoard.LanParty;
using Kairo.ViewModels;

namespace Kairo.Components.DashBoard;

public partial class LanPartyLobbyPage : UserControl
{
    private readonly LanPartyLobbyPageViewModel _viewModel;
    private HostRoomPage? _hostRoomPage;
    private JoinRoomPage? _joinRoomPage;
    
    private NavigationView? _lobbyNavView;
    private NavigationViewItem? _hostNavItem;
    private ContentControl? _lobbyContentHost;

    public LanPartyLobbyPage()
    {
        InitializeComponent();
        _viewModel = new LanPartyLobbyPageViewModel();
        DataContext = _viewModel;
        
        Loaded += (_, _) =>
        {
            _lobbyNavView = this.FindControl<NavigationView>("LobbyNavView");
            _hostNavItem = this.FindControl<NavigationViewItem>("HostNavItem");
            _lobbyContentHost = this.FindControl<ContentControl>("LobbyContentHost");
            
            if (_lobbyNavView != null && _hostNavItem != null)
            {
                _lobbyNavView.SelectedItem = _hostNavItem;
            }
        };
    }

    private void LobbyNavView_OnSelectionChanged(object? sender, NavigationViewSelectionChangedEventArgs e)
    {
        if (e.SelectedItem is NavigationViewItem nvi && nvi.Tag is string tag)
        {
            OpenPage(tag);
        }
    }

    private void OpenPage(string tag)
    {
        if (_lobbyContentHost == null)
        {
            _lobbyContentHost = this.FindControl<ContentControl>("LobbyContentHost");
        }
        
        if (_lobbyContentHost == null) return;
        
        switch (tag)
        {
            case "host":
                _lobbyContentHost.Content = _hostRoomPage ??= new HostRoomPage();
                break;
            case "join":
                _lobbyContentHost.Content = _joinRoomPage ??= new JoinRoomPage();
                break;
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}