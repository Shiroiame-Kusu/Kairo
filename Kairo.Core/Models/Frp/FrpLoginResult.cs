namespace Kairo.Core.Models;

public sealed class FrpLoginResult
{
    public int UserId { get; init; }
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public FrpUserProfile User { get; init; } = new();
    public string FrpToken { get; init; } = string.Empty;
}
