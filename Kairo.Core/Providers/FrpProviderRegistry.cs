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

    /// <summary>
    /// 严格查找服务商（不回退到默认值），可匹配 Id、枚举名或显示名称（均忽略大小写）
    /// </summary>
    public static bool TryGet(string? nameOrId, out IFrpProvider provider)
    {
        provider = Providers[FrpProviderType.Locyan];
        var key = nameOrId?.Trim();
        if (string.IsNullOrEmpty(key) || int.TryParse(key, out _))
            return false;

        if (Enum.TryParse<FrpProviderType>(key, ignoreCase: true, out var type) && Providers.TryGetValue(type, out var byType))
        {
            provider = byType;
            return true;
        }

        var match = Providers.Values.FirstOrDefault(p =>
            p.Id.Equals(key, StringComparison.OrdinalIgnoreCase) ||
            p.DisplayName.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (match == null)
            return false;

        provider = match;
        return true;
    }

    public static IReadOnlyList<IFrpProvider> All => Providers.Values.ToList();
}
