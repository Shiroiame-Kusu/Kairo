using ExtendedNumerics;
using Kairo.Components;
using Kairo.Core.Models;
using AppUserInfo = Kairo.Models.UserInfo;

namespace Kairo.Utils;

internal static class FrpModelMapper
{
    public static AppUserInfo ToUserInfo(this FrpUserProfile user, string frpToken = "") => new()
    {
        ID = user.Id,
        Username = user.Username,
        Email = user.Email,
        Avatar = user.Avatar,
        QQ = user.Qq,
        RegTime = user.RegTime,
        Traffic = new BigDecimal(user.Traffic),
        Inbound = user.Inbound,
        Outbound = user.Outbound,
        FrpToken = frpToken,
        Limit = new AppUserInfo.LimitInfo
        {
            Inbound = user.Inbound,
            Outbound = user.Outbound
        }
    };

    public static Proxy ToProxy(this FrpTunnel tunnel) => new()
    {
        Id = tunnel.Id,
        ProxyName = tunnel.Name,
        ProxyType = tunnel.Type,
        LocalIp = tunnel.LocalIp,
        LocalPort = tunnel.LocalPort,
        RemotePort = tunnel.RemotePort,
        UseCompression = tunnel.UseCompression,
        UseEncryption = tunnel.UseEncryption,
        Domain = tunnel.Domain,
        SecretKey = tunnel.SecretKey,
        NodeInfo = tunnel.Node == null ? null : new ProxyNode
        {
            Id = tunnel.Node.Id,
            Name = tunnel.Node.Name,
            Host = tunnel.Node.Host,
            Ip = tunnel.Node.Ip
        }
    };

    public static Core.Models.Tunnel ToCliTunnel(this FrpTunnel tunnel) => new()
    {
        Id = tunnel.Id,
        ProxyName = tunnel.Name,
        ProxyType = tunnel.Type,
        LocalIp = tunnel.LocalIp,
        LocalPort = tunnel.LocalPort,
        RemotePort = tunnel.RemotePort,
        UseCompression = tunnel.UseCompression,
        UseEncryption = tunnel.UseEncryption,
        Domain = tunnel.Domain,
        SecretKey = tunnel.SecretKey,
        NodeInfo = tunnel.Node == null ? null : new Core.Models.TunnelNode
        {
            Id = tunnel.Node.Id,
            Name = tunnel.Node.Name,
            Host = tunnel.Node.Host,
            Ip = tunnel.Node.Ip
        }
    };
}
