using System.Collections.Generic;
using System.Text.Json.Serialization;

// ReSharper disable ClassNeverInstantiated.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global
namespace Kairo.Models.Api;

// ──────────────────────────  User  ──────────────────────────

/// <summary>GET /user/info</summary>
public class UserInfoData
{
    [JsonPropertyName("avatar")] public string Avatar { get; set; } = string.Empty;
    [JsonPropertyName("bandwidth_limit")] public int BandwidthLimit { get; set; }
    [JsonPropertyName("created_at")] public string CreatedAt { get; set; } = string.Empty;
    [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
    [JsonPropertyName("has_kyc")] public bool HasKyc { get; set; }
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("is_baned")] public bool IsBaned { get; set; }
    [JsonPropertyName("kyc_status")] public string KycStatus { get; set; } = string.Empty;
    [JsonPropertyName("max_tunnel_count")] public int MaxTunnelCount { get; set; }
    [JsonPropertyName("role")] public string Role { get; set; } = string.Empty;
    [JsonPropertyName("today_checked")] public bool TodayChecked { get; set; }
    [JsonPropertyName("traffic_limit")] public long TrafficLimit { get; set; }
    [JsonPropertyName("traffic_used")] public long TrafficUsed { get; set; }
    [JsonPropertyName("username")] public string Username { get; set; } = string.Empty;
}

// ──────────────────────────  Traffic  ──────────────────────────

/// <summary>GET /user/traffic/tunnel/{tunnel_id}</summary>
public class TunnelRealtimeData
{
    [JsonPropertyName("tunnel_name")] public string TunnelName { get; set; } = string.Empty;
    [JsonPropertyName("node_id")] public string NodeId { get; set; } = string.Empty;
    [JsonPropertyName("traffic_in")] public long TrafficIn { get; set; }
    [JsonPropertyName("traffic_out")] public long TrafficOut { get; set; }
    [JsonPropertyName("total_traffic")] public long TotalTraffic { get; set; }
    [JsonPropertyName("connections")] public int Connections { get; set; }
    [JsonPropertyName("last_update")] public string LastUpdate { get; set; } = string.Empty;
}

/// <summary>GET /user/traffic/stats</summary>
public class TrafficStatsData
{
    [JsonPropertyName("user_id")] public string UserId { get; set; } = string.Empty;
    [JsonPropertyName("username")] public string Username { get; set; } = string.Empty;
    [JsonPropertyName("traffic_limit")] public long TrafficLimit { get; set; }
    [JsonPropertyName("traffic_used")] public long TrafficUsed { get; set; }
    [JsonPropertyName("traffic_remaining")] public long TrafficRemaining { get; set; }
}

/// <summary>GET /user/traffic/daily</summary>
public class DailyTrafficData
{
    [JsonPropertyName("daily_stats")] public List<DailyTrafficStat> DailyStats { get; set; } = new();
    [JsonPropertyName("days")] public int Days { get; set; }
    [JsonPropertyName("user_id")] public string UserId { get; set; } = string.Empty;
}

public class DailyTrafficStat
{
    [JsonPropertyName("date")] public string Date { get; set; } = string.Empty;
    [JsonPropertyName("tunnel_stats")] public List<string> TunnelStats { get; set; } = new();
    [JsonPropertyName("total_in")] public long TotalIn { get; set; }
    [JsonPropertyName("total_out")] public long TotalOut { get; set; }
    [JsonPropertyName("total_traffic")] public long TotalTraffic { get; set; }
}

/// <summary>GET /user/traffic/tunnels</summary>
public class TunnelTrafficListData
{
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("days")] public int Days { get; set; }
    [JsonPropertyName("end_time")] public string EndTime { get; set; } = string.Empty;
    [JsonPropertyName("start_time")] public string StartTime { get; set; } = string.Empty;
    [JsonPropertyName("tunnels")] public List<TunnelTrafficItem> Tunnels { get; set; } = new();
    [JsonPropertyName("user_id")] public string UserId { get; set; } = string.Empty;
}

public class TunnelTrafficItem
{
    [JsonPropertyName("tunnel_name")] public string TunnelName { get; set; } = string.Empty;
    [JsonPropertyName("node_id")] public string NodeId { get; set; } = string.Empty;
    [JsonPropertyName("total_in")] public long TotalIn { get; set; }
    [JsonPropertyName("total_out")] public long TotalOut { get; set; }
    [JsonPropertyName("total_traffic")] public long TotalTraffic { get; set; }
    [JsonPropertyName("max_connections")] public int MaxConnections { get; set; }
    [JsonPropertyName("remark")] public string Remark { get; set; } = string.Empty;
}

// ──────────────────────────  Tunnels  ──────────────────────────

/// <summary>GET /user/tunnel (paginated list)</summary>
public class TunnelListData
{
    [JsonPropertyName("limit")] public int Limit { get; set; }
    [JsonPropertyName("list")] public List<TunnelItem> List { get; set; } = new();
    [JsonPropertyName("page")] public int Page { get; set; }
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("total_page")] public int TotalPage { get; set; }
}

public class TunnelItem
{
    [JsonPropertyName("bandwidth_limit")] public int BandwidthLimit { get; set; }
    [JsonPropertyName("custom_domain")] public string CustomDomain { get; set; } = string.Empty;
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("local_ip")] public string LocalIp { get; set; } = string.Empty;
    [JsonPropertyName("local_port")] public int LocalPort { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("node_id")] public int NodeId { get; set; }
    [JsonPropertyName("remark")] public string Remark { get; set; } = string.Empty;
    [JsonPropertyName("remote_port")] public int RemotePort { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
}

/// <summary>GET /user/tunnel/{tunnel_name} (detail)</summary>
public class TunnelDetailData
{
    [JsonPropertyName("bandwidth_limit")] public int BandwidthLimit { get; set; }
    [JsonPropertyName("client_version")] public string ClientVersion { get; set; } = string.Empty;
    [JsonPropertyName("created_at")] public string CreatedAt { get; set; } = string.Empty;
    [JsonPropertyName("custom_domain")] public string CustomDomain { get; set; } = string.Empty;
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("local_ip")] public string LocalIp { get; set; } = string.Empty;
    [JsonPropertyName("local_port")] public int LocalPort { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("node_address")] public string NodeAddress { get; set; } = string.Empty;
    [JsonPropertyName("node_id")] public int NodeId { get; set; }
    [JsonPropertyName("node_name")] public string NodeName { get; set; } = string.Empty;
    [JsonPropertyName("remark")] public string Remark { get; set; } = string.Empty;
    [JsonPropertyName("remote_port")] public int RemotePort { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("tunnel_token")] public string TunnelToken { get; set; } = string.Empty;
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
}

/// <summary>POST /user/tunnel — create request body</summary>
public class CreateTunnelRequest
{
    [JsonPropertyName("node_id")] public int NodeId { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
    [JsonPropertyName("local_ip")] public string LocalIp { get; set; } = string.Empty;
    [JsonPropertyName("local_port")] public int LocalPort { get; set; }
    [JsonPropertyName("remote_port")] public int RemotePort { get; set; }
    [JsonPropertyName("custom_domain")] public string CustomDomain { get; set; } = string.Empty;
    [JsonPropertyName("remark")] public string Remark { get; set; } = string.Empty;
}

/// <summary>DELETE /user/tunnel/{tunnel_name} — response data</summary>
public class DeleteTunnelData
{
    [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
}

/// <summary>GET /user/frpc/config</summary>
public class FrpcConfigData
{
    [JsonPropertyName("config")] public string Config { get; set; } = string.Empty;
}

/// <summary>GET /tunnel/frpc/config/{token}</summary>
public class FrpcConfigByTokenData
{
    [JsonPropertyName("config")] public string Config { get; set; } = string.Empty;
    [JsonPropertyName("node_name")] public string NodeName { get; set; } = string.Empty;
    [JsonPropertyName("tunnel_remark")] public string TunnelRemark { get; set; } = string.Empty;
}

// ──────────────────────────  Domain Whitelist  ──────────────────────────

/// <summary>GET /user/domain</summary>
public class DomainListData
{
    [JsonPropertyName("domains")] public List<string> Domains { get; set; } = new();
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("page")] public int Page { get; set; }
    [JsonPropertyName("limit")] public int Limit { get; set; }
}

/// <summary>POST /user/domain — response data</summary>
public class AddDomainData
{
    [JsonPropertyName("created_at")] public string CreatedAt { get; set; } = string.Empty;
    [JsonPropertyName("domain")] public string Domain { get; set; } = string.Empty;
    [JsonPropertyName("expires_at")] public string ExpiresAt { get; set; } = string.Empty;
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("verification_subdomain")] public string VerificationSubdomain { get; set; } = string.Empty;
    [JsonPropertyName("verification_token")] public string VerificationToken { get; set; } = string.Empty;
}

/// <summary>POST /user/domain — request body</summary>
public class AddDomainRequest
{
    [JsonPropertyName("domain")] public string Domain { get; set; } = string.Empty;
    [JsonPropertyName("remark")] public string Remark { get; set; } = string.Empty;
}

/// <summary>POST /user/domain/verify — response data</summary>
public class VerifyDomainData
{
    [JsonPropertyName("domain")] public string Domain { get; set; } = string.Empty;
    [JsonPropertyName("is_verified")] public bool IsVerified { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("verified_at")] public string VerifiedAt { get; set; } = string.Empty;
}

/// <summary>POST /user/domain/verify — request body</summary>
public class VerifyDomainRequest
{
    [JsonPropertyName("domain")] public string Domain { get; set; } = string.Empty;
}

// ──────────────────────────  Nodes  ──────────────────────────

/// <summary>POST /user/nodes</summary>
public class NodeListData
{
    [JsonPropertyName("nodes")] public List<NodeData> Nodes { get; set; } = new();
    [JsonPropertyName("total")] public int Total { get; set; }
    [JsonPropertyName("page")] public int Page { get; set; }
    [JsonPropertyName("limit")] public int Limit { get; set; }
}

public class NodeData
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
    [JsonPropertyName("ip_address")] public string IpAddress { get; set; } = string.Empty;
    [JsonPropertyName("supported_protocols")] public List<string> SupportedProtocols { get; set; } = new();
    [JsonPropertyName("need_kyc")] public bool NeedKyc { get; set; }
    [JsonPropertyName("frps_version")] public string FrpsVersion { get; set; } = string.Empty;
    [JsonPropertyName("agent_version")] public string AgentVersion { get; set; } = string.Empty;
    [JsonPropertyName("available_ports")] public Dictionary<string, object>? AvailablePorts { get; set; }
    [JsonPropertyName("frps_port")] public int FrpsPort { get; set; }
    [JsonPropertyName("sponsor")] public string Sponsor { get; set; } = string.Empty;
    [JsonPropertyName("bandwidth")] public int Bandwidth { get; set; }
    [JsonPropertyName("last_seen")] public string LastSeen { get; set; } = string.Empty;
    [JsonPropertyName("created_at")] public string CreatedAt { get; set; } = string.Empty;
}

// ──────────────────────────  OAuth2 Management  ──────────────────────────

/// <summary>GET /user/oauth/apps</summary>
public class OAuthAppListData
{
    [JsonPropertyName("apps")] public List<string> Apps { get; set; } = new();
    [JsonPropertyName("total")] public int Total { get; set; }
}

/// <summary>GET /user/oauth/app/{id}</summary>
public class OAuthAppDetailData
{
    [JsonPropertyName("client_id")] public string ClientId { get; set; } = string.Empty;
    [JsonPropertyName("created_at")] public string CreatedAt { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("homepage")] public string Homepage { get; set; } = string.Empty;
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("is_active")] public bool IsActive { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("redirect_uris")] public List<string> RedirectUris { get; set; } = new();
    [JsonPropertyName("updated_at")] public string UpdatedAt { get; set; } = string.Empty;
}

/// <summary>POST /user/oauth/app — create request body</summary>
public class CreateOAuthAppRequest
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("redirect_uris")] public List<string> RedirectUris { get; set; } = new();
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("homepage")] public string Homepage { get; set; } = string.Empty;
}

/// <summary>POST /oauth2/approve — request body</summary>
public class OAuthApproveRequest
{
    [JsonPropertyName("client_id")] public string ClientId { get; set; } = string.Empty;
    [JsonPropertyName("redirect_uri")] public string RedirectUri { get; set; } = string.Empty;
    [JsonPropertyName("scope")] public string Scope { get; set; } = string.Empty;
    [JsonPropertyName("approved")] public bool Approved { get; set; }
    [JsonPropertyName("state")] public string State { get; set; } = string.Empty;
}

/// <summary>POST /oauth2/approve — response data</summary>
public class OAuthApproveData
{
    [JsonPropertyName("redirect_url")] public string RedirectUrl { get; set; } = string.Empty;
}

/// <summary>POST /oauth2/token — response data</summary>
public class OAuthTokenData
{
    [JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;
    [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = string.Empty;
    [JsonPropertyName("token_type")] public string TokenType { get; set; } = string.Empty;
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    [JsonPropertyName("scope")] public string Scope { get; set; } = string.Empty;
}

// ──────────────────────────  Client Version  ──────────────────────────

/// <summary>GET /client/version</summary>
public class ClientVersionData
{
    [JsonPropertyName("tag")] public string Tag { get; set; } = string.Empty;
    [JsonPropertyName("version")] public string Version { get; set; } = string.Empty;
}
