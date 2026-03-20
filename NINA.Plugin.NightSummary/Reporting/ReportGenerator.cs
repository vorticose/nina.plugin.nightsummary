using NINA.Core.Utility;
using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.MyPluginProperties;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Reporting {
    /// <summary>
    /// Generates an HTML report from session data.
    /// Each logical section is a separate private method so individual sections
    /// can be toggled on/off in a future release.
    /// </summary>
    public class ReportGenerator {

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private static readonly HttpClient TsApiClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        /// <summary>
        /// Warnings collected during the most recent report generation.
        /// Cleared at the start of each GenerateHtmlReport call.
        /// </summary>
        public List<string> Warnings { get; } = new List<string>();

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

        // Broadband and narrowband filter classification — user overrides checked first, then first-letter fallback
        private static readonly HashSet<char> BroadbandFirstLetters = new HashSet<char> { 'L', 'R', 'G', 'B' };
        private static readonly HashSet<char> NarrowbandFirstLetters = new HashSet<char> { 'H', 'S', 'O' };

        private static Dictionary<string, string>? _filterOverrides;
        private static Dictionary<string, string> FilterOverrides {
            get {
                if (_filterOverrides == null)
                    _filterOverrides = NightSummaryPlugin.ParseFilterClassifications(Settings.Default.FilterClassifications);
                return _filterOverrides;
            }
        }

        private static bool IsBroadband(string filter) {
            if (string.IsNullOrEmpty(filter)) return false;
            if (FilterOverrides.TryGetValue(filter, out var cls)) return cls == "B";
            return BroadbandFirstLetters.Contains(char.ToUpperInvariant(filter[0]));
        }

        private static bool IsNarrowband(string filter) {
            if (string.IsNullOrEmpty(filter)) return false;
            if (FilterOverrides.TryGetValue(filter, out var cls)) return cls == "N";
            return NarrowbandFirstLetters.Contains(char.ToUpperInvariant(filter[0]));
        }

        private static bool IsExcluded(string filter) {
            if (string.IsNullOrEmpty(filter)) return false;
            return FilterOverrides.TryGetValue(filter, out var cls) && cls == "X";
        }

        private static readonly char[] FilterSortPriority = { 'L', 'R', 'G', 'B', 'H', 'S', 'O' };
        private static int FilterSortKey(string filter) {
            if (string.IsNullOrEmpty(filter)) return int.MaxValue;
            var c = char.ToUpperInvariant(filter[0]);
            var idx = Array.IndexOf(FilterSortPriority, c);
            return idx >= 0 ? idx : int.MaxValue;
        }

        public async Task<string> GenerateHtmlReport(ReportData data) {
            Warnings.Clear();
            _filterOverrides = null; // reload user classifications for this report
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
            sb.AppendLine(".stat-box { background-color: #16213e; border: 1px solid #2d2d5e; border-radius: 8px; padding: 15px; text-align: center; }");
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
            sb.AppendLine(".ts-filter-name { width: 180px; min-width: 180px; max-width: 180px; font-size: 13px; color: #a0c4ff; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; flex-shrink: 0; }");
            sb.AppendLine(".ts-bar-track { flex: 1; height: 14px; background: #2d2d5e; border-radius: 4px; position: relative; overflow: hidden; }");
            sb.AppendLine(".ts-bar-accepted { position: absolute; left: 0; top: 0; bottom: 0; background: #7eb8f7; }");
            sb.AppendLine(".ts-bar-acquired { position: absolute; top: 0; bottom: 0; background: #3a5a7a; }");
            sb.AppendLine(".ts-bar-label { font-size: 12px; color: #888; white-space: nowrap; width: 150px; min-width: 150px; max-width: 150px; text-align: right; flex-shrink: 0; overflow: hidden; text-overflow: ellipsis; }");
            sb.AppendLine(".ts-cumulative { font-size: 12px; color: #888; margin-top: 12px; }");
            sb.AppendLine("details.history-section { margin-top: 12px; }");
            sb.AppendLine("details.history-section > summary { cursor: pointer; color: #a0c4ff; font-size: 14px; font-weight: bold; list-style: none; }");
            sb.AppendLine("details.history-section > summary::-webkit-details-marker { display: none; }");
            sb.AppendLine("details.history-section > summary::before { content: '\\25B6\\00A0'; }");
            sb.AppendLine("details.history-section[open] > summary::before { content: '\\25BC\\00A0'; }");
            sb.AppendLine("details.iq-section { margin-top: 12px; }");
            sb.AppendLine("details.iq-section > summary { cursor: pointer; color: #a0c4ff; font-size: 14px; font-weight: bold; list-style: none; }");
            sb.AppendLine("details.iq-section > summary::-webkit-details-marker { display: none; }");
            sb.AppendLine("details.iq-section > summary::before { content: '\\25B6\\00A0'; }");
            sb.AppendLine("details.iq-section[open] > summary::before { content: '\\25BC\\00A0'; }");
            sb.AppendLine(".iq-table { width: 100%; margin-top: 8px; }");
            sb.AppendLine(".iq-row-grid { display: grid; grid-template-columns: 1fr 1fr 1fr 1fr 1fr; }");
            sb.AppendLine(".iq-header { background-color: #2d2d5e; color: #7eb8f7; padding: 8px; text-align: left; font-weight: bold; }");
            sb.AppendLine(".iq-cell { padding: 8px; border-bottom: 1px solid #2d2d5e; }");
            sb.AppendLine(".iq-row-even .iq-cell { background-color: #16213e; }");
            sb.AppendLine("details.iq-row { margin: 0; }");
            sb.AppendLine("details.iq-row > summary { list-style: none; cursor: pointer; }");
            sb.AppendLine("details.iq-row > summary::-webkit-details-marker { display: none; }");
            sb.AppendLine(".iq-arrow::after { content: ' \\25B6'; font-size: 10px; color: #a0c4ff; }");
            sb.AppendLine("details.iq-row[open] .iq-arrow::after { content: ' \\25BC'; }");
            sb.AppendLine(".iq-expand { padding: 0 8px 8px; }");
            sb.AppendLine("</style></head><body>");

            sb.Append(BuildHeader(data));
            const string warningsPlaceholder = "<!--WARNINGS_PLACEHOLDER-->";
            sb.AppendLine(warningsPlaceholder);

            if (!data.Images.Any()) {
                sb.AppendLine("<p><em>No images were recorded during this session.</em></p>");
                sb.AppendLine("</body></html>");
                return sb.ToString();
            }

            int detailLevel = Settings.Default.ReportDetailLevel;

            if (detailLevel >= 1) sb.Append(BuildEventTimelineSection(data));
            sb.Append(BuildOverviewStatsSection(data, detailLevel));
            sb.Append(await BuildTargetSection(data, detailLevel));
            if (detailLevel >= 1) sb.Append(BuildImageQualitySection(data, detailLevel));
            if (detailLevel >= 1) sb.Append(BuildNextNightPreviewSection(data));
            sb.Append(BuildFooter());

            sb.AppendLine("</body></html>");

            // Replace placeholder with warnings banner if any were collected during generation
            var html = sb.ToString();
            if (Warnings.Any()) {
                var warningHtml = new StringBuilder();
                warningHtml.AppendLine("<div style='background-color:#3a2a00; border:1px solid #b8860b; border-radius:8px; padding:12px 16px; margin:16px 0;'>");
                warningHtml.AppendLine("<p style='color:#f0c040; font-weight:bold; margin:0 0 8px;'>&#9888; Report generated with warnings:</p>");
                warningHtml.AppendLine("<ul style='margin:0; padding-left:20px; color:#d4a850;'>");
                foreach (var warning in Warnings)
                    warningHtml.AppendLine($"<li style='margin:2px 0; font-size:13px;'>{warning}</li>");
                warningHtml.AppendLine("</ul></div>");
                html = html.Replace(warningsPlaceholder, warningHtml.ToString());
            } else {
                html = html.Replace(warningsPlaceholder, "");
            }

            return html;
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

        private string BuildOverviewStatsSection(ReportData data, int detailLevel) {
            var sb = new StringBuilder();
            var totalExposureSec = data.Images.Sum(i => i.ExposureDuration);
            var targetCount      = data.Images.Select(i => i.TargetName).Distinct(StringComparer.OrdinalIgnoreCase).Count();

            // Imaging window: first image to last image
            var firstImage = data.Images.Min(i => i.Timestamp);
            var lastImage  = data.Images.Max(i => i.Timestamp);
            var windowSec  = (lastImage - firstImage).TotalSeconds;

            // Roof-closed exclusion: sum time between each RoofClosed→RoofOpen pair within the imaging window
            var roofEvents    = data.Events
                                    .Where(e => e.EventType == "RoofClosed" || e.EventType == "RoofOpen")
                                    .OrderBy(e => e.Timestamp)
                                    .ToList();
            double roofClosedSec   = 0;
            bool   hasSafetyMonitor = roofEvents.Any();
            DateTime? closedAt = null;
            foreach (var ev in roofEvents) {
                if (ev.EventType == "RoofClosed") {
                    closedAt = ev.Timestamp;
                } else if (ev.EventType == "RoofOpen" && closedAt.HasValue) {
                    var overlapStart = closedAt.Value < firstImage ? firstImage : closedAt.Value;
                    var overlapEnd   = ev.Timestamp  > lastImage  ? lastImage  : ev.Timestamp;
                    if (overlapEnd > overlapStart)
                        roofClosedSec += (overlapEnd - overlapStart).TotalSeconds;
                    closedAt = null;
                }
            }
            // If roof was closed at session end with no matching RoofOpen, count to lastImage
            if (closedAt.HasValue && closedAt.Value < lastImage)
                roofClosedSec += (lastImage - closedAt.Value).TotalSeconds;

            var effectiveWindowSec = windowSec - roofClosedSec;
            double yieldPct = effectiveWindowSec > 0 ? (totalExposureSec / effectiveWindowSec) * 100.0 : 0;
            yieldPct = Math.Min(yieldPct, 100.0); // cap at 100%

            // Avg HFR and Avg Guiding RMS (session-wide)
            var hfrImages     = data.Images.Where(i => i.HFR > 0).ToList();
            var guidingImages = data.Images.Where(i => i.GuidingRMSTotal > 0).ToList();

            var fwhmImages = data.Images.Where(i => i.FWHM > 0).ToList();

            // Column count: Snapshot=3 (one row), Standard=5 (one row), Full=4 (two rows of 4)
            int gridCols = detailLevel == 0 ? 3 : detailLevel == 1 ? 5 : 4;

            sb.AppendLine("<h2>Session Overview</h2>");
            sb.AppendLine($"<div style='display:grid; grid-template-columns:repeat({gridCols},1fr); gap:10px; margin:10px 0;'>");
            sb.AppendLine($"<div class='stat-box'><div class='stat-value'>{data.Images.Count}</div><div class='stat-label'>Total Images</div></div>");
            sb.AppendLine($"<div class='stat-box'><div class='stat-value'>{TimeSpan.FromSeconds(totalExposureSec).TotalHours:F1}h</div><div class='stat-label'>Total Exposure</div></div>");
            sb.AppendLine($"<div class='stat-box'><div class='stat-value'>{targetCount}</div><div class='stat-label'>Targets</div></div>");
            if (detailLevel >= 1 && hfrImages.Any())
                sb.AppendLine($"<div class='stat-box'><div class='stat-value'>{hfrImages.Average(i => i.HFR):F2}px</div><div class='stat-label'>Avg HFR</div></div>");
            if (detailLevel >= 1 && guidingImages.Any())
                sb.AppendLine($"<div class='stat-box'><div class='stat-value'>{guidingImages.Average(i => i.GuidingRMSTotal):F2}\"</div><div class='stat-label'>Avg Guiding RMS</div></div>");
            if (detailLevel >= 2 && fwhmImages.Any())
                sb.AppendLine($"<div class='stat-box'><div class='stat-value'>{fwhmImages.Average(i => i.FWHM):F2}\"</div><div class='stat-label'>Avg FWHM</div></div>");
            if (detailLevel >= 2) {
                var yieldTooltip = hasSafetyMonitor
                    ? "Total exposure time ÷ (imaging window − roof-closed time). Measures how efficiently you collected images during time the roof was open."
                    : "Total exposure time ÷ imaging window (first to last image).";
                sb.AppendLine($"<div class='stat-box' title='{yieldTooltip}' style='cursor:help;'><div class='stat-value'>{yieldPct:F0}%</div><div class='stat-label'>Yield{(hasSafetyMonitor ? "" : "*")}</div></div>");
                var moonIllum = MoonIllumination(data.Session.SessionStart, out bool waxing);
                var moonArrow = waxing ? "&#8593;" : "&#8595;";
                sb.AppendLine($"<div class='stat-box'><div class='stat-value'>{moonIllum:F0}% {moonArrow}</div><div class='stat-label'>Moon</div></div>");
            }
            sb.AppendLine("</div>");
            if (detailLevel >= 2 && !hasSafetyMonitor)
                sb.AppendLine("<p style='font-size:11px; color:#666; margin-top:4px;'>* Yield calculated without cloud exclusion — no safety monitor events recorded</p>");
            return sb.ToString();
        }

        private async Task<string> BuildTargetSection(ReportData data, int detailLevel) {
            var sb = new StringBuilder();
            var targets     = data.Images.GroupBy(i => i.TargetName).OrderBy(g => g.Min(i => i.Timestamp));
            bool multiTarget = targets.Count() > 1;
            sb.AppendLine("<h2>Targets Imaged</h2>");

            // Pre-compute thumbnail/FOV geometry (same for all targets)
            const int thumbPx  = 200;  // CSS display size and SVG overlay dimensions
            const int fetchPx  = 400;  // fetch at 2× for retina/high-DPI screens
            var fovW     = data.CameraFovWidthDeg;
            var fovH     = data.CameraFovHeightDeg;
            var thumbFov = Math.Max(fovW, fovH) * 1.5;
            if (thumbFov <= 0) thumbFov = 1.0;
            Logger.Info($"NightSummary: Thumbnail FOV — fovW={fovW:F4}° fovH={fovH:F4}° thumbFov={thumbFov:F4}°");
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

                // Build subtitle for the h3 heading: start/end times, coords, moon separation
                var timePart   = $"Start: {targetImgStart:HH:mm} &nbsp;&#8594;&nbsp; End: {targetImgEnd:HH:mm}";
                string h3Subtitle;
                if (raH != 0 || decD != 0) {
                    var sessMid    = targetImgStart.AddMinutes((targetImgEnd - targetImgStart).TotalMinutes / 2);
                    var (moonRa, moonDec) = AltitudeCalculator.GetMoonPosition(sessMid.ToUniversalTime());
                    double moonSep = AltitudeCalculator.AngularSeparation(raH, decD, moonRa, moonDec);
                    h3Subtitle = $" <span style='font-weight:normal; font-size:12px; color:#888;'>" +
                                 $"— {timePart} &nbsp;·&nbsp; R.A. {FormatRA(raH)} &nbsp;·&nbsp; Dec. {FormatDec(decD)} &nbsp;·&nbsp; &#127769; &#8596; {moonSep:F0}&#176;" +
                                 $"</span>";
                } else {
                    h3Subtitle = $" <span style='font-weight:normal; font-size:12px; color:#888;'>— {timePart}</span>";
                }

                sb.AppendLine("<div class='target-section'>");
                sb.AppendLine($"<h3>{target.Key}{h3Subtitle}</h3>");

                bool showThumb         = (raH != 0 || decD != 0) && Settings.Default.ShowSkyThumbnails;
                bool showSideBySideChart = (raH != 0 || decD != 0) && detailLevel >= 1 && Settings.Default.ShowAltitudeChart;

                // Pre-build thumbnail HTML so it can be placed in either layout
                string thumbHtml = "";
                if (showThumb) {
                    var tSb     = new StringBuilder();
                    var raDeg   = raH * 15.0;
                    var thumbUrl = $"https://alasky.cds.unistra.fr/hips-image-services/hips2fits" +
                                   $"?hips=CDS%2FP%2FDSS2%2Fcolor&width={fetchPx}&height={fetchPx}" +
                                   $"&fov={thumbFov:F4}&ra={raDeg:F6}&dec={decD:F6}" +
                                   $"&projection=TAN&format=jpg";
                    var svgAngle = tsTarget != null ? -tsTarget.Rotation : 0.0;
                    string imgSrc = thumbUrl;
                    try {
                        var bytes = await Http.GetByteArrayAsync(thumbUrl);
                        imgSrc = "data:image/jpeg;base64," + Convert.ToBase64String(bytes);
                    } catch (Exception ex) {
                        Logger.Warning($"NightSummary: DSS thumbnail fetch failed for {target.Key}. {ex.Message}");
                        Warnings.Add($"Sky thumbnail for {target.Key} could not be loaded — falling back to remote URL (requires internet access to display)");
                    }
                    tSb.AppendLine($"<div class='ts-thumb-wrap'>");
                    tSb.AppendLine($"  <img src='{imgSrc}' alt='{target.Key}' />");
                    tSb.AppendLine($"  <svg width='{thumbPx}' height='{thumbPx}' xmlns='http://www.w3.org/2000/svg'>");
                    tSb.AppendLine($"    <rect x='{(cx - boxW / 2):F1}' y='{(cy - boxH / 2):F1}' width='{boxW:F1}' height='{boxH:F1}'");
                    tSb.AppendLine($"          fill='none' stroke='#7eb8f7' stroke-width='1.5' opacity='0.85'");
                    tSb.AppendLine($"          transform='rotate({svgAngle:F2},{cx:F1},{cy:F1})' />");
                    tSb.AppendLine($"  </svg>");
                    tSb.AppendLine($"</div>"); // ts-thumb-wrap
                    thumbHtml = tSb.ToString();
                }

                // When thumbnail is shown without an altitude chart, place thumbnail left
                // and wrap all remaining content in a flex right column to fill the space.
                bool thumbWithoutChart = showThumb && !showSideBySideChart;

                if (thumbWithoutChart) {
                    sb.AppendLine("<div style='display:flex; gap:16px; align-items:flex-start;'>");
                    sb.Append(thumbHtml);
                    sb.AppendLine("<div style='flex:1; min-width:0;'>");
                } else if (showThumb || showSideBySideChart) {
                    // Thumbnail + altitude chart side by side
                    sb.AppendLine("<div class='ts-target-header'>");
                    sb.Append(thumbHtml);
                    if (showSideBySideChart) {
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
                var filterGroups = target
                    .GroupBy(i => (i.Filter, i.ExposureDuration))
                    .OrderBy(g => FilterSortKey(g.Key.Filter)).ThenBy(g => g.Key.Filter).ThenBy(g => g.Key.ExposureDuration);
                foreach (var filterGroup in filterGroups) {
                    var totalTime = TimeSpan.FromSeconds(filterGroup.Sum(i => i.ExposureDuration));
                    sb.AppendLine($"<tr><td>{filterGroup.Key.Filter}</td><td>{filterGroup.Count()}</td><td>{filterGroup.Key.ExposureDuration:F0}s</td><td>{FormatDuration(totalTime.TotalSeconds)}</td></tr>");
                }
                var targetTotal = TimeSpan.FromSeconds(target.Sum(i => i.ExposureDuration));
                sb.AppendLine($"<tr><td><strong>Total</strong></td><td><strong>{target.Count()}</strong></td><td></td><td><strong>{FormatDuration(targetTotal.TotalSeconds)}</strong></td></tr>");
                sb.AppendLine("</table>");

                if (detailLevel >= 1 && Settings.Default.ShowStarCountCV) {
                    // Star count CV
                    var broadbandImages  = target.Where(i => IsBroadband(i.Filter)  && i.StarCount > 0).ToList();
                    var narrowbandImages = target.Where(i => IsNarrowband(i.Filter) && i.StarCount > 0).ToList();
                    string broadbandCV  = broadbandImages.Count  >= 2 ? $"{CV(broadbandImages.Select(i  => (double)i.StarCount).ToList()):F0}%" : "—";
                    string narrowbandCV = narrowbandImages.Count >= 2 ? $"{CV(narrowbandImages.Select(i => (double)i.StarCount).ToList()):F0}%" : "—";
                    var cvTooltip = "CV (Coefficient of Variation) measures consistency as a percentage of the mean. Lower values indicate more stable conditions. Star count CV is calculated per target and filter type.";
                    sb.AppendLine($"<div title='{cvTooltip}' style='cursor:help;'>");
                    sb.AppendLine("<p style='margin: 12px 0 4px; font-size: 13px; color: #a0c4ff;'><strong>Star Count Consistency</strong></p>");
                    sb.AppendLine("<table class='star-count-table'>");
                    sb.AppendLine("<tr><th>Broadband CV</th><th>Narrowband CV</th></tr>");
                    sb.AppendLine($"<tr><td>{broadbandCV}</td><td>{narrowbandCV}</td></tr>");
                    sb.AppendLine("</table>");
                    sb.AppendLine("</div>");

                    // Warn about unrecognized filter names that were excluded from CV
                    var unrecognizedFilters = target
                        .Select(i => i.Filter)
                        .Where(f => !string.IsNullOrEmpty(f) && !IsBroadband(f) && !IsNarrowband(f))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(f => f)
                        .ToList();
                    if (unrecognizedFilters.Any()) {
                        var filterList = string.Join(", ", unrecognizedFilters.Select(f => $"<strong>{f}</strong>"));
                        var filterListPlain = string.Join(", ", unrecognizedFilters);
                        sb.AppendLine($"<p style='font-size:11px; color:#b8860b; margin-top:6px;'>&#9888; Filter{(unrecognizedFilters.Count == 1 ? "" : "s")} not recognized and excluded from CV calculation: {filterList}. Filters are classified by first letter — broadband (L, R, G, B) and narrowband (H, S, O).</p>");
                        Warnings.Add($"Unrecognized filter{(unrecognizedFilters.Count == 1 ? "" : "s")} excluded from CV calculation: {filterListPlain}");
                    }
                }

                // Per-target image quality (collapsible) — only for multi-target sessions
                if (detailLevel >= 1 && multiTarget && Settings.Default.ShowPerTargetIQ) {
                    var targetList = target.ToList();
                    bool hasData = targetList.Any(i => i.HFR > 0 || i.FWHM > 0 || i.Eccentricity > 0 || i.GuidingRMSTotal > 0);
                    if (hasData) {
                        sb.AppendLine("<details class='iq-section'>");
                        sb.AppendLine("<summary>Image Quality</summary>");
                        sb.AppendLine("<div class='iq-table'>");
                        sb.AppendLine("<div class='iq-row-grid'><div class='iq-header'>Metric</div><div class='iq-header'>Min</div><div class='iq-header'>Max</div><div class='iq-header'>Mean</div><div class='iq-header'>CV</div></div>");
                        AppendIqRows(sb, targetList);
                        sb.AppendLine("</div>");
                        sb.AppendLine("</details>");
                    }
                }

                // Session history (collapsible)
                if (detailLevel >= 2 && Settings.Default.ShowSessionHistory) {
                    List<TargetSessionHistory> history = null;
                    data.SessionHistory?.TryGetValue(target.Key, out history);
                    if (history != null && history.Any()) {
                        var label = $"Session History ({history.Count} previous session{(history.Count == 1 ? "" : "s")})";
                        sb.AppendLine($"<details class='history-section'>");
                        sb.AppendLine($"<summary>{label}</summary>");
                        sb.AppendLine("<table>");
                        sb.AppendLine("<tr><th>Date</th><th>Integration</th><th>Avg HFR</th><th>Avg FWHM</th><th>Avg Guiding RMS</th></tr>");
                        foreach (var h in history) {
                            var hfrStr  = h.AvgHFR        > 0 ? h.AvgHFR.ToString("F2") + "px" : "—";
                            var fwhmStr = h.AvgFWHM       > 0 ? h.AvgFWHM.ToString("F2") + "\"" : "—";
                            var rmsStr  = h.AvgGuidingRMS > 0 ? $"{h.AvgGuidingRMS:F2}&quot;" : "—";
                            sb.AppendLine($"<tr><td>{h.SessionStart:MMM d, yyyy}</td><td>{FormatIntegration(h.IntegrationSeconds)}</td><td>{hfrStr}</td><td>{fwhmStr}</td><td>{rmsStr}</td></tr>");
                        }
                        sb.AppendLine("</table>");
                        sb.AppendLine("</details>");
                    }
                }

                if (tsTarget == null && detailLevel >= 1 && Settings.Default.ShowTSProgressBars) {
                    if (data.TsData == null || data.TsData.Count == 0) {
                        if (!Warnings.Any(w => w.StartsWith("Target Scheduler progress bars unavailable —")))
                            Warnings.Add("Target Scheduler progress bars unavailable — Target Scheduler not installed or database not found");
                    } else {
                        Warnings.Add($"Target Scheduler progress bars unavailable for {target.Key} — target not found in Target Scheduler");
                    }
                }
                if (tsTarget != null && detailLevel >= 1 && Settings.Default.ShowTSProgressBars) {
                    // TS progress bars — one per exposure plan row (template + filter)
                    sb.AppendLine("<p style='margin: 12px 0 4px; font-size: 13px; color: #a0c4ff;'><strong>Target Scheduler Progress</strong></p>");
                    double totalIntegrationSec = 0;
                    foreach (var f in tsTarget.Filters.OrderBy(f => FilterSortKey(f.Filter)).ThenBy(f => f.Filter).ThenBy(f => f.TemplateName)) {
                        var desired     = f.Desired;
                        var accepted    = f.Accepted;
                        var acceptedPct = desired > 0 ? (double)accepted / desired * 100.0 : 0;
                        var pctLabel    = desired > 0 ? $" ({acceptedPct:F0}%)" : "";

                        // Tonight's contribution: images captured this session for this filter (match by exposure duration too to handle multiple templates per filter)
                        var tonightImages = target.Where(i => string.Equals(i.Filter, f.Filter, StringComparison.OrdinalIgnoreCase)
                                                           && (f.ExposureSec <= 0 || Math.Abs(i.ExposureDuration - f.ExposureSec) < 1.0)).ToList();
                        var tonightCount  = tonightImages.Count;

                        // When grading is pending (accepted=0 but acquired>0), use acquired so tonight's bar is visible
                        var gradingPending  = accepted == 0 && f.Acquired > 0;
                        var effectiveFilled = gradingPending ? f.Acquired : accepted;

                        // Integration: use effectiveFilled so grading-pending frames are included
                        totalIntegrationSec += effectiveFilled * f.ExposureSec;
                        var tonightBar = Math.Min(tonightCount, effectiveFilled);
                        var priorBar   = Math.Max(0, effectiveFilled - tonightBar);
                        var priorPct   = desired > 0 ? (double)priorBar   / desired * 100.0 : 0;
                        var tonightPct = desired > 0 ? (double)tonightBar / desired * 100.0 : 0;

                        var expLabel  = f.ExposureSec > 0 ? $" ({f.ExposureSec:F0}s)" : "";
                        var barLabel  = !string.IsNullOrEmpty(f.TemplateName) ? $"{f.TemplateName}{expLabel}" : $"{f.Filter}{expLabel}";
                        var tooltip   = tonightCount > 0
                            ? (gradingPending ? $"+{tonightCount} images tonight (grading pending)" : $"+{tonightCount} images tonight")
                            : "";

                        sb.AppendLine("<div class='ts-filter-row'>");
                        sb.AppendLine($"  <span class='ts-filter-name'>{barLabel}</span>");
                        sb.AppendLine($"  <div class='ts-bar-track' title='{tooltip}'>");
                        sb.AppendLine($"    <div class='ts-bar-accepted' style='width:{priorPct:F1}%'></div>");
                        sb.AppendLine($"    <div class='ts-bar-acquired' style='left:{priorPct:F1}%;width:{tonightPct:F1}%'></div>");
                        sb.AppendLine($"  </div>");
                        var barRightLabel = gradingPending
                            ? $"{f.Acquired}/{desired} acquired ({(desired > 0 ? (double)f.Acquired / desired * 100.0 : 0):F0}%)"
                            : $"{accepted}/{desired} accepted{pctLabel}";
                        sb.AppendLine($"  <span class='ts-bar-label'>{barRightLabel}</span>");
                        sb.AppendLine("</div>");
                    }

                    // Cumulative integration estimate
                    var totalHours    = totalIntegrationSec / 3600.0;
                    var integTooltip  = "Estimated from TS accepted frames (or acquired if grading is pending) × configured exposure time per template. Reduce the TS accepted count manually to account for culled images.";
                    sb.AppendLine($"<p class='ts-cumulative' title='{integTooltip}' style='cursor:help;'>Total integration (all sessions, estimate): ~{totalHours:F1}h</p>");
                }


                if (thumbWithoutChart) {
                    sb.AppendLine("</div>"); // flex right column
                    sb.AppendLine("</div>"); // flex wrapper
                }

                sb.AppendLine("</div>"); // target-section
            }

            return sb.ToString();
        }

        private void AppendIqRows(StringBuilder sb, List<ImageRecord> images) {
            int rowIdx = 0;
            var imagesWithHFR     = images.Where(i => i.HFR > 0).ToList();
            var imagesWithFWHM    = images.Where(i => i.FWHM > 0).ToList();
            var imagesWithEcc     = images.Where(i => i.Eccentricity > 0).ToList();
            var imagesWithGuiding = images.Where(i => i.GuidingRMSTotal > 0).ToList();

            // HFR row — expandable via <details>
            if (imagesWithHFR.Any()) {
                var hfrValues  = imagesWithHFR.Select(i => i.HFR).ToList();
                var hfrFilters = imagesWithHFR.GroupBy(i => i.Filter).Where(g => g.Any()).OrderBy(g => FilterSortKey(g.Key)).ThenBy(g => g.Key).ToList();
                string evenCls = rowIdx % 2 == 1 ? " iq-row-even" : "";
                sb.AppendLine($"<details class='iq-row{evenCls}'><summary>");
                sb.AppendLine($"<div class='iq-row-grid'><div class='iq-cell'>HFR<span class='iq-arrow'></span></div><div class='iq-cell'>{hfrValues.Min():F2}px</div><div class='iq-cell'>{hfrValues.Max():F2}px</div><div class='iq-cell'>{hfrValues.Average():F2}px</div><div class='iq-cell'>{CV(hfrValues):F0}%</div></div>");
                sb.AppendLine("</summary>");
                sb.AppendLine("<div class='iq-expand'>");
                sb.AppendLine("<table style='margin:0;'><tr><th>Filter</th><th>Min</th><th>Max</th><th>Mean</th><th>CV</th></tr>");
                foreach (var g in hfrFilters) {
                    var vals  = g.Select(i => i.HFR).ToList();
                    var cvStr = vals.Count >= 2 ? $"{CV(vals):F0}%" : "—";
                    sb.AppendLine($"<tr><td>{g.Key} <span style='color:#7eb8f7;font-style:italic;'>({vals.Count})</span></td><td>{vals.Min():F2}px</td><td>{vals.Max():F2}px</td><td>{vals.Average():F2}px</td><td>{cvStr}</td></tr>");
                }
                sb.AppendLine("</table></div></details>");
                rowIdx++;
            }

            // FWHM row — expandable via <details>
            if (imagesWithFWHM.Any()) {
                var fwhmValues  = imagesWithFWHM.Select(i => i.FWHM).ToList();
                var fwhmFilters = imagesWithFWHM.GroupBy(i => i.Filter).Where(g => g.Any()).OrderBy(g => FilterSortKey(g.Key)).ThenBy(g => g.Key).ToList();
                string evenCls = rowIdx % 2 == 1 ? " iq-row-even" : "";
                sb.AppendLine($"<details class='iq-row{evenCls}'><summary>");
                sb.AppendLine($"<div class='iq-row-grid'><div class='iq-cell'>FWHM<span class='iq-arrow'></span></div><div class='iq-cell'>{fwhmValues.Min():F2}\"</div><div class='iq-cell'>{fwhmValues.Max():F2}\"</div><div class='iq-cell'>{fwhmValues.Average():F2}\"</div><div class='iq-cell'>{CV(fwhmValues):F0}%</div></div>");
                sb.AppendLine("</summary>");
                sb.AppendLine("<div class='iq-expand'>");
                sb.AppendLine("<table style='margin:0;'><tr><th>Filter</th><th>Min</th><th>Max</th><th>Mean</th><th>CV</th></tr>");
                foreach (var g in fwhmFilters) {
                    var vals  = g.Select(i => i.FWHM).ToList();
                    var cvStr = vals.Count >= 2 ? $"{CV(vals):F0}%" : "—";
                    sb.AppendLine($"<tr><td>{g.Key} <span style='color:#7eb8f7;font-style:italic;'>({vals.Count})</span></td><td>{vals.Min():F2}\"</td><td>{vals.Max():F2}\"</td><td>{vals.Average():F2}\"</td><td>{cvStr}</td></tr>");
                }
                sb.AppendLine("</table></div></details>");
                rowIdx++;
            }

            // Eccentricity row — plain (not expandable)
            if (imagesWithEcc.Any()) {
                var eccValues = imagesWithEcc.Select(i => i.Eccentricity).ToList();
                string evenCls = rowIdx % 2 == 1 ? " iq-row-even" : "";
                sb.AppendLine($"<div class='iq-row-grid{evenCls}'><div class='iq-cell'>Eccentricity</div><div class='iq-cell'>{eccValues.Min():F2}</div><div class='iq-cell'>{eccValues.Max():F2}</div><div class='iq-cell'>{eccValues.Average():F2}</div><div class='iq-cell'>{CV(eccValues):F0}%</div></div>");
                rowIdx++;
            }

            // Guiding RMS row — plain (not expandable)
            if (imagesWithGuiding.Any()) {
                var rmsValues = imagesWithGuiding.Select(i => i.GuidingRMSTotal).ToList();
                string evenCls = rowIdx % 2 == 1 ? " iq-row-even" : "";
                sb.AppendLine($"<div class='iq-row-grid{evenCls}'><div class='iq-cell'>Guiding RMS</div><div class='iq-cell'>{rmsValues.Min():F2}\"</div><div class='iq-cell'>{rmsValues.Max():F2}\"</div><div class='iq-cell'>{rmsValues.Average():F2}\"</div><div class='iq-cell'>{CV(rmsValues):F0}%</div></div>");
            }
        }

        private string BuildImageQualitySection(ReportData data, int detailLevel) {
            var sb = new StringBuilder();
            var hasHFR     = data.Images.Any(i => i.HFR > 0);
            var hasFWHM    = data.Images.Any(i => i.FWHM > 0);
            var hasGuiding = data.Images.Any(i => i.GuidingRMSTotal > 0);

            if (!hasHFR && !hasFWHM && !hasGuiding) return string.Empty;

            sb.AppendLine("<div class='target-section'>");
            sb.AppendLine("<h2>Session Image Quality</h2>");
            sb.AppendLine("<div class='iq-table'>");
            sb.AppendLine("<div class='iq-row-grid'><div class='iq-header'>Metric</div><div class='iq-header'>Min</div><div class='iq-header'>Max</div><div class='iq-header'>Mean</div><div class='iq-header'>CV</div></div>");
            AppendIqRows(sb, data.Images);
            sb.AppendLine("</div>"); // iq-table

            if (detailLevel >= 2 && Settings.Default.ShowHFRGraph) {
                int primary   = Settings.Default.ChartPrimaryMetric;
                int secondary = Settings.Default.ChartSecondaryMetric;
                sb.AppendLine($"<h2>{ChartGenerator.GetChartTitle(primary, secondary)}</h2>");
                sb.AppendLine(ChartGenerator.GenerateMetricChart(data.Images, primary, secondary));
            }

            sb.AppendLine("</div>");
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
            sb.AppendLine($"<rect x='{padL}' y='{padT}' width='{plotW}' height='{plotH}' fill='none' stroke='#2d2d5e' stroke-width='1' rx='4'/>");

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

            // ── Moon altitude curve ──────────────────────────────────────────────
            if (!Settings.Default.ShowMoonCurve) goto skipMoon;
            var moonPoints = AltitudeCalculator.GetMoonAltitudeCurve(latDeg, lonDeg, dayStart, dayEnd, stepMinutes: 5);
            var moonSegments = new List<List<(DateTime t, double alt)>>();
            List<(DateTime t, double alt)> moonSeg = null;
            foreach (var (t, alt) in moonPoints) {
                if (alt >= 0) {
                    if (moonSeg == null) { moonSeg = new List<(DateTime, double)>(); moonSegments.Add(moonSeg); }
                    moonSeg.Add((t, Math.Min(maxAlt, alt)));
                } else {
                    moonSeg = null;
                }
            }
            foreach (var seg in moonSegments) {
                if (seg.Count < 2) continue;
                var pts = new StringBuilder();
                foreach (var (t, alt) in seg)
                    pts.Append($"{X(t):F1},{Y(alt):F1} ");
                sb.AppendLine("<g><title>Moon Position</title>");
                sb.AppendLine($"<polyline points='{pts}' fill='none' stroke='transparent' stroke-width='12'/>");
                sb.AppendLine($"<polyline points='{pts}' fill='none' stroke='#c0c0c0' stroke-width='1.5' stroke-dasharray='5,4' opacity='0.45'/>");
                sb.AppendLine("</g>");
            }
            skipMoon:;

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

        // ── Tonight's Preview (via TS REST API) ──────────────────────────
        private static readonly string[] PreviewColors = {
            "#4e79a7", "#f28e2b", "#e15759", "#76b7b2", "#59a14f", "#edc948"
        };

        private string PreviewNotice(string message) {
            Warnings.Add($"Tonight's Preview: {message}");
            return $"<div class='target-section'><h2>Tonight's Preview</h2><p style='color:#888;font-style:italic;'>{message}</p></div>";
        }

        private string BuildNextNightPreviewSection(ReportData data) {
            if (!Settings.Default.ShowNextNightPreview) return "";

            var tsDb = new TargetSchedulerDatabase();
            if (!tsDb.IsAvailable)
                return PreviewNotice("Target Scheduler is not installed.");

            var (apiEnabled, apiPort) = tsDb.GetApiSettings(data.ActiveProfileId);
            if (!apiEnabled)
                return PreviewNotice("Target Scheduler API is not enabled. In Target Scheduler, go to Options → Profile Preferences → API Preferences → Enable API.");

            try {
                var baseUrl = $"http://localhost:{apiPort}/ts/v0";
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                // Step 1: Get active profile ID
                var profilesJson = TsApiClient.GetStringAsync($"{baseUrl}/profiles").Result;
                var profiles = JsonSerializer.Deserialize<List<TsProfileInfo>>(profilesJson, options);
                var active = profiles?.FirstOrDefault(p => p.Active);
                if (active == null)
                    return PreviewNotice("No active NINA profile found.");

                // Step 2: Compute tonight's sunset and use as the preview start time
                if (data.ObserverLatitude == 0 && data.ObserverLongitude == 0)
                    return PreviewNotice("Observer location not configured in NINA profile.");
                var tomorrow = DateTime.Today.AddDays(1);
                var (sunset, sunrise) = AltitudeCalculator.FindNightWindow(
                    data.ObserverLatitude, data.ObserverLongitude, tomorrow.AddHours(-6));
                var startTime = sunset;
                var previewUrl = $"{baseUrl}/profiles/{active.Id}/preview?startTime={startTime:o}";

                var previewJson = TsApiClient.GetStringAsync(previewUrl).Result;
                var entries = JsonSerializer.Deserialize<List<TsPreviewEntry>>(previewJson, options);
                if (entries == null || !entries.Any())
                    return PreviewNotice("Target Scheduler returned an empty preview — no targets scheduled for tonight.");

                // Filter to target blocks only (skip wait periods) for the summary
                var targets = entries.Where(e => !e.WaitPeriod && e.Name != null).ToList();
                if (!targets.Any())
                    return PreviewNotice("Target Scheduler returned an empty preview — no targets scheduled for tonight.");

                // Trim leading wait periods so the timeline starts at the first target block
                var firstTargetStart = targets.First().StartTime;
                entries = entries.Where(e => e.EndTime > firstTargetStart).ToList();

                // Timeline spans from first target start to last entry end
                var timelineStart = firstTargetStart;
                var timelineEnd   = entries.Last().EndTime;
                var totalSeconds  = (timelineEnd - timelineStart).TotalSeconds;
                if (totalSeconds <= 0) return "";

                // Assign colors to unique target names
                var uniqueTargets = targets.Select(t => t.Name).Distinct().ToList();
                var colorMap = new Dictionary<string, string>();
                for (int i = 0; i < uniqueTargets.Count; i++)
                    colorMap[uniqueTargets[i]] = PreviewColors[i % PreviewColors.Length];

                var sb = new StringBuilder();
                sb.AppendLine("<div class='target-section'>");
                var previewDate = targets.First().StartTime;
                sb.AppendLine($"<h2 style='display:inline;'>Tonight's Preview</h2>");
                sb.AppendLine($"<span style='color:#555;font-size:12px;font-style:italic;margin-left:12px;'>Generated by Target Scheduler — actual imaging may differ based on conditions</span>");
                sb.AppendLine($"<p style='color:#888;margin-top:8px;'>Planned schedule for {previewDate:MMMM d, yyyy} &mdash; {timelineStart:HH:mm} to {timelineEnd:HH:mm}</p>");

                // ── SVG Timeline ──
                const int svgWidth   = 760;
                const int trackH     = 24;
                const int topPad     = 10;
                const int leftPad    = 8;
                const int rightPad   = 8;
                const int barAreaW   = svgWidth - leftPad - rightPad;
                const int legendRowH = 20;

                double TimeToX(DateTime t) =>
                    leftPad + (t - timelineStart).TotalSeconds / totalSeconds * barAreaW;

                int rulerH    = 28;
                int legendTop = topPad + trackH + rulerH + 8;
                int legendH   = 18 + uniqueTargets.Count * legendRowH;
                int svgHeight = legendTop + legendH + 10;

                sb.AppendLine("<div class='timeline-container'>");
                sb.AppendLine($"<svg viewBox='0 0 {svgWidth} {svgHeight}' xmlns='http://www.w3.org/2000/svg' style='width:100%;font-family:Arial,sans-serif;font-size:11px;'>");

                // Background track
                sb.AppendLine($"<rect x='{leftPad}' y='{topPad}' width='{barAreaW}' height='{trackH}' rx='4' fill='#0f0f23' />");

                // Wait period hatching
                sb.AppendLine("<defs>");
                sb.AppendLine("  <pattern id='ns-preview-idle' patternUnits='userSpaceOnUse' width='8' height='8' patternTransform='rotate(45)'>");
                sb.AppendLine("    <rect width='8' height='8' fill='#0f0f23'/>");
                sb.AppendLine("    <line x1='0' y1='0' x2='0' y2='8' stroke='#2d2d5e' stroke-width='3'/>");
                sb.AppendLine("  </pattern>");
                sb.AppendLine("</defs>");

                foreach (var entry in entries.Where(e => e.WaitPeriod)) {
                    double x1 = TimeToX(entry.StartTime);
                    double x2 = TimeToX(entry.EndTime);
                    double w  = Math.Max(x2 - x1, 1);
                    sb.AppendLine($"<rect x='{x1:F1}' y='{topPad}' width='{w:F1}' height='{trackH}' fill='url(#ns-preview-idle)' />");
                }

                // Target blocks
                foreach (var entry in targets) {
                    double x1 = TimeToX(entry.StartTime);
                    double x2 = TimeToX(entry.EndTime);
                    double w  = Math.Max(x2 - x1, 2);
                    sb.AppendLine($"<rect x='{x1:F1}' y='{topPad}' width='{w:F1}' height='{trackH}' fill='{colorMap[entry.Name]}' opacity='0.85'/>");
                }

                // Ruler
                int rulerY     = topPad + trackH;
                int tickLabelY = rulerY + 20;
                sb.AppendLine($"<line x1='{leftPad}' y1='{rulerY}' x2='{svgWidth - rightPad}' y2='{rulerY}' stroke='#444' stroke-width='1'/>");

                double durationHours   = totalSeconds / 3600.0;
                int    tickIntervalMin = durationHours < 2 ? 15 : durationHours < 5 ? 30 : 60;
                var firstTick = new DateTime(timelineStart.Year, timelineStart.Month, timelineStart.Day, timelineStart.Hour, 0, 0);
                while (firstTick <= timelineStart) firstTick = firstTick.AddMinutes(tickIntervalMin);
                var tick = firstTick;
                while (tick < timelineEnd) {
                    double tx = TimeToX(tick);
                    if (tx - leftPad > 40 && (svgWidth - rightPad) - tx > 40) {
                        sb.AppendLine($"<line x1='{tx:F1}' y1='{rulerY}' x2='{tx:F1}' y2='{rulerY + 6}' stroke='#555' stroke-width='1'/>");
                        sb.AppendLine($"<text x='{tx:F1}' y='{tickLabelY}' fill='#888' text-anchor='middle'>{tick:HH:mm}</text>");
                    }
                    tick = tick.AddMinutes(tickIntervalMin);
                }
                sb.AppendLine($"<text x='{leftPad}' y='{tickLabelY}' fill='#888'>{timelineStart:HH:mm}</text>");
                sb.AppendLine($"<text x='{svgWidth - rightPad}' y='{tickLabelY}' fill='#888' text-anchor='end'>{timelineEnd:HH:mm}</text>");

                // Legend
                int ly = legendTop;
                sb.AppendLine($"<text x='{leftPad}' y='{ly + 12}' fill='#aaa' font-weight='bold'>Targets</text>");
                ly += 18;
                foreach (var name in uniqueTargets) {
                    sb.AppendLine($"<rect x='{leftPad}' y='{ly}' width='14' height='12' fill='{colorMap[name]}' rx='2'/>");
                    sb.AppendLine($"<text x='{leftPad + 18}' y='{ly + 10}' fill='#e0e0e0'>{name}</text>");
                    ly += legendRowH;
                }

                sb.AppendLine("</svg>");
                sb.AppendLine("</div>");

                // ── Per-target summary list ──
                sb.AppendLine("<table style='margin-top:12px;'>");
                sb.AppendLine("<tr><th>Target</th><th>Window</th><th>Images</th><th>Total Time</th></tr>");

                foreach (var target in targets) {
                    int totalFrames = target.ExposurePlan.Sum(e => e.Count);
                    double totalIntSec = target.ExposurePlan.Sum(e => e.Exposure * e.Count);
                    sb.AppendLine($"<tr>");
                    sb.AppendLine($"  <td>{target.Name}</td>");
                    sb.AppendLine($"  <td>{target.StartTime:HH:mm} - {target.EndTime:HH:mm}</td>");
                    sb.AppendLine($"  <td>{totalFrames}</td>");
                    sb.AppendLine($"  <td>{FormatDuration(totalIntSec)}</td>");
                    sb.AppendLine($"</tr>");
                }
                sb.AppendLine("</table>");

                // ── Expandable per-target filter details ──
                // Aggregate exposure plans across all timeline blocks for the same target,
                // then group by (filter, exposure length) — matches main report grouping logic.
                foreach (var targetGroup in targets.GroupBy(t => t.Name)) {
                    var allExposures = targetGroup.SelectMany(t => t.ExposurePlan).ToList();
                    if (!allExposures.Any()) continue;
                    var filterGroups = allExposures
                        .GroupBy(e => (e.FilterName, e.Exposure))
                        .OrderBy(g => FilterSortKey(g.Key.FilterName)).ThenBy(g => g.Key.FilterName).ThenBy(g => g.Key.Exposure);
                    sb.AppendLine("<details class='history-section'>");
                    sb.AppendLine($"<summary>{targetGroup.Key} - Filter Breakdown</summary>");
                    sb.AppendLine("<table style='margin-top:8px;width:auto;'>");
                    sb.AppendLine("<tr><th>Filter</th><th>Images</th><th>Exposure</th><th>Total Time</th></tr>");
                    foreach (var g in filterGroups) {
                        int totalCount = g.Sum(e => e.Count);
                        double intSec = g.Key.Exposure * totalCount;
                        sb.AppendLine($"<tr>");
                        sb.AppendLine($"  <td>{g.Key.FilterName}</td>");
                        sb.AppendLine($"  <td>{totalCount}</td>");
                        sb.AppendLine($"  <td>{g.Key.Exposure:F0}s</td>");
                        sb.AppendLine($"  <td>{FormatDuration(intSec)}</td>");
                        sb.AppendLine($"</tr>");
                    }
                    sb.AppendLine("</table>");
                    sb.AppendLine("</details>");
                }

                sb.AppendLine("</div>");
                return sb.ToString();
            } catch (Exception ex) {
                Logger.Warning($"NightSummary: Next night preview unavailable. {ex.Message}");
                var reason = ex.InnerException is TaskCanceledException
                    ? "Target Scheduler API did not respond in time — the server may not be running."
                    : $"Could not connect to Target Scheduler API (port {apiPort}). Ensure NINA and Target Scheduler are running.";
                return PreviewNotice(reason);
            }
        }

        private string BuildFooter() {
            var sb = new StringBuilder();
            sb.AppendLine("<p class='footnote'>CV (Coefficient of Variation) measures consistency as a percentage of the mean. Lower values indicate more stable conditions. Star count CV is calculated per target and filter type.</p>");
            sb.AppendLine("<p class='footnote'>Generated by Night Summary plugin for N.I.N.A.</p>");
            return sb.ToString();
        }

        private static string FormatDuration(double seconds) {
            var ts = TimeSpan.FromSeconds(seconds);
            if (ts.TotalSeconds < 60)
                return $"{(int)ts.TotalSeconds}s";
            if (ts.TotalMinutes < 60) {
                var m = (int)ts.TotalMinutes;
                var s = (int)(ts.TotalSeconds - m * 60);
                return s > 0 ? $"{m}m {s}s" : $"{m}m";
            } else {
                var h = (int)ts.TotalHours;
                var m = (int)(ts.TotalMinutes - h * 60);
                return m > 0 ? $"{h}h {m}m" : $"{h}h";
            }
        }

        private static string FormatIntegration(double seconds) {
            var ts = TimeSpan.FromSeconds(seconds);
            return ts.TotalHours >= 1 ? $"{ts.TotalHours:F1}h" : $"{ts.TotalMinutes:F0}m";
        }

        /// <summary>
        /// Returns the moon illumination fraction (0–100%) at the given local time.
        /// Also sets <paramref name="waxing"/> to true if the moon is brightening.
        /// Uses a mean-anomaly approximation accurate to ~1–2%.
        /// Reference new moon: 2000-01-06 18:14 UTC (JD 2451549.5).
        /// </summary>
        private static double MoonIllumination(DateTime localTime, out bool waxing) {
            const double synodicPeriod = 29.53058868;
            var referenceNewMoon = new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);
            var utc = localTime.Kind == DateTimeKind.Utc ? localTime : localTime.ToUniversalTime();
            var daysSinceNew = (utc - referenceNewMoon).TotalDays % synodicPeriod;
            if (daysSinceNew < 0) daysSinceNew += synodicPeriod;
            waxing = daysSinceNew < synodicPeriod / 2.0;
            var phaseAngle = daysSinceNew / synodicPeriod * 2.0 * Math.PI;
            return (1.0 - Math.Cos(phaseAngle)) / 2.0 * 100.0;
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
