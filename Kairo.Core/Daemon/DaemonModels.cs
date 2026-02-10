using System.Text.Json.Serialization;

namespace Kairo.Core.Daemon;

/// <summary>
/// 隧道运行状态
/// </summary>
public sealed class ProxyState
{
    [JsonPropertyName("proxyId")]
    public int ProxyId { get; set; }

    [JsonPropertyName("pid")]
    public int Pid { get; set; }

    [JsonPropertyName("startTime")]
    public DateTime StartTime { get; set; }

    [JsonPropertyName("running")]
    public bool Running { get; set; }

    [JsonPropertyName("frpcPath")]
    public string FrpcPath { get; set; } = "";

    [JsonPropertyName("logFile")]
    public string LogFile { get; set; } = "";
}

/// <summary>
/// Daemon 持久化状态
/// </summary>
public sealed class DaemonState
{
    [JsonPropertyName("daemonPid")]
    public int DaemonPid { get; set; }

    [JsonPropertyName("protocolVersion")]
    public int ProtocolVersion { get; set; } = DaemonConstants.ProtocolVersion;

    [JsonPropertyName("startTime")]
    public DateTime StartTime { get; set; }

    [JsonPropertyName("socketPath")]
    public string SocketPath { get; set; } = "";

    [JsonPropertyName("proxies")]
    public List<ProxyState> Proxies { get; set; } = [];
}

/// <summary>
/// 日志行
/// </summary>
public sealed class LogLine
{
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("proxyId")]
    public int ProxyId { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("isError")]
    public bool IsError { get; set; }
}

// ─── IPC Request/Response ─────────────────────────────────────────

/// <summary>
/// IPC 请求消息
/// </summary>
public sealed class IpcRequest
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("data")]
    public Dictionary<string, object>? Data { get; set; }
}

/// <summary>
/// IPC 响应消息
/// </summary>
public sealed class IpcResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("data")]
    public Dictionary<string, object>? Data { get; set; }
}

/// <summary>
/// IPC 事件消息（daemon → client 推送）
/// </summary>
public sealed class IpcEvent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("data")]
    public Dictionary<string, object>? Data { get; set; }
}

// ─── 消息类型常量 ──────────────────────────────────────────────────

/// <summary>
/// IPC 请求类型
/// </summary>
public static class IpcRequestType
{
    public const string Ping = "Ping";
    public const string StartProxy = "StartProxy";
    public const string StopProxy = "StopProxy";
    public const string StopAll = "StopAll";
    public const string GetStatus = "GetStatus";
    public const string GetLogHistory = "GetLogHistory";
    public const string SubscribeLogs = "SubscribeLogs";
    public const string UnsubscribeLogs = "UnsubscribeLogs";
    public const string Shutdown = "Shutdown";
}

/// <summary>
/// IPC 事件类型
/// </summary>
public static class IpcEventType
{
    public const string LogLine = "LogLine";
    public const string ProxyStarted = "ProxyStarted";
    public const string ProxyExited = "ProxyExited";
}
