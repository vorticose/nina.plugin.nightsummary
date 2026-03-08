using NINA.Plugin.NightSummary.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Reporting {
    /// <summary>
    /// Generates an HTML report from session data.
    /// Each logical section is a separate private method so individual sections
    /// can be toggled on/off in a future release.
    /// </summary>
    public class ReportGenerator {

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        // Lazily-loaded plugin icon as a base64 data URI (embedded resource)
        private static string? _iconDataUri;
        private static string? IconDataUri {
            get {
                if (_iconDataUri != null) return _iconDataUri;
                try {
                    using var stream = Assembly.GetExecutingAssembly()
                                               .GetManifestResourceStream("plugin-icon.png");
                    if (stream == null) return null;
                    var bytes = new byte[stream.Length];
                    stream.Read(bytes, 0, bytes.Length);
                    _iconDataUri = "data:image/png;base64," + Convert.ToBase64String(bytes);
                } catch {
                    _iconDataUri = null;
                }
                return _iconDataUri;
            }
        }

        // Broadband and narrowband filter definitions for star count CV calculation
        private static readonly HashSet<string> BroadbandFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "L", "R", "G", "B" };
        private static readonly HashSet<string> NarrowbandFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "H", "Ha", "S", "Sii", "O", "Oiii" };

        private static readonly string[] FilterPriority = { "L", "R", "G", "B", "H", "S", "O" };
        private static int FilterSortKey(string filter) {
            var idx = Array.FindIndex(FilterPriority, p => string.Equals(p, filter, StringComparison.OrdinalIgnoreCase));
            return idx >= 0 ? idx : int.MaxValue;
        }

        public async Task<string> GenerateHtmlReport(ReportData data) {
            var sb = new StringBuilder();

            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><meta charset='UTF-8'><style>");
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
            sb.AppendLine(".ts-target-header { display: flex; gap: 16px; align-items: flex-start; margin-bottom: 12px; flex-wrap: wrap; }");
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
            sb.AppendLine("details.history-section { margin-top: 12px; }");
            sb.AppendLine("details.history-section > summary { cursor: pointer; color: #a0c4ff; font-size: 13px; user-select: none; list-style: none; }");
            sb.AppendLine("details.history-section > summary::-webkit-details-marker { display: none; }");
            sb.AppendLine("details.history-section > summary::before { content: '\\25B6\\00A0'; }");
            sb.AppendLine("details.history-section[open] > summary::before { content: '\\25BC\\00A0'; }");
            sb.AppendLine("</style></head><body>");

            sb.Append(BuildHeader(data));

            if (!data.Images.Any()) {
                sb.AppendLine("<p><em>No images were recorded during this session.</em></p>");
                sb.AppendLine("</body></html>");
                return sb.ToString();
            }

            sb.Append(BuildEventTimelineSection(data));
            sb.Append(BuildOverviewStatsSection(data));
            sb.Append(await BuildTargetSection(data));
            sb.Append(BuildImageQualitySection(data));
            sb.Append(BuildFooter());

            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        private string BuildHeader(ReportData data) {
            var sb = new StringBuilder();
            var icon = IconDataUri;
            if (icon != null) {
                sb.AppendLine("<div style='display:flex; align-items:center; gap:14px; border-bottom:2px solid #7eb8f7; padding-bottom:10px; margin-bottom:8px;'>");
                sb.AppendLine($"  <img src='{icon}' alt='Night Summary' style='width:48px; height:48px; border-radius:6px; flex-shrink:0;' />");
                sb.AppendLine("  <h1 style='margin:0; border:none; padding:0;'>Night Summary Report</h1>");
                sb.AppendLine("</div>");
            } else {
                sb.AppendLine("<h1>Night Summary Report</h1>");
            }
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

        private async Task<string> BuildTargetSection(ReportData data) {
            var sb = new StringBuilder();
            var targets = data.Images.GroupBy(i => i.TargetName).OrderBy(g => g.Min(i => i.Timestamp));
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

                // Resolve RA/Dec: prefer TS data, fall back to image metadata
                double raH = 0, decD = 0;
                if (tsTarget != null && (tsTarget.RA != 0 || tsTarget.Dec != 0)) {
                    raH = tsTarget.RA; decD = tsTarget.Dec;
                } else {
                    var coordImg = target.FirstOrDefault(i => i.RaHours != 0 || i.DecDegrees != 0);
                    if (coordImg != null) { raH = coordImg.RaHours; decD = coordImg.DecDegrees; }
                }

                // Imaging window for this target: first to last image timestamp
                var targetImgStart = target.Min(i => i.Timestamp);
                var targetImgEnd   = target.Max(i => i.Timestamp);

                // Build coords + moon subtitle for the h3 heading
                string h3Subtitle = "";
                if (raH != 0 || decD != 0) {
                    var sessMid    = targetImgStart.AddMinutes((targetImgEnd - targetImgStart).TotalMinutes / 2);
                    var (moonRa, moonDec) = AltitudeCalculator.GetMoonPosition(sessMid.ToUniversalTime());
                    double moonSep = AltitudeCalculator.AngularSeparation(raH, decD, moonRa, moonDec);
                    h3Subtitle = $" <span style='font-weight:normal; font-size:12px; color:#888;'>" +
                                 $"— {FormatRA(raH)} &nbsp;·&nbsp; {FormatDec(decD)} &nbsp;·&nbsp; &#127769; Moon: {moonSep:F0}&#176;" +
                                 $"</span>";
                }

                sb.AppendLine("<div class='target-section'>");
                sb.AppendLine($"<h3>{target.Key}{h3Subtitle}</h3>");

                if (tsTarget != null) {
                    // Row: thumbnail (200px) | altitude chart (~360px) side by side
                    sb.AppendLine("<div class='ts-target-header'>");

                    var raDeg    = tsTarget.RA * 15.0;
                    var thumbUrl = $"https://alasky.cds.unistra.fr/hips-image-services/hips2fits" +
                                   $"?hips=CDS%2FP%2FDSS2%2Fcolor&width={thumbPx}&height={thumbPx}" +
                                   $"&fov={thumbFov:F4}&ra={raDeg:F6}&dec={tsTarget.Dec:F6}" +
                                   $"&projection=TAN&format=jpg";
                    var svgAngle = -tsTarget.Rotation;

                    // Attempt to download and embed the thumbnail as a base64 data URI so
                    // the report displays correctly when opened offline. Falls back to the
                    // remote URL if the download fails or times out.
                    string imgSrc = thumbUrl;
                    try {
                        var bytes = await Http.GetByteArrayAsync(thumbUrl);
                        imgSrc = "data:image/jpeg;base64," + Convert.ToBase64String(bytes);
                    } catch { /* leave imgSrc as the remote URL */ }

                    sb.AppendLine($"<div class='ts-thumb-wrap'>");
                    sb.AppendLine($"  <img src='{imgSrc}' alt='{target.Key}' />");
                    sb.AppendLine($"  <svg width='{thumbPx}' height='{thumbPx}' xmlns='http://www.w3.org/2000/svg'>");
                    sb.AppendLine($"    <rect x='{(cx - boxW / 2):F1}' y='{(cy - boxH / 2):F1}' width='{boxW:F1}' height='{boxH:F1}'");
                    sb.AppendLine($"          fill='none' stroke='#7eb8f7' stroke-width='1.5' opacity='0.85'");
                    sb.AppendLine($"          transform='rotate({svgAngle:F2},{cx:F1},{cy:F1})' />");
                    sb.AppendLine($"  </svg>");
                    sb.AppendLine($"</div>"); // ts-thumb-wrap

                    // Altitude chart beside the thumbnail
                    if (raH != 0 || decD != 0) {
                        var altChart = BuildAltitudeChart(raH, decD, data.ObserverLatitude, data.ObserverLongitude,
                                                          targetImgStart, targetImgEnd, width: 500);
                        if (!string.IsNullOrEmpty(altChart))
                            sb.Append($"<div style='flex:1; min-width:0; margin-top:-20px;'>{altChart}</div>");
                    }

                    sb.AppendLine("</div>"); // ts-target-header
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
                sb.AppendLine("<p style='margin: 12px 0 4px; font-size: 13px; color: #a0c4ff;'><strong>Star Count Consistency</strong></p>");
                sb.AppendLine("<table class='star-count-table'>");
                sb.AppendLine("<tr><th>Broadband CV</th><th>Narrowband CV</th></tr>");
                sb.AppendLine($"<tr><td>{broadbandCV}</td><td>{narrowbandCV}</td></tr>");
                sb.AppendLine("</table>");

                // Session history (collapsible)
                List<TargetSessionHistory> history = null;
                data.SessionHistory?.TryGetValue(target.Key, out history);
                if (history != null && history.Any()) {
                    var label = $"Session History ({history.Count} previous session{(history.Count == 1 ? "" : "s")})";
                    sb.AppendLine($"<details class='history-section'>");
                    sb.AppendLine($"<summary>{label}</summary>");
                    sb.AppendLine("<table>");
                    sb.AppendLine("<tr><th>Date</th><th>Integration</th><th>Avg HFR</th><th>Avg FWHM</th><th>Avg Guiding RMS</th></tr>");
                    foreach (var h in history) {
                        var hfrStr = h.AvgHFR        > 0 ? h.AvgHFR.ToString("F2")         : "—";
                        var fwhmStr = h.AvgFWHM      > 0 ? h.AvgFWHM.ToString("F2")        : "—";
                        var rmsStr = h.AvgGuidingRMS > 0 ? $"{h.AvgGuidingRMS:F2}&quot;" : "—";
                        sb.AppendLine($"<tr><td>{h.SessionStart:MMM d, yyyy}</td><td>{FormatIntegration(h.IntegrationSeconds)}</td><td>{hfrStr}</td><td>{fwhmStr}</td><td>{rmsStr}</td></tr>");
                    }
                    sb.AppendLine("</table>");
                    sb.AppendLine("</details>");
                }

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
                }

                // No TS data: altitude chart at full width below target info
                if (tsTarget == null && (raH != 0 || decD != 0)) {
                    var altChart = BuildAltitudeChart(raH, decD, data.ObserverLatitude, data.ObserverLongitude,
                                                      targetImgStart, targetImgEnd, width: 560);
                    if (!string.IsNullOrEmpty(altChart))
                        sb.Append(altChart);
                }

                sb.AppendLine("</div>"); // target-section
            }

            return sb.ToString();
        }

        private string BuildImageQualitySection(ReportData data) {
            var sb = new StringBuilder();
            var imagesWithHFR     = data.Images.Where(i => i.HFR > 0).ToList();
            var imagesWithFWHM    = data.Images.Where(i => i.FWHM > 0).ToList();
            var imagesWithEcc     = data.Images.Where(i => i.Eccentricity > 0).ToList();
            var imagesWithGuiding = data.Images.Where(i => i.GuidingRMSTotal > 0).ToList();

            if (!imagesWithHFR.Any() && !imagesWithFWHM.Any() && !imagesWithGuiding.Any()) return string.Empty;

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
                sb.AppendLine($"<tr><td>Eccentricity</td><td>{eccValues.Min():F2}</td><td>{eccValues.Max():F2}</td><td>{eccValues.Average():F2}</td><td>{CV(eccValues):F0}%</td></tr>");
            }

            if (imagesWithGuiding.Any()) {
                var rmsValues = imagesWithGuiding.Select(i => i.GuidingRMSTotal).ToList();
                sb.AppendLine($"<tr><td>Guiding RMS</td><td>{rmsValues.Min():F2}\"</td><td>{rmsValues.Max():F2}\"</td><td>{rmsValues.Average():F2}\"</td><td>{CV(rmsValues):F0}%</td></tr>");
            }

            sb.AppendLine("</table>");

            var hfrChart = ChartGenerator.GenerateHfrChart(data.Images);
            if (!string.IsNullOrEmpty(hfrChart)) {
                sb.AppendLine("<h2>HFR Over Time</h2>");
                sb.AppendLine(hfrChart);
            }

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

        private string BuildAltitudeChart(double raHours, double decDeg, double latDeg, double lonDeg,
                                          DateTime sessionStart, DateTime sessionEnd, int width = 560) {
            if (latDeg == 0 && lonDeg == 0) return string.Empty;

            // Chart window: sunset to sunrise (zoomed in to the imaging night)
            var (dayStart, dayEnd) = AltitudeCalculator.FindNightWindow(latDeg, lonDeg, sessionStart);

            int svgW     = width;
            bool compact = width <= 210;
            int padL     = compact ? 28 : 38;
            int padR     = compact ? 6  : 10;
            int padT     = compact ? 20 : 20;
            int padB     = compact ? 28 : 28;  // room for time-axis labels only
            int plotW    = svgW - padL - padR;
            int plotH    = 200;  // fixed to match 200px survey thumbnail
            int svgH     = padT + plotH + padB;

            const double minAlt = 0.0, maxAlt = 90.0, altRange = maxAlt - minAlt;
            double totalMin = (dayEnd - dayStart).TotalMinutes;  // always 1440

            var points = AltitudeCalculator.GetAltitudeCurve(raHours, decDeg, latDeg, lonDeg,
                                                              dayStart, dayEnd, stepMinutes: 5);
            if (points.Count < 2) return string.Empty;

            double X(DateTime t) => padL + ((t - dayStart).TotalMinutes / totalMin * plotW);
            double Y(double alt)  => padT + plotH - (alt / altRange * plotH);

            double xSessStart = X(sessionStart);
            double xSessEnd   = X(sessionEnd);

            // Collect above-horizon segments for segmented polylines
            var segments = new List<List<(DateTime t, double alt)>>();
            List<(DateTime t, double alt)> currentSeg = null;
            foreach (var (t, alt) in points) {
                if (alt >= 0) {
                    if (currentSeg == null) { currentSeg = new List<(DateTime, double)>(); segments.Add(currentSeg); }
                    currentSeg.Add((t, Math.Min(maxAlt, alt)));
                } else {
                    currentSeg = null;
                }
            }

            int timeLabelY = padT + plotH + 18;

            var sb = new StringBuilder();
            sb.AppendLine($"<svg viewBox='0 0 {svgW} {svgH}' width='102%' height='{svgH}' xmlns='http://www.w3.org/2000/svg' style='display:block;' preserveAspectRatio='none'>");

            // Background
            sb.AppendLine($"<rect x='{padL}' y='{padT}' width='{plotW}' height='{plotH}' fill='#0d1117' rx='4'/>");

            // Session window subtle highlight
            sb.AppendLine($"<rect x='{xSessStart:F1}' y='{padT}' width='{(xSessEnd - xSessStart):F1}' height='{plotH}' fill='#7eb8f7' opacity='0.07'/>");

            // Grid lines at 30° and 60°
            foreach (var gridAlt in new[] { 30.0, 60.0 }) {
                double gy = Y(gridAlt);
                sb.AppendLine($"<line x1='{padL}' y1='{gy:F1}' x2='{padL + plotW}' y2='{gy:F1}' stroke='#2d2d5e' stroke-width='1'/>");
                sb.AppendLine($"<text x='{padL - 4}' y='{gy + 4:F1}' text-anchor='end' font-size='10' fill='#555'>{gridAlt:F0}°</text>");
            }
            sb.AppendLine($"<text x='{padL - 4}' y='{padT + 4}' text-anchor='end' font-size='10' fill='#555'>90°</text>");
            sb.AppendLine($"<text x='{padL - 4}' y='{padT + plotH + 4}' text-anchor='end' font-size='10' fill='#555'>0°</text>");

            // Altitude curve — one polyline per continuous above-horizon segment
            foreach (var seg in segments) {
                if (seg.Count < 2) continue;
                var pts = new StringBuilder();
                foreach (var (t, alt) in seg)
                    pts.Append($"{X(t):F1},{Y(alt):F1} ");
                sb.AppendLine($"<polyline points='{pts}' fill='none' stroke='#7eb8f7' stroke-width='2'/>");
            }

            // Session start line with tooltip
            sb.AppendLine("<g>");
            sb.AppendLine($"  <title>Start: {sessionStart:HH:mm}</title>");
            sb.AppendLine($"  <line x1='{xSessStart:F1}' y1='{padT}' x2='{xSessStart:F1}' y2='{padT + plotH}' stroke='#7eb8f7' stroke-width='1.5' stroke-dasharray='4,3' opacity='0.7'/>");
            sb.AppendLine($"  <text x='{xSessStart:F1}' y='{padT - 5}' text-anchor='middle' font-size='9' fill='#7eb8f7'>Start</text>");
            sb.AppendLine("</g>");

            // Session end line with tooltip
            sb.AppendLine("<g>");
            sb.AppendLine($"  <title>End: {sessionEnd:HH:mm}</title>");
            sb.AppendLine($"  <line x1='{xSessEnd:F1}' y1='{padT}' x2='{xSessEnd:F1}' y2='{padT + plotH}' stroke='#7eb8f7' stroke-width='1.5' stroke-dasharray='4,3' opacity='0.7'/>");
            sb.AppendLine($"  <text x='{xSessEnd:F1}' y='{padT - 5}' text-anchor='middle' font-size='9' fill='#7eb8f7'>End</text>");
            sb.AppendLine("</g>");

            // Sunset / sunrise edge markers
            sb.AppendLine($"<text x='{padL + 2}' y='{padT + plotH - 4}' font-size='10' fill='#f59e0b' opacity='0.8'>&#9660; Sunset {dayStart:HH:mm}</text>");
            sb.AppendLine($"<text x='{padL + plotW - 2}' y='{padT + plotH - 4}' text-anchor='end' font-size='10' fill='#f59e0b' opacity='0.8'>Sunrise {dayEnd:HH:mm} &#9650;</text>");

            // X-axis time labels — edge labels + intermediate ticks every 2h
            sb.AppendLine($"<text x='{padL}' y='{timeLabelY}' text-anchor='start' font-size='10' fill='#888'>{dayStart:HH:mm}</text>");
            sb.AppendLine($"<text x='{padL + plotW}' y='{timeLabelY}' text-anchor='end' font-size='10' fill='#888'>{dayEnd:HH:mm}</text>");
            var firstTick = new DateTime(dayStart.Year, dayStart.Month, dayStart.Day, dayStart.Hour, 0, 0).AddHours(compact ? 4 : 2);
            if (firstTick <= dayStart) firstTick = firstTick.AddHours(compact ? 4 : 2);
            for (var tick = firstTick; tick < dayEnd; tick = tick.AddHours(compact ? 4 : 2)) {
                double tx = X(tick);
                if (tx - padL > 30 && (padL + plotW) - tx > 30)
                    sb.AppendLine($"<text x='{tx:F1}' y='{timeLabelY}' text-anchor='middle' font-size='10' fill='#888'>{tick:HH:mm}</text>");
            }

            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        private string BuildFooter() {
            var sb = new StringBuilder();
            sb.AppendLine("<p class='footnote'>CV (Coefficient of Variation) measures consistency as a percentage of the mean. Lower values indicate more stable conditions. Star count CV is calculated per target and filter type.</p>");
            sb.AppendLine("<p class='footnote'>Generated by Night Summary plugin for N.I.N.A.</p>");
            return sb.ToString();
        }

        private static string FormatIntegration(double seconds) {
            var ts = TimeSpan.FromSeconds(seconds);
            return ts.TotalHours >= 1 ? $"{ts.TotalHours:F1}h" : $"{ts.TotalMinutes:F0}m";
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
