namespace Kairo.Cli;

internal sealed class CliOptions
{
    public bool ShowHelp { get; set; }
    public bool ShowVersion { get; set; }
    public bool GetOAuthUrl { get; set; }
    public bool ListProxies { get; set; }
    public bool InteractiveMode { get; set; }
    public bool ForceGitHub { get; set; }
    public string? OAuthCode { get; set; }
    public string? RefreshToken { get; set; }
    public string? FrpToken { get; set; }
    public string? FrpcPath { get; set; }
    public List<int> ProxyIds { get; } = new();
}
