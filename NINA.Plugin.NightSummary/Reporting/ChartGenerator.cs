using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.MyPluginProperties;
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
        public const int PrimaryAltitude     = 6;
        public const int PrimaryAirmass      = 7;
        public const int PrimaryHumidity     = 8;
        public const int PrimaryFocuserPos   = 9;
        public const int PrimarySkyQuality   = 10;
        public const int PrimaryCloudCover   = 11;
        public const int PrimaryCameraTemp   = 12;
        public const int PrimaryDewPoint     = 13;
        public const int PrimaryWindSpeed    = 14;
        public const int PrimaryPressure     = 15;
        public const int PrimaryStarCount    = 16;
        public const int PrimaryAzimuth      = 17;
        public const int PrimarySeeingFWHM   = 18;

        // Secondary metric indices (ChartSecondaryMetric setting, SelectedIndex in secondary ComboBox)
        // Index 0 = None; indices 1–N mirror the primary set offset by 1
        public const int SecNone         = 0;
        public const int SecHFR          = 1;
        public const int SecFWHM         = 2;
        public const int SecGuidingRMS   = 3;
        public const int SecFocuserTemp  = 4;
        public const int SecAmbientTemp  = 5;
        public const int SecEccentricity = 6;
        public const int SecAltitude     = 7;
        public const int SecAirmass      = 8;
        public const int SecHumidity     = 9;
        public const int SecFocuserPos   = 10;
        public const int SecSkyQuality   = 11;
        public const int SecCloudCover   = 12;
        public const int SecCameraTemp   = 13;
        public const int SecDewPoint     = 14;
        public const int SecWindSpeed    = 15;
        public const int SecPressure     = 16;
        public const int SecStarCount    = 17;
        public const int SecAzimuth      = 18;
        public const int SecSeeingFWHM   = 19;

        private const int Width        = 800;
        private const int Height       = 300;
        private const int PadLeft      = 55;
        private const int PadRight     = 20;
        private const int PadRightDual = 62;
        private const int PadTop       = 20;
        private const int PadBottom    = 45;

        private static bool IsLight => Settings.Default.ReportLightMode;

        private static string ColorBackground   => IsLight ? "#f5f5f5" : "#1a1a2e";
        private static string ColorGrid         => IsLight ? "#c8cdd4" : "#2a2a4a";
        private static string ColorAxis         => IsLight ? "#666688" : "#555577";
        private static string ColorPrimary      => IsLight ? "#2563b8" : "#7eb8f7";
        private static string ColorPrimaryDot   => IsLight ? "#1a4f9e" : "#a8d4ff";
        private static string ColorSecondary    => IsLight ? "#d47020" : "#f7a87e";
        private static string ColorSecondaryDot => IsLight ? "#b85c10" : "#ffd4a8";
        private static string ColorLabel        => IsLight ? "#555577" : "#aaaacc";
        private static string ColorWarning      => IsLight ? "#d47020" : "#f7a87e";
        private static string ColorWarningBg    => IsLight ? "#fff3cd" : "#3a1e00";

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
            var (minL, maxL, stepL) = ComputeNiceScale(leftPts.Select(p => p.v), leftMinSpan);
            double rangeL = maxL - minL;
            double minR = 0, maxR = 0, stepR = 1, rangeR = 1;
            if (hasDual) {
                (minR, maxR, stepR) = ComputeNiceScale(rightPts.Select(p => p.v), GetSecondaryMinSpan(secondaryMetric));
                rangeR = maxR - minR;
            }

            double ToX(DateTime t)  => PadLeft + ((t - minTime).TotalSeconds / totalSec) * plotW;
            double ToYL(double v)   => PadTop  + plotH - ((v - minL) / rangeL) * plotH;
            double ToYR(double v)   => PadTop  + plotH - ((v - minR) / rangeR) * plotH;

            var sb = new StringBuilder();
            sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {Width} {Height}\" style=\"width:100%;max-width:{Width}px;display:block;margin:0 auto 16px;font-family:sans-serif\">");
            sb.AppendLine("<style>circle { cursor: pointer; }</style>");
            sb.AppendLine($"<rect width=\"{Width}\" height=\"{Height}\" fill=\"{ColorBackground}\" rx=\"6\"/>");

            // Horizontal grid lines + left Y labels
            string leftFmt  = GetValueFormat(swapped ? secondaryMetric : primaryMetric, !swapped);
            for (double v = minL; v <= maxL + stepL * 0.001; v += stepL) {
                double y = ToYL(v);
                sb.AppendLine($"<line x1=\"{PadLeft}\" y1=\"{y:F1}\" x2=\"{Width - padRight}\" y2=\"{y:F1}\" stroke=\"{ColorGrid}\" stroke-width=\"1\"/>");
                sb.AppendLine($"<text x=\"{PadLeft - 6}\" y=\"{y + 4:F1}\" fill=\"{ColorLabel}\" font-size=\"11\" text-anchor=\"end\">{v.ToString(leftFmt)}</text>");
            }

            // Right Y axis
            if (hasDual) {
                string rightFmt = GetValueFormat(secondaryMetric, false);
                int rightLineX  = Width - padRight;
                int rightLabelX = rightLineX + 6;
                int rightTitleX = Width - 10;
                sb.AppendLine($"<line x1=\"{rightLineX}\" y1=\"{PadTop}\" x2=\"{rightLineX}\" y2=\"{PadTop + plotH}\" stroke=\"{ColorAxis}\" stroke-width=\"1\"/>");
                for (double v = minR; v <= maxR + stepR * 0.001; v += stepR) {
                    double y = ToYR(v);
                    sb.AppendLine($"<text x=\"{rightLabelX}\" y=\"{y + 4:F1}\" fill=\"{ColorSecondary}\" font-size=\"11\" text-anchor=\"start\">{v.ToString(rightFmt)}</text>");
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
                string secUnit = GetTooltipUnit(secondaryMetric, false);
                string secFmt  = GetValueFormat(secondaryMetric, false);
                foreach (var p in rightPts)
                    sb.AppendLine($"<circle cx=\"{ToX(p.t):F1}\" cy=\"{ToYR(p.v):F1}\" r=\"3\" fill=\"{ColorSecondaryDot}\"><title>{p.t:HH:mm} — {p.v.ToString(secFmt)}{secUnit}</title></circle>");
            }

            // Primary line
            var leftPoly = string.Join(" ", leftPts.Select(p => $"{ToX(p.t):F1},{ToYL(p.v):F1}"));
            sb.AppendLine($"<polyline points=\"{leftPoly}\" fill=\"none\" stroke=\"{leftColor}\" stroke-width=\"2\" stroke-linejoin=\"round\"/>");
            int leftMetricIdx = swapped ? secondaryMetric : primaryMetric;
            string leftUnit    = GetTooltipUnit(leftMetricIdx, !swapped);
            string leftTipFmt  = GetValueFormat(leftMetricIdx, !swapped);
            foreach (var p in leftPts)
                sb.AppendLine($"<circle cx=\"{ToX(p.t):F1}\" cy=\"{ToYL(p.v):F1}\" r=\"3\" fill=\"{leftDotColor}\"><title>{p.t:HH:mm} — {p.v.ToString(leftTipFmt)}{leftUnit}</title></circle>");

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
                PrimaryAltitude     => images.Where(i => i.Altitude.HasValue)  .OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.Altitude!.Value)).ToList(),
                PrimaryAirmass      => images.Where(i => i.Airmass.HasValue)   .OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.Airmass!.Value)).ToList(),
                PrimaryHumidity     => images.Where(i => i.Humidity.HasValue)  .OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.Humidity!.Value)).ToList(),
                PrimaryFocuserPos   => images.Where(i => i.FocuserPosition.HasValue).OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, (double)i.FocuserPosition!.Value)).ToList(),
                PrimarySkyQuality   => images.Where(i => i.SkyQuality.HasValue)   .OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.SkyQuality!.Value)).ToList(),
                PrimaryCloudCover   => images.Where(i => i.CloudCover.HasValue)   .OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.CloudCover!.Value)).ToList(),
                PrimaryCameraTemp   => images.Where(i => i.CameraTemp.HasValue)   .OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.CameraTemp!.Value)).ToList(),
                PrimaryDewPoint     => images.Where(i => i.DewPoint.HasValue)     .OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.DewPoint!.Value)).ToList(),
                PrimaryWindSpeed    => images.Where(i => i.WindSpeed.HasValue)    .OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.WindSpeed!.Value)).ToList(),
                PrimaryPressure     => images.Where(i => i.Pressure.HasValue)     .OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.Pressure!.Value)).ToList(),
                PrimaryStarCount    => images.Where(i => i.StarCount > 0)         .OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, (double)i.StarCount)).ToList(),
                PrimaryAzimuth      => images.Where(i => i.Azimuth.HasValue)      .OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.Azimuth!.Value)).ToList(),
                PrimarySeeingFWHM   => images.Where(i => i.SeeingFWHM.HasValue)  .OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.SeeingFWHM!.Value)).ToList(),
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
                SecAltitude     => images.Where(i => i.Altitude.HasValue)  .OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.Altitude!.Value)).ToList(),
                SecAirmass      => images.Where(i => i.Airmass.HasValue)   .OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.Airmass!.Value)).ToList(),
                SecHumidity     => images.Where(i => i.Humidity.HasValue)  .OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.Humidity!.Value)).ToList(),
                SecFocuserPos   => images.Where(i => i.FocuserPosition.HasValue).OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, (double)i.FocuserPosition!.Value)).ToList(),
                SecSkyQuality   => images.Where(i => i.SkyQuality.HasValue)   .OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.SkyQuality!.Value)).ToList(),
                SecCloudCover   => images.Where(i => i.CloudCover.HasValue)   .OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.CloudCover!.Value)).ToList(),
                SecCameraTemp   => images.Where(i => i.CameraTemp.HasValue)   .OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.CameraTemp!.Value)).ToList(),
                SecDewPoint     => images.Where(i => i.DewPoint.HasValue)     .OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.DewPoint!.Value)).ToList(),
                SecWindSpeed    => images.Where(i => i.WindSpeed.HasValue)    .OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.WindSpeed!.Value)).ToList(),
                SecPressure     => images.Where(i => i.Pressure.HasValue)     .OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.Pressure!.Value)).ToList(),
                SecStarCount    => images.Where(i => i.StarCount > 0)         .OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, (double)i.StarCount)).ToList(),
                SecAzimuth      => images.Where(i => i.Azimuth.HasValue)      .OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.Azimuth!.Value)).ToList(),
                SecSeeingFWHM   => images.Where(i => i.SeeingFWHM.HasValue)  .OrderBy(i => i.Timestamp).Select(i => (i.Timestamp, i.SeeingFWHM!.Value)).ToList(),
                _               => new List<(DateTime, double)>()
            };
        }

        // ── Scale helpers ────────────────────────────────────────────────────

        private static (double min, double max, double step) ComputeNiceScale(IEnumerable<double> vals, double minSpan) {
            var list   = vals.ToList();
            double rawMin = list.Min();
            double rawMax = list.Max();

            // Enforce minimum span
            if (rawMax - rawMin < minSpan) {
                double mid = (rawMin + rawMax) / 2.0;
                rawMin = mid - minSpan / 2;
                rawMax = mid + minSpan / 2;
            }

            // Pick a nice step size targeting ~5 ticks
            double range    = rawMax - rawMin;
            double rough    = range / 4.0;
            double mag      = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(rough, 1e-10))));
            double norm     = rough / mag;
            double niceStep = norm < 1.5 ? mag
                            : norm < 3.5 ? 2 * mag
                            : norm < 7.5 ? 5 * mag
                            :              10 * mag;

            // Snap bounds to multiples of the step
            double niceMin = Math.Floor(rawMin / niceStep) * niceStep;
            double niceMax = Math.Ceiling(rawMax / niceStep) * niceStep;

            return (niceMin, niceMax, niceStep);
        }

        private static double GetPrimaryMinSpan(int metric) => metric switch {
            PrimaryFocuserTemp  => 2.0,
            PrimaryAmbientTemp  => 2.0,
            PrimaryEccentricity => 0.2,
            PrimaryAltitude     => 10.0,
            PrimaryAirmass      => 0.5,
            PrimaryHumidity     => 10.0,
            PrimaryFocuserPos   => 100.0,
            PrimarySkyQuality   => 1.0,
            PrimaryCloudCover   => 10.0,
            PrimaryCameraTemp   => 2.0,
            PrimaryDewPoint     => 2.0,
            PrimaryWindSpeed    => 1.0,
            PrimaryPressure     => 5.0,
            PrimaryStarCount    => 50.0,
            PrimaryAzimuth      => 10.0,
            _                   => 0.5
        };

        private static double GetSecondaryMinSpan(int metric) => metric switch {
            SecFocuserTemp  => 2.0,
            SecAmbientTemp  => 2.0,
            SecEccentricity => 0.2,
            SecAltitude     => 10.0,
            SecAirmass      => 0.5,
            SecHumidity     => 10.0,
            SecFocuserPos   => 100.0,
            SecSkyQuality   => 1.0,
            SecCloudCover   => 10.0,
            SecCameraTemp   => 2.0,
            SecDewPoint     => 2.0,
            SecWindSpeed    => 1.0,
            SecPressure     => 5.0,
            SecStarCount    => 50.0,
            SecAzimuth      => 10.0,
            _               => 0.5
        };

        // ── Label helpers ────────────────────────────────────────────────────

        internal static string GetPrimaryLabel(int metric) => metric switch {
            PrimaryHFR          => "HFR",
            PrimaryFWHM         => "FWHM",
            PrimaryGuidingRMS   => "Guiding RMS",
            PrimaryFocuserTemp  => "Focuser Temp",
            PrimaryAmbientTemp  => "Ambient Temp",
            PrimaryEccentricity => "Eccentricity",
            PrimaryAltitude     => "Altitude",
            PrimaryAirmass      => "Airmass",
            PrimaryHumidity     => "Humidity",
            PrimaryFocuserPos   => "Focuser Position",
            PrimarySkyQuality   => "Sky Quality",
            PrimaryCloudCover   => "Cloud Cover",
            PrimaryCameraTemp   => "Camera Temp",
            PrimaryDewPoint     => "Dew Point",
            PrimaryWindSpeed    => "Wind Speed",
            PrimaryPressure     => "Pressure",
            PrimaryStarCount    => "Star Count",
            PrimaryAzimuth      => "Azimuth",
            PrimarySeeingFWHM   => "Seeing (FWHM)",
            _                   => "HFR"
        };

        private static string GetSecondaryLabel(int metric) => metric switch {
            SecHFR          => "HFR",
            SecFWHM         => "FWHM",
            SecGuidingRMS   => "Guiding RMS",
            SecFocuserTemp  => "Focuser Temp",
            SecAmbientTemp  => "Ambient Temp",
            SecEccentricity => "Eccentricity",
            SecAltitude     => "Altitude",
            SecAirmass      => "Airmass",
            SecHumidity     => "Humidity",
            SecFocuserPos   => "Focuser Position",
            SecSkyQuality   => "Sky Quality",
            SecCloudCover   => "Cloud Cover",
            SecCameraTemp   => "Camera Temp",
            SecDewPoint     => "Dew Point",
            SecWindSpeed    => "Wind Speed",
            SecPressure     => "Pressure",
            SecStarCount    => "Star Count",
            SecAzimuth      => "Azimuth",
            SecSeeingFWHM   => "Seeing (FWHM)",
            _               => ""
        };

        private static string GetPrimaryAxisLabel(int metric) => metric switch {
            PrimaryHFR          => "HFR (px)",
            PrimaryFWHM         => "FWHM (\")",
            PrimaryGuidingRMS   => "RMS (\")",
            PrimaryFocuserTemp  => "Temp (&#176;C)",
            PrimaryAmbientTemp  => "Temp (&#176;C)",
            PrimaryEccentricity => "Eccentricity",
            PrimaryAltitude     => "Altitude (&#176;)",
            PrimaryAirmass      => "Airmass",
            PrimaryHumidity     => "Humidity (%)",
            PrimaryFocuserPos   => "Position (steps)",
            PrimarySkyQuality   => "SQM (mag/arcsec&#178;)",
            PrimaryCloudCover   => "Cloud Cover (%)",
            PrimaryCameraTemp   => "Temp (&#176;C)",
            PrimaryDewPoint     => "Dew Point (&#176;C)",
            PrimaryWindSpeed    => "Wind (m/s)",
            PrimaryPressure     => "Pressure (hPa)",
            PrimaryStarCount    => "Star Count",
            PrimaryAzimuth      => "Azimuth (&#176;)",
            PrimarySeeingFWHM   => "Seeing FWHM (\")",
            _                   => "HFR (px)"
        };

        private static string GetSecondaryAxisLabel(int metric) => metric switch {
            SecHFR          => "HFR (px)",
            SecFWHM         => "FWHM (\")",
            SecGuidingRMS   => "RMS (\")",
            SecFocuserTemp  => "Temp (&#176;C)",
            SecAmbientTemp  => "Temp (&#176;C)",
            SecEccentricity => "Eccentricity",
            SecAltitude     => "Altitude (&#176;)",
            SecAirmass      => "Airmass",
            SecHumidity     => "Humidity (%)",
            SecFocuserPos   => "Position (steps)",
            SecSkyQuality   => "SQM (mag/arcsec&#178;)",
            SecCloudCover   => "Cloud Cover (%)",
            SecCameraTemp   => "Temp (&#176;C)",
            SecDewPoint     => "Dew Point (&#176;C)",
            SecWindSpeed    => "Wind (m/s)",
            SecPressure     => "Pressure (hPa)",
            SecStarCount    => "Star Count",
            SecAzimuth      => "Azimuth (&#176;)",
            SecSeeingFWHM   => "Seeing FWHM (\")",
            _               => ""
        };

        private static string GetTooltipUnit(int metric, bool isPrimary) {
            int m = isPrimary ? metric : metric - 1;  // secondary indices are offset by 1
            return m switch {
                0 => " px",       // HFR
                1 => "\"",        // FWHM
                2 => "\"",        // Guiding RMS
                3 => " °C",       // Focuser Temp
                4 => " °C",       // Ambient Temp
                5 => "",          // Eccentricity
                6 => "°",         // Altitude
                7 => "",          // Airmass
                8 => "%",         // Humidity
                9  => " steps",   // Focuser Position
                10 => " mag/arcsec²", // Sky Quality
                11 => "%",        // Cloud Cover
                12 => " °C",      // Camera Temp
                13 => " °C",      // Dew Point
                14 => " m/s",     // Wind Speed
                15 => " hPa",     // Pressure
                16 => "",         // Star Count
                17 => "°",        // Azimuth
                18 => "\"",       // Seeing FWHM
                _ => ""
            };
        }

        // ── Value formatting ─────────────────────────────────────────────────

        /// <summary>
        /// Returns "F0" for metrics where integer precision is appropriate (large-scale or whole-number values),
        /// and "F1" for metrics where one decimal place is meaningful.
        /// </summary>
        private static string GetValueFormat(int metric, bool isPrimary) {
            int m = isPrimary ? metric : metric - 1;  // secondary indices offset by 1
            return m switch {
                6  => "F0",   // Altitude (degrees)
                8  => "F0",   // Humidity (%)
                9  => "F0",   // Focuser Position (steps)
                11 => "F0",   // Cloud Cover (%)
                15 => "F0",   // Pressure (hPa)
                16 => "F0",   // Star Count
                17 => "F0",   // Azimuth (degrees)
                _  => "F1"
            };
        }

        // ── No-data messaging ────────────────────────────────────────────────

        private static string GetPrimaryNoDataMsg(int metric) => metric switch {
            PrimaryHFR          => "No HFR data \u2014 fewer than 2 images with star detection",
            PrimaryFWHM         => "No FWHM data \u2014 Hocus Focus plugin required",
            PrimaryGuidingRMS   => "No Guiding RMS data recorded this session",
            PrimaryFocuserTemp  => "No focuser temperature data recorded",
            PrimaryAmbientTemp  => "No ambient temperature data recorded",
            PrimaryEccentricity => "No eccentricity data \u2014 Hocus Focus plugin required",
            PrimaryAltitude     => "No altitude data recorded",
            PrimaryAirmass      => "No airmass data recorded",
            PrimaryHumidity     => "No humidity data recorded",
            PrimaryFocuserPos   => "No focuser position data recorded",
            PrimarySkyQuality   => "No sky quality (SQM) data recorded",
            PrimaryCloudCover   => "No cloud cover data recorded",
            PrimaryCameraTemp   => "No camera temperature data recorded",
            PrimaryDewPoint     => "No dew point data recorded",
            PrimaryWindSpeed    => "No wind speed data recorded",
            PrimaryPressure     => "No atmospheric pressure data recorded",
            PrimaryStarCount    => "No star count data recorded",
            PrimaryAzimuth      => "No azimuth data recorded",
            PrimarySeeingFWHM   => "No seeing FWHM data recorded",
            _                   => "No data available"
        };

        private static string? GetPrimaryNoDataHint(int metric) => metric switch {
            PrimaryFWHM         => "Requires Hocus Focus plugin",
            PrimaryFocuserTemp  => "Requires focuser with temperature sensor",
            PrimaryAmbientTemp  => "Requires NINA weather data source",
            PrimaryEccentricity => "Requires Hocus Focus plugin",
            PrimaryHumidity     => "Requires NINA weather data source",
            PrimaryFocuserPos   => "Requires motorized focuser",
            PrimarySkyQuality   => "Requires a sky quality meter connected as a NINA weather data source",
            PrimaryCloudCover   => "Requires a cloud sensor connected as a NINA weather data source",
            PrimaryDewPoint     => "Requires NINA weather data source",
            PrimaryWindSpeed    => "Requires NINA weather data source",
            PrimaryPressure     => "Requires NINA weather data source",
            PrimarySeeingFWHM   => "Requires an ASCOM seeing monitor as a NINA weather data source",
            _                   => null
        };

        private static string GetSecondaryNoDataMsg(int metric) => metric switch {
            SecHFR          => "No HFR data recorded",
            SecFWHM         => "No FWHM data \u2014 Hocus Focus plugin required",
            SecGuidingRMS   => "No Guiding RMS data recorded",
            SecFocuserTemp  => "No focuser temperature data recorded",
            SecAmbientTemp  => "No ambient temperature data recorded",
            SecEccentricity => "No eccentricity data \u2014 Hocus Focus plugin required",
            SecAltitude     => "No altitude data recorded",
            SecAirmass      => "No airmass data recorded",
            SecHumidity     => "No humidity data recorded",
            SecFocuserPos   => "No focuser position data recorded",
            SecSkyQuality   => "No sky quality (SQM) data recorded",
            SecCloudCover   => "No cloud cover data recorded",
            SecCameraTemp   => "No camera temperature data recorded",
            SecDewPoint     => "No dew point data recorded",
            SecWindSpeed    => "No wind speed data recorded",
            SecPressure     => "No atmospheric pressure data recorded",
            SecStarCount    => "No star count data recorded",
            SecAzimuth      => "No azimuth data recorded",
            SecSeeingFWHM   => "No seeing FWHM data recorded",
            _               => ""
        };

        private static string? GetSecondaryNoDataHint(int metric) => metric switch {
            SecFWHM         => "Requires Hocus Focus plugin",
            SecFocuserTemp  => "Requires focuser with temperature sensor",
            SecAmbientTemp  => "Requires NINA weather data source",
            SecEccentricity => "Requires Hocus Focus plugin",
            SecHumidity     => "Requires NINA weather data source",
            SecFocuserPos   => "Requires motorized focuser",
            SecSkyQuality   => "Requires a sky quality meter connected as a NINA weather data source",
            SecCloudCover   => "Requires a cloud sensor connected as a NINA weather data source",
            SecDewPoint     => "Requires NINA weather data source",
            SecWindSpeed    => "Requires NINA weather data source",
            SecPressure     => "Requires NINA weather data source",
            SecSeeingFWHM   => "Requires an ASCOM seeing monitor as a NINA weather data source",
            _               => null
        };

        // ── Placeholder SVG ──────────────────────────────────────────────────

        private static string GeneratePlaceholderSvg(List<string> messages) {
            var sb = new StringBuilder();
            sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {Width} {Height}\" style=\"width:100%;max-width:{Width}px;display:block;margin:0 auto 16px;font-family:sans-serif\">");
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
