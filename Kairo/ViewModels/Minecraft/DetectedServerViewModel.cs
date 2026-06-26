using System.Net;
using HakuuLib.MultiplayerLAN.Minecraft.Bedrock.Discovery;
using HakuuLib.MultiplayerLAN.Minecraft.Java.Discovery;

namespace Kairo.ViewModels;

public class DetectedServerViewModel : ViewModelBase
{
    public string Motd { get; }
    public int Port { get; }
    public IPEndPoint Sender { get; }
    public MinecraftEdition Edition { get; }
    public string EditionDisplay => Edition == MinecraftEdition.Java ? "Java" : "基岩";
    public string AddressDisplay => $"{Sender.Address}:{Port}";
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

    public DetectedServerViewModel(JavaLanAnnouncement announcement)
    {
        Edition = MinecraftEdition.Java;
        Motd = announcement.Motd;
        Port = announcement.Port;
        Sender = announcement.Sender;
    }

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
