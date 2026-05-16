namespace Kairo.Core.Models;

public sealed class FrpcDownloadProgress
{
    public FrpcDownloadStage Stage { get; init; }
    public string Message { get; init; } = string.Empty;
    public string DownloadUrl { get; init; } = string.Empty;
    public long ReceivedBytes { get; init; }
    public long TotalBytes { get; init; }
    public double Percent { get; init; }
    public double SpeedBytesPerSecond { get; init; }
    public string FrpcPath { get; init; } = string.Empty;
}
