using Kairo.Core.Configuration;
using Kairo.Core.Providers;
using Kairo.Cli.Configuration;

namespace Kairo.Cli.Services;

internal static class ProviderAuth
{
    public static void ApplyCurrent() => Apply(FrpProviderRegistry.Get(CliConfigManager.Config.ProviderId));

    public static void Apply(IFrpProvider provider)
    {
        var state = Get(provider);
        CliConfigManager.Config.AccessToken = state.AccessToken;
        CliConfigManager.Config.RefreshToken = state.RefreshToken;
        CliConfigManager.Config.Username = state.Username;
        CliConfigManager.Config.ID = state.ID;
        CliConfigManager.Config.FrpToken = state.FrpToken;
    }

    public static void SaveCurrent(bool save = true) => Save(FrpProviderRegistry.Get(CliConfigManager.Config.ProviderId), save);

    public static void Save(IFrpProvider provider, bool save = true)
    {
        Set(provider, new ProviderAuthState
        {
            AccessToken = CliConfigManager.Config.AccessToken,
            RefreshToken = CliConfigManager.Config.RefreshToken,
            Username = CliConfigManager.Config.Username,
            ID = CliConfigManager.Config.ID,
            FrpToken = CliConfigManager.Config.FrpToken
        }, save);
    }

    public static void ClearCurrent(bool save = true) => Clear(FrpProviderRegistry.Get(CliConfigManager.Config.ProviderId), save);

    public static void Clear(IFrpProvider provider, bool save = true) => Set(provider, new ProviderAuthState(), save);

    private static ProviderAuthState Get(IFrpProvider provider)
    {
        if (CliConfigManager.Config.ProviderAuth.TryGetValue(provider.Id, out var state))
            return state;

        return provider.Type == FrpProviderType.Locyan && CliConfigManager.Config.ProviderAuth.Count == 0
            ? new ProviderAuthState
            {
                AccessToken = CliConfigManager.Config.AccessToken,
                RefreshToken = CliConfigManager.Config.RefreshToken,
                Username = CliConfigManager.Config.Username,
                ID = CliConfigManager.Config.ID,
                FrpToken = CliConfigManager.Config.FrpToken
            }
            : new ProviderAuthState();
    }

    private static void Set(IFrpProvider provider, ProviderAuthState state, bool save)
    {
        CliConfigManager.Config.ProviderAuth[provider.Id] = state;
        if (provider.Type == FrpProviderType.Locyan)
        {
            CliConfigManager.Config.AccessToken = state.AccessToken;
            CliConfigManager.Config.RefreshToken = state.RefreshToken;
            CliConfigManager.Config.Username = state.Username;
            CliConfigManager.Config.ID = state.ID;
            CliConfigManager.Config.FrpToken = state.FrpToken;
        }
        if (save)
            CliConfigManager.Save();
    }
}
