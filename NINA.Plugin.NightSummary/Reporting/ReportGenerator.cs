using NINA.Core.Utility;
using NINA.Plugin.NightSummary.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Reporting {
    /// <summary>
    /// Generates an HTML report from session data.
    /// Each logical section is a separate private method so individual sections
    /// can be toggled on/off in a future release.
    /// </summary>
    public class ReportGenerator {

        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        private static readonly HttpClient SkyViewHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        private static readonly HttpClient TsApiClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

        /// <summary>
        /// Warnings collected during the most recent report generation.
        /// Cleared at the start of each GenerateHtmlReport call.
        /// </summary>
        public List<string> Warnings { get; } = new List<string>();

        // Counter incremented per EmitMetricChart() call to generate unique CSS IDs
        private int _chartIndex = 0;

        // SVG theme colors (set at the start of each report generation)
        private string svgBg, svgBorder, svgMuted, svgDim, svgAccent, svgChartBg, svgChartDark, svgMoonStroke, svgMoonOpacity, svgSunrise;

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

        // Lazily-loaded metric-chart.js renderer source (embedded resource)
        private static string? _metricChartJs;
        private static string MetricChartJs {
            get {
                if (_metricChartJs != null) return _metricChartJs;
                try {
                    using var stream = Assembly.GetExecutingAssembly()
                                               .GetManifestResourceStream("metric-chart.js");
                    if (stream == null) { _metricChartJs = ""; return _metricChartJs; }
                    using var reader = new System.IO.StreamReader(stream);
                    _metricChartJs = reader.ReadToEnd();
                } catch {
                    _metricChartJs = "";
                }
                return _metricChartJs;
            }
        }

        // Filter classification and sorting delegated to FilterHelper
        private static bool IsBroadband(string filter) => FilterHelper.IsBroadband(filter);
        private static bool IsNarrowband(string filter) => FilterHelper.IsNarrowband(filter);
        private static bool IsExcluded(string filter) => FilterHelper.IsExcluded(filter);
        private static int FilterSortKey(string filter) => FilterHelper.SortKey(filter);

        public async Task<string> GenerateHtmlReport(ReportData data) {
            Warnings.Clear();
            FilterHelper.ReloadOverrides();
            var sb = new StringBuilder();

            bool lightMode = SettingsManager.Instance.Current.ReportLightMode;

            // Set SVG theme colors (SVG attributes can't use CSS variables)
            // Altitude chart keeps dark background in both modes for better line visibility
            if (lightMode) {
                svgBg = "#f5f5f5"; svgBorder = "#c0c8d4"; svgMuted = "#666"; svgDim = "#888";
                svgAccent = "#2563b8"; svgChartBg = "#e8eef5"; svgChartDark = "#0f0f23";
                svgMoonStroke = "#7a8a9e"; svgMoonOpacity = "0.75"; svgSunrise = "#c07a00";
            } else {
                svgBg = "#1a1a2e"; svgBorder = "#2d2d5e"; svgMuted = "#888"; svgDim = "#555";
                svgAccent = "#7eb8f7"; svgChartBg = "#0d1117"; svgChartDark = "#0f0f23";
                svgMoonStroke = "#c0c0c0"; svgMoonOpacity = "0.45"; svgSunrise = "#f59e0b";
            }

            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><meta charset='UTF-8'><style>");

            // Theme colors via CSS custom properties
            if (lightMode) {
                sb.AppendLine(":root { --bg: #f5f5f5; --text: #1a1a2e; --accent: #2563b8; --accent-light: #3b7dd8; --accent-lighter: #5a9ae6; --surface: #e8ecf1; --border: #c0c8d4; --muted: #666; --dim: #888; --chart-bg: #e0e4ea; --chart-dark: #d0d4da; --bar-acquired: #8bb0d4; --warn-bg: #fff3cd; --warn-border: #d4a850; --warn-text: #856404; --warn-item: #6d5200; --skip-color: #cc3333; }");
            } else {
                sb.AppendLine(":root { --bg: #1a1a2e; --text: #e0e0e0; --accent: #7eb8f7; --accent-light: #a0c4ff; --accent-lighter: #c0d8ff; --surface: #16213e; --border: #2d2d5e; --muted: #888; --dim: #555; --chart-bg: #0d1117; --chart-dark: #0f0f23; --bar-acquired: #3a5a7a; --warn-bg: #3a2a00; --warn-border: #b8860b; --warn-text: #f0c040; --warn-item: #d4a850; --skip-color: #cc6666; }");
            }

            sb.AppendLine("body { font-family: Arial, sans-serif; max-width: 800px; margin: 0 auto; padding: 20px; background-color: var(--bg); color: var(--text); }");
            sb.AppendLine("h1 { color: var(--accent); border-bottom: 2px solid var(--accent); padding-bottom: 10px; }");
            sb.AppendLine("h2 { color: var(--accent-light); margin-top: 30px; }");
            sb.AppendLine("h3 { color: var(--accent-lighter); }");
            sb.AppendLine("table { width: 100%; border-collapse: collapse; margin-top: 10px; }");
            sb.AppendLine("th { background-color: var(--border); color: var(--accent); padding: 8px; text-align: left; }");
            sb.AppendLine("td { padding: 8px; border-bottom: 1px solid var(--border); }");
            sb.AppendLine("tr:nth-child(even) { background-color: var(--surface); }");
            sb.AppendLine(".stat-box { background-color: var(--surface); border: 1px solid var(--border); border-radius: 8px; padding: 15px; text-align: center; }");
            sb.AppendLine(".stat-value { font-size: 24px; color: var(--accent); font-weight: bold; }");
            sb.AppendLine(".stat-label { font-size: 12px; color: var(--muted); margin-top: 5px; }");
            sb.AppendLine("details.stat-breakdown > summary { list-style: none; cursor: pointer; display: block; }");
            sb.AppendLine("details.stat-breakdown > summary::-webkit-details-marker { display: none; }");
            sb.AppendLine("details.stat-breakdown .stat-value::after { content: ' \\25BC'; font-size: 14px; color: var(--accent); }");
            sb.AppendLine("details.stat-breakdown[open] .stat-value::after { content: ' \\25B2'; font-size: 14px; color: var(--accent); }");
            sb.AppendLine(".stat-breakdown-body { margin-top: 8px; font-size: 11px; text-align: left; border-top: 1px solid var(--border); padding-top: 6px; }");
            sb.AppendLine(".stat-breakdown-row { display: flex; justify-content: space-between; padding: 1px 2px; }");
            sb.AppendLine(".stat-breakdown-filter { color: var(--accent-light); }");
            sb.AppendLine(".star-count-table { width: auto; margin-top: 8px; }");
            sb.AppendLine(".footnote { color: var(--dim); font-size: 12px; margin-top: 40px; }");
            sb.AppendLine(".target-section { border-top: 1px solid var(--border); margin-top: 24px; padding-top: 16px; }");
            sb.AppendLine(".timeline-container { background-color: var(--surface); border: 1px solid var(--border); border-radius: 8px; padding: 16px; margin: 16px 0; }");
            sb.AppendLine(".ts-target-header { display: flex; gap: 16px; align-items: flex-start; margin-bottom: 12px; flex-wrap: wrap; }");
            sb.AppendLine(".ts-thumb-wrap { position: relative; width: 200px; height: 200px; flex-shrink: 0; }");
            sb.AppendLine(".ts-thumb-wrap img { width: 200px; height: 200px; border-radius: 6px; border: 1px solid var(--border); display: block; }");
            sb.AppendLine(".ts-thumb-wrap svg { position: absolute; top: 0; left: 0; border-radius: 6px; }");
            sb.AppendLine(".ts-livestack-row { display: flex; gap: 8px; flex-wrap: wrap; margin: 12px 0; }");
            sb.AppendLine(".ts-livestack-item { text-align: center; }");
            sb.AppendLine(".ts-livestack-img { border-radius: 6px; border: 1px solid var(--border); display: block; width: 100%; }");
            sb.AppendLine(".ts-livestack-label { font-size: 11px; color: var(--muted); margin-top: 4px; }");
            sb.AppendLine(".ts-livestack-composite { margin: 12px 0; text-align: center; }");
            sb.AppendLine(".ts-livestack-composite img { border-radius: 6px; border: 1px solid var(--border); display: block; max-width: 520px; height: auto; margin: 0 auto; }");
            sb.AppendLine(".ts-target-info { flex: 1; }");
            sb.AppendLine(".ts-coords { font-size: 12px; color: var(--muted); margin: 4px 0 12px; }");
            sb.AppendLine(".ts-filter-row { display: flex; align-items: center; gap: 8px; margin: 4px 0; }");
            sb.AppendLine(".ts-filter-name { width: 180px; min-width: 180px; max-width: 180px; font-size: 13px; color: var(--accent-light); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; flex-shrink: 0; }");
            sb.AppendLine(".ts-bar-track { flex: 1; height: 14px; background: var(--border); border-radius: 4px; position: relative; overflow: hidden; }");
            sb.AppendLine(".ts-bar-accepted { position: absolute; left: 0; top: 0; bottom: 0; background: var(--accent); }");
            sb.AppendLine(".ts-bar-acquired { position: absolute; top: 0; bottom: 0; background: var(--bar-acquired); }");
            sb.AppendLine(".ts-bar-label { font-size: 12px; color: var(--muted); white-space: nowrap; width: 150px; min-width: 150px; max-width: 150px; text-align: right; flex-shrink: 0; overflow: hidden; text-overflow: ellipsis; }");
            sb.AppendLine(".ts-cumulative { font-size: 12px; color: var(--muted); margin-top: 12px; }");
            sb.AppendLine("details.history-section { margin-top: 12px; }");
            sb.AppendLine("details.history-section > summary { cursor: pointer; color: var(--accent-light); font-size: 14px; font-weight: bold; list-style: none; }");
            sb.AppendLine("details.history-section > summary::-webkit-details-marker { display: none; }");
            sb.AppendLine("details.history-section > summary::before { content: '\\25B6\\00A0'; }");
            sb.AppendLine("details.history-section[open] > summary::before { content: '\\25BC\\00A0'; }");
            sb.AppendLine("details.equipment-section { margin-top: 4px; margin-bottom: 8px; }");
            sb.AppendLine("details.equipment-section > summary { cursor: pointer; color: var(--accent-light); font-size: 13px; font-weight: bold; list-style: none; }");
            sb.AppendLine("details.equipment-section > summary::-webkit-details-marker { display: none; }");
            sb.AppendLine("details.equipment-section > summary::before { content: '\\25B6\\00A0'; }");
            sb.AppendLine("details.equipment-section[open] > summary::before { content: '\\25BC\\00A0'; }");
            sb.AppendLine(".equipment-grid { display: grid; grid-template-columns: auto 1fr; gap: 2px 12px; margin-top: 6px; font-size: 13px; }");
            sb.AppendLine(".equipment-label { color: var(--muted); }");
            sb.AppendLine(".equipment-value { color: var(--text); }");
            sb.AppendLine("details.iq-section { margin-top: 12px; }");
            sb.AppendLine("details.iq-section > summary { cursor: pointer; color: var(--accent-light); font-size: 14px; font-weight: bold; list-style: none; }");
            sb.AppendLine("details.iq-section > summary::-webkit-details-marker { display: none; }");
            sb.AppendLine("details.iq-section > summary::before { content: '\\25B6\\00A0'; }");
            sb.AppendLine("details.iq-section[open] > summary::before { content: '\\25BC\\00A0'; }");
            sb.AppendLine("details.livestack-section { margin-top: 12px; }");
            sb.AppendLine("details.livestack-section > summary { cursor: pointer; color: var(--accent-light); font-size: 14px; font-weight: bold; list-style: none; }");
            sb.AppendLine("details.livestack-section > summary::-webkit-details-marker { display: none; }");
            sb.AppendLine("details.livestack-section > summary::before { content: '\\25B6\\00A0'; }");
            sb.AppendLine("details.livestack-section[open] > summary::before { content: '\\25BC\\00A0'; }");
            sb.AppendLine(".iq-table { width: 100%; margin-top: 8px; }");
            sb.AppendLine(".iq-row-grid { display: grid; grid-template-columns: 1fr 1fr 1fr 1fr 1fr; }");
            sb.AppendLine(".iq-header { background-color: var(--border); color: var(--accent); padding: 8px; text-align: left; font-weight: bold; }");
            sb.AppendLine(".iq-cell { padding: 8px; border-bottom: 1px solid var(--border); }");
            sb.AppendLine(".iq-row-even .iq-cell { background-color: var(--surface); }");
            sb.AppendLine("details.iq-row { margin: 0; }");
            sb.AppendLine("details.iq-row > summary { list-style: none; cursor: pointer; }");
            sb.AppendLine("details.iq-row > summary::-webkit-details-marker { display: none; }");
            sb.AppendLine(".iq-arrow::after { content: ' \\25B6'; font-size: 10px; color: var(--accent-light); }");
            sb.AppendLine("details.iq-row[open] .iq-arrow::after { content: ' \\25BC'; }");
            sb.AppendLine(".iq-expand { padding: 0 8px 8px; }");
            // Metric chart filter selector
            sb.AppendLine(".metric-chart-container { margin: 0 auto 16px; max-width: 800px; }");
            sb.AppendLine(".ns-chart-filter-bar { display: flex; flex-wrap: wrap; gap: 6px; justify-content: center; margin: 0 auto 8px; }");
            sb.AppendLine(".ns-chart-filter-btn { background: var(--surface); color: var(--muted); border: 1px solid var(--border); border-radius: 18px; padding: 5px 18px; font-size: 18px; font-family: inherit; font-weight: bold; cursor: pointer; transition: all 0.15s; max-width: 100%; overflow: hidden; text-overflow: ellipsis; white-space: nowrap;}");
            sb.AppendLine(".ns-chart-filter-btn:hover { border-color: var(--accent-light); color: var(--text); }");
            sb.AppendLine(".ns-chart-filter-btn.active { background: var(--accent); color: var(--bg); border-color: var(--accent); }");
            sb.AppendLine(".ns-chart-svg { width: 100%; }");
            sb.AppendLine("svg g:has(> title), svg circle:has(> title), svg line:has(> title), svg [data-tip] { cursor: pointer; }");
            sb.AppendLine("</style></head><body>");

            sb.Append(BuildHeader(data));
            const string warningsPlaceholder = "<!--WARNINGS_PLACEHOLDER-->";
            sb.AppendLine(warningsPlaceholder);

            int detailLevel = SettingsManager.Instance.Current.ReportDetailLevel;
            string detailsOpen = SettingsManager.Instance.Current.ExpandSectionsDefault ? " open" : "";

            if (!data.Images.Any()) {
                sb.AppendLine("<p><em>No images were recorded during this session.</em></p>");
                if (detailLevel >= 2) sb.Append(BuildNextNightPreviewSection(data));
                sb.Append(BuildFooter());
                // AppendChartRendererScript(sb); // JS renderer deactivated — CSS chip selector used instead (dead code, kept for v3 dashboard)
                sb.AppendLine("</body></html>");
                return sb.ToString();
            }

            if (detailLevel >= 1) sb.Append(BuildEventTimelineSection(data));
            sb.Append(BuildOverviewStatsSection(data, detailLevel));
            if (detailLevel >= 2 && SettingsManager.Instance.Current.ShowOverheadBreakdown) {
                Logger.Info($"NightSummary: Overhead section — TimingEvents={data.TimingEvents?.Count ?? -1}, detailLevel={detailLevel}, ShowOverheadBreakdown={SettingsManager.Instance.Current.ShowOverheadBreakdown}");
                sb.Append(BuildOverheadBreakdownSection(data, detailsOpen));
            } else {
                Logger.Info($"NightSummary: Overhead section SKIPPED — TimingEvents={data.TimingEvents?.Count ?? -1}, detailLevel={detailLevel}, ShowOverheadBreakdown={SettingsManager.Instance.Current.ShowOverheadBreakdown}");
            }
            sb.Append(await BuildTargetSection(data, detailLevel, detailsOpen));
            if (detailLevel >= 1) sb.Append(BuildImageQualitySection(data, detailLevel, detailsOpen));
            if (detailLevel >= 2) sb.Append(BuildNextNightPreviewSection(data));
            sb.Append(BuildFooter());

            // AppendChartRendererScript(sb); // JS renderer deactivated — CSS chip selector used instead (dead code, kept for v3 dashboard)
            sb.AppendLine("</body></html>");

            // Replace placeholder with warnings banner if any were collected during generation
            var html = sb.ToString();
            if (Warnings.Any()) {
                var warningHtml = new StringBuilder();
                warningHtml.AppendLine("<div style='background-color:var(--warn-bg); border:1px solid var(--warn-border); border-radius:8px; padding:12px 16px; margin:16px 0;'>");
                warningHtml.AppendLine("<p style='color:var(--warn-text); font-weight:bold; margin:0 0 8px;'>&#9888; Report generated with warnings:</p>");
                warningHtml.AppendLine("<ul style='margin:0; padding-left:20px; color:var(--warn-item);'>");
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
                sb.AppendLine("<div style='display:flex; align-items:center; gap:14px; border-bottom:2px solid var(--accent); padding-bottom:10px; margin-bottom:8px;'>");
                sb.AppendLine($"  <img src='{icon}' alt='Night Summary' style='width:48px; height:48px; border-radius:6px; flex-shrink:0;' />");
                sb.AppendLine("  <h1 style='margin:0; border:none; padding:0;'>Night Summary Report</h1>");
                sb.AppendLine("</div>");
            } else {
                sb.AppendLine("<h1>Night Summary Report</h1>");
            }
            sb.AppendLine($"<p><strong>Session Date:</strong> {data.Session.SessionStart:yyyy-MM-dd}</p>");
            var sessionEnd = data.Session.SessionEnd > data.Session.SessionStart ? data.Session.SessionEnd : DateTime.Now;
            var isActive = data.Session.SessionEnd <= data.Session.SessionStart;
            sb.AppendLine($"<p><strong>Session Start:</strong> {data.Session.SessionStart:HH:mm:ss} &nbsp;&nbsp; <strong>Session End:</strong> {(isActive ? "In Progress" : sessionEnd.ToString("HH:mm:ss"))}</p>");
            sb.AppendLine($"<p><strong>Duration:</strong> {(sessionEnd - data.Session.SessionStart).TotalHours:F1} hours{(isActive ? " (so far)" : "")}</p>");
            sb.AppendLine($"<p><strong>Profile:</strong> {data.Session.ProfileName}</p>");

            // Equipment profile section (collapsed by default)
            if (SettingsManager.Instance.Current.ShowEquipmentProfile && data.Equipment != null && data.Equipment.Count > 0) {
                sb.AppendLine("<details class='equipment-section' open>");
                sb.AppendLine("<summary>Equipment</summary>");
                sb.AppendLine("<div class='equipment-grid'>");
                foreach (var kvp in data.Equipment) {
                    var safeLabel = System.Web.HttpUtility.HtmlEncode(kvp.Key);
                    var safeValue = System.Web.HttpUtility.HtmlEncode(kvp.Value);
                    sb.AppendLine($"<span class='equipment-label'>{safeLabel}</span><span class='equipment-value'>{safeValue}</span>");
                }
                sb.AppendLine("</div>");
                sb.AppendLine("</details>");
            }

            return sb.ToString();
        }

        private string BuildOverviewStatsSection(ReportData data, int detailLevel) {
            var sb = new StringBuilder();
            var totalExposureSec = data.Images.Sum(i => i.ExposureDuration);
            var targetCount      = data.Images.Select(i => i.TargetName).Distinct(StringComparer.OrdinalIgnoreCase).Count();

            // Per-filter stats for expandable breakdown
            var filterStats = data.Images
                .GroupBy(i => string.IsNullOrEmpty(i.Filter) ? "—" : i.Filter)
                .OrderBy(g => FilterSortKey(g.Key)).ThenBy(g => g.Key)
                .Select(g => (filter: g.Key, count: g.Count(), expSec: g.Sum(i => i.ExposureDuration)))
                .ToList();
            var imageBreakdown = new StringBuilder("<div class='stat-breakdown-body'>");
            var expBreakdown   = new StringBuilder("<div class='stat-breakdown-body'>");
            foreach (var (filter, count, expSec) in filterStats) {
                var safeFilter = System.Web.HttpUtility.HtmlEncode(filter);
                imageBreakdown.Append($"<div class='stat-breakdown-row'><span class='stat-breakdown-filter'>{safeFilter}</span><span>{count}</span></div>");
                expBreakdown.Append($"<div class='stat-breakdown-row'><span class='stat-breakdown-filter'>{safeFilter}</span><span>{FormatDuration(expSec)}</span></div>");
            }
            imageBreakdown.Append("</div>");
            expBreakdown.Append("</div>");

            var yield = YieldCalculator.Calculate(data.Images, data.Events, data.Session.SessionStart, data.Session.SessionEnd);
            var yieldPct        = yield.YieldPct;
            var hasSafetyMonitor = yield.HasSafetyMonitor;

            // Avg HFR and Avg Guiding RMS (session-wide)
            var hfrImages     = data.Images.Where(i => i.HFR > 0).ToList();
            var guidingImages = data.Images.Where(i => i.GuidingRMSTotal > 0).ToList();

            var fwhmImages = data.Images.Where(i => i.FWHM > 0).ToList();

            // Column count: Snapshot=3 (one row), Standard=5 (one row), Full=4 (two rows of 4)
            int gridCols = detailLevel == 0 ? 3 : detailLevel == 1 ? 5 : 4;

            sb.AppendLine("<h2>Session Overview</h2>");
            sb.AppendLine($"<div style='display:grid; grid-template-columns:repeat({gridCols},1fr); gap:10px; margin:10px 0;'>");
            var rejectedCount = data.Images.Count(i => !i.Accepted);
            var qualityNotes = new System.Text.StringBuilder();
            if (data.SkippedExposures > 0)
                qualityNotes.Append($"<div style='font-size:12px; font-weight:bold; color:var(--skip-color); margin-bottom:2px;'>{data.SkippedExposures} aborted</div>");
            if (rejectedCount > 0)
                qualityNotes.Append($"<div style='font-size:12px; font-weight:bold; color:var(--skip-color); margin-bottom:2px;'>{rejectedCount} rejected</div>");
            var imageBreakdownWithNotes = qualityNotes.Length > 0
                ? $"<div style='margin-bottom:8px;'>{qualityNotes}</div>{imageBreakdown}"
                : imageBreakdown.ToString();
            sb.AppendLine($"<div class='stat-box'><details class='stat-breakdown'><summary><div class='stat-value'>{data.Images.Count}</div><div class='stat-label'>Total Images</div></summary>{imageBreakdownWithNotes}</details></div>");
            sb.AppendLine($"<div class='stat-box'><details class='stat-breakdown'><summary><div class='stat-value'>{TimeSpan.FromSeconds(totalExposureSec).TotalHours:F1}h</div><div class='stat-label'>Total Exposure</div></summary>{expBreakdown}</details></div>");
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
                sb.AppendLine("<p style='font-size:11px; color:var(--muted); margin-top:4px;'>* Yield calculated without cloud exclusion — no safety monitor events recorded</p>");
            return sb.ToString();
        }

        private string BuildOverheadBreakdownSection(ReportData data, string detailsOpen = "") {
            var timingEvents = data.TimingEvents;
            if (timingEvents == null || !timingEvents.Any()) return "";

            // Exclude Exposure events (useful imaging time) and zero-duration events.
            var overheadEvents = timingEvents.Where(e => e.EventType != "Exposure" && e.DurationSeconds > 0).ToList();
            if (!overheadEvents.Any()) return "";

            // Compute the overhead window from all non-aborted events (AbortedExposure
            // end times are unreliable — orphaned aborts default to sessionEnd).
            var totalIntegrationSec = data.Images.Sum(i => i.ExposureDuration);
            var allEvents = timingEvents.Where(e => e.DurationSeconds > 0 && e.EventType != "AbortedExposure").ToList();
            var windowStart = allEvents.Min(e => e.StartTime);
            var windowEnd = allEvents.Max(e => e.EndTime);
            var windowSec = (windowEnd - windowStart).TotalSeconds;

            // Exclude roof-closed (unsafe) periods — imaging isn't possible while
            // the roof is closed, so this time is neither overhead nor integration.
            // Extend roof-closed intervals backwards to cover any aborted exposures
            // that were interrupted by the unsafe trigger (weather-lost time, not overhead).
            var roofIntervals = RoofClosedHelper.GetIntervals(data.Events, windowStart, windowEnd);
            roofIntervals = RoofClosedHelper.ExtendForAbortedExposures(roofIntervals, timingEvents);
            var roofClosedSec = RoofClosedHelper.TotalSeconds(roofIntervals);
            var effectiveWindowSec = windowSec - roofClosedSec;
            var impliedOverheadSec = effectiveWindowSec - totalIntegrationSec;

            // Filter out overhead events within roof-closed periods:
            // - Regular events: excluded if entirely within a closed interval
            // - AbortedExposure: excluded if start time is within a closed interval
            //   (end time is unreliable for orphaned aborts that default to sessionEnd)
            var effectiveOverheadEvents = roofIntervals.Count > 0
                ? overheadEvents.Where(e => {
                    if (e.EventType == "AbortedExposure")
                        return !roofIntervals.Any(c => e.StartTime >= c.start && e.StartTime <= c.end);
                    return !RoofClosedHelper.IsEntirelyWithinClosed(e.StartTime, e.EndTime, roofIntervals);
                }).ToList()
                : overheadEvents;

            var sb = new StringBuilder();
            sb.AppendLine("<details class='iq-section' open>");
            sb.AppendLine("<summary>Yield and Imaging Overhead Analysis</summary>");

            // Group by event type, sum durations, filter out negligible categories (< 1s total)
            var groups = effectiveOverheadEvents
                .GroupBy(e => e.EventType)
                .Select(g => new {
                    Type = g.Key,
                    Count = g.Count(),
                    TotalSeconds = g.Sum(e => e.DurationSeconds),
                    AvgSeconds = g.Average(e => e.DurationSeconds)
                })
                .Where(g => g.TotalSeconds >= 1.0)
                .OrderByDescending(g => g.TotalSeconds)
                .ToList();

            var totalOverheadSec = groups.Sum(g => g.TotalSeconds);

            // Merge overhead intervals to compute wall-clock overhead, deduplicating
            // any events that overlap with each other in time.
            var mergedOverheadSec = MergeOverheadIntervals(effectiveOverheadEvents);
            var coveragePct = impliedOverheadSec > 0
                ? Math.Min(mergedOverheadSec / impliedOverheadSec * 100.0, 100.0) : 0;
            var unaccountedSec = Math.Max(0, impliedOverheadSec - mergedOverheadSec);

            Logger.Info($"NightSummary: Overhead — window={windowSec:F0}s, integration={totalIntegrationSec:F0}s, " +
                $"roofClosed={roofClosedSec:F0}s, effective={effectiveWindowSec:F0}s, " +
                $"implied={impliedOverheadSec:F0}s, merged={mergedOverheadSec:F0}s, " +
                $"coverage={coveragePct:F1}%, unaccounted={unaccountedSec:F0}s");

            // Summary stat boxes
            sb.AppendLine("<div style='display:grid; grid-template-columns:repeat(3,1fr); gap:10px; margin:10px 0;'>");
            var infoIcon = "<span style='cursor:help; opacity:0.5; margin-left:4px; font-size:12px;'>&#9432;</span>";
            sb.AppendLine($"<div class='stat-box'><div class='stat-value'>{FormatDuration(mergedOverheadSec)}</div><div class='stat-label'>Total Overhead <span title='Wall-clock time spent on non-imaging tasks. Overlapping operations (e.g. image saves during the next exposure) are counted once.'>{infoIcon}</span></div></div>");
            sb.AppendLine($"<div class='stat-box'><div class='stat-value'>{coveragePct:F1}%</div><div class='stat-label'>Overhead Accounted <span title='Percentage of implied overhead (imaging window minus exposure time) accounted for by parsed log events.'>{infoIcon}</span></div></div>");
            if (unaccountedSec > 10)
                sb.AppendLine($"<div class='stat-box'><div class='stat-value'>{FormatDuration(unaccountedSec)}</div><div class='stat-label'>Unaccounted <span title='Time not attributed to any parsed event. Includes NINA internal processing between sequence items (trigger evaluations, scheduler planning, plugin hooks).'>{infoIcon}</span></div></div>");
            else
                sb.AppendLine($"<div class='stat-box'><div class='stat-value'>{groups.Count}</div><div class='stat-label'>Categories</div></div>");
            sb.AppendLine("</div>");

            // Horizontal stacked bar chart
            if (totalOverheadSec > 0) {
                var barColors = new Dictionary<string, string> {
                    ["CameraDownload"] = "#4a9eff",
                    ["FilterChange"]   = "#f59e0b",
                    ["Dither"]         = "#10b981",
                    ["TempCompFocus"]  = "#8b5cf6",
                    ["Autofocus"]      = "#ef4444",
                    ["PlateSolve"]     = "#06b6d4",
                    ["ImageSave"]      = "#f97316",
                    ["Centering"]      = "#6366f1",
                    ["MeridianFlip"]   = "#14b8a6",
                    ["Slew"]           = "#a855f7",
                    ["DomeSync"]       = "#2dd4bf",
                    ["DomeOps"]        = "#0d9488",
                    ["FlatPanel"]      = "#fbbf24",
                    ["CameraTemp"]     = "#60a5fa",
                    ["MountOps"]       = "#c084fc",
                    ["Guiding"]        = "#34d399",
                    ["SafetyWait"]     = "#f472b6",
                    ["FocuserMove"]    = "#a78bfa",
                    ["Rotator"]        = "#818cf8",
                    ["Switch"]         = "#94a3b8",
                    ["AbortedExposure"]= "#fb7185"
                };

                sb.AppendLine("<div style='display:flex; height:24px; border-radius:6px; overflow:hidden; margin:8px 0;'>");
                foreach (var g in groups) {
                    var pct = g.TotalSeconds / totalOverheadSec * 100.0;
                    if (pct < 0.5) continue;
                    var color = barColors.TryGetValue(g.Type, out var c) ? c : "#888";
                    var label = FormatEventTypeName(g.Type);
                    // Only show text label if the block is wide enough (~7px per character at 11px font)
                    var minPctForLabel = label.Length * 0.9;
                    var showLabel = pct >= minPctForLabel;
                    sb.AppendLine($"<div style='width:{pct:F1}%; background:{color}; display:flex; align-items:center; justify-content:center; font-size:11px; color:#fff; white-space:nowrap; overflow:hidden;' title='{label}: {FormatDuration(g.TotalSeconds)} ({pct:F1}%)'>{(showLabel ? label : "")}</div>");
                }
                sb.AppendLine("</div>");
            }

            // Detail table
            sb.AppendLine("<p style='font-size:11px; color:var(--dim); margin:8px 0 0;'>Category totals may exceed the overall overhead because some operations run concurrently.</p>");
            sb.AppendLine("<table style='width:100%; border-collapse:collapse; margin-top:8px; font-size:13px;'>");
            sb.AppendLine("<tr style='border-bottom:2px solid var(--border);'>");
            sb.AppendLine("<th style='text-align:left; padding:6px 8px; color:var(--accent);'>Category</th>");
            sb.AppendLine("<th style='text-align:right; padding:6px 8px; color:var(--accent);'>Count</th>");
            sb.AppendLine("<th style='text-align:right; padding:6px 8px; color:var(--accent);'>Total</th>");
            sb.AppendLine("<th style='text-align:right; padding:6px 8px; color:var(--accent);'>Avg</th>");
            sb.AppendLine("<th style='text-align:right; padding:6px 8px; color:var(--accent);'>% of Overhead</th>");
            sb.AppendLine("</tr>");

            bool even = false;
            foreach (var g in groups) {
                var pct = totalOverheadSec > 0 ? g.TotalSeconds / totalOverheadSec * 100.0 : 0;
                var bgStyle = even ? " background-color:var(--surface);" : "";
                sb.AppendLine($"<tr style='border-bottom:1px solid var(--border);{bgStyle}'>");
                sb.AppendLine($"<td style='padding:6px 8px;'>{FormatEventTypeName(g.Type)}</td>");
                sb.AppendLine($"<td style='text-align:right; padding:6px 8px;'>{g.Count}</td>");
                sb.AppendLine($"<td style='text-align:right; padding:6px 8px;'>{FormatDuration(g.TotalSeconds)}</td>");
                sb.AppendLine($"<td style='text-align:right; padding:6px 8px;'>{g.AvgSeconds:F1}s</td>");
                sb.AppendLine($"<td style='text-align:right; padding:6px 8px;'>{pct:F1}%</td>");
                sb.AppendLine("</tr>");
                even = !even;
            }
            sb.AppendLine("</table>");
            sb.AppendLine("</details>");

            return sb.ToString();
        }

        /// <summary>
        /// Merges overlapping time intervals to compute actual wall-clock overhead seconds.
        /// Many overhead events run concurrently (e.g. ImageSave during next exposure,
        /// CameraDownload overlapping with other operations), so naively summing durations
        /// double-counts shared time. This returns the true elapsed overhead time.
        /// </summary>
        internal static double MergeOverheadIntervals(List<TimingEvent> events) {
            if (events == null || events.Count == 0) return 0;

            var intervals = events
                .Where(e => e.StartTime != DateTime.MinValue && e.EndTime != DateTime.MinValue && e.EndTime > e.StartTime)
                .OrderBy(e => e.StartTime)
                .ToList();

            if (intervals.Count == 0) return 0;

            double totalSeconds = 0;
            var currentStart = intervals[0].StartTime;
            var currentEnd = intervals[0].EndTime;

            for (int i = 1; i < intervals.Count; i++) {
                if (intervals[i].StartTime <= currentEnd) {
                    // Overlapping — extend the current interval
                    if (intervals[i].EndTime > currentEnd)
                        currentEnd = intervals[i].EndTime;
                } else {
                    // Gap — flush the current interval and start a new one
                    totalSeconds += (currentEnd - currentStart).TotalSeconds;
                    currentStart = intervals[i].StartTime;
                    currentEnd = intervals[i].EndTime;
                }
            }
            // Flush the last interval
            totalSeconds += (currentEnd - currentStart).TotalSeconds;

            return totalSeconds;
        }

        private static string FormatEventTypeName(string eventType) => eventType switch {
            "CameraDownload" => "Camera Download",
            "FilterChange"   => "Filter Change",
            "TempCompFocus"  => "Temp Comp Focus",
            "PlateSolve"     => "Plate Solve",
            "ImageSave"      => "Image Save",
            "MeridianFlip"   => "Meridian Flip",
            "DomeSync"       => "Dome Sync",
            "DomeOps"        => "Dome",
            "FlatPanel"      => "Flat Panel",
            "CameraTemp"     => "Camera Temp",
            "MountOps"       => "Mount",
            "SafetyWait"     => "Safety Wait",
            "FocuserMove"    => "Focuser Move",
            "AbortedExposure"=> "Skipped Exposure",
            _                => eventType
        };

        private async Task<string> BuildTargetSection(ReportData data, int detailLevel, string detailsOpen = "") {
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

            // ── Pre-fetch all sky thumbnails in parallel ──────────────────────
            var thumbResults = new Dictionary<string, (string imgSrc, bool usedFallback)>();
            if (SettingsManager.Instance.Current.ShowSkyThumbnails) {
                var thumbTasks = new List<(string targetName, double raDeg, double decD, Task<(string imgSrc, bool usedFallback)> task)>();

                foreach (var target in targets) {
                    var tsT = data.TsData?.FirstOrDefault(t =>
                        string.Equals(t.TargetName, target.Key, StringComparison.OrdinalIgnoreCase));
                    double ra = 0, dec = 0;
                    if (tsT != null && (tsT.RA != 0 || tsT.Dec != 0)) { ra = tsT.RA; dec = tsT.Dec; }
                    else { var ci = target.FirstOrDefault(i => i.RaHours != 0 || i.DecDegrees != 0); if (ci != null) { ra = ci.RaHours; dec = ci.DecDegrees; } }

                    if (ra == 0 && dec == 0) continue;

                    var raDeg = ra * 15.0;
                    thumbTasks.Add((target.Key, raDeg, dec, FetchThumbnailAsync(target.Key, raDeg, dec, fetchPx, thumbFov)));
                }

                if (thumbTasks.Any()) {
                    Logger.Info($"NightSummary: Fetching {thumbTasks.Count} sky thumbnail(s) in parallel...");
                    await Task.WhenAll(thumbTasks.Select(t => t.task));
                    bool anyFallback = false;
                    foreach (var t in thumbTasks) {
                        var result = t.task.Result;
                        thumbResults[t.targetName] = result;
                        if (result.usedFallback) anyFallback = true;
                    }
                    if (anyFallback) {
                        Warnings.Add("Sky thumbnails loaded from fallback survey (NASA SkyView DSS2 Red) — images are monochrome because the primary color service (CDS) is unavailable");
                    }
                }
            }

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
                // Sky position angle: prefer TS data, fall back to plate solve PA from images
                double rotation = (tsTarget != null && tsTarget.Rotation != 0) ? tsTarget.Rotation
                    : target.Where(i => i.PositionAngle.HasValue && i.PositionAngle.Value != 0)
                            .Select(i => i.PositionAngle.Value).DefaultIfEmpty(0).Average();

                string h3Subtitle;
                if (raH != 0 || decD != 0) {
                    var sessMid    = targetImgStart.AddMinutes((targetImgEnd - targetImgStart).TotalMinutes / 2);
                    var (moonRa, moonDec) = AltitudeCalculator.GetMoonPosition(sessMid.ToUniversalTime());
                    double moonSep = AltitudeCalculator.AngularSeparation(raH, decD, moonRa, moonDec);
                    var rotPart = rotation != 0 ? $" &nbsp;·&nbsp; &#x21BB; {rotation:F0}&#176;" : "";
                    h3Subtitle = $" <span style='font-weight:normal; font-size:12px; color:var(--muted);'>" +
                                 $"— {timePart} &nbsp;·&nbsp; R.A. {FormatRA(raH)} &nbsp;·&nbsp; Dec. {FormatDec(decD)}{rotPart} &nbsp;·&nbsp; &#127769; &#8596; {moonSep:F0}&#176;" +
                                 $"</span>";
                } else {
                    h3Subtitle = $" <span style='font-weight:normal; font-size:12px; color:var(--muted);'>— {timePart}</span>";
                }

                sb.AppendLine("<div class='target-section'>");
                sb.AppendLine($"<h3>{target.Key}{h3Subtitle}</h3>");

                bool showThumb         = (raH != 0 || decD != 0) && SettingsManager.Instance.Current.ShowSkyThumbnails;
                bool showSideBySideChart = (raH != 0 || decD != 0) && detailLevel >= 1 && SettingsManager.Instance.Current.ShowAltitudeChart;

                // Build thumbnail HTML from pre-fetched results
                string thumbHtml = "";
                if (showThumb && thumbResults.TryGetValue(target.Key, out var thumbResult)) {
                    var tSb = new StringBuilder();
                    var svgAngle = -rotation;
                    tSb.AppendLine($"<div class='ts-thumb-wrap'>");
                    tSb.AppendLine($"  <img src='{thumbResult.imgSrc}' alt='{target.Key}' />");
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
                        double minAlt = SettingsManager.Instance.Current.ShowMinAltitude ? (tsTarget?.MinimumAltitude ?? 0) : 0;
                        var altChart = BuildAltitudeChart(raH, decD, data.ObserverLatitude, data.ObserverLongitude,
                                                          targetImgStart, targetImgEnd, width: 500,
                                                          minimumAltitude: minAlt);
                        if (!string.IsNullOrEmpty(altChart))
                            sb.Append($"<div style='flex:1; min-width:0; margin-top:-20px;'>{altChart}</div>");
                    }
                    sb.AppendLine("</div>"); // ts-target-header
                }

                // Live Stack images
                if (SettingsManager.Instance.Current.ShowLiveStackImages && data.LiveStackImages.Count > 0) {
                    var targetImages = data.LiveStackImages
                        .Where(i => i.Target.Equals(target.Key, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (targetImages.Count > 0) {
                        Logger.Info($"NightSummary: Rendering {targetImages.Count} live stack image(s) for target '{target.Key}'");
                        // Build filter → total integration lookup from session image records
                        var filterIntegration = target
                            .GroupBy(i => i.Filter, StringComparer.OrdinalIgnoreCase)
                            .ToDictionary(g => g.Key, g => g.Sum(i => i.ExposureDuration), StringComparer.OrdinalIgnoreCase);
                        sb.AppendLine("<details class='livestack-section' open>");
                        sb.AppendLine($"<summary>Live Stack ({targetImages.Count} {(targetImages.Count == 1 ? "image" : "images")})</summary>");
                        sb.Append(BuildLiveStackRow(targetImages, filterIntegration));
                        sb.AppendLine("</details>");
                    }
                } else if (data.LiveStackImages == null || data.LiveStackImages.Count == 0) {
                    Logger.Info($"NightSummary: No live stack images for target '{target.Key}' — " +
                        $"ShowLiveStackImages={SettingsManager.Instance.Current.ShowLiveStackImages}, " +
                        $"totalImages={data.LiveStackImages?.Count ?? 0}");
                } else {
                    Logger.Info($"NightSummary: No live stack images matched target '{target.Key}' — " +
                        $"available targets: {string.Join(", ", data.LiveStackImages.Select(i => i.Target).Distinct())}");
                }

                // Session filter table
                bool hasRejections = target.Any(i => !i.Accepted);
                sb.AppendLine("<table>");
                sb.AppendLine(hasRejections
                    ? "<tr><th>Filter</th><th>Images</th><th>Rejected</th><th>Exposure</th><th>Total Time</th></tr>"
                    : "<tr><th>Filter</th><th>Images</th><th>Exposure</th><th>Total Time</th></tr>");
                var filterGroups = target
                    .GroupBy(i => (i.Filter, i.ExposureDuration))
                    .OrderBy(g => FilterSortKey(g.Key.Filter)).ThenBy(g => g.Key.Filter).ThenBy(g => g.Key.ExposureDuration);
                foreach (var filterGroup in filterGroups) {
                    var totalTime     = TimeSpan.FromSeconds(filterGroup.Sum(i => i.ExposureDuration));
                    var rejectedCount = filterGroup.Count(i => !i.Accepted);
                    if (hasRejections)
                        sb.AppendLine($"<tr><td>{filterGroup.Key.Filter}</td><td>{filterGroup.Count()}</td><td>{(rejectedCount > 0 ? rejectedCount.ToString() : "—")}</td><td>{filterGroup.Key.ExposureDuration:F0}s</td><td>{FormatDuration(totalTime.TotalSeconds)}</td></tr>");
                    else
                        sb.AppendLine($"<tr><td>{filterGroup.Key.Filter}</td><td>{filterGroup.Count()}</td><td>{filterGroup.Key.ExposureDuration:F0}s</td><td>{FormatDuration(totalTime.TotalSeconds)}</td></tr>");
                }
                var targetTotal         = TimeSpan.FromSeconds(target.Sum(i => i.ExposureDuration));
                var targetRejectedTotal = target.Count(i => !i.Accepted);
                if (hasRejections)
                    sb.AppendLine($"<tr><td><strong>Total</strong></td><td><strong>{target.Count()}</strong></td><td><strong>{targetRejectedTotal}</strong></td><td></td><td><strong>{FormatDuration(targetTotal.TotalSeconds)}</strong></td></tr>");
                else
                    sb.AppendLine($"<tr><td><strong>Total</strong></td><td><strong>{target.Count()}</strong></td><td></td><td><strong>{FormatDuration(targetTotal.TotalSeconds)}</strong></td></tr>");
                sb.AppendLine("</table>");

                // TS rejection reason breakdown (shown only when TS-graded rejections exist)
                var tsRejections = target
                    .Where(i => !i.Accepted && !string.IsNullOrEmpty(i.RejectReason))
                    .GroupBy(i => i.RejectReason)
                    .OrderByDescending(g => g.Count())
                    .ToList();
                if (tsRejections.Any()) {
                    sb.AppendLine("<p style='margin:8px 0 4px; font-size:12px; color:var(--muted);'><strong>Rejection reasons (Target Scheduler)</strong></p>");
                    sb.AppendLine("<table class='star-count-table'>");
                    sb.AppendLine("<tr><th>Reason</th><th>Count</th></tr>");
                    foreach (var g in tsRejections)
                        sb.AppendLine($"<tr><td>{System.Web.HttpUtility.HtmlEncode(g.Key)}</td><td>{g.Count()}</td></tr>");
                    sb.AppendLine("</table>");
                }

                if (detailLevel >= 1 && SettingsManager.Instance.Current.ShowStarCountCV) {
                    // Star count CV
                    var broadbandImages  = target.Where(i => IsBroadband(i.Filter)  && i.StarCount > 0).ToList();
                    var narrowbandImages = target.Where(i => IsNarrowband(i.Filter) && i.StarCount > 0).ToList();
                    string broadbandCV  = broadbandImages.Count  >= 2 ? $"{CV(broadbandImages.Select(i  => (double)i.StarCount).ToList()):F0}%" : "—";
                    string narrowbandCV = narrowbandImages.Count >= 2 ? $"{CV(narrowbandImages.Select(i => (double)i.StarCount).ToList()):F0}%" : "—";
                    var cvTooltip = "CV (Coefficient of Variation) measures consistency as a percentage of the mean. Lower values indicate more stable conditions. Star count CV is calculated per target and filter type.";
                    sb.AppendLine($"<div title='{cvTooltip}' style='cursor:help;'>");
                    sb.AppendLine("<p style='margin: 12px 0 4px; font-size: 13px; color: var(--accent-light);'><strong>Star Count Consistency</strong></p>");
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
                        sb.AppendLine($"<p style='font-size:11px; color:var(--warn-border); margin-top:6px;'>&#9888; Filter{(unrecognizedFilters.Count == 1 ? "" : "s")} not recognized and excluded from CV calculation: {filterList}. Filters are classified by first letter — broadband (L, R, G, B) and narrowband (H, S, O). You can manually classify filters in Night Summary Options → Filter Classification.</p>");
                        Warnings.Add($"Unrecognized filter{(unrecognizedFilters.Count == 1 ? "" : "s")} excluded from CV calculation: {filterListPlain}");
                    }
                }

                // Per-target image quality (collapsible) — only for multi-target sessions
                if (detailLevel >= 1 && multiTarget && SettingsManager.Instance.Current.ShowPerTargetIQ) {
                    var targetList = target.ToList();
                    bool hasData = targetList.Any(i => i.HFR > 0 || i.FWHM > 0 || i.Eccentricity > 0 || i.GuidingRMSTotal > 0);
                    if (hasData) {
                        sb.AppendLine($"<details class='iq-section'{detailsOpen}>");
                        sb.AppendLine("<summary>Image Quality</summary>");
                        sb.AppendLine("<div class='iq-table'>");
                        sb.AppendLine("<div class='iq-row-grid'><div class='iq-header'>Metric</div><div class='iq-header'>Min</div><div class='iq-header'>Max</div><div class='iq-header'>Mean</div><div class='iq-header'>CV</div></div>");
                        AppendIqRows(sb, targetList, detailsOpen);
                        sb.AppendLine("</div>");
                        sb.AppendLine("</details>");
                    }
                }

                // Session history (collapsible)
                if (detailLevel >= 2 && SettingsManager.Instance.Current.ShowSessionHistory) {
                    List<TargetSessionHistory> history = null;
                    data.SessionHistory?.TryGetValue(target.Key, out history);
                    if (history != null && history.Any()) {
                        var label = $"Session History ({history.Count} previous session{(history.Count == 1 ? "" : "s")})";
                        sb.AppendLine($"<details class='history-section'{detailsOpen}>");
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

                if (tsTarget == null && detailLevel >= 1 && SettingsManager.Instance.Current.ShowTSProgressBars && TargetSchedulerDatabase.IsPluginInstalled) {
                    if (data.TsData != null && data.TsData.Count > 0) {
                        // TS is installed but this specific target wasn't found in it
                        Warnings.Add($"Target Scheduler progress bars unavailable for {target.Key} — target not found in Target Scheduler");
                    }
                    // If TS isn't installed at all, silently skip — the Options UI already shows it's unavailable
                }
                if (tsTarget != null && detailLevel >= 1 && SettingsManager.Instance.Current.ShowTSProgressBars && TargetSchedulerDatabase.IsPluginInstalled) {
                    // TS progress bars — one per exposure plan row (template + filter)
                    sb.AppendLine("<p style='margin: 12px 0 4px; font-size: 13px; color: var(--accent-light);'><strong>Target Scheduler Progress</strong></p>");
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

        private static string BuildLiveStackRow(List<Session.LiveStackImage> images, Dictionary<string, double> filterIntegration = null) {
            var sb = new StringBuilder();
            var monoImages = images.Where(i => i.IsMonochrome && !i.Filter.Equals("RGB", StringComparison.OrdinalIgnoreCase)).ToList();
            var composites = images.Where(i => !i.IsMonochrome || i.Filter.Equals("RGB", StringComparison.OrdinalIgnoreCase)).ToList();

            // Group mono images by filter type: broadband first, then narrowband
            if (monoImages.Count > 0) {
                var broadband  = monoImages.Where(i => IsBroadband(i.Filter)).ToList();
                var narrowband = monoImages.Where(i => IsNarrowband(i.Filter)).ToList();
                var other      = monoImages.Where(i => !IsBroadband(i.Filter) && !IsNarrowband(i.Filter)).ToList();

                // Group by classification when we have a clean split and >4 images.
                // Otherwise fall back to simple row wrapping (max 4 per row).
                var rows = new List<List<Session.LiveStackImage>>();
                bool cleanSplit = other.Count == 0 && broadband.Count > 0 && narrowband.Count > 0;

                if (monoImages.Count > 4 && cleanSplit) {
                    rows.Add(broadband);
                    rows.Add(narrowband);
                } else {
                    // Simple chunking: max 4 per row, centered
                    for (int i = 0; i < monoImages.Count; i += 4) {
                        rows.Add(monoImages.GetRange(i, Math.Min(4, monoImages.Count - i)));
                    }
                }

                foreach (var row in rows) {
                    AppendMonoRow(sb, row, filterIntegration);
                }
            }

            // Color composite row (full width)
            foreach (var img in composites) {
                sb.AppendLine("<div class='ts-livestack-composite'>");
                sb.AppendLine($"<img src='data:image/jpeg;base64,{Convert.ToBase64String(img.JpegData)}' alt='Live Stack composite' />");
                string label;
                if (img.RedStackCount.HasValue) {
                    label = $"Live Stack Composite &middot; R:{img.RedStackCount} G:{img.GreenStackCount} B:{img.BlueStackCount}";
                    // Add total integration across all RGB channels
                    if (filterIntegration != null) {
                        double totalSec = filterIntegration.Values.Sum();
                        if (totalSec > 0) label += $" &middot; {FormatDuration(totalSec)}";
                    }
                } else {
                    label = $"Live Stack &middot; {img.StackCount} frames";
                    if (filterIntegration != null && filterIntegration.TryGetValue(img.Filter, out var totalSec) && totalSec > 0) {
                        label += $" &middot; {FormatDuration(totalSec)}";
                    }
                }
                sb.AppendLine($"<div class='ts-livestack-label'>{label}</div>");
                sb.AppendLine("</div>");
            }

            return sb.ToString();
        }

        private static void AppendMonoRow(StringBuilder sb, List<Session.LiveStackImage> row, Dictionary<string, double> filterIntegration = null) {
            int perRow = Math.Min(row.Count, 4);
            int itemWidth = row.Count == 1 ? 400 : (760 - (perRow - 1) * 8) / perRow;
            sb.AppendLine("<div class='ts-livestack-row' style='justify-content:center;'>");
            foreach (var img in row) {
                var label = $"{img.Filter} &middot; {img.StackCount} frames";
                if (filterIntegration != null && filterIntegration.TryGetValue(img.Filter, out var totalSec) && totalSec > 0) {
                    label += $" &middot; {FormatDuration(totalSec)}";
                }
                sb.AppendLine($"<div class='ts-livestack-item' style='width:{itemWidth}px;'>");
                sb.AppendLine($"<img class='ts-livestack-img' src='data:image/jpeg;base64,{Convert.ToBase64String(img.JpegData)}' alt='{img.Filter} stack' />");
                sb.AppendLine($"<div class='ts-livestack-label'>{label}</div>");
                sb.AppendLine("</div>");
            }
            sb.AppendLine("</div>");
        }

        private void AppendIqRows(StringBuilder sb, List<ImageRecord> images, string detailsOpen = "") {
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
                sb.AppendLine($"<details class='iq-row{evenCls}'{detailsOpen}><summary>");
                sb.AppendLine($"<div class='iq-row-grid'><div class='iq-cell'>HFR<span class='iq-arrow'></span></div><div class='iq-cell'>{hfrValues.Min():F2}px</div><div class='iq-cell'>{hfrValues.Max():F2}px</div><div class='iq-cell'>{hfrValues.Average():F2}px</div><div class='iq-cell'>{CV(hfrValues):F0}%</div></div>");
                sb.AppendLine("</summary>");
                sb.AppendLine("<div class='iq-expand'>");
                sb.AppendLine("<table style='margin:0;'><tr><th>Filter</th><th>Min</th><th>Max</th><th>Mean</th><th>CV</th></tr>");
                foreach (var g in hfrFilters) {
                    var vals  = g.Select(i => i.HFR).ToList();
                    var cvStr = vals.Count >= 2 ? $"{CV(vals):F0}%" : "—";
                    sb.AppendLine($"<tr><td>{g.Key} <span style='color:var(--accent);font-style:italic;'>({vals.Count})</span></td><td>{vals.Min():F2}px</td><td>{vals.Max():F2}px</td><td>{vals.Average():F2}px</td><td>{cvStr}</td></tr>");
                }
                sb.AppendLine("</table></div></details>");
                rowIdx++;
            }

            // FWHM row — expandable via <details>
            if (imagesWithFWHM.Any()) {
                var fwhmValues  = imagesWithFWHM.Select(i => i.FWHM).ToList();
                var fwhmFilters = imagesWithFWHM.GroupBy(i => i.Filter).Where(g => g.Any()).OrderBy(g => FilterSortKey(g.Key)).ThenBy(g => g.Key).ToList();
                string evenCls = rowIdx % 2 == 1 ? " iq-row-even" : "";
                sb.AppendLine($"<details class='iq-row{evenCls}'{detailsOpen}><summary>");
                sb.AppendLine($"<div class='iq-row-grid'><div class='iq-cell'>FWHM<span class='iq-arrow'></span></div><div class='iq-cell'>{fwhmValues.Min():F2}\"</div><div class='iq-cell'>{fwhmValues.Max():F2}\"</div><div class='iq-cell'>{fwhmValues.Average():F2}\"</div><div class='iq-cell'>{CV(fwhmValues):F0}%</div></div>");
                sb.AppendLine("</summary>");
                sb.AppendLine("<div class='iq-expand'>");
                sb.AppendLine("<table style='margin:0;'><tr><th>Filter</th><th>Min</th><th>Max</th><th>Mean</th><th>CV</th></tr>");
                foreach (var g in fwhmFilters) {
                    var vals  = g.Select(i => i.FWHM).ToList();
                    var cvStr = vals.Count >= 2 ? $"{CV(vals):F0}%" : "—";
                    sb.AppendLine($"<tr><td>{g.Key} <span style='color:var(--accent);font-style:italic;'>({vals.Count})</span></td><td>{vals.Min():F2}\"</td><td>{vals.Max():F2}\"</td><td>{vals.Average():F2}\"</td><td>{cvStr}</td></tr>");
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

        private string BuildImageQualitySection(ReportData data, int detailLevel, string detailsOpen = "") {
            var sb = new StringBuilder();
            var hasHFR     = data.Images.Any(i => i.HFR > 0);
            var hasFWHM    = data.Images.Any(i => i.FWHM > 0);
            var hasGuiding = data.Images.Any(i => i.GuidingRMSTotal > 0);

            if (!hasHFR && !hasFWHM && !hasGuiding) return string.Empty;

            sb.AppendLine("<div class='target-section'>");
            sb.AppendLine("<h2>Session Image Quality</h2>");
            sb.AppendLine("<div class='iq-table'>");
            sb.AppendLine("<div class='iq-row-grid'><div class='iq-header'>Metric</div><div class='iq-header'>Min</div><div class='iq-header'>Max</div><div class='iq-header'>Mean</div><div class='iq-header'>CV</div></div>");
            AppendIqRows(sb, data.Images, detailsOpen);
            sb.AppendLine("</div>"); // iq-table

            if (detailLevel >= 2 && SettingsManager.Instance.Current.ShowHFRGraph) {
                int primary   = SettingsManager.Instance.Current.ChartPrimaryMetric;
                int secondary = SettingsManager.Instance.Current.ChartSecondaryMetric;
                int xAxis     = SettingsManager.Instance.Current.ChartXAxisMetric;

                // Build event marker list from enabled event types
                var eventMarkers = new List<(DateTime timestamp, string eventType, string description)>();
                if (data.Events != null) {
                    var settings = SettingsManager.Instance.Current;
                    if (settings.ShowChartAfMarkers)
                        eventMarkers.AddRange(data.Events.Where(e => e.EventType == "AutoFocus")
                            .Select(e => (e.Timestamp, e.EventType, e.Description)));
                    if (settings.ShowChartFlipMarkers)
                        eventMarkers.AddRange(data.Events.Where(e => e.EventType == "MeridianFlip")
                            .Select(e => (e.Timestamp, e.EventType, e.Description)));
                    if (settings.ShowChartRoofMarkers)
                        eventMarkers.AddRange(data.Events.Where(e => e.EventType is "RoofOpen" or "RoofClosed")
                            .Select(e => (e.Timestamp, e.EventType, e.Description)));
                }
                var markers = eventMarkers.Count > 0 ? eventMarkers : null;

                sb.AppendLine($"<h2>{ChartGenerator.GetChartTitle(primary, secondary, xAxis)}</h2>");
                EmitMetricChart(sb, data.Images, primary, secondary, xAxis, markers);

                var additionalRaw = SettingsManager.Instance.Current.AdditionalChartConfigs;
                if (!string.IsNullOrWhiteSpace(additionalRaw)) {
                    foreach (var part in additionalRaw.Split('|')) {
                        var tokens = part.Split(':');
                        if (tokens.Length >= 2
                            && int.TryParse(tokens[0], out int p)
                            && int.TryParse(tokens[1], out int s)) {
                            int ax = tokens.Length >= 3 && int.TryParse(tokens[2], out int a) ? a : 0;
                            sb.AppendLine($"<h2>{ChartGenerator.GetChartTitle(p, s, ax)}</h2>");
                            EmitMetricChart(sb, data.Images, p, s, ax, markers);
                        }
                    }
                }
            }

            sb.AppendLine("</div>");
            return sb.ToString();
        }

        // JSON serializer options for chart models: camelCase, no pretty-printing
        // (size matters when this is embedded in every report), and don't escape
        // HTML characters — they'll be inside a single-quoted attribute and we
        // handle apostrophes explicitly in EmitMetricChart.
        private static readonly JsonSerializerOptions ChartJsonOptions = new JsonSerializerOptions {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = false
        };

        /// <summary>
        /// Emits one metric chart as a <c>&lt;div data-chart='...'&gt;</c> container
        /// consumed by the metric-chart.js renderer. The JSON model carries all
        /// points, axis metadata, event markers, and the distinct filter list for
        /// the per-filter selector.
        /// </summary>
        // Sanitize a filter name for use as a CSS ID fragment — replaces every
        // non-alphanumeric character with an underscore.
        private static string ChartSafeId(string filter) =>
            Regex.Replace(filter, "[^a-zA-Z0-9]", "_");

        /// <summary>
        /// Emits a metric chart with a pure-CSS per-filter chip selector.
        /// Each visible state is a pre-rendered C# SVG (axes auto-scaled to that
        /// filter's data), toggled by hidden radio inputs + CSS sibling selectors.
        /// Works in every HTML viewer including Gmail and iOS Quick Look — no JS
        /// required. The JS renderer (metric-chart.js) is kept as dead code for
        /// the future v3 dashboard.
        /// </summary>
        private void EmitMetricChart(
                StringBuilder sb,
                List<ImageRecord> images,
                int primary,
                int secondary,
                int xAxis,
                List<(DateTime timestamp, string eventType, string description)>? markers) {

            // Use BuildChartModel only for the sorted filter list
            var filters = ChartGenerator.BuildChartModel(images, primary, secondary, xAxis, markers).Filters;

            // No chip selector when there's only one filter or none
            if (filters.Count < 2) {
                var svg = ChartGenerator.GenerateMetricChart(images, primary, secondary, xAxis, markers);
                sb.AppendLine($"<div class=\"metric-chart-container\"><div class=\"ns-chart-svg\">{svg}</div></div>");
                return;
            }

            int ci = _chartIndex++;
            string pfx = $"nsc{ci}";

            // Pre-render one SVG per visible state: "All filters" + one per filter
            var allSvg = ChartGenerator.GenerateMetricChart(images, primary, secondary, xAxis, markers);
            var filterSvgs = filters.ToDictionary(
                f => f,
                f => ChartGenerator.GenerateMetricChart(
                    images.Where(i => i.Filter == f).ToList(),
                    primary, secondary, xAxis, markers));

            // ── Per-chart CSS ────────────────────────────────────────────────
            // Active chip: whichever radio is :checked highlights its paired label.
            // SVG visibility: only the selected state's container is display:block.
            sb.AppendLine("<style>");

            // Active chip highlight rules
            var activeSelectors = new[] { $"#{pfx}-all:checked ~ .{pfx}-bar label[for=\"{pfx}-all\"]" }
                .Concat(filters.Select(f =>
                    $"#{pfx}-{ChartSafeId(f)}:checked ~ .{pfx}-bar label[for=\"{pfx}-{ChartSafeId(f)}\"]"));
            sb.AppendLine(string.Join(",\n", activeSelectors));
            sb.AppendLine("{ background: var(--accent); color: var(--bg); border-color: var(--accent); }");

            // Hide all per-filter SVG containers by default (shown individually below)
            sb.AppendLine(string.Join(", ", filters.Select(f => $"#{pfx}-svg-{ChartSafeId(f)}"))
                + " { display: none; }");

            // Show/hide rules per filter chip
            foreach (var f in filters) {
                string fid = ChartSafeId(f);
                sb.AppendLine($"#{pfx}-{fid}:checked ~ #{pfx}-svg-all {{ display: none; }}");
                // !important needed to override the inline style="display:none" on per-filter containers
                sb.AppendLine($"#{pfx}-{fid}:checked ~ #{pfx}-svg-{fid} {{ display: block !important; }}");
            }
            sb.AppendLine("</style>");

            // ── HTML structure ───────────────────────────────────────────────
            sb.AppendLine($"<div class=\"metric-chart-container\">");

            // Radio inputs — hidden but still toggled by their paired labels
            sb.AppendLine($"<input type=\"radio\" name=\"{pfx}\" id=\"{pfx}-all\" checked style=\"display:none\">");
            foreach (var f in filters)
                sb.AppendLine($"<input type=\"radio\" name=\"{pfx}\" id=\"{pfx}-{ChartSafeId(f)}\" style=\"display:none\">");

            // Chip bar (labels styled identically to the old JS buttons)
            sb.Append($"<div class=\"ns-chart-filter-bar {pfx}-bar\">");
            sb.Append($"<label class=\"ns-chart-filter-btn\" for=\"{pfx}-all\">All</label>");
            foreach (var f in filters) {
                var encoded = WebUtility.HtmlEncode(f);
                sb.Append($"<label class=\"ns-chart-filter-btn\" for=\"{pfx}-{ChartSafeId(f)}\" title=\"{encoded}\">{encoded}</label>");
            }
            sb.AppendLine("</div>");

            // SVG containers — "All" visible by default, per-filter hidden
            sb.AppendLine($"<div class=\"ns-chart-svg\" id=\"{pfx}-svg-all\">{allSvg}</div>");
            foreach (var f in filters)
                sb.AppendLine($"<div class=\"ns-chart-svg\" id=\"{pfx}-svg-{ChartSafeId(f)}\" style=\"display:none\">{filterSvgs[f]}</div>");

            sb.AppendLine("</div>"); // metric-chart-container
        }

        /// <summary>
        /// Appends the metric-chart.js renderer source inside a &lt;script&gt; tag.
        /// Called once, just before &lt;/body&gt;. The IIFE inside the renderer
        /// auto-initializes all <c>[data-chart]</c> containers on DOMContentLoaded.
        /// </summary>
        private static void AppendChartRendererScript(StringBuilder sb) {
            var js = MetricChartJs;
            if (string.IsNullOrEmpty(js)) return;
            sb.AppendLine("<script>");
            sb.Append(js);
            sb.AppendLine("</script>");
        }

        /// <summary>
        /// Builds the session event timeline section with a CSS-only toggle
        /// between the simple flat-bar timeline and the altitude chart view.
        /// Falls back to simple-only when observer coordinates are unavailable.
        /// </summary>
        private string BuildEventTimelineSection(ReportData data) {
            if (!data.Images.Any()) return string.Empty;

            var simpleHtml = EventTimelineGenerator.GenerateTimeline(data.Session, data.Images, data.Events ?? new List<SessionEvent>());
            if (string.IsNullOrEmpty(simpleHtml)) return string.Empty;

            var altitudeHtml = BuildSessionAltitudeChart(data);

            // No altitude data → just show simple view without toggle
            if (string.IsNullOrEmpty(altitudeHtml)) {
                var sb0 = new StringBuilder();
                sb0.AppendLine("<h2>Session Timeline</h2>");
                sb0.AppendLine("<div class='timeline-container'>");
                sb0.AppendLine(simpleHtml);
                sb0.AppendLine("</div>");
                return sb0.ToString();
            }

            // Both views available — emit CSS-only toggle
            int ci = _chartIndex++;
            string pfx = $"nsc{ci}";

            var sb = new StringBuilder();
            sb.AppendLine("<h2>Session Timeline</h2>");

            // Which view is default?
            bool altDefault = SettingsManager.Instance.Current.TimelineAltitudeDefault;
            string altChecked = altDefault ? " checked" : "";
            string simChecked = altDefault ? "" : " checked";
            string hiddenView = altDefault ? "simple" : "altitude";

            // Toggle CSS
            sb.AppendLine("<style>");
            sb.AppendLine($"#{pfx}-altitude:checked ~ .{pfx}-bar label[for=\"{pfx}-altitude\"],");
            sb.AppendLine($"#{pfx}-simple:checked ~ .{pfx}-bar label[for=\"{pfx}-simple\"]");
            sb.AppendLine("{ background: var(--accent); color: var(--bg); border-color: var(--accent); }");
            sb.AppendLine($"#{pfx}-svg-{hiddenView} {{ display: none; }}");
            sb.AppendLine($"#{pfx}-simple:checked ~ #{pfx}-svg-altitude {{ display: none; }}");
            sb.AppendLine($"#{pfx}-simple:checked ~ #{pfx}-svg-simple {{ display: block !important; }}");
            sb.AppendLine($"#{pfx}-altitude:checked ~ #{pfx}-svg-simple {{ display: none; }}");
            sb.AppendLine($"#{pfx}-altitude:checked ~ #{pfx}-svg-altitude {{ display: block !important; }}");
            sb.AppendLine("</style>");

            sb.AppendLine("<div class='timeline-container'>");

            // Radio inputs
            sb.AppendLine($"<input type=\"radio\" name=\"{pfx}\" id=\"{pfx}-altitude\"{altChecked} style=\"display:none\">");
            sb.AppendLine($"<input type=\"radio\" name=\"{pfx}\" id=\"{pfx}-simple\"{simChecked} style=\"display:none\">");

            // Chip bar
            sb.Append($"<div class=\"ns-chart-filter-bar {pfx}-bar\">");
            sb.Append($"<label class=\"ns-chart-filter-btn\" for=\"{pfx}-altitude\">Altitude</label>");
            sb.Append($"<label class=\"ns-chart-filter-btn\" for=\"{pfx}-simple\">Simple</label>");
            sb.AppendLine("</div>");

            // Altitude view
            sb.AppendLine($"<div class=\"ns-chart-svg\" id=\"{pfx}-svg-altitude\"{(altDefault ? "" : " style=\"display:none\"")}>");
            sb.AppendLine(altitudeHtml);
            sb.AppendLine("</div>");

            // Simple view
            sb.AppendLine($"<div class=\"ns-chart-svg\" id=\"{pfx}-svg-simple\"{(altDefault ? " style=\"display:none\"" : "")}>");
            sb.AppendLine(simpleHtml);
            sb.AppendLine("</div>");

            sb.AppendLine("</div>"); // timeline-container
            return sb.ToString();
        }

        internal static string FormatRA(double raHours) {
            var h     = (int)raHours;
            var mFrac = (raHours - h) * 60;
            var m     = (int)mFrac;
            var s     = (mFrac - m) * 60;
            return $"{h:D2}h {m:D2}m {s:F0}s";
        }

        internal static string FormatDec(double decDeg) {
            var sign  = decDeg >= 0 ? "+" : "-";
            var abs   = Math.Abs(decDeg);
            var d     = (int)abs;
            var mFrac = (abs - d) * 60;
            var m     = (int)mFrac;
            var s     = (mFrac - m) * 60;
            return $"{sign}{d:D2}° {m:D2}′ {s:F0}″";
        }

        private string BuildAltitudeChart(double raHours, double decDeg, double latDeg, double lonDeg,
                                          DateTime sessionStart, DateTime sessionEnd, int width = 560,
                                          double minimumAltitude = 0) {
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
            sb.AppendLine($"<svg class='altitude-chart' viewBox='0 0 {svgW} {svgH}' width='102%' height='{svgH}' xmlns='http://www.w3.org/2000/svg' style='display:block;' preserveAspectRatio='none'>");

            // All chart colors are theme-aware via svg* instance variables
            string altGrid = svgBorder, altLabel = svgMuted, altAccent = svgAccent;

            // Background
            sb.AppendLine($"<rect x='{padL}' y='{padT}' width='{plotW}' height='{plotH}' fill='{svgChartBg}' rx='4'/>");
            sb.AppendLine($"<rect x='{padL}' y='{padT}' width='{plotW}' height='{plotH}' fill='none' stroke='{altGrid}' stroke-width='1' rx='4'/>");

            // Session window subtle highlight
            sb.AppendLine($"<rect x='{xSessStart:F1}' y='{padT}' width='{(xSessEnd - xSessStart):F1}' height='{plotH}' fill='{altAccent}' opacity='0.07'/>");

            // Grid lines at 30° and 60°
            foreach (var gridAlt in new[] { 30.0, 60.0 }) {
                double gy = Y(gridAlt);
                sb.AppendLine($"<line x1='{padL}' y1='{gy:F1}' x2='{padL + plotW}' y2='{gy:F1}' stroke='{altGrid}' stroke-width='1'/>");
                sb.AppendLine($"<text x='{padL - 4}' y='{gy + 4:F1}' text-anchor='end' font-size='10' fill='{altLabel}'>{gridAlt:F0}°</text>");
            }
            sb.AppendLine($"<text x='{padL - 4}' y='{padT + 4}' text-anchor='end' font-size='10' fill='{altLabel}'>90°</text>");
            sb.AppendLine($"<text x='{padL - 4}' y='{padT + plotH + 4}' text-anchor='end' font-size='10' fill='{altLabel}'>0°</text>");

            // Minimum altitude line (from Target Scheduler)
            if (minimumAltitude > 0 && minimumAltitude < maxAlt) {
                double minAltY = Y(minimumAltitude);
                sb.AppendLine($"<line x1='{padL}' y1='{minAltY:F1}' x2='{padL + plotW}' y2='{minAltY:F1}' stroke='#cc4444' stroke-width='1.2' stroke-dasharray='5,4' opacity='0.7'/>");
                sb.AppendLine($"<text x='{padL + plotW - 2}' y='{minAltY - 4:F1}' text-anchor='end' font-size='9' fill='#cc4444' opacity='0.85'>Min Alt {minimumAltitude:F0}°</text>");
            }

            // Altitude curve — one polyline per continuous above-horizon segment
            foreach (var seg in segments) {
                if (seg.Count < 2) continue;
                var pts = new StringBuilder();
                foreach (var (t, alt) in seg)
                    pts.Append($"{X(t):F1},{Y(alt):F1} ");
                sb.AppendLine($"<polyline points='{pts}' fill='none' stroke='{altAccent}' stroke-width='2'/>");
            }

            // ── Moon altitude curve ──────────────────────────────────────────────
            if (!SettingsManager.Instance.Current.ShowMoonCurve) goto skipMoon;
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
                sb.AppendLine($"<polyline points='{pts}' fill='none' stroke='{svgMoonStroke}' stroke-width='1.5' stroke-dasharray='5,4' opacity='{svgMoonOpacity}'/>");
                sb.AppendLine("</g>");
            }
            skipMoon:;

            // Session start line with tooltip
            sb.AppendLine("<g>");
            sb.AppendLine($"  <title>Start: {sessionStart:HH:mm}</title>");
            sb.AppendLine($"  <line x1='{xSessStart:F1}' y1='{padT}' x2='{xSessStart:F1}' y2='{padT + plotH}' stroke='{altAccent}' stroke-width='1.5' stroke-dasharray='4,3' opacity='0.7'/>");
            sb.AppendLine($"  <text x='{xSessStart:F1}' y='{padT - 5}' text-anchor='middle' font-size='9' fill='{altAccent}'>Start</text>");
            sb.AppendLine("</g>");

            // Session end line with tooltip
            sb.AppendLine("<g>");
            sb.AppendLine($"  <title>End: {sessionEnd:HH:mm}</title>");
            sb.AppendLine($"  <line x1='{xSessEnd:F1}' y1='{padT}' x2='{xSessEnd:F1}' y2='{padT + plotH}' stroke='{altAccent}' stroke-width='1.5' stroke-dasharray='4,3' opacity='0.7'/>");
            sb.AppendLine($"  <text x='{xSessEnd:F1}' y='{padT - 5}' text-anchor='middle' font-size='9' fill='{altAccent}'>End</text>");
            sb.AppendLine("</g>");

            // Sunset / sunrise edge markers
            sb.AppendLine($"<text x='{padL + 2}' y='{padT + plotH - 4}' font-size='10' fill='{svgSunrise}' opacity='0.8'>&#9660; Sunset {dayStart:HH:mm}</text>");
            sb.AppendLine($"<text x='{padL + plotW - 2}' y='{padT + plotH - 4}' text-anchor='end' font-size='10' fill='{svgSunrise}' opacity='0.8'>Sunrise {dayEnd:HH:mm} &#9650;</text>");

            // X-axis time labels — edge labels + intermediate ticks every 2h
            sb.AppendLine($"<text x='{padL}' y='{timeLabelY}' text-anchor='start' font-size='10' fill='{altLabel}'>{dayStart:HH:mm}</text>");
            sb.AppendLine($"<text x='{padL + plotW}' y='{timeLabelY}' text-anchor='end' font-size='10' fill='{altLabel}'>{dayEnd:HH:mm}</text>");
            var firstTick = new DateTime(dayStart.Year, dayStart.Month, dayStart.Day, dayStart.Hour, 0, 0).AddHours(compact ? 4 : 2);
            if (firstTick <= dayStart) firstTick = firstTick.AddHours(compact ? 4 : 2);
            for (var tick = firstTick; tick < dayEnd; tick = tick.AddHours(compact ? 4 : 2)) {
                double tx = X(tick);
                if (tx - padL > 30 && (padL + plotW) - tx > 30)
                    sb.AppendLine($"<text x='{tx:F1}' y='{timeLabelY}' text-anchor='middle' font-size='10' fill='{altLabel}'>{tick:HH:mm}</text>");
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
            return $"<div class='target-section'><h2>Tonight's Preview</h2><p style='color:var(--muted);font-style:italic;'>{message}</p></div>";
        }

        private string BuildNextNightPreviewSection(ReportData data) {
            if (!SettingsManager.Instance.Current.ShowNextNightPreview) return "";

            var tsDb = new TargetSchedulerDatabase();
            if (!tsDb.IsAvailable)
                return "";  // TS not installed — silently skip, Options UI already indicates it's unavailable

            var (apiEnabled, apiPort) = tsDb.GetApiSettings(data.ActiveProfileId);
            if (!apiEnabled) {
                // API not enabled is a normal default state — silently skip, no report warning
                Logger.Info($"NightSummary: Tonight's Preview skipped — TS API not enabled for profile '{data.ActiveProfileId ?? "unknown"}'");
                return "";
            }

            try {
                var baseUrl = $"http://localhost:{apiPort}/ts/v0";
                Logger.Info($"NightSummary: Tonight's Preview — connecting to TS API at {baseUrl}");
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
                var encodedStart = Uri.EscapeDataString(startTime.ToString("o"));
                var previewUrl = $"{baseUrl}/profiles/{active.Id}/preview?startTime={encodedStart}";

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
                sb.AppendLine($"<span style='color:var(--dim);font-size:12px;font-style:italic;margin-left:12px;'>Generated by Target Scheduler — actual imaging may differ based on conditions</span>");
                sb.AppendLine($"<p style='color:var(--muted);margin-top:8px;'>Planned schedule for {previewDate:MMMM d, yyyy} &mdash; {timelineStart:HH:mm} to {timelineEnd:HH:mm}</p>");

                // Look up RA/Dec for preview targets from the TS database
                var coordLookup = new Dictionary<string, (double Ra, double Dec)>(StringComparer.OrdinalIgnoreCase);
                try {
                    var tsProgress = tsDb.GetProgressForTargets(uniqueTargets, data.ActiveProfileId);
                    foreach (var tt in tsProgress)
                        if (!coordLookup.ContainsKey(tt.TargetName))
                            coordLookup[tt.TargetName] = (tt.RA, tt.Dec);
                } catch (Exception ex) {
                    Logger.Warning($"NightSummary: Could not look up coordinates for preview targets: {ex.Message}");
                }

                // ── Timeline with toggle (Altitude / Simple) ──
                var altChart = BuildPreviewAltitudeChart(targets, colorMap, coordLookup,
                    data.ObserverLatitude, data.ObserverLongitude, timelineStart, timelineEnd);
                var simpleChart = BuildPreviewSimpleTimeline(entries, targets, colorMap, timelineStart, timelineEnd);

                if (!string.IsNullOrEmpty(altChart) && !string.IsNullOrEmpty(simpleChart)) {
                    int ci = _chartIndex++;
                    string pfx = $"nsc{ci}";
                    bool pvAltDefault = SettingsManager.Instance.Current.PreviewAltitudeDefault;
                    string pvAltChecked = pvAltDefault ? " checked" : "";
                    string pvSimChecked = pvAltDefault ? "" : " checked";
                    string pvHidden = pvAltDefault ? "simple" : "altitude";

                    sb.AppendLine("<style>");
                    sb.AppendLine($"#{pfx}-altitude:checked ~ .{pfx}-bar label[for=\"{pfx}-altitude\"],");
                    sb.AppendLine($"#{pfx}-simple:checked ~ .{pfx}-bar label[for=\"{pfx}-simple\"]");
                    sb.AppendLine("{ background: var(--accent); color: var(--bg); border-color: var(--accent); }");
                    sb.AppendLine($"#{pfx}-svg-{pvHidden} {{ display: none; }}");
                    sb.AppendLine($"#{pfx}-simple:checked ~ #{pfx}-svg-altitude {{ display: none; }}");
                    sb.AppendLine($"#{pfx}-simple:checked ~ #{pfx}-svg-simple {{ display: block !important; }}");
                    sb.AppendLine($"#{pfx}-altitude:checked ~ #{pfx}-svg-simple {{ display: none; }}");
                    sb.AppendLine($"#{pfx}-altitude:checked ~ #{pfx}-svg-altitude {{ display: block !important; }}");
                    sb.AppendLine("</style>");

                    sb.AppendLine("<div class='timeline-container'>");
                    sb.AppendLine($"<input type=\"radio\" name=\"{pfx}\" id=\"{pfx}-altitude\"{pvAltChecked} style=\"display:none\">");
                    sb.AppendLine($"<input type=\"radio\" name=\"{pfx}\" id=\"{pfx}-simple\"{pvSimChecked} style=\"display:none\">");
                    sb.Append($"<div class=\"ns-chart-filter-bar {pfx}-bar\">");
                    sb.Append($"<label class=\"ns-chart-filter-btn\" for=\"{pfx}-altitude\">Altitude</label>");
                    sb.Append($"<label class=\"ns-chart-filter-btn\" for=\"{pfx}-simple\">Simple</label>");
                    sb.AppendLine("</div>");
                    sb.AppendLine($"<div class=\"ns-chart-svg\" id=\"{pfx}-svg-altitude\"{(pvAltDefault ? "" : " style=\"display:none\"")}>{altChart}</div>");
                    sb.AppendLine($"<div class=\"ns-chart-svg\" id=\"{pfx}-svg-simple\"{(pvAltDefault ? " style=\"display:none\"" : "")}>{simpleChart}</div>");
                    sb.AppendLine("</div>");
                } else {
                    // Fallback — show whichever is available
                    sb.AppendLine("<div class='timeline-container'>");
                    sb.AppendLine(!string.IsNullOrEmpty(altChart) ? altChart : simpleChart);
                    sb.AppendLine("</div>");
                }

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
                    string detailsOpen = SettingsManager.Instance.Current.ExpandSectionsDefault ? " open" : "";
                    sb.AppendLine($"<details class='history-section'{detailsOpen}>");
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

        /// <summary>
        /// Multi-target altitude chart for the Tonight's Preview section.
        /// Renders altitude curves for each scheduled target over the full night window,
        /// with per-target colored shading for each imaging block.
        /// </summary>
        private string BuildPreviewAltitudeChart(
            List<TsPreviewEntry> imagingBlocks,
            Dictionary<string, string> colorMap,
            Dictionary<string, (double Ra, double Dec)> coordLookup,
            double latDeg, double lonDeg,
            DateTime nightStart, DateTime nightEnd) {

            const int svgW = 760, padL = 38, padR = 10, padT = 20, padB = 28;
            int plotW = svgW - padL - padR;
            int plotH = 200;
            int svgH  = padT + plotH + padB;
            const double maxAlt = 90.0;
            double totalMin = (nightEnd - nightStart).TotalMinutes;

            double X(DateTime t) => padL + (t - nightStart).TotalMinutes / totalMin * plotW;
            double Y(double alt)  => padT + plotH - alt / maxAlt * plotH;

            var sb = new StringBuilder();
            sb.AppendLine($"<svg viewBox='0 0 {svgW} {svgH}' xmlns='http://www.w3.org/2000/svg' style='width:100%;font-family:Arial,sans-serif;font-size:10px;'>");

            // Background
            sb.AppendLine($"<rect x='{padL}' y='{padT}' width='{plotW}' height='{plotH}' fill='{svgChartBg}' rx='4'/>");
            sb.AppendLine($"<rect x='{padL}' y='{padT}' width='{plotW}' height='{plotH}' fill='none' stroke='{svgBorder}' stroke-width='1' rx='4'/>");

            // Grid lines and altitude axis labels
            foreach (var gridAlt in new[] { 30.0, 60.0 }) {
                double gy = Y(gridAlt);
                sb.AppendLine($"<line x1='{padL}' y1='{gy:F1}' x2='{padL + plotW}' y2='{gy:F1}' stroke='{svgBorder}' stroke-width='1'/>");
                sb.AppendLine($"<text x='{padL - 4}' y='{gy + 4:F1}' text-anchor='end' fill='{svgMuted}'>{gridAlt:F0}°</text>");
            }
            sb.AppendLine($"<text x='{padL - 4}' y='{padT + 4}' text-anchor='end' fill='{svgMuted}'>90°</text>");
            sb.AppendLine($"<text x='{padL - 4}' y='{padT + plotH + 4}' text-anchor='end' fill='{svgMuted}'>0°</text>");

            // Per-target imaging window shading — one vertical band per schedule block
            foreach (var entry in imagingBlocks) {
                var color = colorMap[entry.Name];
                var wStart = entry.StartTime < nightStart ? nightStart : entry.StartTime;
                var wEnd   = entry.EndTime   > nightEnd   ? nightEnd   : entry.EndTime;
                if (wStart >= wEnd) continue;
                double bx1 = X(wStart), bx2 = X(wEnd);
                sb.AppendLine($"<g><title>{entry.Name}&#10;{wStart:HH:mm} – {wEnd:HH:mm}</title>");
                sb.AppendLine($"<rect x='{bx1:F1}' y='{padT}' width='{(bx2 - bx1):F1}' height='{plotH}' fill='{color}' opacity='0.15'/>");
                sb.AppendLine($"<line x1='{bx1:F1}' y1='{padT}' x2='{bx1:F1}' y2='{padT + plotH}' stroke='{color}' stroke-width='1' opacity='0.5'/>");
                sb.AppendLine($"<line x1='{bx2:F1}' y1='{padT}' x2='{bx2:F1}' y2='{padT + plotH}' stroke='{color}' stroke-width='1' opacity='0.5'/>");
                sb.AppendLine("</g>");
            }

            // Moon altitude curve
            if (SettingsManager.Instance.Current.ShowMoonCurve) {
                var moonPts  = AltitudeCalculator.GetMoonAltitudeCurve(latDeg, lonDeg, nightStart, nightEnd, stepMinutes: 5);
                var moonSegs = BuildAltSegments(moonPts, maxAlt);
                foreach (var seg in moonSegs) {
                    if (seg.Count < 2) continue;
                    var pts = new StringBuilder();
                    foreach (var (t, alt) in seg) pts.Append($"{X(t):F1},{Y(alt):F1} ");
                    sb.AppendLine("<g><title>Moon</title>");
                    sb.AppendLine($"<polyline points='{pts}' fill='none' stroke='transparent' stroke-width='12'/>");
                    sb.AppendLine($"<polyline points='{pts}' fill='none' stroke='{svgMoonStroke}' stroke-width='1.5' stroke-dasharray='5,4' opacity='{svgMoonOpacity}'/>");
                    sb.AppendLine("</g>");
                }
            }

            // Per-target altitude curves with target colors
            var uniqueTargets = imagingBlocks.Select(e => e.Name).Distinct().ToList();
            foreach (var name in uniqueTargets) {
                if (!coordLookup.TryGetValue(name, out var coords)) continue;
                if (coords.Ra == 0 && coords.Dec == 0) continue;
                var color  = colorMap[name];
                var altPts = AltitudeCalculator.GetAltitudeCurve(coords.Ra, coords.Dec,
                                                                  latDeg, lonDeg,
                                                                  nightStart, nightEnd, stepMinutes: 5);
                var segs = BuildAltSegments(altPts, maxAlt);
                sb.AppendLine($"<g><title>{name}</title>");
                foreach (var seg in segs) {
                    if (seg.Count < 2) continue;
                    var pts = new StringBuilder();
                    foreach (var (t, alt) in seg) pts.Append($"{X(t):F1},{Y(alt):F1} ");
                    // Wide transparent stroke for easier hover hit-testing
                    sb.AppendLine($"<polyline points='{pts}' fill='none' stroke='transparent' stroke-width='10'/>");
                    sb.AppendLine($"<polyline points='{pts}' fill='none' stroke='{color}' stroke-width='2'/>");
                }
                sb.AppendLine("</g>");
            }

            // Time axis labels — edge labels + ticks every 2 hours
            int timeLabelY = padT + plotH + 18;
            sb.AppendLine($"<text x='{padL}' y='{timeLabelY}' text-anchor='start' fill='{svgMuted}'>{nightStart:HH:mm}</text>");
            sb.AppendLine($"<text x='{padL + plotW}' y='{timeLabelY}' text-anchor='end' fill='{svgMuted}'>{nightEnd:HH:mm}</text>");
            var firstTick = new DateTime(nightStart.Year, nightStart.Month, nightStart.Day, nightStart.Hour, 0, 0).AddHours(2);
            if (firstTick <= nightStart) firstTick = firstTick.AddHours(2);
            for (var tick = firstTick; tick < nightEnd; tick = tick.AddHours(2)) {
                double tx = X(tick);
                if (tx - padL > 30 && (padL + plotW) - tx > 30)
                    sb.AppendLine($"<text x='{tx:F1}' y='{timeLabelY}' text-anchor='middle' fill='{svgMuted}'>{tick:HH:mm}</text>");
            }

            sb.AppendLine("</svg>");

            // Legend as inline chips below the chart
            sb.AppendLine("<div style='margin-top:8px;display:flex;flex-wrap:wrap;gap:12px;'>");
            foreach (var name in uniqueTargets) {
                var color = colorMap[name];
                sb.AppendLine($"<span style='display:inline-flex;align-items:center;gap:6px;font-size:12px;color:var(--muted);'>" +
                              $"<span style='display:inline-block;width:16px;height:3px;background:{color};border-radius:2px;flex-shrink:0;'></span>" +
                              $"{name}</span>");
            }
            sb.AppendLine("</div>");

            return sb.ToString();
        }

        /// <summary>Splits an altitude point list into continuous above-horizon segments.</summary>
        private static List<List<(DateTime t, double alt)>> BuildAltSegments(
            List<(DateTime Time, double Altitude)> points, double maxAlt) {
            var segs = new List<List<(DateTime t, double alt)>>();
            List<(DateTime t, double alt)> cur = null;
            foreach (var (time, alt) in points) {
                if (alt >= 0) {
                    if (cur == null) { cur = new List<(DateTime, double)>(); segs.Add(cur); }
                    cur.Add((time, Math.Min(maxAlt, alt)));
                } else {
                    cur = null;
                }
            }
            return segs;
        }

        /// <summary>
        /// Simple flat-bar timeline for the Tonight's Preview section.
        /// Colored blocks per target, crosshatch for wait/idle periods, time ruler.
        /// Visual language matches EventTimelineGenerator but driven by TS preview data.
        /// </summary>
        private string BuildPreviewSimpleTimeline(
            List<TsPreviewEntry> allEntries,
            List<TsPreviewEntry> imagingBlocks,
            Dictionary<string, string> colorMap,
            DateTime timelineStart, DateTime timelineEnd) {

            double totalSeconds = (timelineEnd - timelineStart).TotalSeconds;
            if (totalSeconds <= 0) return string.Empty;

            bool light = SettingsManager.Instance.Current.ReportLightMode;
            string idleBg     = light ? "#d0d4da" : "#0f0f23";
            string idleStripe = light ? "#b04040" : "#7a1a1a";
            string tickColor  = light ? "#888" : "#555";
            string labelColor = light ? "#666" : "#888";
            string trackBg    = light ? "#e0e4ea" : "#0f0f23";

            const int svgWidth   = 760;
            const int trackHeight = 24;
            const int topPad     = 10;
            const int leftPad    = 8;
            const int rightPad   = 8;
            const int barAreaW   = svgWidth - leftPad - rightPad;
            const int legendRowH = 20;

            double TimeToX(DateTime t) =>
                leftPad + (t - timelineStart).TotalSeconds / totalSeconds * barAreaW;

            var uniqueTargets = imagingBlocks.Select(t => t.Name).Distinct().ToList();

            int trackY   = topPad;
            int rulerH   = 28;
            int legendTop = trackY + trackHeight + rulerH + 8;
            int legendHeight = 18 + uniqueTargets.Count * legendRowH;
            int svgHeight = legendTop + legendHeight + 10;

            string legendText = light ? "#1a1a2e" : "#e0e0e0";

            var sb = new StringBuilder();
            sb.AppendLine($"<svg viewBox='0 0 {svgWidth} {svgHeight}' xmlns='http://www.w3.org/2000/svg' style='width:100%;font-family:Arial,sans-serif;font-size:11px;'>");

            // Idle crosshatch pattern
            sb.AppendLine("<defs>");
            sb.AppendLine("  <pattern id='ns-idle-pv' patternUnits='userSpaceOnUse' width='8' height='8' patternTransform='rotate(45)'>");
            sb.AppendLine($"    <rect width='8' height='8' fill='{idleBg}'/>");
            sb.AppendLine($"    <line x1='0' y1='0' x2='0' y2='8' stroke='{idleStripe}' stroke-width='3'/>");
            sb.AppendLine("  </pattern>");
            sb.AppendLine("</defs>");

            // Solid background track
            sb.AppendLine($"<rect x='{leftPad}' y='{trackY}' width='{barAreaW}' height='{trackHeight}' rx='4' fill='{trackBg}' />");

            // Crosshatch idle/wait gaps
            var cursor = timelineStart;
            foreach (var block in imagingBlocks) {
                var bStart = block.StartTime < timelineStart ? timelineStart : block.StartTime;
                if (bStart > cursor) {
                    double gx1 = TimeToX(cursor), gx2 = TimeToX(bStart);
                    sb.AppendLine($"<rect x='{gx1:F1}' y='{trackY}' width='{(gx2 - gx1):F1}' height='{trackHeight}' fill='url(#ns-idle-pv)' />");
                }
                if (block.EndTime > cursor) cursor = block.EndTime;
            }
            if (cursor < timelineEnd) {
                double gx1 = TimeToX(cursor), gx2 = TimeToX(timelineEnd);
                sb.AppendLine($"<rect x='{gx1:F1}' y='{trackY}' width='{(gx2 - gx1):F1}' height='{trackHeight}' fill='url(#ns-idle-pv)' />");
            }

            // Colored imaging bands
            foreach (var block in imagingBlocks) {
                var color = colorMap[block.Name];
                var bStart = block.StartTime < timelineStart ? timelineStart : block.StartTime;
                var bEnd   = block.EndTime   > timelineEnd   ? timelineEnd   : block.EndTime;
                if (bStart >= bEnd) continue;
                double x1 = TimeToX(bStart), x2 = TimeToX(bEnd);
                double w = Math.Max(x2 - x1, 2);
                sb.AppendLine($"<g><title>{block.Name}&#10;{bStart:HH:mm} – {bEnd:HH:mm}</title>");
                sb.AppendLine($"<rect x='{x1:F1}' y='{trackY}' width='{w:F1}' height='{trackHeight}' fill='{color}' opacity='0.85'/>");
                sb.AppendLine("</g>");
            }

            // Ruler-style time axis
            int rulerY     = trackY + trackHeight;
            int tickH      = 6;
            int tickLabelY = rulerY + 20;
            sb.AppendLine($"<line x1='{leftPad}' y1='{rulerY}' x2='{svgWidth - rightPad}' y2='{rulerY}' stroke='#444' stroke-width='1'/>");

            double durationHours = totalSeconds / 3600.0;
            int tickIntervalMins = durationHours < 2 ? 15 : durationHours < 5 ? 30 : 60;
            var firstTick = new DateTime(timelineStart.Year, timelineStart.Month, timelineStart.Day, timelineStart.Hour, 0, 0);
            while (firstTick <= timelineStart) firstTick = firstTick.AddMinutes(tickIntervalMins);
            var tick = firstTick;
            while (tick < timelineEnd) {
                double tx = TimeToX(tick);
                if (tx - leftPad > 40 && (svgWidth - rightPad) - tx > 40) {
                    sb.AppendLine($"<line x1='{tx:F1}' y1='{rulerY}' x2='{tx:F1}' y2='{rulerY + tickH}' stroke='{tickColor}' stroke-width='1'/>");
                    sb.AppendLine($"<text x='{tx:F1}' y='{tickLabelY}' fill='{labelColor}' text-anchor='middle'>{tick:HH:mm}</text>");
                }
                tick = tick.AddMinutes(tickIntervalMins);
            }
            sb.AppendLine($"<text x='{leftPad}' y='{tickLabelY}' fill='{labelColor}'>{timelineStart:HH:mm}</text>");
            sb.AppendLine($"<text x='{svgWidth - rightPad}' y='{tickLabelY}' fill='{labelColor}' text-anchor='end'>{timelineEnd:HH:mm}</text>");

            // Legend
            int ly = legendTop;
            sb.AppendLine($"<text x='{leftPad}' y='{ly + 12}' fill='#aaa' font-weight='bold'>Targets</text>");
            ly += 18;
            foreach (var name in uniqueTargets) {
                var color = colorMap[name];
                sb.AppendLine($"<rect x='{leftPad}' y='{ly}' width='14' height='12' fill='{color}' rx='2'/>");
                sb.AppendLine($"<text x='{leftPad + 18}' y='{ly + 10}' fill='{legendText}'>{name}</text>");
                ly += legendRowH;
            }

            sb.AppendLine("</svg>");
            return sb.ToString();
        }

        /// <summary>
        /// Multi-target altitude chart for the Session Timeline section.
        /// Same visual as the preview altitude chart, plus event markers (AF, MF, S, US)
        /// as vertical dashed lines with labels, and crosshatch idle patterns.
        /// </summary>
        private string BuildSessionAltitudeChart(ReportData data) {
            var images = data.Images;
            if (!images.Any()) return string.Empty;
            if (data.ObserverLatitude == 0 && data.ObserverLongitude == 0) return string.Empty;

            var session = data.Session;
            var sessionStart = session.SessionStart;
            var sessionEnd   = session.SessionEnd;
            double totalSeconds = (sessionEnd - sessionStart).TotalSeconds;
            if (totalSeconds <= 0) return string.Empty;

            bool light = SettingsManager.Instance.Current.ReportLightMode;

            // Event marker colors (match ChartGenerator and EventTimelineGenerator)
            string colorAF   = light ? "#7c3aed" : "#a78bfa";
            string colorFlip = light ? "#d97706" : "#fbbf24";
            string colorSafe = light ? "#059669" : "#34d399";
            string colorUnsafe = light ? "#dc2626" : "#f87171";

            // Idle crosshatch colors (match EventTimelineGenerator)
            string idleBg     = light ? "#d0d4da" : "#0f0f23";
            string idleStripe = light ? "#b04040" : "#7a1a1a";

            // Build target list in chronological order and assign colors
            var targets = images
                .GroupBy(i => i.TargetName)
                .OrderBy(g => g.Min(i => i.Timestamp))
                .Select((g, idx) => (Name: g.Key, Color: PreviewColors[idx % PreviewColors.Length], Images: g.ToList()))
                .ToList();

            var colorMap = targets.ToDictionary(t => t.Name, t => t.Color);

            // Build imaging blocks (same gap-merge logic as EventTimelineGenerator)
            static DateTime EstimatedStart(ImageRecord r) =>
                r.Timestamp.AddSeconds(-(r.ExposureDuration > 0 ? r.ExposureDuration : 60));

            var allBlocks = new List<(string Name, string Color, DateTime Start, DateTime End)>();
            foreach (var target in targets) {
                var sorted = target.Images.OrderBy(i => i.Timestamp).ToList();
                if (!sorted.Any()) continue;
                var blockStart = EstimatedStart(sorted[0]);
                var blockEnd   = sorted[0].Timestamp;
                for (int i = 1; i <= sorted.Count; i++) {
                    if (i < sorted.Count) {
                        var gap = (EstimatedStart(sorted[i]) - blockEnd).TotalMinutes;
                        if (gap <= 15) { blockEnd = sorted[i].Timestamp; continue; }
                    }
                    allBlocks.Add((target.Name, target.Color, blockStart, blockEnd));
                    if (i < sorted.Count) {
                        blockStart = EstimatedStart(sorted[i]);
                        blockEnd   = sorted[i].Timestamp;
                    }
                }
            }
            allBlocks.Sort((a, b) => a.Start.CompareTo(b.Start));

            // Look up RA/Dec from first image per target with valid coords
            var coordLookup = new Dictionary<string, (double Ra, double Dec)>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in targets) {
                var withCoords = target.Images.FirstOrDefault(i => i.RaHours != 0 || i.DecDegrees != 0);
                if (withCoords != null)
                    coordLookup[target.Name] = (withCoords.RaHours, withCoords.DecDegrees);
            }

            // SVG layout — identical to preview altitude chart
            const int svgW = 760, padL = 38, padR = 10, padT = 20, padB = 28;
            int plotW = svgW - padL - padR;
            int plotH = 200;
            int svgH  = padT + plotH + padB;
            const double maxAlt = 90.0;
            double totalMin = (sessionEnd - sessionStart).TotalMinutes;

            double X(DateTime t) => padL + (t - sessionStart).TotalMinutes / totalMin * plotW;
            double Y(double alt)  => padT + plotH - alt / maxAlt * plotH;

            var sb = new StringBuilder();
            sb.AppendLine($"<svg viewBox='0 0 {svgW} {svgH}' xmlns='http://www.w3.org/2000/svg' style='width:100%;font-family:Arial,sans-serif;font-size:10px;'>");

            // Idle crosshatch pattern definition
            sb.AppendLine("<defs>");
            sb.AppendLine("  <pattern id='ns-idle-alt' patternUnits='userSpaceOnUse' width='8' height='8' patternTransform='rotate(45)'>");
            sb.AppendLine($"    <rect width='8' height='8' fill='{idleBg}'/>");
            sb.AppendLine($"    <line x1='0' y1='0' x2='0' y2='8' stroke='{idleStripe}' stroke-width='3'/>");
            sb.AppendLine("  </pattern>");
            sb.AppendLine("</defs>");

            // Background
            sb.AppendLine($"<rect x='{padL}' y='{padT}' width='{plotW}' height='{plotH}' fill='{svgChartBg}' rx='4'/>");
            sb.AppendLine($"<rect x='{padL}' y='{padT}' width='{plotW}' height='{plotH}' fill='none' stroke='{svgBorder}' stroke-width='1' rx='4'/>");

            // Grid lines and altitude axis labels
            foreach (var gridAlt in new[] { 30.0, 60.0 }) {
                double gy = Y(gridAlt);
                sb.AppendLine($"<line x1='{padL}' y1='{gy:F1}' x2='{padL + plotW}' y2='{gy:F1}' stroke='{svgBorder}' stroke-width='1'/>");
                sb.AppendLine($"<text x='{padL - 4}' y='{gy + 4:F1}' text-anchor='end' fill='{svgMuted}'>{gridAlt:F0}°</text>");
            }
            sb.AppendLine($"<text x='{padL - 4}' y='{padT + 4}' text-anchor='end' fill='{svgMuted}'>90°</text>");
            sb.AppendLine($"<text x='{padL - 4}' y='{padT + plotH + 4}' text-anchor='end' fill='{svgMuted}'>0°</text>");

            // Idle crosshatch in gaps between imaging blocks
            var cursor = sessionStart;
            foreach (var block in allBlocks) {
                if (block.Start > cursor) {
                    double gx1 = X(cursor), gx2 = X(block.Start);
                    sb.AppendLine($"<rect x='{gx1:F1}' y='{padT}' width='{(gx2 - gx1):F1}' height='{plotH}' fill='url(#ns-idle-alt)' opacity='0.4'/>");
                }
                if (block.End > cursor) cursor = block.End;
            }
            if (cursor < sessionEnd) {
                double gx1 = X(cursor), gx2 = X(sessionEnd);
                sb.AppendLine($"<rect x='{gx1:F1}' y='{padT}' width='{(gx2 - gx1):F1}' height='{plotH}' fill='url(#ns-idle-alt)' opacity='0.4'/>");
            }

            // Per-target imaging window shading — one vertical band per block
            foreach (var block in allBlocks) {
                var wStart = block.Start < sessionStart ? sessionStart : block.Start;
                var wEnd   = block.End   > sessionEnd   ? sessionEnd   : block.End;
                if (wStart >= wEnd) continue;
                double bx1 = X(wStart), bx2 = X(wEnd);
                sb.AppendLine($"<g><title>{block.Name}&#10;{wStart:HH:mm} – {wEnd:HH:mm}</title>");
                sb.AppendLine($"<rect x='{bx1:F1}' y='{padT}' width='{(bx2 - bx1):F1}' height='{plotH}' fill='{block.Color}' opacity='0.15'/>");
                sb.AppendLine($"<line x1='{bx1:F1}' y1='{padT}' x2='{bx1:F1}' y2='{padT + plotH}' stroke='{block.Color}' stroke-width='1' opacity='0.5'/>");
                sb.AppendLine($"<line x1='{bx2:F1}' y1='{padT}' x2='{bx2:F1}' y2='{padT + plotH}' stroke='{block.Color}' stroke-width='1' opacity='0.5'/>");
                sb.AppendLine("</g>");
            }

            // Moon altitude curve
            if (SettingsManager.Instance.Current.ShowMoonCurve) {
                var moonPts  = AltitudeCalculator.GetMoonAltitudeCurve(data.ObserverLatitude, data.ObserverLongitude, sessionStart, sessionEnd, stepMinutes: 5);
                var moonSegs = BuildAltSegments(moonPts, maxAlt);
                foreach (var seg in moonSegs) {
                    if (seg.Count < 2) continue;
                    var pts = new StringBuilder();
                    foreach (var (t, alt) in seg) pts.Append($"{X(t):F1},{Y(alt):F1} ");
                    sb.AppendLine("<g><title>Moon</title>");
                    sb.AppendLine($"<polyline points='{pts}' fill='none' stroke='transparent' stroke-width='12'/>");
                    sb.AppendLine($"<polyline points='{pts}' fill='none' stroke='{svgMoonStroke}' stroke-width='1.5' stroke-dasharray='5,4' opacity='{svgMoonOpacity}'/>");
                    sb.AppendLine("</g>");
                }
            }

            // Per-target altitude curves
            foreach (var target in targets) {
                if (!coordLookup.TryGetValue(target.Name, out var coords)) continue;
                var altPts = AltitudeCalculator.GetAltitudeCurve(coords.Ra, coords.Dec,
                    data.ObserverLatitude, data.ObserverLongitude,
                    sessionStart, sessionEnd, stepMinutes: 5);
                var segs = BuildAltSegments(altPts, maxAlt);
                sb.AppendLine($"<g><title>{target.Name}</title>");
                foreach (var seg in segs) {
                    if (seg.Count < 2) continue;
                    var pts = new StringBuilder();
                    foreach (var (t, alt) in seg) pts.Append($"{X(t):F1},{Y(alt):F1} ");
                    sb.AppendLine($"<polyline points='{pts}' fill='none' stroke='transparent' stroke-width='10'/>");
                    sb.AppendLine($"<polyline points='{pts}' fill='none' stroke='{target.Color}' stroke-width='2'/>");
                }
                sb.AppendLine("</g>");
            }

            // Event markers — vertical dashed lines with labels at top + tooltips
            var events = data.Events;
            if (events != null) {
                foreach (var evt in events) {
                    if (evt.Timestamp < sessionStart || evt.Timestamp > sessionEnd) continue;
                    double mx = X(evt.Timestamp);
                    if (mx < padL || mx > padL + plotW) continue;
                    var (color, label) = evt.EventType switch {
                        "AutoFocus"    => (colorAF,     "AF"),
                        "MeridianFlip" => (colorFlip,   "MF"),
                        "RoofOpen"     => (colorSafe,   "S"),
                        _              => (colorUnsafe,  "US")
                    };
                    string tipLabel = evt.EventType switch {
                        "RoofOpen"   => "Safety monitor: safe",
                        "RoofClosed" => "Safety monitor: unsafe",
                        _            => evt.Description ?? evt.EventType
                    };
                    string tip = $"{label}: {tipLabel} @ {evt.Timestamp:HH:mm:ss}";
                    sb.AppendLine($"<g><title>{tip}</title>");
                    sb.AppendLine($"<line x1='{mx:F1}' y1='{padT}' x2='{mx:F1}' y2='{padT + plotH}' stroke='{color}' stroke-width='1' stroke-dasharray='4,3' opacity='0.7'/>");
                    sb.AppendLine($"<line x1='{mx:F1}' y1='{padT}' x2='{mx:F1}' y2='{padT + plotH}' stroke='transparent' stroke-width='8'/>");
                    sb.AppendLine($"<text x='{mx:F1}' y='{padT - 4}' fill='{color}' font-size='8' text-anchor='middle' opacity='0.85'>{label}</text>");
                    sb.AppendLine("</g>");
                }
            }

            // Time axis labels — edge labels + adaptive ticks
            int timeLabelY = padT + plotH + 18;
            sb.AppendLine($"<text x='{padL}' y='{timeLabelY}' text-anchor='start' fill='{svgMuted}'>{sessionStart:HH:mm}</text>");
            sb.AppendLine($"<text x='{padL + plotW}' y='{timeLabelY}' text-anchor='end' fill='{svgMuted}'>{sessionEnd:HH:mm}</text>");
            double durationHours = totalSeconds / 3600.0;
            int tickIntervalHrs = durationHours < 4 ? 1 : 2;
            var firstTick = new DateTime(sessionStart.Year, sessionStart.Month, sessionStart.Day, sessionStart.Hour, 0, 0).AddHours(tickIntervalHrs);
            if (firstTick <= sessionStart) firstTick = firstTick.AddHours(tickIntervalHrs);
            for (var tick = firstTick; tick < sessionEnd; tick = tick.AddHours(tickIntervalHrs)) {
                double tx = X(tick);
                if (tx - padL > 30 && (padL + plotW) - tx > 30)
                    sb.AppendLine($"<text x='{tx:F1}' y='{timeLabelY}' text-anchor='middle' fill='{svgMuted}'>{tick:HH:mm}</text>");
            }

            sb.AppendLine("</svg>");

            // Legend — target color chips + event marker legend
            sb.AppendLine("<div style='margin-top:8px;display:flex;flex-wrap:wrap;gap:12px;'>");
            foreach (var target in targets) {
                sb.AppendLine($"<span style='display:inline-flex;align-items:center;gap:6px;font-size:12px;color:var(--muted);'>" +
                              $"<span style='display:inline-block;width:16px;height:3px;background:{target.Color};border-radius:2px;flex-shrink:0;'></span>" +
                              $"{target.Name}</span>");
            }
            // Event type legend chips
            if (events != null) {
                var eventTypes = events
                    .Where(e => e.Timestamp >= sessionStart && e.Timestamp <= sessionEnd)
                    .Select(e => e.EventType).Distinct().ToList();
                foreach (var evtType in eventTypes) {
                    var (c, lbl) = evtType switch {
                        "AutoFocus"    => (colorAF,     "AutoFocus"),
                        "MeridianFlip" => (colorFlip,   "Meridian Flip"),
                        "RoofOpen"     => (colorSafe,   "Safe"),
                        _              => (colorUnsafe,  "Unsafe")
                    };
                    sb.AppendLine($"<span style='display:inline-flex;align-items:center;gap:6px;font-size:12px;color:var(--muted);'>" +
                                  $"<span style='display:inline-block;width:16px;border-top:2px dashed {c};flex-shrink:0;'></span>" +
                                  $"{lbl}</span>");
                }
            }
            sb.AppendLine("</div>");

            return sb.ToString();
        }

        private string BuildFooter() {
            var sb = new StringBuilder();
            sb.AppendLine("<p class='footnote'>CV (Coefficient of Variation) measures consistency as a percentage of the mean. Lower values indicate more stable conditions. Star count CV is calculated per target and filter type.</p>");
            var pluginVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "?";
            var ninaVersion = CoreUtil.Version ?? "?";
            sb.AppendLine($"<p class='footnote'>Generated by Night Summary v{pluginVersion} · N.I.N.A. {ninaVersion} · Created by Evan Pegors (@sleepypuppy15)</p>");
            return sb.ToString();
        }

        internal static string FormatDuration(double seconds) {
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

        internal static string FormatIntegration(double seconds) {
            var ts = TimeSpan.FromSeconds(seconds);
            return ts.TotalHours >= 1 ? $"{ts.TotalHours:F1}h" : $"{ts.TotalMinutes:F0}m";
        }

        /// <summary>
        /// Returns the moon illumination fraction (0–100%) at the given local time.
        /// Also sets <paramref name="waxing"/> to true if the moon is brightening.
        /// Uses a mean-anomaly approximation accurate to ~1–2%.
        /// Reference new moon: 2000-01-06 18:14 UTC (JD 2451549.5).
        /// </summary>
        internal static double MoonIllumination(DateTime localTime, out bool waxing) {
            const double synodicPeriod = 29.53058868;
            var referenceNewMoon = new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);
            var utc = localTime.Kind == DateTimeKind.Utc ? localTime : localTime.ToUniversalTime();
            var daysSinceNew = (utc - referenceNewMoon).TotalDays % synodicPeriod;
            if (daysSinceNew < 0) daysSinceNew += synodicPeriod;
            waxing = daysSinceNew < synodicPeriod / 2.0;
            var phaseAngle = daysSinceNew / synodicPeriod * 2.0 * Math.PI;
            return (1.0 - Math.Cos(phaseAngle)) / 2.0 * 100.0;
        }

        private static double CV(List<double> values) => FilterHelper.CV(values);
        private static double StdDev(List<double> values) => FilterHelper.StdDev(values);

        /// <summary>
        /// Fetches a sky survey thumbnail, trying CDS (color) first, then NASA SkyView (mono) as fallback.
        /// Returns the image as a base64 data URI and whether the fallback was used.
        /// </summary>
        private async Task<(string imgSrc, bool usedFallback)> FetchThumbnailAsync(
            string targetName, double raDeg, double decDeg, int px, double fovDeg) {

            // Primary: CDS HiPS color survey
            try {
                var cdsUrl = $"https://alasky.cds.unistra.fr/hips-image-services/hips2fits?hips=CDS/P/DSS2/color&ra={raDeg:F6}&dec={decDeg:F6}&fov={fovDeg:F6}&width={px}&height={px}&format=jpg";
                var bytes = await Http.GetByteArrayAsync(cdsUrl);
                if (bytes.Length > 500) {
                    Logger.Info($"NightSummary: CDS thumbnail OK for {targetName} ({bytes.Length:N0} bytes)");
                    return ($"data:image/jpeg;base64,{Convert.ToBase64String(bytes)}", false);
                }
                Logger.Warning($"NightSummary: CDS returned tiny response for {targetName} ({bytes.Length} bytes), trying fallback");
            } catch (Exception ex) {
                Logger.Warning($"NightSummary: CDS thumbnail failed for {targetName}: {ex.Message}");
            }

            // Fallback: NASA SkyView DSS2 Red (monochrome but reliable)
            try {
                var svUrl = $"https://skyview.gsfc.nasa.gov/current/cgi/runquery.pl?Position={raDeg:F6},{decDeg:F6}&Survey=DSS2+Red&Pixels={px}&Size={fovDeg:F6}&Return=GIF";
                var bytes = await SkyViewHttp.GetByteArrayAsync(svUrl);
                if (bytes.Length > 500) {
                    Logger.Info($"NightSummary: SkyView fallback OK for {targetName} ({bytes.Length:N0} bytes)");
                    return ($"data:image/gif;base64,{Convert.ToBase64String(bytes)}", true);
                }
                Logger.Warning($"NightSummary: SkyView returned tiny response for {targetName} ({bytes.Length} bytes)");
            } catch (Exception ex) {
                Logger.Warning($"NightSummary: SkyView fallback failed for {targetName}: {ex.Message}");
            }

            // Both failed — return remote URL as last resort
            var remoteUrl = $"https://alasky.cds.unistra.fr/hips-image-services/hips2fits?hips=CDS/P/DSS2/color&ra={raDeg:F6}&dec={decDeg:F6}&fov={fovDeg:F6}&width={px}&height={px}&format=jpg";
            Logger.Warning($"NightSummary: All thumbnail services failed for {targetName}, using remote URL");
            return (remoteUrl, true);
        }
    }
}
