using System;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Kairo.Core.Providers;

namespace Kairo.Utils;

internal static class ProviderBranding
{
    public const string LocyanIcon = "avares://Kairo/Assets/kairo.ico";
    public const string LoliaIcon = "avares://Kairo/Assets/lolia.ico";
    public const string LocyanBanner = "avares://Kairo/Assets/banner.png";
    public const string LoliaBanner = "avares://Kairo/Assets/banner2.png";

    private static readonly Lazy<IImage> LocyanIconImage = new(() => LoadImage(LocyanIcon));
    private static readonly Lazy<IImage> LoliaIconImage = new(() => LoadImage(LoliaIcon));
    private static readonly Lazy<IImage> LocyanBannerImage = new(() => LoadImage(LocyanBanner));
    private static readonly Lazy<IImage> LoliaBannerImage = new(() => LoadImage(LoliaBanner));

    public static string GetIconPath(IFrpProvider provider) => provider.Type == FrpProviderType.Lolia ? LoliaIcon : LocyanIcon;

    public static IImage GetIconImage(IFrpProvider provider) => provider.Type == FrpProviderType.Lolia ? LoliaIconImage.Value : LocyanIconImage.Value;

    public static IImage GetBannerImage(IFrpProvider provider) => provider.Type == FrpProviderType.Lolia ? LoliaBannerImage.Value : LocyanBannerImage.Value;

    public static WindowIcon LoadIcon(IFrpProvider provider)
    {
        using var stream = AssetLoader.Open(new Uri(GetIconPath(provider)));
        return new WindowIcon(stream);
    }

    private static Bitmap LoadImage(string uri)
    {
        using var stream = AssetLoader.Open(new Uri(uri));
        return new Bitmap(stream);
    }
}
