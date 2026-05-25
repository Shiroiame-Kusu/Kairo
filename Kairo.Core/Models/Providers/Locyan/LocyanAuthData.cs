namespace Kairo.Core.Models;

public sealed class LocyanRefreshTokenData
{
    public string RefreshToken { get; init; } = string.Empty;
}

public sealed class LocyanAccessTokenData
{
    public int UserId { get; init; }
    public string AccessToken { get; init; } = string.Empty;
}
