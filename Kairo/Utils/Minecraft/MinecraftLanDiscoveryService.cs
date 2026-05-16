using System;
using System.Threading;
using System.Threading.Tasks;
using HakuuLib.MultiplayerLAN.Minecraft.Bedrock.Discovery;
using HakuuLib.MultiplayerLAN.Minecraft.Java.Discovery;

namespace Kairo.Utils;

internal sealed class MinecraftLanDiscoveryService : IDisposable
{
    private JavaLanDiscoveryListener? _javaDiscoveryListener;
    private BedrockLanDiscoveryListener? _bedrockDiscoveryListener;
    private CancellationTokenSource? _cts;

    public bool IsRunning { get; private set; }

    public event EventHandler<JavaLanAnnouncement>? JavaAnnouncementReceived;
    public event EventHandler<BedrockLanAnnouncement>? BedrockServerDiscovered;

    public async Task StartAsync()
    {
        if (IsRunning) return;

        _cts = new CancellationTokenSource();
        try
        {
            _javaDiscoveryListener = new JavaLanDiscoveryListener();
            _javaDiscoveryListener.AnnouncementReceived += OnJavaAnnouncementReceived;
            await _javaDiscoveryListener.StartAsync(_cts.Token);

            _bedrockDiscoveryListener = new BedrockLanDiscoveryListener();
            _bedrockDiscoveryListener.ServerDiscovered += OnBedrockServerDiscovered;
            await _bedrockDiscoveryListener.StartAsync(_cts.Token);

            IsRunning = true;
        }
        catch (System.Exception ex)
        {
            AppLogger.Exception("Unhandled exception in Kairo/Utils/Minecraft/MinecraftLanDiscoveryService.cs:37", ex);
            Stop();
            throw;
        }
    }

    public void Stop()
    {
        _cts?.Cancel();

        if (_javaDiscoveryListener != null)
        {
            _javaDiscoveryListener.AnnouncementReceived -= OnJavaAnnouncementReceived;
            _ = _javaDiscoveryListener.StopAsync();
            _ = _javaDiscoveryListener.DisposeAsync();
            _javaDiscoveryListener = null;
        }

        if (_bedrockDiscoveryListener != null)
        {
            _bedrockDiscoveryListener.ServerDiscovered -= OnBedrockServerDiscovered;
            _ = _bedrockDiscoveryListener.StopAsync();
            _ = _bedrockDiscoveryListener.DisposeAsync();
            _bedrockDiscoveryListener = null;
        }

        _cts?.Dispose();
        _cts = null;
        IsRunning = false;
    }

    public void Dispose()
    {
        Stop();
    }

    private void OnJavaAnnouncementReceived(object? sender, JavaLanAnnouncement announcement)
    {
        JavaAnnouncementReceived?.Invoke(this, announcement);
    }

    private void OnBedrockServerDiscovered(object? sender, BedrockLanAnnouncement announcement)
    {
        BedrockServerDiscovered?.Invoke(this, announcement);
    }
}
