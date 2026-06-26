using Kairo.Core.Providers;
using Kairo.Cli.Configuration;

namespace Kairo.Cli.Services;

internal static class ProviderFrpcPath
{
    public static string Get(IFrpProvider provider)
    {
        if (CliConfigManager.Config.FrpcPaths.TryGetValue(provider.Id, out var path) && !string.IsNullOrWhiteSpace(path))
            return path;
        return string.Equals(provider.Id, "locyan", StringComparison.OrdinalIgnoreCase)
            ? CliConfigManager.Config.FrpcPath
            : string.Empty;
    }

    public static void Set(IFrpProvider provider, string path, bool save = true)
    {
        CliConfigManager.Config.FrpcPaths[provider.Id] = path;
        if (string.Equals(provider.Id, "locyan", StringComparison.OrdinalIgnoreCase))
            CliConfigManager.Config.FrpcPath = path;
        if (save)
            CliConfigManager.Save();
    }
}
