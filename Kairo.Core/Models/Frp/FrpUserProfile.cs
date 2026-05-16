namespace Kairo.Core.Models;

public sealed class FrpUserProfile
{
    public int Id { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Avatar { get; init; } = string.Empty;
    public long Qq { get; init; }
    public string RegTime { get; init; } = string.Empty;
    public decimal Traffic { get; init; }
    public int Inbound { get; init; }
    public int Outbound { get; init; }
    public bool TodayChecked { get; init; }
}
