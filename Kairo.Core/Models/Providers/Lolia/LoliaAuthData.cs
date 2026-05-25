namespace Kairo.Core.Models;

public sealed class LoliaOAuthTokenData
{
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public string TokenType { get; init; } = string.Empty;
    public int ExpiresIn { get; init; }
    public string Scope { get; init; } = string.Empty;
}
