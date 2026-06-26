using ExtendedNumerics;

namespace Kairo.Utils
{
    /// <summary>
    /// Holds user session data independent of UI.
    /// </summary>
    public static class SessionState
    {
        public static bool IsLoggedIn { get; set; }
        public static string? AvatarUrl { get; set; }
        public static string? UserEmail { get; set; }
        public static long UserQQ { get; set; }
        public static string? UserRegTime { get; set; }
        public static int Inbound { get; set; }
        public static int Outbound { get; set; }
        public static BigDecimal Traffic { get; set; }

        public static void Reset()
        {
            IsLoggedIn = false;
            AvatarUrl = null;
            UserEmail = null;
            UserQQ = 0;
            UserRegTime = null;
            Inbound = 0;
            Outbound = 0;
            Traffic = 0;
        }
    }
}
