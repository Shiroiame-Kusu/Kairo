using Kairo.Core.Localization;

namespace Kairo.Core.Models;

/// <summary>
/// 隧道访问地址的统一计算规则（GUI 与 CLI 共用）
/// </summary>
public static class TunnelAddress
{
    /// <summary>xtcp / stcp / sudp 为点对点类型，没有公网访问地址</summary>
    public static bool IsPeerToPeer(string? type) => Normalize(type) is "xtcp" or "stcp" or "sudp";

    public static bool IsHttp(string? type) => Normalize(type) is "http" or "https";

    /// <summary>
    /// 获取公网访问地址：http/https 为域名，tcp/udp 为「节点地址:远程端口」，其余返回 null
    /// </summary>
    public static string? GetPublicAddress(string? type, string? nodeHost, string? nodeIp, int? remotePort, string? domain)
    {
        if (IsHttp(type))
            return string.IsNullOrWhiteSpace(domain) ? null : domain.Trim();

        if (Normalize(type) is "tcp" or "udp")
        {
            var host = !string.IsNullOrWhiteSpace(nodeHost) ? nodeHost : nodeIp;
            if (!string.IsNullOrWhiteSpace(host) && remotePort is > 0)
                return $"{host}:{remotePort}";
        }

        return null;
    }

    public static string GetPublicAddress(Tunnel tunnel) =>
        GetPublicAddress(tunnel.ProxyType, tunnel.NodeInfo?.Host, tunnel.NodeInfo?.Ip, tunnel.RemotePort, tunnel.Domain)
        ?? (IsPeerToPeer(tunnel.ProxyType) ? L.T("core.tunnel.peerToPeer") : "-");

    private static string Normalize(string? type) => (type ?? string.Empty).Trim().ToLowerInvariant();
}
