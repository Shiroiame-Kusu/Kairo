namespace Kairo.Core.Models;

public sealed class FrpcInstallResult
{
    public bool Success { get; init; }
    public string FrpcPath { get; init; } = string.Empty;
    public string Version { get; init; } = string.Empty;
    public string ProviderId { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
