using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using HakuuLib.Minecraft.Discovery;
using HakuuLib.Minecraft.Forwarding;

namespace Kairo.Utils
{
    /// <summary>
    /// Helper for Minecraft LAN party hosting and joining.
    /// </summary>
    public static class MinecraftLanPartyHandler
    {
        private static MinecraftLanForwarder? _lanForwarder;
        private static LanDiscoveryListener? _lanHostListener;

        /// <summary>
        /// Creates a forwarder for joining a remote Minecraft server and broadcasting on LAN.
        /// </summary>
        public static MinecraftLanForwarder CreateForwarder(string remoteHost, int remotePort)
        {
            int port = 20000;
            while (port <= 65535 && IsPortInUse(port))
                port++;
            if (port > 65535)
                throw new Exception("无可用高位端口, 请检查您的网络情况");
            return CreateForwarder(port, remoteHost, remotePort);
        }

        /// <summary>
        /// Creates a forwarder with a specific local listen port.
        /// </summary>
        public static MinecraftLanForwarder CreateForwarder(int listenPort, string remoteHost, int remotePort)
        {
            return new MinecraftLanForwarder(new MinecraftLanForwarderOptions
            {
                ListenPort = listenPort,
                RemoteHost = remoteHost,
                RemotePort = remotePort
            });
        }

        /// <summary>
        /// Creates and starts a host listener for detecting local Minecraft LAN servers.
        /// </summary>
        public static LanDiscoveryListener CreateHostListener()
        {
            _lanHostListener ??= new LanDiscoveryListener();
            return _lanHostListener;
        }

        /// <summary>
        /// Starts detection and invokes the callback for each detected server.
        /// </summary>
        public static async Task StartDetectionAsync(
            Action<LanAnnouncement> onServerDetected,
            CancellationToken cancellationToken = default)
        {
            var listener = CreateHostListener();
            listener.AnnouncementReceived += (_, announcement) => onServerDetected(announcement);
            await listener.StartAsync(cancellationToken);
        }

        /// <summary>
        /// Stops the current host listener.
        /// </summary>
        public static async Task StopDetectionAsync()
        {
            if (_lanHostListener != null)
            {
                await _lanHostListener.StopAsync();
                await _lanHostListener.DisposeAsync();
                _lanHostListener = null;
            }
        }

        /// <summary>
        /// Starts a forwarder to join a remote room.
        /// </summary>
        public static async Task<MinecraftLanForwarder> StartForwarderAsync(
            string remoteHost,
            int remotePort,
            string motd,
            CancellationToken cancellationToken = default)
        {
            // Stop existing forwarder
            await StopForwarderAsync();

            var port = FindAvailablePort(25565);
            _lanForwarder = new MinecraftLanForwarder(new MinecraftLanForwarderOptions
            {
                ListenPort = port,
                RemoteHost = remoteHost,
                RemotePort = remotePort,
                Motd = motd
            });

            await _lanForwarder.StartAsync(cancellationToken);
            return _lanForwarder;
        }

        /// <summary>
        /// Stops the current forwarder.
        /// </summary>
        public static async Task StopForwarderAsync()
        {
            if (_lanForwarder != null)
            {
                await _lanForwarder.StopAsync();
                await _lanForwarder.DisposeAsync();
                _lanForwarder = null;
            }
        }

        /// <summary>
        /// Checks if a TCP port is currently in use.
        /// </summary>
        public static bool IsPortInUse(int port)
        {
            IPGlobalProperties ipProperties = IPGlobalProperties.GetIPGlobalProperties();
            IPEndPoint[] tcpEndPoints = ipProperties.GetActiveTcpListeners();
            foreach (var endPoint in tcpEndPoints)
            {
                if (endPoint.Port == port)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Finds an available port starting from the specified port.
        /// </summary>
        public static int FindAvailablePort(int startPort = 25565)
        {
            int port = startPort;
            while (IsPortInUse(port) && port < 65535)
                port++;
            if (port > 65535)
                throw new Exception("无可用端口");
            return port;
        }
    }
}
