using System;

namespace IsekaiLeveling
{
    /// <summary>
    /// Utility for formatting large numbers with SI suffixes (k, M, B, T).
    /// 1,450,430,584 → "1.45B"    5,748 → "5.74k"    342 → "342"
    /// </summary>
    public static class NumberFormatting
    {
        private static readonly (long threshold, string suffix)[] Suffixes = new[]
        {
            (1_000_000_000_000L, "T"),
            (1_000_000_000L,     "B"),
            (1_000_000L,         "M"),
            (1_000L,             "k"),
        };

        /// <summary>
        /// Format a number with SI suffix. Numbers below 1000 are shown as-is.
        /// Examples: 342 → "342", 5748 → "5.74k", 1450430584 → "1.45B"
        /// </summary>
        public static string FormatNum(long value)
        {
            if (value < 0)
                return "-" + FormatNum(-value);

            foreach (var (threshold, suffix) in Suffixes)
            {
                if (value >= threshold)
                {
                    double divided = (double)value / threshold;
                    // Use 2 decimal places for cleaner display
                    if (divided >= 100)
                        return divided.ToString("F1") + suffix;  // 123.4B
                    if (divided >= 10)
                        return divided.ToString("F2") + suffix;  // 12.34B
                    return divided.ToString("F2") + suffix;      // 1.45B
                }
            }

            // Below 1000 — show as-is with no suffix
            return value.ToString("N0");
        }

        /// <summary>
        /// Format a float/double value with SI suffix.
        /// </summary>
        public static string FormatNum(float value)
        {
            return FormatNum((long)value);
        }

        /// <summary>
        /// Format a double value with SI suffix.
        /// </summary>
        public static string FormatNum(double value)
        {
            return FormatNum((long)value);
        }

        /// <summary>
        /// Format an int value with SI suffix.
        /// </summary>
        public static string FormatNum(int value)
        {
            return FormatNum((long)value);
        }
    }
}
