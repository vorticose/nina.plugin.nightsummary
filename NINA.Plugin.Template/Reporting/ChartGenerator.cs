using NINA.Plugin.NightSummary.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NINA.Plugin.NightSummary.Reporting {
    /// <summary>
    /// Generates inline SVG charts for embedding in the HTML report.
    /// </summary>
    public static class ChartGenerator {

        private const int Width  = 800;
        private const int Height = 300;
        private const int PadLeft   = 55;
        private const int PadRight  = 20;
        private const int PadTop    = 20;
        private const int PadBottom = 45;

        private const string ColorBackground = "#1a1a2e";
        private const string ColorGrid       = "#2a2a4a";
        private const string ColorAxis       = "#555577";
        private const string ColorHFR        = "#7eb8f7";
        private const string ColorDot        = "#a8d4ff";
        private const string ColorLabel      = "#aaaacc";

        /// <summary>
        /// Generates an SVG line chart of HFR over the course of the session.
        /// Returns an empty string if there are fewer than 2 data points.
        /// </summary>
        public static string GenerateHfrChart(List<ImageRecord> images) {
            var points = images
                .Where(i => i.HFR > 0)
                .OrderBy(i => i.Timestamp)
                .ToList();

            if (points.Count < 2) return string.Empty;

            var minTime  = points.First().Timestamp;
            var maxTime  = points.Last().Timestamp;
            var totalSeconds = (maxTime - minTime).TotalSeconds;
            if (totalSeconds <= 0) totalSeconds = 1;

            var hfrValues = points.Select(p => p.HFR).ToList();
            var minHFR = Math.Floor(hfrValues.Min() * 10) / 10;
            var maxHFR = Math.Ceiling(hfrValues.Max() * 10) / 10;
            var hfrRange = maxHFR - minHFR;
            if (hfrRange < 0.5) {
                var mid = (minHFR + maxHFR) / 2;
                minHFR = Math.Round(mid - 0.5, 1);
                maxHFR = Math.Round(mid + 0.5, 1);
                hfrRange = maxHFR - minHFR;
            }

            int plotW = Width - PadLeft - PadRight;
            int plotH = Height - PadTop - PadBottom;

            double ToX(DateTime t) => PadLeft + ((t - minTime).TotalSeconds / totalSeconds) * plotW;
            double ToY(double hfr)  => PadTop + plotH - ((hfr - minHFR) / hfrRange) * plotH;

            var sb = new StringBuilder();
            sb.AppendLine($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {Width} {Height}\" style=\"width:100%;max-width:{Width}px;display:block;margin:0 auto;font-family:sans-serif\">");

            // Background
            sb.AppendLine($"<rect width=\"{Width}\" height=\"{Height}\" fill=\"{ColorBackground}\" rx=\"6\"/>");

            // Horizontal grid lines and Y-axis labels
            int ySteps = 5;
            for (int i = 0; i <= ySteps; i++) {
                double hfr = minHFR + (hfrRange / ySteps) * i;
                double y   = ToY(hfr);
                sb.AppendLine($"<line x1=\"{PadLeft}\" y1=\"{y:F1}\" x2=\"{Width - PadRight}\" y2=\"{y:F1}\" stroke=\"{ColorGrid}\" stroke-width=\"1\"/>");
                sb.AppendLine($"<text x=\"{PadLeft - 6}\" y=\"{y + 4:F1}\" fill=\"{ColorLabel}\" font-size=\"11\" text-anchor=\"end\">{hfr:F1}</text>");
            }

            // X-axis time labels (up to 6 evenly spaced)
            int xSteps = Math.Min(6, points.Count - 1);
            for (int i = 0; i <= xSteps; i++) {
                var t = minTime + TimeSpan.FromSeconds(totalSeconds / xSteps * i);
                double x = ToX(t);
                sb.AppendLine($"<line x1=\"{x:F1}\" y1=\"{PadTop}\" x2=\"{x:F1}\" y2=\"{PadTop + plotH}\" stroke=\"{ColorGrid}\" stroke-width=\"1\"/>");
                sb.AppendLine($"<text x=\"{x:F1}\" y=\"{Height - 10}\" fill=\"{ColorLabel}\" font-size=\"11\" text-anchor=\"middle\">{t:HH:mm}</text>");
            }

            // Axes
            sb.AppendLine($"<line x1=\"{PadLeft}\" y1=\"{PadTop}\" x2=\"{PadLeft}\" y2=\"{PadTop + plotH}\" stroke=\"{ColorAxis}\" stroke-width=\"1\"/>");
            sb.AppendLine($"<line x1=\"{PadLeft}\" y1=\"{PadTop + plotH}\" x2=\"{Width - PadRight}\" y2=\"{PadTop + plotH}\" stroke=\"{ColorAxis}\" stroke-width=\"1\"/>");

            // Y-axis title
            sb.AppendLine($"<text x=\"14\" y=\"{Height / 2}\" fill=\"{ColorLabel}\" font-size=\"11\" text-anchor=\"middle\" transform=\"rotate(-90,14,{Height / 2})\">HFR</text>");

            // HFR line
            var polyPoints = string.Join(" ", points.Select(p => $"{ToX(p.Timestamp):F1},{ToY(p.HFR):F1}"));
            sb.AppendLine($"<polyline points=\"{polyPoints}\" fill=\"none\" stroke=\"{ColorHFR}\" stroke-width=\"2\" stroke-linejoin=\"round\"/>");

            // Data points
            foreach (var p in points) {
                sb.AppendLine($"<circle cx=\"{ToX(p.Timestamp):F1}\" cy=\"{ToY(p.HFR):F1}\" r=\"3\" fill=\"{ColorDot}\"/>");
            }

            sb.AppendLine("</svg>");
            return sb.ToString();
        }
    }
}
