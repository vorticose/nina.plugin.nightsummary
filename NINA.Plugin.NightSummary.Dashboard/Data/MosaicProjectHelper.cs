using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NINA.Plugin.NightSummary.Data {

    /// <summary>
    /// Name helpers for dashboard-created mosaic projects (no Target Scheduler).
    /// Strips common panel suffixes so "North America Panel 1" + "Panel 2"
    /// become "North America".
    /// </summary>
    public static class MosaicProjectHelper {

        // " Panel 2", "-P1", "_pane 3", " P 12" at the end of a target name.
        private static readonly Regex PanelWordSuffix = new Regex(
            @"[\s_-]+(?:panels?|panes?|p)\s*\d+$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        // " #2" or " # 12"
        private static readonly Regex HashNumberSuffix = new Regex(
            @"[\s_-]+#\s*\d+$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        // "_1" or "-12" but not a space+number (keeps "M31")
        private static readonly Regex TrailingSepNumber = new Regex(
            @"[_-]\d+$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static string StripPanelSuffix(string name) {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var s = name.Trim();
            s = PanelWordSuffix.Replace(s, "");
            s = HashNumberSuffix.Replace(s, "");
            s = TrailingSepNumber.Replace(s, "");
            return s.Trim().TrimEnd('-', '_', ' ');
        }

        /// <summary>
        /// Suggested mosaic project name from selected panel target names.
        /// Falls back to "Mosaic" when the names do not share a useful prefix.
        /// </summary>
        public static string SuggestName(IEnumerable<string> targetNames) {
            var names = (targetNames ?? Array.Empty<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .ToList();
            if (names.Count == 0) return "Mosaic";
            if (names.Count == 1) {
                var one = StripPanelSuffix(names[0]);
                return string.IsNullOrEmpty(one) ? names[0] : one;
            }

            var stripped = names.Select(StripPanelSuffix).ToList();
            if (stripped.All(s => s.Length > 0) &&
                stripped.Skip(1).All(s => string.Equals(s, stripped[0], StringComparison.OrdinalIgnoreCase))) {
                return stripped[0];
            }

            var prefix = LongestCommonPrefix(names).TrimEnd(' ', '-', '_', '#');
            prefix = StripPanelSuffix(prefix);
            if (prefix.Length >= 3) return prefix;
            return "Mosaic";
        }

        internal static string LongestCommonPrefix(IReadOnlyList<string> names) {
            if (names == null || names.Count == 0) return "";
            var prefix = names[0] ?? "";
            for (int i = 1; i < names.Count; i++) {
                var s = names[i] ?? "";
                int n = Math.Min(prefix.Length, s.Length);
                int k = 0;
                while (k < n && char.ToUpperInvariant(prefix[k]) == char.ToUpperInvariant(s[k])) k++;
                prefix = prefix.Substring(0, k);
                if (prefix.Length == 0) return "";
            }
            return prefix;
        }
    }
}
