using Kairo.Core.Configuration;
using Kairo.Core.Providers;
using Kairo.Utils.Configuration;

namespace Kairo.Utils;

internal static class ProviderAuth
{
    public static void ApplyCurrent()
    {
        var provider = Global.CurrentProvider;
        var state = Get(provider);
        Global.Config.AccessToken = state.AccessToken;
        Global.Config.RefreshToken = state.RefreshToken;
        Global.Config.Username = state.Username;
        Global.Config.ID = state.ID;
        Global.Config.FrpToken = state.FrpToken;
    }

    public static void SaveCurrent(bool save = true)
    {
        Set(Global.CurrentProvider, new ProviderAuthState
        {
            AccessToken = Global.Config.AccessToken,
            RefreshToken = Global.Config.RefreshToken,
            Username = Global.Config.Username,
            ID = Global.Config.ID,
            FrpToken = Global.Config.FrpToken
        }, save);
    }

    public static void ClearCurrent(bool save = true)
    {
        Set(Global.CurrentProvider, new ProviderAuthState(), save);
    }

    private static ProviderAuthState Get(IFrpProvider provider)
    {
        if (Global.Config.ProviderAuth.TryGetValue(provider.Id, out var state))
            return state;

        return provider.Type == FrpProviderType.Locyan && Global.Config.ProviderAuth.Count == 0
            ? new ProviderAuthState
            {
                AccessToken = Global.Config.AccessToken,
                RefreshToken = Global.Config.RefreshToken,
                Username = Global.Config.Username,
                ID = Global.Config.ID,
                FrpToken = Global.Config.FrpToken
            }
            : new ProviderAuthState();
    }

    private static void Set(IFrpProvider provider, ProviderAuthState state, bool save)
    {
        Global.Config.ProviderAuth[provider.Id] = state;
        if (provider.Type == FrpProviderType.Locyan)
        {
            Global.Config.AccessToken = state.AccessToken;
            Global.Config.RefreshToken = state.RefreshToken;
            Global.Config.Username = state.Username;
            Global.Config.ID = state.ID;
            Global.Config.FrpToken = state.FrpToken;
        }
        if (save)
            ConfigManager.Save();
    }
}
