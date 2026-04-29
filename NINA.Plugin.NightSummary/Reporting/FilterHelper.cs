using NINA.Plugin.NightSummary.Data;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NINA.Plugin.NightSummary.Reporting {

    /// <summary>
    /// Shared filter classification, sorting, and statistics helpers.
    /// Used by ReportGenerator, DiscordSender, and SessionService.
    /// </summary>
    internal static class FilterHelper {

        private static readonly HashSet<char> BroadbandFirstLetters = new HashSet<char> { 'L', 'R', 'G', 'B' };
        private static readonly HashSet<char> NarrowbandFirstLetters = new HashSet<char> { 'H', 'S', 'O' };
        private static readonly char[] SortPriority = { 'L', 'R', 'G', 'B', 'H', 'S', 'O' };

        private static Dictionary<string, string>? _overrides;

        /// <summary>
        /// Reloads user filter classification overrides from settings.
        /// Call at the start of each report generation to pick up changes.
        /// </summary>
        public static void ReloadOverrides() {
            _overrides = null;
        }

        private static Dictionary<string, string> Overrides {
            get {
                if (_overrides == null)
                    _overrides = ParseClassifications(SettingsManager.Instance.Current.FilterClassifications);
                return _overrides;
            }
        }

        /// <summary>
        /// Returns true if the filter is classified as broadband (user override or first-letter fallback).
        /// </summary>
        public static bool IsBroadband(string filter) {
            if (string.IsNullOrEmpty(filter)) return false;
            if (Overrides.TryGetValue(filter, out var cls)) return cls == "B";
            if (filter.Equals("None", StringComparison.OrdinalIgnoreCase)) return true;
            return BroadbandFirstLetters.Contains(char.ToUpperInvariant(filter[0]));
        }

        /// <summary>
        /// Returns true if the filter is classified as narrowband (user override or first-letter fallback).
        /// </summary>
        public static bool IsNarrowband(string filter) {
            if (string.IsNullOrEmpty(filter)) return false;
            if (Overrides.TryGetValue(filter, out var cls)) return cls == "N";
            return NarrowbandFirstLetters.Contains(char.ToUpperInvariant(filter[0]));
        }

        /// <summary>
        /// Returns true if the user explicitly excluded this filter from CV calculations.
        /// </summary>
        public static bool IsExcluded(string filter) {
            if (string.IsNullOrEmpty(filter)) return false;
            return Overrides.TryGetValue(filter, out var cls) && cls == "X";
        }

        /// <summary>
        /// Returns a sort key for canonical filter ordering: L, R, G, B, H, S, O, then others.
        /// </summary>
        public static int SortKey(string filter) {
            if (string.IsNullOrEmpty(filter)) return int.MaxValue;
            if (filter.Equals("None", StringComparison.OrdinalIgnoreCase)) return -1;
            var c = char.ToUpperInvariant(filter[0]);
            var idx = Array.IndexOf(SortPriority, c);
            return idx >= 0 ? idx : int.MaxValue;
        }

        /// <summary>
        /// Parses a serialized filter classification string (e.g. "Luminance=B,Ha=N,Green=X").
        /// </summary>
        public static Dictionary<string, string> ParseClassifications(string raw) {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(raw)) return result;
            foreach (var pair in raw.Split(',')) {
                // Split into at most 2 parts so a stray `=` in the value (e.g. "Foo=Bar=B")
                // doesn't drop the entry; the value side is opaque to this parser.
                var parts = pair.Split('=', 2);
                if (parts.Length == 2 && !string.IsNullOrWhiteSpace(parts[0]))
                    result[parts[0].Trim()] = parts[1].Trim();
            }
            return result;
        }

        // ── Statistics ──

        /// <summary>
        /// Coefficient of Variation as a percentage.
        /// </summary>
        public static double CV(List<double> values) {
            if (values.Count < 2) return 0;
            var avg = values.Average();
            if (avg == 0) return 0;
            return (StdDev(values) / avg) * 100;
        }

        /// <summary>
        /// Sample standard deviation.
        /// </summary>
        public static double StdDev(List<double> values) {
            if (values.Count < 2) return 0;
            var avg = values.Average();
            var sumOfSquares = values.Sum(v => Math.Pow(v - avg, 2));
            return Math.Sqrt(sumOfSquares / (values.Count - 1));
        }
    }
}
