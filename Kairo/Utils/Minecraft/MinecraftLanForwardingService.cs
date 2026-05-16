using System;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using HakuuLib.MultiplayerLAN.Minecraft.Bedrock.Forwarding;
using HakuuLib.MultiplayerLAN.Minecraft.Java.Forwarding;

namespace Kairo.Utils;

internal sealed class MinecraftLanForwardingService : IDisposable
{
    private JavaLanForwarder? _javaForwarder;
    private BedrockLanForwarder? _bedrockForwarder;

    public bool IsActive { get; private set; }

    public async Task<MinecraftLanForwardingResult> StartAsync(string host, int port, string name, bool isUdp)
    {
        await StopAsync();

        try
        {
            if (isUdp)
            {
                var listenPort = FindAvailableUdpPort();
                var options = new BedrockLanForwarderOptions
                {
                    RemoteHost = host,
                    RemotePort = port,
                    ListenPort = listenPort,
                    MotdLine1 = name,
                    MotdLine2 = "Kairo LAN Party"
                };

                _bedrockForwarder = new BedrockLanForwarder(options);
                await _bedrockForwarder.StartAsync();
                IsActive = true;

                return new MinecraftLanForwardingResult
                {
                    ForwarderStatus = $"基岩版转发 localhost:{listenPort} → {host}:{port}\n在 Minecraft 基岩版中打开好友页面即可看到 \"{name}\"",
                    StatusText = "已连接！在 Minecraft 基岩版的好友游戏列表中可以看到房间",
                    SuccessMessage = "在 Minecraft 基岩版好友列表中可以看到房间"
                };
            }

            var javaListenPort = FindAvailableTcpPort();
            var javaOptions = new JavaLanForwarderOptions
            {
                RemoteHost = host,
                RemotePort = port,
                ListenPort = javaListenPort,
                Motd = name
            };

            _javaForwarder = new JavaLanForwarder(javaOptions);
            await _javaForwarder.StartAsync();
            IsActive = true;

            return new MinecraftLanForwardingResult
            {
                ForwarderStatus = $"Java 版转发 localhost:{javaListenPort} → {host}:{port}\n在 Minecraft 中打开多人游戏即可看到 \"{name}\"",
                StatusText = "已连接！在 Minecraft 多人游戏中可以看到房间",
                SuccessMessage = "在 Minecraft 多人游戏中可以看到房间"
            };
        }
        catch (System.Exception ex)
        {
            AppLogger.Exception("Unhandled exception in Kairo/Utils/Minecraft/MinecraftLanForwardingService.cs:67", ex);
            await StopAsync();
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (_javaForwarder != null)
        {
            try
            {
                await _javaForwarder.StopAsync();
                await _javaForwarder.DisposeAsync();
            }
            catch (System.Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/Utils/Minecraft/MinecraftLanForwardingService.cs:83", ex);
            }
            finally
            {
                _javaForwarder = null;
            }
        }

        if (_bedrockForwarder != null)
        {
            try
            {
                await _bedrockForwarder.StopAsync();
                await _bedrockForwarder.DisposeAsync();
            }
            catch (System.Exception ex)
            {
                AppLogger.Exception("Unhandled exception in Kairo/Utils/Minecraft/MinecraftLanForwardingService.cs:97", ex);
            }
            finally
            {
                _bedrockForwarder = null;
            }
        }

        IsActive = false;
    }

    public void Dispose()
    {
        StopAsync().Wait(1000);
    }

    private static int FindAvailableTcpPort()
    {
        var port = 25565;
        var properties = IPGlobalProperties.GetIPGlobalProperties();
        var tcpEndPoints = properties.GetActiveTcpListeners();
        var usedPorts = new HashSet<int>();

        foreach (var ep in tcpEndPoints)
        {
            usedPorts.Add(ep.Port);
        }

        while (usedPorts.Contains(port) && port < 65535)
        {
            port++;
        }

        return port;
    }

    private static int FindAvailableUdpPort()
    {
        var port = 19132;
        var properties = IPGlobalProperties.GetIPGlobalProperties();
        var udpEndPoints = properties.GetActiveUdpListeners();
        var usedPorts = new HashSet<int>();

        foreach (var ep in udpEndPoints)
        {
            usedPorts.Add(ep.Port);
        }

        while (usedPorts.Contains(port) && port < 65535)
        {
            port++;
        }

        return port;
    }
}

internal sealed class MinecraftLanForwardingResult
{
    public string ForwarderStatus { get; init; } = string.Empty;
    public string StatusText { get; init; } = string.Empty;
    public string SuccessMessage { get; init; } = string.Empty;
}
