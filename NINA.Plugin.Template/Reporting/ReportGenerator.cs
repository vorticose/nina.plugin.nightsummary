using NINA.Plugin.NightSummary.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NINA.Plugin.NightSummary.Reporting {
    /// <summary>
    /// Generates an HTML email report from session data.
    /// </summary>
    public class ReportGenerator {

        // Broadband and narrowband filter definitions for star count CV calculation
        private static readonly HashSet<string> BroadbandFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "L", "R", "G", "B" };
        private static readonly HashSet<string> NarrowbandFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "H", "Ha", "S", "Sii", "O", "Oiii" };

        /// <summary>
        /// Generates a complete HTML email body for the given session.
        /// </summary>
        public string GenerateHtmlReport(SessionRecord session, List<ImageRecord> images) {
            var sb = new StringBuilder();

            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><style>");
            sb.AppendLine("body { font-family: Arial, sans-serif; max-width: 800px; margin: 0 auto; padding: 20px; background-color: #1a1a2e; color: #e0e0e0; }");
            sb.AppendLine("h1 { color: #7eb8f7; border-bottom: 2px solid #7eb8f7; padding-bottom: 10px; }");
            sb.AppendLine("h2 { color: #a0c4ff; margin-top: 30px; }");
            sb.AppendLine("h3 { color: #c0d8ff; }");
            sb.AppendLine("table { width: 100%; border-collapse: collapse; margin-top: 10px; }");
            sb.AppendLine("th { background-color: #2d2d5e; color: #7eb8f7; padding: 8px; text-align: left; }");
            sb.AppendLine("td { padding: 8px; border-bottom: 1px solid #2d2d5e; }");
            sb.AppendLine("tr:nth-child(even) { background-color: #16213e; }");
            sb.AppendLine(".stat-box { display: inline-block; background-color: #16213e; border: 1px solid #2d2d5e; border-radius: 8px; padding: 15px; margin: 10px; min-width: 150px; text-align: center; }");
            sb.AppendLine(".stat-value { font-size: 24px; color: #7eb8f7; font-weight: bold; }");
            sb.AppendLine(".stat-label { font-size: 12px; color: #888; margin-top: 5px; }");
            sb.AppendLine(".star-count-table { width: auto; margin-top: 8px; }");
            sb.AppendLine(".footnote { color: #555; font-size: 12px; margin-top: 40px; }");
            sb.AppendLine("</style></head><body>");

            // Header
            sb.AppendLine($"<h1>🔭 Night Summary Report</h1>");
            sb.AppendLine($"<p><strong>Session Date:</strong> {session.SessionStart:yyyy-MM-dd}</p>");
            sb.AppendLine($"<p><strong>Session Start:</strong> {session.SessionStart:HH:mm:ss} &nbsp;&nbsp; <strong>Session End:</strong> {session.SessionEnd:HH:mm:ss}</p>");
            sb.AppendLine($"<p><strong>Duration:</strong> {(session.SessionEnd - session.SessionStart).TotalHours:F1} hours</p>");
            sb.AppendLine($"<p><strong>Profile:</strong> {session.ProfileName}</p>");

            if (!images.Any()) {
                sb.AppendLine("<p><em>No images were recorded during this session.</em></p>");
                sb.AppendLine("</body></html>");
                return sb.ToString();
            }

            // Session overview stats
            var acceptedImages = images.Where(i => i.Accepted).ToList();
            var totalExposureTime = images.Sum(i => i.ExposureDuration);
            sb.AppendLine("<h2>Session Overview</h2>");
            sb.AppendLine("<div>");
            sb.AppendLine($"<div class='stat-box'><div class='stat-value'>{images.Count}</div><div class='stat-label'>Total Images</div></div>");
            sb.AppendLine($"<div class='stat-box'><div class='stat-value'>{acceptedImages.Count}</div><div class='stat-label'>Accepted</div></div>");
            sb.AppendLine($"<div class='stat-box'><div class='stat-value'>{images.Count - acceptedImages.Count}</div><div class='stat-label'>Rejected</div></div>");
            sb.AppendLine($"<div class='stat-box'><div class='stat-value'>{TimeSpan.FromSeconds(totalExposureTime).TotalHours:F1}h</div><div class='stat-label'>Total Exposure</div></div>");
            sb.AppendLine("</div>");

            // Per target breakdown
            var targets = images.GroupBy(i => i.TargetName).OrderBy(g => g.Key);
            sb.AppendLine("<h2>Targets Imaged</h2>");

            foreach (var target in targets) {
                sb.AppendLine($"<h3>🌌 {target.Key}</h3>");

                // Filter breakdown table
                sb.AppendLine("<table>");
                sb.AppendLine("<tr><th>Filter</th><th>Images</th><th>Exposure</th><th>Total Time</th></tr>");

                var filterGroups = target.GroupBy(i => i.Filter).OrderBy(g => g.Key);
                foreach (var filterGroup in filterGroups) {
                    var totalTime = TimeSpan.FromSeconds(filterGroup.Sum(i => i.ExposureDuration));
                    sb.AppendLine($"<tr><td>{filterGroup.Key}</td><td>{filterGroup.Count()}</td><td>{filterGroup.First().ExposureDuration:F0}s</td><td>{totalTime.TotalMinutes:F1} min</td></tr>");
                }

                var targetTotal = TimeSpan.FromSeconds(target.Sum(i => i.ExposureDuration));
                sb.AppendLine($"<tr><td><strong>Total</strong></td><td><strong>{target.Count()}</strong></td><td></td><td><strong>{targetTotal.TotalMinutes:F1} min</strong></td></tr>");
                sb.AppendLine("</table>");

                // Star count CV per target
                var broadbandImages = target.Where(i => BroadbandFilters.Contains(i.Filter) && i.StarCount > 0).ToList();
                var narrowbandImages = target.Where(i => NarrowbandFilters.Contains(i.Filter) && i.StarCount > 0).ToList();

                string broadbandCV = broadbandImages.Count >= 2
                    ? $"{CV(broadbandImages.Select(i => (double)i.StarCount).ToList()):F0}%"
                    : "—";
                string narrowbandCV = narrowbandImages.Count >= 2
                    ? $"{CV(narrowbandImages.Select(i => (double)i.StarCount).ToList()):F0}%"
                    : "—";

                sb.AppendLine("<p><strong>Star Count</strong></p>");
                sb.AppendLine("<table class='star-count-table'>");
                sb.AppendLine("<tr><th>Broadband CV</th><th>Narrowband CV</th></tr>");
                sb.AppendLine($"<tr><td>{broadbandCV}</td><td>{narrowbandCV}</td></tr>");
                sb.AppendLine("</table>");
            }

            // Image quality metrics
            var imagesWithHFR = images.Where(i => i.HFR > 0).ToList();
            var imagesWithFWHM = images.Where(i => i.FWHM > 0).ToList();
            var imagesWithEcc = images.Where(i => i.Eccentricity > 0).ToList();

            if (imagesWithHFR.Any() || imagesWithFWHM.Any()) {
                sb.AppendLine("<h2>Image Quality</h2>");
                sb.AppendLine("<table>");
                sb.AppendLine("<tr><th>Metric</th><th>Min</th><th>Max</th><th>Mean</th><th>CV</th></tr>");

                if (imagesWithHFR.Any()) {
                    var hfrValues = imagesWithHFR.Select(i => i.HFR).ToList();
                    sb.AppendLine($"<tr><td>HFR</td><td>{hfrValues.Min():F2}</td><td>{hfrValues.Max():F2}</td><td>{hfrValues.Average():F2}</td><td>{CV(hfrValues):F0}%</td></tr>");
                }

                if (imagesWithFWHM.Any()) {
                    var fwhmValues = imagesWithFWHM.Select(i => i.FWHM).ToList();
                    sb.AppendLine($"<tr><td>FWHM</td><td>{fwhmValues.Min():F2}</td><td>{fwhmValues.Max():F2}</td><td>{fwhmValues.Average():F2}</td><td>{CV(fwhmValues):F0}%</td></tr>");
                }

                if (imagesWithEcc.Any()) {
                    var eccValues = imagesWithEcc.Select(i => i.Eccentricity).ToList();
                    sb.AppendLine($"<tr><td>Eccentricity</td><td>{eccValues.Min():F3}</td><td>{eccValues.Max():F3}</td><td>{eccValues.Average():F3}</td><td>{CV(eccValues):F0}%</td></tr>");
                }

                sb.AppendLine("</table>");
            }

            // Guiding metrics
            var imagesWithGuiding = images.Where(i => i.GuidingRMSTotal > 0).ToList();
            if (imagesWithGuiding.Any()) {
                sb.AppendLine("<h2>Guiding</h2>");
                sb.AppendLine("<table>");
                sb.AppendLine("<tr><th>Metric</th><th>Min</th><th>Max</th><th>Mean</th><th>CV</th></tr>");

                var rmsValues = imagesWithGuiding.Select(i => i.GuidingRMSTotal).ToList();
                sb.AppendLine($"<tr><td>RMS Total</td><td>{rmsValues.Min():F2}\"</td><td>{rmsValues.Max():F2}\"</td><td>{rmsValues.Average():F2}\"</td><td>{CV(rmsValues):F0}%</td></tr>");

                sb.AppendLine("</table>");
            }

            // Footnote and footer
            sb.AppendLine("<p class='footnote'>CV (Coefficient of Variation) measures consistency as a percentage of the mean. Lower values indicate more stable conditions. Star count CV is calculated per target and filter type.</p>");
            sb.AppendLine("<p class='footnote'>Generated by Night Summary plugin for N.I.N.A.</p>");
            sb.AppendLine("</body></html>");

            return sb.ToString();
        }

        /// <summary>
        /// Calculates the Coefficient of Variation (std dev / mean) as a percentage.
        /// </summary>
        private double CV(List<double> values) {
            if (values.Count < 2) return 0;
            var avg = values.Average();
            if (avg == 0) return 0;
            return (StdDev(values) / avg) * 100;
        }

        /// <summary>
        /// Calculates the standard deviation of a list of values.
        /// </summary>
        private double StdDev(List<double> values) {
            if (values.Count < 2) return 0;
            var avg = values.Average();
            var sumOfSquares = values.Sum(v => Math.Pow(v - avg, 2));
            return Math.Sqrt(sumOfSquares / (values.Count - 1));
        }
    }
}