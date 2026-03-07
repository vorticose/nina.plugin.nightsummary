using NINA.Plugin.NightSummary.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NINA.Plugin.NightSummary.Reporting {
    /// <summary>
    /// Generates an HTML report from session data.
    /// Each logical section is a separate private method so individual sections
    /// can be toggled on/off in a future release.
    /// </summary>
    public class ReportGenerator {

        // Broadband and narrowband filter definitions for star count CV calculation
        private static readonly HashSet<string> BroadbandFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "L", "R", "G", "B" };
        private static readonly HashSet<string> NarrowbandFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "H", "Ha", "S", "Sii", "O", "Oiii" };

        private static readonly string[] FilterPriority = { "L", "R", "G", "B", "H", "S", "O" };
        private static int FilterSortKey(string filter) {
            var idx = Array.FindIndex(FilterPriority, p => string.Equals(p, filter, StringComparison.OrdinalIgnoreCase));
            return idx >= 0 ? idx : int.MaxValue;
        }

        public string GenerateHtmlReport(ReportData data) {
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
            sb.AppendLine(".target-section { border-top: 1px solid #2d2d5e; margin-top: 24px; padding-top: 16px; }");
            sb.AppendLine(".timeline-container { background-color: #16213e; border: 1px solid #2d2d5e; border-radius: 8px; padding: 16px; margin: 16px 0; }");
            sb.AppendLine(".ts-target-header { display: flex; gap: 16px; align-items: flex-start; margin-bottom: 12px; }");
            sb.AppendLine(".ts-thumb-wrap { position: relative; width: 200px; height: 200px; flex-shrink: 0; }");
            sb.AppendLine(".ts-thumb-wrap img { width: 200px; height: 200px; border-radius: 6px; border: 1px solid #2d2d5e; display: block; }");
            sb.AppendLine(".ts-thumb-wrap svg { position: absolute; top: 0; left: 0; border-radius: 6px; }");
            sb.AppendLine(".ts-target-info { flex: 1; }");
            sb.AppendLine(".ts-coords { font-size: 12px; color: #888; margin: 4px 0 12px; }");
            sb.AppendLine(".ts-filter-row { display: flex; align-items: center; gap: 8px; margin: 4px 0; }");
            sb.AppendLine(".ts-filter-name { min-width: 44px; font-size: 13px; color: #a0c4ff; }");
            sb.AppendLine(".ts-bar-track { flex: 1; height: 14px; background: #2d2d5e; border-radius: 4px; position: relative; overflow: hidden; }");
            sb.AppendLine(".ts-bar-accepted { position: absolute; left: 0; top: 0; bottom: 0; background: #7eb8f7; }");
            sb.AppendLine(".ts-bar-acquired { position: absolute; top: 0; bottom: 0; background: #3a5a7a; }");
            sb.AppendLine(".ts-bar-label { font-size: 12px; color: #888; white-space: nowrap; min-width: 110px; text-align: right; }");
            sb.AppendLine(".ts-cumulative { font-size: 12px; color: #888; margin-top: 12px; }");
            sb.AppendLine("</style></head><body>");

            sb.Append(BuildHeader(data));

            if (!data.Images.Any()) {
                sb.AppendLine("<p><em>No images were recorded during this session.</em></p>");
                sb.AppendLine("</body></html>");
                return sb.ToString();
            }

            sb.Append(BuildEventTimelineSection(data));
            sb.Append(BuildOverviewStatsSection(data));
            sb.Append(BuildTargetSection(data));
            sb.Append(BuildImageQualitySection(data));
            sb.Append(BuildGuidingSection(data));
            sb.Append(BuildFooter());

            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        private string BuildHeader(ReportData data) {
            var sb = new StringBuilder();
            sb.AppendLine($"<h1>Night Summary Report</h1>");
            sb.AppendLine($"<p><strong>Session Date:</strong> {data.Session.SessionStart:yyyy-MM-dd}</p>");
            sb.AppendLine($"<p><strong>Session Start:</strong> {data.Session.SessionStart:HH:mm:ss} &nbsp;&nbsp; <strong>Session End:</strong> {data.Session.SessionEnd:HH:mm:ss}</p>");
            sb.AppendLine($"<p><strong>Duration:</strong> {(data.Session.SessionEnd - data.Session.SessionStart).TotalHours:F1} hours</p>");
            sb.AppendLine($"<p><strong>Profile:</strong> {data.Session.ProfileName}</p>");
            return sb.ToString();
        }

        private string BuildOverviewStatsSection(ReportData data) {
            var sb = new StringBuilder();
            var acceptedImages = data.Images.Where(i => i.Accepted).ToList();
            var totalExposureTime = data.Images.Sum(i => i.ExposureDuration);
            sb.AppendLine("<h2>Session Overview</h2>");
            sb.AppendLine("<div>");
            sb.AppendLine($"<div class='stat-box'><div class='stat-value'>{data.Images.Count}</div><div class='stat-label'>Total Images</div></div>");
            sb.AppendLine($"<div class='stat-box'><div class='stat-value'>{acceptedImages.Count}</div><div class='stat-label'>Accepted</div></div>");
            sb.AppendLine($"<div class='stat-box'><div class='stat-value'>{data.Images.Count - acceptedImages.Count}</div><div class='stat-label'>Rejected</div></div>");
            sb.AppendLine($"<div class='stat-box'><div class='stat-value'>{TimeSpan.FromSeconds(totalExposureTime).TotalHours:F1}h</div><div class='stat-label'>Total Exposure</div></div>");
            sb.AppendLine("</div>");
            return sb.ToString();
        }

        private string BuildTargetSection(ReportData data) {
            var sb = new StringBuilder();
            var targets = data.Images.GroupBy(i => i.TargetName).OrderBy(g => g.Key);
            sb.AppendLine("<h2>Targets Imaged</h2>");

            // Pre-compute thumbnail/FOV geometry (same for all targets)
            const int thumbPx = 200;
            var fovW     = data.CameraFovWidthDeg;
            var fovH     = data.CameraFovHeightDeg;
            var thumbFov = Math.Max(fovW, fovH) * 1.5;
            if (thumbFov <= 0) thumbFov = 1.0;
            double boxW = (fovW / thumbFov) * thumbPx;
            double boxH = (fovH / thumbFov) * thumbPx;
            double cx   = thumbPx / 2.0;
            double cy   = thumbPx / 2.0;

            foreach (var target in targets) {
                var tsTarget = data.TsData?.FirstOrDefault(t =>
                    string.Equals(t.TargetName, target.Key, StringComparison.OrdinalIgnoreCase));

                sb.AppendLine("<div class='target-section'>");
                sb.AppendLine($"<h3>{target.Key}</h3>");

                if (tsTarget != null) {
                    // Two-column layout: thumbnail left, all info right
                    sb.AppendLine("<div class='ts-target-header'>");

                    var raDeg    = tsTarget.RA * 15.0;
                    var thumbUrl = $"https://alasky.cds.unistra.fr/hips-image-services/hips2fits" +
                                   $"?hips=CDS%2FP%2FDSS2%2Fcolor&width={thumbPx}&height={thumbPx}" +
                                   $"&fov={thumbFov:F4}&ra={raDeg:F6}&dec={tsTarget.Dec:F6}" +
                                   $"&projection=TAN&format=jpg";
                    var svgAngle = -tsTarget.Rotation;

                    sb.AppendLine($"<div class='ts-thumb-wrap'>");
                    sb.AppendLine($"  <img src='{thumbUrl}' alt='{target.Key}' />");
                    sb.AppendLine($"  <svg width='{thumbPx}' height='{thumbPx}' xmlns='http://www.w3.org/2000/svg'>");
                    sb.AppendLine($"    <rect x='{(cx - boxW / 2):F1}' y='{(cy - boxH / 2):F1}' width='{boxW:F1}' height='{boxH:F1}'");
                    sb.AppendLine($"          fill='none' stroke='#7eb8f7' stroke-width='1.5' opacity='0.85'");
                    sb.AppendLine($"          transform='rotate({svgAngle:F2},{cx:F1},{cy:F1})' />");
                    sb.AppendLine($"  </svg>");
                    sb.AppendLine($"</div>");

                    sb.AppendLine("<div class='ts-target-info'>");
                    sb.AppendLine($"<p class='ts-coords'>{FormatRA(tsTarget.RA)} &nbsp;·&nbsp; {FormatDec(tsTarget.Dec)}</p>");
                }

                // Session filter table
                sb.AppendLine("<table>");
                sb.AppendLine("<tr><th>Filter</th><th>Images</th><th>Exposure</th><th>Total Time</th></tr>");
                var filterGroups = target.GroupBy(i => i.Filter).OrderBy(g => FilterSortKey(g.Key)).ThenBy(g => g.Key);
                foreach (var filterGroup in filterGroups) {
                    var totalTime = TimeSpan.FromSeconds(filterGroup.Sum(i => i.ExposureDuration));
                    sb.AppendLine($"<tr><td>{filterGroup.Key}</td><td>{filterGroup.Count()}</td><td>{filterGroup.First().ExposureDuration:F0}s</td><td>{totalTime.TotalMinutes:F1} min</td></tr>");
                }
                var targetTotal = TimeSpan.FromSeconds(target.Sum(i => i.ExposureDuration));
                sb.AppendLine($"<tr><td><strong>Total</strong></td><td><strong>{target.Count()}</strong></td><td></td><td><strong>{targetTotal.TotalMinutes:F1} min</strong></td></tr>");
                sb.AppendLine("</table>");

                // Star count CV
                var broadbandImages  = target.Where(i => BroadbandFilters.Contains(i.Filter)  && i.StarCount > 0).ToList();
                var narrowbandImages = target.Where(i => NarrowbandFilters.Contains(i.Filter) && i.StarCount > 0).ToList();
                string broadbandCV  = broadbandImages.Count  >= 2 ? $"{CV(broadbandImages.Select(i  => (double)i.StarCount).ToList()):F0}%" : "—";
                string narrowbandCV = narrowbandImages.Count >= 2 ? $"{CV(narrowbandImages.Select(i => (double)i.StarCount).ToList()):F0}%" : "—";
                sb.AppendLine("<table class='star-count-table'>");
                sb.AppendLine("<tr><th>Broadband CV</th><th>Narrowband CV</th></tr>");
                sb.AppendLine($"<tr><td>{broadbandCV}</td><td>{narrowbandCV}</td></tr>");
                sb.AppendLine("</table>");

                if (tsTarget != null) {
                    // TS progress bars
                    sb.AppendLine("<p style='margin: 12px 0 4px; font-size: 13px; color: #a0c4ff;'><strong>Target Scheduler Progress</strong></p>");
                    foreach (var f in tsTarget.Filters.OrderBy(f => FilterSortKey(f.Filter)).ThenBy(f => f.Filter)) {
                        var desired     = f.Desired;
                        var acquired    = Math.Min(f.Acquired, desired);
                        var accepted    = Math.Min(f.Accepted, acquired);
                        var acceptedPct = desired > 0 ? (double)accepted            / desired * 100 : 0;
                        var acquiredPct = desired > 0 ? (double)(acquired - accepted) / desired * 100 : 0;

                        sb.AppendLine("<div class='ts-filter-row'>");
                        sb.AppendLine($"  <span class='ts-filter-name'>{f.Filter}</span>");
                        sb.AppendLine($"  <div class='ts-bar-track'>");
                        sb.AppendLine($"    <div class='ts-bar-accepted' style='width:{acceptedPct:F1}%'></div>");
                        sb.AppendLine($"    <div class='ts-bar-acquired' style='left:{acceptedPct:F1}%;width:{acquiredPct:F1}%'></div>");
                        sb.AppendLine($"  </div>");
                        sb.AppendLine($"  <span class='ts-bar-label'>{accepted}/{desired} accepted</span>");
                        sb.AppendLine("</div>");
                    }

                    // Cumulative integration
                    double prevSec = 0;
                    data.CumulativeIntegrationSeconds?.TryGetValue(tsTarget.TargetName, out prevSec);
                    var thisSec    = target.Where(i => i.Accepted).Sum(i => i.ExposureDuration);
                    var totalHours = (prevSec + thisSec) / 3600.0;
                    sb.AppendLine($"<p class='ts-cumulative'>Total integration (all sessions): {totalHours:F1}h</p>");

                    sb.AppendLine("</div>"); // ts-target-info
                    sb.AppendLine("</div>"); // ts-target-header
                }

                sb.AppendLine("</div>"); // target-section
            }

            return sb.ToString();
        }

        private string BuildImageQualitySection(ReportData data) {
            var sb = new StringBuilder();
            var imagesWithHFR = data.Images.Where(i => i.HFR > 0).ToList();
            var imagesWithFWHM = data.Images.Where(i => i.FWHM > 0).ToList();
            var imagesWithEcc = data.Images.Where(i => i.Eccentricity > 0).ToList();

            if (!imagesWithHFR.Any() && !imagesWithFWHM.Any()) return string.Empty;

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

            var hfrChart = ChartGenerator.GenerateHfrChart(data.Images);
            if (!string.IsNullOrEmpty(hfrChart)) {
                sb.AppendLine("<h2>HFR Over Time</h2>");
                sb.AppendLine(hfrChart);
            }

            return sb.ToString();
        }

        private string BuildGuidingSection(ReportData data) {
            var sb = new StringBuilder();
            var imagesWithGuiding = data.Images.Where(i => i.GuidingRMSTotal > 0).ToList();
            if (!imagesWithGuiding.Any()) return string.Empty;

            sb.AppendLine("<h2>Guiding</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine("<tr><th>Metric</th><th>Min</th><th>Max</th><th>Mean</th><th>CV</th></tr>");

            var rmsValues = imagesWithGuiding.Select(i => i.GuidingRMSTotal).ToList();
            sb.AppendLine($"<tr><td>RMS Total</td><td>{rmsValues.Min():F2}\"</td><td>{rmsValues.Max():F2}\"</td><td>{rmsValues.Average():F2}\"</td><td>{CV(rmsValues):F0}%</td></tr>");

            sb.AppendLine("</table>");
            return sb.ToString();
        }

        /// <summary>
        /// Builds the session event timeline as an inline SVG.
        /// Shows target imaging bands and event markers (autofocus, roof open/close, meridian flip).
        /// </summary>
        private string BuildEventTimelineSection(ReportData data) {
            var events = data.Events;
            if (events == null || !events.Any()) return string.Empty;

            return EventTimelineGenerator.GenerateTimeline(data.Session, data.Images, events);
        }

        private static string FormatRA(double raHours) {
            var h     = (int)raHours;
            var mFrac = (raHours - h) * 60;
            var m     = (int)mFrac;
            var s     = (mFrac - m) * 60;
            return $"{h:D2}h {m:D2}m {s:F0}s";
        }

        private static string FormatDec(double decDeg) {
            var sign  = decDeg >= 0 ? "+" : "-";
            var abs   = Math.Abs(decDeg);
            var d     = (int)abs;
            var mFrac = (abs - d) * 60;
            var m     = (int)mFrac;
            var s     = (mFrac - m) * 60;
            return $"{sign}{d:D2}° {m:D2}′ {s:F0}″";
        }

        private string BuildFooter() {
            var sb = new StringBuilder();
            sb.AppendLine("<p class='footnote'>CV (Coefficient of Variation) measures consistency as a percentage of the mean. Lower values indicate more stable conditions. Star count CV is calculated per target and filter type.</p>");
            sb.AppendLine("<p class='footnote'>Generated by Night Summary plugin for N.I.N.A.</p>");
            return sb.ToString();
        }

        private double CV(List<double> values) {
            if (values.Count < 2) return 0;
            var avg = values.Average();
            if (avg == 0) return 0;
            return (StdDev(values) / avg) * 100;
        }

        private double StdDev(List<double> values) {
            if (values.Count < 2) return 0;
            var avg = values.Average();
            var sumOfSquares = values.Sum(v => Math.Pow(v - avg, 2));
            return Math.Sqrt(sumOfSquares / (values.Count - 1));
        }
    }
}
