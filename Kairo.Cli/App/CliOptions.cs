namespace Kairo.Cli;

internal enum CliCommand
{
    /// <summary>默认：交互式向导，登录后启动隧道</summary>
    Run,
    Start,
    List,
    Login,
    Logout,
    Status,
    Provider,
    Help,
    Version
}

internal sealed class CliOptions
{
    public CliCommand Command { get; set; } = CliCommand.Run;
    public bool GetOAuthUrl { get; set; }
    public bool InteractiveMode { get; set; }
    public bool ForceGitHub { get; set; }
    public bool NoColor { get; set; }
    public string? OAuthCode { get; set; }
    public string? RefreshToken { get; set; }
    public string? FrpToken { get; set; }
    public string? FrpcPath { get; set; }

    /// <summary>是否通过参数请求切换服务商（--provider / provider 子命令）</summary>
    public bool ProviderRequested { get; set; }

    /// <summary>要切换到的服务商；为空表示仅查看或交互选择</summary>
    public string? ProviderName { get; set; }

    public List<int> ProxyIds { get; } = new();

    /// <summary>解析过程中发现的错误（未知参数、缺少参数值等）</summary>
    public List<string> Errors { get; } = new();

    public bool ShowHelp => Command == CliCommand.Help;
    public bool ShowVersion => Command == CliCommand.Version;
    public bool ListProxies => Command == CliCommand.List;
}
