namespace Kairo.Core.Models;

public sealed class LocyanFrpTokenData
{
    public string Token { get; init; } = string.Empty;
}

public sealed class LocyanAnnouncementData
{
    public string Announcement { get; init; } = string.Empty;
    public string Broadcast { get; init; } = string.Empty;
}

public sealed class LocyanSignStatusData
{
    public bool Status { get; init; }
}

public sealed class LocyanSignData
{
    public decimal GetTraffic { get; init; }
}

public sealed class LocyanRandomPortData
{
    public int Port { get; init; }
}
