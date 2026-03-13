using NINA.Plugin.NightSummary.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NINA.Plugin.NightSummary.Reporting {
    /// <summary>
    /// Generates inline SVG metric charts for embedding in the HTML report.
    /// Supports any combination of primary and secondary metrics on dual Y axes.
    /// </summary>
    public static class ChartGenerator {

        // Primary metric indices (ChartPrimaryMetric setting, SelectedIndex in primary ComboBox)
        public const int PrimaryHFR          = 0;
        public const int PrimaryFWHM         = 1;
        public const int PrimaryGuidingRMS   = 2;
        public const int PrimaryFocuserTemp  = 3;
        public const int PrimaryAmbientTemp  = 4;
        public const int PrimaryEccentricity = 5;

        // Secondary metric indices (ChartSecondaryMetric setting, SelectedIndex in secondary ComboBox)
        // Index 0 = None; indices 1–6 mirror the primary set offset by 1
        public const int SecNone         = 0;
        public const int SecHFR          = 1;
        public const int SecFWHM         = 2;
        public const int SecGuidingRMS   = 3;
        public const int SecFocuserTemp  = 4;
        public const int SecAmbientTemp  = 5;
        public const int SecEccentricity = 6;

        private const int Width        = 800;
        private const int Height       = 300;
        private const int PadLeft      = 55;
        private const int PadRight     = 20;
        private const int PadRightDual = 62;
        private const int PadTop       = 20;
        private const int PadBottom    = 45;

        private const string ColorBackground   = "#1a1a2e";
        private const string ColorGrid         = "#2a2a4a";
        private const string ColorAxis         = "#555577";
        private const string ColorPrimary      = "#7eb8f7";
        private const string ColorPrimaryDot   = "#a8d4ff";
        private const string ColorSecondary    = "#f7a87e";
        private const string ColorSecondaryDot = "#ffd4a8";
        private const string ColorLabel        = "#aaaacc";
        private const string ColorWarning      = "#f7a87e";
        private const string ColorWarningBg    = "#3a1e00";

        /// <summary>
        /// Returns the chart section heading based on configured metrics.
        /// </summary>
        public static string GetChartTitle(int primaryMetric, int secondaryMetric) {
            string primary = GetPrimaryLabel(primaryMetric);
            if (secondaryMetric == SecNone) return $"{primary} Vs. Time";
            return $"{primary} and {GetSecondaryLabel(secondaryMetric)} Vs. Time";
        }

        /// <summary>
        /// Generates an inline SVG chart. Always returns a non-empty SVG —
        /// shows a placeholder when no data is available.
        /// </summary>
        public static string GenerateMetricChart(List<ImageRecord> images, int primaryMetric, int secondaryMetric) {
            var primaryPts   = ExtractPrimary(images, primaryMetric);
            var secondaryPts = secondaryMetric > SecNone
                ? ExtractSecondary(images, secondaryMetric)
                : new List<(DateTime t, double v)>();

            bool hasPrimary    = primaryPts.Count >= 2;
            bool hasSecondary  = secondaryPts.Count >= 2;
            bool wantSecondary = secondaryMetric > SecNone;

            // Both empty → full placeholder
            if (!hasPrimary && !hasSecondary) {
                var msgs = new List<string> { GetPrimaryNoDataMsg(primaryMetric) };
                if (wantSecondary) msgs.Add(GetSecondaryNoDataMsg(secondaryMetric));
                return GeneratePlaceholderSvg(msgs);
            }

            // If primary has no data but secondary does: put secondary on the left axis
            bool swapped = !hasPrimary && hasSecondary;

            var leftPts  = swapped ? secondaryPts : primaryPts;
            var rightPts = (!swapped && hasSecondary) ? secondaryPts : new List<(DateTime t, double v)>();
            bool hasDual = rightPts.Count >= 2;

            string leftColor     = swapped ? ColorSecondary    : ColorPrimary;
            string leftDotColor  = swapped ? ColorSecondaryDot : ColorPrimaryDot;
            string leftAxisLabel = swapped
                ? GetSecondaryAxisLabel(secondaryMetric)
                : GetPrimaryAxisLabel(primaryMetric);

            // Warning badge
            string? badgeText    = null;
            string? badgeSubtext = null;
            if (swapped) {
                badgeText    = $"{GetPrimaryLabel(primaryMetric)}: no data";
                badgeSubtext = GetPrimaryNoDataHint(primaryMetric);
            } else if (wantSecondary && !hasSecondary) {
                badgeText    = $"{GetSecondaryLabel(secondaryMetric)}: no data";
                badgeSubtext = GetSecondaryNoDataHint(secondaryMetric);
            }

            int padRight = hasDual ? PadRightDual : PadRight;
            int plotW    = Width  - PadLeft - padRight;
            int plotH    = Height - PadTop  - PadBottom;

            // X range — union of all points
            var allTimes    = leftPts.Select(p => p.t).Concat(rightPts.Select(p => p.t)).ToList();
            var minTime     = allTimes.Min();
            var maxTime     = allTimes.Max();
            double totalSec = Math.Max((maxTime - minTime).TotalSeconds, 1);

            // Y scales
            double leftMinSpan = swapped ? GetSecondaryMinSpan(secondaryMetric) : GetPrimaryMinSpan(primaryMetric);
            var (minL, maxL, rangeL) = ComputeScale(leftPts.Select(p => p.v), leftMinSpan);
            double minR = 0, maxR = 0, rangeR = 1;
            if (hasDual)
                (minR, maxR, rangeR) = ComputeScale(rightPts.Select(p => p.v), GetSecondaryMinSpan(secondaryMetric));

            double ToX(DateTime t)  => PadLeft + ((t - minTime).TotalSeconds / totalSec) * plotW;
            double ToYL(double v)   => PadTop  + plotH - ((v - minL) / rangeL) * plotH;
            double ToYR(double v)   => PadTop  + plotH - ((v - minR) / rangeR) * plotH;

            var sb = new StringBuilder();
            sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {Width} {Height}\" style=\"width:100%;max-width:{Width}px;display:block;margin:0 auto;font-family:sans-serif\">");
            sb.AppendLine($"<rect width=\"{Width}\" height=\"{Height}\" fill=\"{ColorBackground}\" rx=\"6\"/>");

            // Horizontal grid lines + left Y labels
            const int ySteps = 5;
            for (int i = 0; i <= ySteps; i++) {
                double v = minL + (rangeL / ySteps) * i;
                double y = ToYL(v);
                sb.AppendLine($"<line x1=\"{PadLeft}\" y1=\"{y:F1}\" x2=\"{Width - padRight}\" y2=\"{y:F1}\" stroke=\"{ColorGrid}\" stroke-width=\"1\"/>");
                sb.AppendLine($"<text x=\"{PadLeft - 6}\" y=\"{y + 4:F1}\" fill=\"{ColorLabel}\" font-size=\"11\" text-anchor=\"end\">{v:F1}</text>");
            }

            // Right Y axis
            if (hasDual) {
                int rightLineX  = Width - padRight;
                int rightLabelX = rightLineX + 6;
                int rightTitleX = Width - 10;
                sb.AppendLine($"<line x1=\"{rightLineX}\" y1=\"{PadTop}\" x2=\"{rightLineX}\" y2=\"{PadTop + plotH}\" stroke=\"{ColorAxis}\" stroke-width=\"1\"/>");
                for (int i = 0; i <= ySteps; i++) {
                    double v = minR + (rangeR / ySteps) * i;
                    double y = ToYR(v);
                    sb.AppendLine($"<text x=\"{rightLabelX}\" y=\"{y + 4:F1}\" fill=\"{ColorSecondary}\" font-size=\"11\" text-anchor=\"start\">{v:F1}</text>");
                }
                sb.AppendLine($"<text x=\"{rightTitleX}\" y=\"{Height / 2}\" fill=\"{ColorSecondary}\" font-size=\"11\" text-anchor=\"middle\" transform=\"rotate(90,{rightTitleX},{Height / 2})\">{GetSecondaryAxisLabel(secondaryMetric)}</text>");
            }

            // X axis time labels
            int xSteps = Math.Max(1, Math.Min(6, leftPts.Count - 1));
            if (hasDual) xSteps = Math.Max(1, Math.Min(6, Math.Max(leftPts.Count, rightPts.Count) - 1));
            for (int i = 0; i <= xSteps; i++) {
                var t    = minTime + TimeSpan.FromSeconds(totalSec / xSteps * i);
                double x = ToX(t);
                sb.AppendLine($"<line x1=\"{x:F1}\" y1=\"{PadTop}\" x2=\"{x:F1}\" y2=\"{PadTop + plotH}\" stroke=\"{ColorGrid}\" stroke-width=\"1\"/>");
                sb.AppendLine($"<text x=\"{x:F1}\" y=\"{Height - 10}\" fill=\"{ColorLabel}\" font-size=\"11\" text-anchor=\"middle\">{t:HH:mm}</text>");
            }

            // Left and bottom axes
            sb.AppendLine($"<line x1=\"{PadLeft}\" y1=\"{PadTop}\" x2=\"{PadLeft}\" y2=\"{PadTop + plotH}\" stroke=\"{ColorAxis}\" stroke-width=\"1\"/>");
            sb.AppendLine($"<line x1=\"{PadLeft}\" y1=\"{PadTop + plotH}\" x2=\"{Width - padRight}\" y2=\"{PadTop + plotH}\" stroke=\"{ColorAxis}\" stroke-width=\"1\"/>");

            // Left Y axis title
            sb.AppendLine($"<text x=\"14\" y=\"{Height / 2}\" fill=\"{(swapped ? ColorSecondary : ColorLabel)}\" font-size=\"11\" text-anchor=\"middle\" transform=\"rotate(-90,14,{Height / 2})\">{leftAxisLabel}</text>");

            // Secondary line (drawn first so primary renders on top)
            if (hasDual) {
                var rightPoly = string.Join(" ", rightPts.Select(p => $"{ToX(p.t):F1},{ToYR(p.v):F1}"));
                sb.AppendLine($"<polyline points=\"{rightPoly}\" fill=\"none\" stroke=\"{ColorSecondary}\" stroke-width=\"2\" stroke-linejoin=\"round\" stroke-dasharray=\"6,3\"/>");
                foreach (var p in rightPts)
                    sb.AppendLine($"<circle cx=\"{ToX(p.t):F1}\" cy=\"{ToYR(p.v):F1}\" r=\"3\" fill=\"{ColorSecondaryDot}\"/>");
            }

            // Primary line
            var leftPoly = string.Join(" ", leftPts.Select(p => $"{ToX(p.t):F1},{ToYL(p.v):F1}"));
            sb.AppendLine($"<polyline points=\"{leftPoly}\" fill=\"none\" stroke=\"{leftColor}\" stroke-width=\"2\" stroke-linejoin=\"round\"/>");
            foreach (var p in leftPts)
                sb.AppendLine($"<circle cx=\"{ToX(p.t):F1}\" cy=\"{ToYL(p.v):F1}\" r=\"3\" fill=\"{leftDotColor}\"/>");

            // Warning badge
            if (badgeText != null) {
                int bx = PadLeft + 8;
                int by = PadTop  + 6;
                int bw = Math.Min(plotW - 16, 280);
                int bh = badgeSubtext != null ? 32 : 20;
                sb.AppendLine($"<rect x=\"{bx}\" y=\"{by}\" width=\"{bw}\" height=\"{bh}\" rx=\"3\" fill=\"{ColorWarningBg}\" stroke=\"{ColorWarning}\" stroke-width=\"1\" opacity=\"0.92\"/>");
                sb.AppendLine($"<text x=\"{bx + 7}\" y=\"{by + 14}\" fill=\"{ColorWarning}\" font-size=\"11\">&#x26A0; {EscapeXml(badgeText)}</text>");
                if (badgeSubtext != null)
                    sb.AppendLine($"<text x=\"{bx + 7}\" y=\"{by + 27}\" fill=\"{ColorWarning}\" font-size=\"10\" opacity=\"0.8\">{EscapeXml(badgeSubtext)}</text>");
            }

            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        // ── Data extraction ──────────────────────────────────────────────────

        private static List<(DateTime t, double v)> ExtractPrimary(List<ImageRecord> images, int metric) {
            return metric switch {
                PrimaryHFR         => images.Where(i => i.HFR > 0)            .OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.HFR)).ToList(),
                PrimaryFWHM        => images.Where(i => i.FWHM > 0)           .OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.FWHM)).ToList(),
                PrimaryGuidingRMS  => images.Where(i => i.GuidingRMSTotal > 0) .OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.GuidingRMSTotal)).ToList(),
                PrimaryFocuserTemp  => images.Where(i => i.FocuserTemp.HasValue).OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.FocuserTemp!.Value)).ToList(),
                PrimaryAmbientTemp  => images.Where(i => i.AmbientTemp.HasValue).OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.AmbientTemp!.Value)).ToList(),
                PrimaryEccentricity => images.Where(i => i.Eccentricity > 0)   .OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.Eccentricity)).ToList(),
                _                   => new List<(DateTime, double)>()
            };
        }

        private static List<(DateTime t, double v)> ExtractSecondary(List<ImageRecord> images, int metric) {
            return metric switch {
                SecHFR         => images.Where(i => i.HFR > 0)            .OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.HFR)).ToList(),
                SecFWHM        => images.Where(i => i.FWHM > 0)           .OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.FWHM)).ToList(),
                SecGuidingRMS  => images.Where(i => i.GuidingRMSTotal > 0) .OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.GuidingRMSTotal)).ToList(),
                SecFocuserTemp  => images.Where(i => i.FocuserTemp.HasValue).OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.FocuserTemp!.Value)).ToList(),
                SecAmbientTemp  => images.Where(i => i.AmbientTemp.HasValue).OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.AmbientTemp!.Value)).ToList(),
                SecEccentricity => images.Where(i => i.Eccentricity > 0)   .OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.Eccentricity)).ToList(),
                _               => new List<(DateTime, double)>()
            };
        }

        // ── Scale helpers ────────────────────────────────────────────────────

        private static (double min, double max, double range) ComputeScale(IEnumerable<double> vals, double minSpan) {
            var list  = vals.ToList();
            var min   = Math.Floor(list.Min() * 10) / 10;
            var max   = Math.Ceiling(list.Max() * 10) / 10;
            var range = max - min;
            if (range < minSpan) {
                var mid = (min + max) / 2.0;
                min   = Math.Round(mid - minSpan / 2, 1);
                max   = Math.Round(mid + minSpan / 2, 1);
                range = max - min;
            }
            return (min, max, range);
        }

        private static double GetPrimaryMinSpan(int metric) => metric switch {
            PrimaryFocuserTemp  => 2.0,
            PrimaryAmbientTemp  => 2.0,
            PrimaryEccentricity => 0.2,
            _                   => 0.5
        };

        private static double GetSecondaryMinSpan(int metric) => metric switch {
            SecFocuserTemp  => 2.0,
            SecAmbientTemp  => 2.0,
            SecEccentricity => 0.2,
            _               => 0.5
        };

        // ── Label helpers ────────────────────────────────────────────────────

        private static string GetPrimaryLabel(int metric) => metric switch {
            PrimaryHFR          => "HFR",
            PrimaryFWHM         => "FWHM",
            PrimaryGuidingRMS   => "Guiding RMS",
            PrimaryFocuserTemp  => "Focuser Temp",
            PrimaryAmbientTemp  => "Ambient Temp",
            PrimaryEccentricity => "Eccentricity",
            _                   => "HFR"
        };

        private static string GetSecondaryLabel(int metric) => metric switch {
            SecHFR          => "HFR",
            SecFWHM         => "FWHM",
            SecGuidingRMS   => "Guiding RMS",
            SecFocuserTemp  => "Focuser Temp",
            SecAmbientTemp  => "Ambient Temp",
            SecEccentricity => "Eccentricity",
            _               => ""
        };

        private static string GetPrimaryAxisLabel(int metric) => metric switch {
            PrimaryHFR          => "HFR (\")",
            PrimaryFWHM         => "FWHM (\")",
            PrimaryGuidingRMS   => "RMS (\")",
            PrimaryFocuserTemp  => "Temp (&#176;C)",
            PrimaryAmbientTemp  => "Temp (&#176;C)",
            PrimaryEccentricity => "Eccentricity",
            _                   => "HFR (\")"
        };

        private static string GetSecondaryAxisLabel(int metric) => metric switch {
            SecHFR          => "HFR (\")",
            SecFWHM         => "FWHM (\")",
            SecGuidingRMS   => "RMS (\")",
            SecFocuserTemp  => "Temp (&#176;C)",
            SecAmbientTemp  => "Temp (&#176;C)",
            SecEccentricity => "Eccentricity",
            _               => ""
        };

        // ── No-data messaging ────────────────────────────────────────────────

        private static string GetPrimaryNoDataMsg(int metric) => metric switch {
            PrimaryHFR          => "No HFR data \u2014 fewer than 2 images with star detection",
            PrimaryFWHM         => "No FWHM data \u2014 Hocus Focus plugin required",
            PrimaryGuidingRMS   => "No Guiding RMS data recorded this session",
            PrimaryFocuserTemp  => "No focuser temperature data recorded",
            PrimaryAmbientTemp  => "No ambient temperature data recorded",
            PrimaryEccentricity => "No eccentricity data \u2014 Hocus Focus plugin required",
            _                   => "No data available"
        };

        private static string? GetPrimaryNoDataHint(int metric) => metric switch {
            PrimaryFWHM         => "Requires Hocus Focus plugin",
            PrimaryFocuserTemp  => "Requires focuser with temperature sensor",
            PrimaryAmbientTemp  => "Requires NINA weather data source",
            PrimaryEccentricity => "Requires Hocus Focus plugin",
            _                   => null
        };

        private static string GetSecondaryNoDataMsg(int metric) => metric switch {
            SecHFR          => "No HFR data recorded",
            SecFWHM         => "No FWHM data \u2014 Hocus Focus plugin required",
            SecGuidingRMS   => "No Guiding RMS data recorded",
            SecFocuserTemp  => "No focuser temperature data recorded",
            SecAmbientTemp  => "No ambient temperature data recorded",
            SecEccentricity => "No eccentricity data \u2014 Hocus Focus plugin required",
            _               => ""
        };

        private static string? GetSecondaryNoDataHint(int metric) => metric switch {
            SecFWHM         => "Requires Hocus Focus plugin",
            SecFocuserTemp  => "Requires focuser with temperature sensor",
            SecAmbientTemp  => "Requires NINA weather data source",
            SecEccentricity => "Requires Hocus Focus plugin",
            _               => null
        };

        // ── Placeholder SVG ──────────────────────────────────────────────────

        private static string GeneratePlaceholderSvg(List<string> messages) {
            var sb = new StringBuilder();
            sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {Width} {Height}\" style=\"width:100%;max-width:{Width}px;display:block;margin:0 auto;font-family:sans-serif\">");
            sb.AppendLine($"<rect width=\"{Width}\" height=\"{Height}\" fill=\"{ColorBackground}\" rx=\"6\"/>");
            int cx   = Width  / 2;
            int iconY = Height / 2 - (messages.Count > 1 ? 24 : 18);
            sb.AppendLine($"<text x=\"{cx}\" y=\"{iconY}\" fill=\"{ColorWarning}\" font-size=\"22\" text-anchor=\"middle\">&#x26A0;</text>");
            for (int i = 0; i < messages.Count; i++) {
                int y = iconY + 28 + i * 18;
                sb.AppendLine($"<text x=\"{cx}\" y=\"{y}\" fill=\"{ColorLabel}\" font-size=\"12\" text-anchor=\"middle\">{EscapeXml(messages[i])}</text>");
            }
            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        private static string EscapeXml(string s) => s
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }
}
