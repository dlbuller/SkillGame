using System;

namespace SkillGame
{
    /// <summary>Caps the master volume overnight so the cabinet isn't loud after hours (off by default).</summary>
    public static class QuietHours
    {
        public static bool Enabled = false;
        public static int StartHour = 22;    // 10 PM
        public static int EndHour = 8;        // 8 AM
        public static int QuietPercent = 20;  // cap volume to this while quiet

        public static bool IsQuietNow(DateTime now) => Enabled && InWindow(now.Hour, StartHour, EndHour);

        /// <summary>Tests whether an hour falls in the window, handling overnight wrap-around.</summary>
        public static bool InWindow(int hour, int start, int end) =>
            start <= end ? (hour >= start && hour < end)
                         : (hour >= start || hour < end);
    }
}
