namespace Kairo.Core.Models;

public sealed class LoliaUserInfoData
{
    public int Id { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Avatar { get; init; } = string.Empty;
    public decimal TrafficLimit { get; init; }
    public decimal TrafficUsed { get; init; }
    public int BandwidthLimit { get; init; }
    public bool TodayChecked { get; init; }
}

public sealed class LoliaCheckinData
{
    public long TrafficBytes { get; init; }
    public decimal TrafficGb { get; init; }
}
