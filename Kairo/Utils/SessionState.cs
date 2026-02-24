namespace Kairo.Utils
{
    /// <summary>
    /// Holds user session data independent of UI.
    /// </summary>
    public static class SessionState
    {
        public static bool IsLoggedIn { get; set; }
        public static string? AvatarUrl { get; set; }
        public static int BandwidthLimit { get; set; }
        public static long TrafficLimit { get; set; }
        public static long TrafficUsed { get; set; }
        public static long TrafficRemaining => TrafficLimit - TrafficUsed;
        public static string Username { get; set; } = string.Empty;
        public static string Role { get; set; } = string.Empty;
        public static bool TodayChecked { get; set; }
        public static int MaxTunnelCount { get; set; }

        public static void Reset()
        {
            IsLoggedIn = false;
            AvatarUrl = null;
            BandwidthLimit = 0;
            TrafficLimit = 0;
            TrafficUsed = 0;
            Username = string.Empty;
            Role = string.Empty;
            TodayChecked = false;
            MaxTunnelCount = 0;
        }
    }
}
