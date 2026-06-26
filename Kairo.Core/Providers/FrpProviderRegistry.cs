namespace Kairo.Core.Providers;

public static class FrpProviderRegistry
{
    private static readonly IReadOnlyDictionary<FrpProviderType, IFrpProvider> Providers = new Dictionary<FrpProviderType, IFrpProvider>
    {
        [FrpProviderType.Locyan] = new LocyanFrpProvider(),
        [FrpProviderType.Lolia] = new LoliaFrpProvider()
    };

    public static IFrpProvider Get(FrpProviderType type) => Providers.TryGetValue(type, out var provider)
        ? provider
        : Providers[FrpProviderType.Locyan];

    public static IFrpProvider Get(string? providerId)
    {
        if (Enum.TryParse<FrpProviderType>(providerId, ignoreCase: true, out var type))
            return Get(type);
        return Providers.Values.FirstOrDefault(p => p.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase))
               ?? Providers[FrpProviderType.Locyan];
    }

    public static IReadOnlyList<IFrpProvider> All => Providers.Values.ToList();
}
