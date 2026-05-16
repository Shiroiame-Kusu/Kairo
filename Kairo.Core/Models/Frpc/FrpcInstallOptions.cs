namespace Kairo.Core.Models;

public sealed class FrpcInstallOptions
{
    public bool UseMirror { get; init; }
    public bool ForceOrigin { get; init; }
    public string WorkDirectory { get; init; } = string.Empty;
}
