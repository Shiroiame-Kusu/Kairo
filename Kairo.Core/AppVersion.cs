using System.Text.RegularExpressions;

namespace Kairo.Core;

/// <summary>
/// Represents a version in the format v{a}.{b}.{c}-{tagname}.{rev}
/// where a, b, c, rev are integers and tagname is Alpha/Beta/RC/Release.
/// </summary>
public readonly partial struct AppVersion : IComparable<AppVersion>, IEquatable<AppVersion>
{
    /// <summary>Major version number</summary>
    public int Major { get; }
    
    /// <summary>Minor version number</summary>
    public int Minor { get; }
    
    /// <summary>Patch version number</summary>
    public int Patch { get; }
    
    /// <summary>Release channel (Alpha, Beta, RC, Release)</summary>
    public ReleaseChannel Channel { get; }
    
    /// <summary>Revision number within the channel</summary>
    public int Revision { get; }

    public AppVersion(int major, int minor, int patch, ReleaseChannel channel, int revision)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Channel = channel;
        Revision = revision;
    }

    /// <summary>
    /// Parse a version string like "v1.2.3-Alpha.4" or "1.2.3-release.1"
    /// </summary>
    public static bool TryParse(string? input, out AppVersion result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var match = VersionRegex().Match(input);
        if (!match.Success)
            return false;

        if (!int.TryParse(match.Groups["major"].Value, out var major) ||
            !int.TryParse(match.Groups["minor"].Value, out var minor) ||
            !int.TryParse(match.Groups["patch"].Value, out var patch))
            return false;

        var channelStr = match.Groups["channel"].Value;
        var channel = ParseChannel(channelStr);

        int.TryParse(match.Groups["rev"].Value, out var revision);

        result = new AppVersion(major, minor, patch, channel, revision);
        return true;
    }

    /// <summary>
    /// Parse a version string, throwing if invalid.
    /// </summary>
    public static AppVersion Parse(string input)
    {
        if (!TryParse(input, out var result))
            throw new FormatException($"Invalid version format: {input}");
        return result;
    }

    /// <summary>
    /// Create an AppVersion from individual components.
    /// </summary>
    public static AppVersion FromComponents(string version, string branch, int revision)
    {
        var parts = version.Split('.');
        int major = 0, minor = 0, patch = 0;
        if (parts.Length >= 1) int.TryParse(parts[0], out major);
        if (parts.Length >= 2) int.TryParse(parts[1], out minor);
        if (parts.Length >= 3) int.TryParse(parts[2], out patch);
        var channel = ParseChannel(branch);
        return new AppVersion(major, minor, patch, channel, revision);
    }

    private static ReleaseChannel ParseChannel(string? channelStr)
    {
        if (string.IsNullOrWhiteSpace(channelStr))
            return ReleaseChannel.Release;

        return channelStr switch
        {
            "Alpha" => ReleaseChannel.Alpha,
            "Beta" => ReleaseChannel.Beta,
            "RC" => ReleaseChannel.RC,
            "Release" => ReleaseChannel.Release,
            _ => ReleaseChannel.Release
        };
    }

    /// <summary>
    /// Compare two versions. Higher channels (Release > RC > Beta > Alpha) are considered "newer".
    /// </summary>
    public int CompareTo(AppVersion other)
    {
        // Compare major.minor.patch first
        var cmp = Major.CompareTo(other.Major);
        if (cmp != 0) return cmp;

        cmp = Minor.CompareTo(other.Minor);
        if (cmp != 0) return cmp;

        cmp = Patch.CompareTo(other.Patch);
        if (cmp != 0) return cmp;

        // Compare channel (Release > RC > Beta > Alpha)
        cmp = ((int)Channel).CompareTo((int)other.Channel);
        if (cmp != 0) return cmp;

        // Compare revision
        return Revision.CompareTo(other.Revision);
    }

    public bool Equals(AppVersion other) =>
        Major == other.Major &&
        Minor == other.Minor &&
        Patch == other.Patch &&
        Channel == other.Channel &&
        Revision == other.Revision;

    public override bool Equals(object? obj) => obj is AppVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Channel, Revision);

    public static bool operator ==(AppVersion left, AppVersion right) => left.Equals(right);
    public static bool operator !=(AppVersion left, AppVersion right) => !left.Equals(right);
    public static bool operator <(AppVersion left, AppVersion right) => left.CompareTo(right) < 0;
    public static bool operator >(AppVersion left, AppVersion right) => left.CompareTo(right) > 0;
    public static bool operator <=(AppVersion left, AppVersion right) => left.CompareTo(right) <= 0;
    public static bool operator >=(AppVersion left, AppVersion right) => left.CompareTo(right) >= 0;

    /// <summary>
    /// Returns the tag name portion (e.g., "Alpha", "Beta", "RC", "Release")
    /// </summary>
    public string ChannelName => Channel switch
    {
        ReleaseChannel.Alpha => "Alpha",
        ReleaseChannel.Beta => "Beta",
        ReleaseChannel.RC => "RC",
        ReleaseChannel.Release => "Release",
        _ => "Release"
    };

    /// <summary>
    /// Returns the version in tag format: v{major}.{minor}.{patch}-{channel}.{rev}
    /// </summary>
    public string ToTagString() => $"v{Major}.{Minor}.{Patch}-{ChannelName}.{Revision}";

    /// <summary>
    /// Returns a display string: {major}.{minor}.{patch}-{channel}.{rev}
    /// </summary>
    public override string ToString() => $"{Major}.{Minor}.{Patch}-{ChannelName}.{Revision}";

    /// <summary>
    /// Returns just the semver portion: {major}.{minor}.{patch}
    /// </summary>
    public string ToSemVerString() => $"{Major}.{Minor}.{Patch}";

    // Regex pattern: v{a}.{b}.{c}-{tagname}.{rev}
    // Examples: v1.2.3-Alpha.4, v3.3.0-Release.1
    [GeneratedRegex(@"^v?(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)-(?<channel>Alpha|Beta|RC|Release)\.(?<rev>\d+)$")]
    private static partial Regex VersionRegex();
}

/// <summary>
/// Release channel enumeration. Order matters for comparison (higher = more stable).
/// </summary>
public enum ReleaseChannel
{
    Alpha = 0,
    Beta = 1,
    RC = 2,
    Release = 3
}
