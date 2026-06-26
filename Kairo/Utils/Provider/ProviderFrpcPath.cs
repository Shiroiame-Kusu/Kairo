using Kairo.Core.Providers;
using Kairo.Utils.Configuration;

namespace Kairo.Utils;

internal static class ProviderFrpcPath
{
    public static string Get(IFrpProvider provider)
    {
        if (Global.Config.FrpcPaths.TryGetValue(provider.Id, out var path) && !string.IsNullOrWhiteSpace(path))
            return path;
        return string.Equals(provider.Id, "locyan", System.StringComparison.OrdinalIgnoreCase)
            ? Global.Config.FrpcPath
            : string.Empty;
    }

    public static void Set(IFrpProvider provider, string path, bool save = true)
    {
        Global.Config.FrpcPaths[provider.Id] = path;
        if (string.Equals(provider.Id, "locyan", System.StringComparison.OrdinalIgnoreCase))
            Global.Config.FrpcPath = path;
        if (save)
            ConfigManager.Save();
    }
}
