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

        // "_1" is almost always a panel index. "-12" only when the char before
        // the hyphen is not a digit, so "Sh2-27" / "NGC-7000" stay intact.
        private static readonly Regex TrailingUnderscoreNumber = new Regex(
            @"_\d+$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex TrailingHyphenPanel = new Regex(
            @"(?<=\D)-\d{1,2}$",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);

        public static string StripPanelSuffix(string name) {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var s = name.Trim();
            s = PanelWordSuffix.Replace(s, "");
            s = HashNumberSuffix.Replace(s, "");
            s = TrailingUnderscoreNumber.Replace(s, "");
            s = TrailingHyphenPanel.Replace(s, "");
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

        /// <summary>
        /// Sky angle for an FOV rectangle. Prefer a plate-solve position angle,
        /// then a non-zero Target Scheduler rotation, then the rotator mechanical
        /// angle. A rectangle looks the same at theta and theta+180, so rotator
        /// vs plate-solve offsets of 180 deg are fine.
        /// </summary>
        public static double? EffectiveFovAngle(double? plateSolvePa, double? tsRotation, double? rotatorPosition) {
            if (plateSolvePa.HasValue) return plateSolvePa.Value;
            if (tsRotation.HasValue && tsRotation.Value != 0) return tsRotation.Value;
            if (rotatorPosition.HasValue) return rotatorPosition.Value;
            return null;
        }

        /// <summary>
        /// Replace null angles with the circular median of the known ones
        /// (mosaic panels almost always share a camera rotation).
        /// </summary>
        public static void CoalesceSiblingAngles(IList<double?> angles) {
            if (angles == null || angles.Count == 0) return;
            var known = new List<double>();
            for (int i = 0; i < angles.Count; i++) {
                if (angles[i].HasValue) known.Add(angles[i].Value);
            }
            if (known.Count == 0) return;
            var fill = CircularMedian(known);
            for (int i = 0; i < angles.Count; i++) {
                if (!angles[i].HasValue) angles[i] = fill;
            }
        }

        public static double CircularMedian(IReadOnlyList<double> degrees) {
            if (degrees == null || degrees.Count == 0) return 0;
            var reference = degrees[0];
            var unwrapped = degrees.Select(d => UnwrapTo(d, reference)).OrderBy(x => x).ToList();
            var mid = unwrapped[unwrapped.Count / 2];
            return Normalize360(mid);
        }

        internal static double UnwrapTo(double angle, double reference) {
            var d = angle - reference;
            while (d > 180) d -= 360;
            while (d < -180) d += 360;
            return reference + d;
        }

        internal static double Normalize360(double angle) {
            var a = angle % 360.0;
            if (a < 0) a += 360.0;
            return a;
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
