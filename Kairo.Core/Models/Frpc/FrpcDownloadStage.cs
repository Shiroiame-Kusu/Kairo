namespace Kairo.Core.Models;

public enum FrpcDownloadStage
{
    FetchingRelease,
    Downloading,
    Verifying,
    Extracting,
    Completed
}
