using System;
using System.Globalization;

namespace Project.UI
{
    /// <summary>Short Russian HUD amounts. Wallet values and calculations retain their full integer precision.</summary>
    public static class CompactResourceAmount
    {
        private static readonly decimal[] Divisors = { 1000m, 1000000m, 1000000000m, 1000000000000m, 1000000000000000m, 1000000000000000000m };
        private static readonly string[] Suffixes = { "К", "М", "Б", "Т", "Кв", "Кн" };

        public static string Format(long value)
        {
            if (value <= 0) return "0";
            if (value < 10000) return value.ToString(CultureInfo.InvariantCulture);
            var unit = 0;
            while (unit + 1 < Divisors.Length && value >= Divisors[unit + 1]) unit++;
            var scaled = value / Divisors[unit];
            var rounded = Math.Round(scaled, scaled < 100 ? 1 : 0, MidpointRounding.AwayFromZero);
            if (rounded >= 1000 && unit + 1 < Divisors.Length)
            {
                unit++;
                scaled = value / Divisors[unit];
                rounded = Math.Round(scaled, scaled < 100 ? 1 : 0, MidpointRounding.AwayFromZero);
            }
            return rounded.ToString("0.#", CultureInfo.InvariantCulture) + Suffixes[unit];
        }
    }
}
