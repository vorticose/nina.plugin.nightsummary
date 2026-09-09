using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace NINA.Plugin.NightSummary.Server {
    /// <summary>
    /// Builds the dashboard session-card altitude chart from a report HTML file.
    ///
    /// Prefer the Session Timeline altitude SVG (the same chart the session page
    /// shows) so a target imaged in two windows — for example before and after a
    /// roof close — appears in both views. Older reports without that SVG fall
    /// back to compositing the per-target <c>altitude-chart</c> SVGs, taking
    /// every imaging-window rect and every above-horizon polyline rather than
    /// just the first of each.
    /// </summary>
    internal static class AltitudeChartComposer {
        public const int CacheVersion = 2;

        // Widen the fallback composite from the per-target 500-wide plot to a
        // 950-wide viewBox so the card chart isn't letterboxed.
        private const double AltPadL = 38.0;
        private const double AltOrigRight = 490.0;
        private const double AltNewSvgW = 950.0;
        private const double AltNewRight = 940.0;
        private static readonly double AltScaleX = (AltNewRight - AltPadL) / (AltOrigRight - AltPadL);

        internal static readonly string[] TargetColors = {
            "#4e79a7", "#f28e2b", "#e15759", "#76b7b2", "#59a14f", "#edc948"
        };

        internal sealed class Result {
            public string Svg { get; init; } = "";
            public IReadOnlyList<LegendItem> Legend { get; init; } = Array.Empty<LegendItem>();
        }

        internal sealed class LegendItem {
            public string Name { get; init; } = "";
            public string Color { get; init; } = "";
        }

        public static Result FromReportHtml(string html, string sessionId) {
            if (string.IsNullOrEmpty(html)) return Empty();

            var sessionSvg = TryExtractSessionTimelineSvg(html, sessionId);
            if (sessionSvg != null)
                return new Result { Svg = sessionSvg, Legend = LegendFromSessionSvg(sessionSvg) };

            return ComposeFromPerTargetCharts(html);
        }

        public static Result Empty() => new Result { Svg = "", Legend = Array.Empty<LegendItem>() };

        public static bool IsCurrentCache(string json) {
            if (string.IsNullOrEmpty(json)) return false;
            try {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                return doc.RootElement.TryGetProperty("v", out var v)
                    && v.ValueKind == System.Text.Json.JsonValueKind.Number
                    && v.GetInt32() >= CacheVersion;
            } catch {
                return false;
            }
        }

        // ── Session Timeline SVG (preferred) ────────────────────────────────

        private static readonly Regex SessionAltDivPattern = new Regex(
            @"<div class=""ns-chart-svg"" id=""[^""]*-svg-altitude""[^>]*>\s*(<svg[\s\S]*?</svg>)",
            RegexOptions.CultureInvariant);

        private static string? TryExtractSessionTimelineSvg(string html, string sessionId) {
            var match = SessionAltDivPattern.Match(html);
            if (!match.Success) return null;
            var svg = match.Groups[1].Value;
            if (svg.Length < 50) return null;

            svg = RemapLightToDark(svg);
            svg = Regex.Replace(svg, @"\sstyle='[^']*'", "");
            if (svg.IndexOf("preserveAspectRatio", StringComparison.OrdinalIgnoreCase) < 0)
                svg = svg.Replace("<svg ", "<svg preserveAspectRatio='none' ");

            var suffix = SanitizeId(sessionId);
            svg = svg.Replace("id='ns-idle-alt'", $"id='ns-idle-alt-{suffix}'")
                     .Replace("url(#ns-idle-alt)", $"url(#ns-idle-alt-{suffix})");
            svg = svg.Replace("<title>Moon</title>", "<title>Moon Position</title>");
            return svg;
        }

        private static List<LegendItem> LegendFromSessionSvg(string svg) {
            var items = new List<LegendItem>();
            var groupRe = new Regex(@"<g><title>([^<]+)</title>([\s\S]*?)</g>");
            foreach (Match m in groupRe.Matches(svg)) {
                var name = m.Groups[1].Value.Trim();
                if (name.Equals("Moon", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("Moon Position", StringComparison.OrdinalIgnoreCase))
                    continue;
                var body = m.Groups[2].Value;
                if (body.IndexOf("<polyline", StringComparison.Ordinal) < 0) continue;
                var colorMatch = Regex.Match(body, @"stroke='(#[0-9a-fA-F]{3,8})' stroke-width='2'");
                var color = colorMatch.Success
                    ? colorMatch.Groups[1].Value
                    : TargetColors[items.Count % TargetColors.Length];
                items.Add(new LegendItem { Name = name, Color = color });
            }
            return items;
        }

        // ── Per-target composite (fallback for older reports) ───────────────

        private static readonly Regex H3Pattern = new Regex(@"<h3>([^<]+)");
        private static readonly Regex SvgPattern = new Regex(
            @"<svg class='altitude-chart'.*?</svg>", RegexOptions.Singleline);
        private static readonly Regex PolylineDark = new Regex(
            @"<polyline points='([^']+)' fill='none' stroke='#7eb8f7' stroke-width='2'/>");
        private static readonly Regex PolylineLight = new Regex(
            @"<polyline points='([^']+)' fill='none' stroke='#2563b8' stroke-width='2'/>");
        private static readonly Regex RectDark = new Regex(
            @"<rect x='([\d.]+)' y='\d+' width='([\d.]+)' height='\d+' fill='#7eb8f7' opacity='0\.07'/>");
        private static readonly Regex RectLight = new Regex(
            @"<rect x='([\d.]+)' y='\d+' width='([\d.]+)' height='\d+' fill='#2563b8' opacity='0\.07'/>");

        private static Result ComposeFromPerTargetCharts(string html) {
            var sections = html.Split(new[] { "<div class='target-section'>" }, StringSplitOptions.None);
            var targetData = new List<(string Name, List<string> Polylines, List<(double X, double W)> Windows)>();
            string? scaffoldSvg = null;

            for (int i = 1; i < sections.Length; i++) {
                var block = sections[i];
                var h3Match = H3Pattern.Match(block);
                var svgMatch = SvgPattern.Match(block);
                if (!h3Match.Success || !svgMatch.Success) continue;

                var targetName = h3Match.Groups[1].Value.Trim();
                var svgContent = svgMatch.Value;
                if (scaffoldSvg == null) scaffoldSvg = svgContent;

                var polyMatches = PolylineDark.Matches(svgContent);
                if (polyMatches.Count == 0) polyMatches = PolylineLight.Matches(svgContent);
                if (polyMatches.Count == 0) continue;

                var rectMatches = RectDark.Matches(svgContent);
                if (rectMatches.Count == 0) rectMatches = RectLight.Matches(svgContent);

                var windows = new List<(double X, double W)>(rectMatches.Count);
                foreach (Match r in rectMatches) {
                    windows.Add((
                        double.Parse(r.Groups[1].Value, CultureInfo.InvariantCulture),
                        double.Parse(r.Groups[2].Value, CultureInfo.InvariantCulture)));
                }

                var polylines = new List<string>(polyMatches.Count);
                foreach (Match p in polyMatches)
                    polylines.Add(p.Groups[1].Value);

                targetData.Add((targetName, polylines, windows));
            }

            if (targetData.Count == 0 || scaffoldSvg == null) return Empty();

            scaffoldSvg = RemapLightToDark(scaffoldSvg);

            var inv = CultureInfo.InvariantCulture;
            const int vbTopTrim = 14;
            const int vbBotTrim = 2;
            var viewBoxMatch = Regex.Match(scaffoldSvg, @"viewBox='[\d.]+ [\d.]+ [\d.]+ ([\d.]+)'");
            int origH = viewBoxMatch.Success ? (int)double.Parse(viewBoxMatch.Groups[1].Value, inv) : 248;
            var viewBoxY = vbTopTrim.ToString();
            var viewBoxH = (origH - vbTopTrim - vbBotTrim).ToString();

            var moonPattern = new Regex(@"<g><title>Moon Position</title>.*?</g>", RegexOptions.Singleline);
            var timeLabelPattern = new Regex(@"<text[^>]*fill='#888'[^>]*>\d{2}:\d{2}</text>");

            var sb = new StringBuilder();
            sb.AppendLine($"<svg viewBox='0 {viewBoxY} {AltNewSvgW.ToString("F0", inv)} {viewBoxH}' xmlns='http://www.w3.org/2000/svg' preserveAspectRatio='none'>");

            var bgRects = Regex.Matches(scaffoldSvg, @"<rect x='38'[^/]*/>");
            foreach (Match r in bgRects) {
                var rect = Regex.Replace(r.Value, @"width='([\d.]+)'", m => {
                    if (double.TryParse(m.Groups[1].Value, NumberStyles.Float, inv, out double w))
                        return $"width='{(w * AltScaleX).ToString("F1", inv)}'";
                    return m.Value;
                });
                sb.AppendLine(rect);
            }

            for (int t = 0; t < targetData.Count; t++) {
                var td = targetData[t];
                var color = TargetColors[t % TargetColors.Length];
                foreach (var (sessX, sessW) in td.Windows) {
                    if (sessW <= 0) continue;
                    var sx = MapX(sessX).ToString("F1", inv);
                    var sw = (sessW * AltScaleX).ToString("F1", inv);
                    sb.AppendLine($"<rect x='{sx}' y='20' width='{sw}' height='200' fill='{color}' opacity='0.15'/>");
                    var endX = MapX(sessX + sessW).ToString("F1", inv);
                    sb.AppendLine($"<line x1='{sx}' y1='20' x2='{sx}' y2='220' stroke='{color}' stroke-width='1' opacity='0.6'/>");
                    sb.AppendLine($"<line x1='{endX}' y1='20' x2='{endX}' y2='220' stroke='{color}' stroke-width='1' opacity='0.6'/>");
                }
            }

            var gridLines = Regex.Matches(scaffoldSvg, @"<line x1='38'[^/]*/>");
            foreach (Match g in gridLines) {
                if (g.Value.Contains("#cc4444")) continue;
                var line = Regex.Replace(g.Value, @"x2='([\d.]+)'", m => {
                    if (double.TryParse(m.Groups[1].Value, NumberStyles.Float, inv, out double x))
                        return $"x2='{MapX(x).ToString("F1", inv)}'";
                    return m.Value;
                });
                sb.AppendLine(line);
            }

            var axisLabels = Regex.Matches(scaffoldSvg, @"<text x='34'[^>]*>[^<]*</text>");
            foreach (Match a in axisLabels) sb.AppendLine(a.Value);

            for (int t = 0; t < targetData.Count; t++) {
                var td = targetData[t];
                var color = TargetColors[t % TargetColors.Length];
                sb.AppendLine($"<g><title>{td.Name}</title>");
                foreach (var points in td.Polylines) {
                    var scaledPoints = ScalePolylineX(points);
                    sb.AppendLine($"<polyline points='{scaledPoints}' fill='none' stroke='transparent' stroke-width='10'/>");
                    sb.AppendLine($"<polyline points='{scaledPoints}' fill='none' stroke='{color}' stroke-width='2'/>");
                }
                sb.AppendLine("</g>");
            }

            var moonMatch = moonPattern.Match(scaffoldSvg);
            if (moonMatch.Success) {
                var moonSvg = Regex.Replace(moonMatch.Value, @"points='([^']+)'", m => {
                    return $"points='{ScalePolylineX(m.Groups[1].Value)}'";
                });
                sb.AppendLine(moonSvg);
            }

            foreach (Match t in timeLabelPattern.Matches(scaffoldSvg)) sb.AppendLine(RemapSvgX(t.Value));

            sb.AppendLine("</svg>");

            var legend = targetData.Select((td, i) => new LegendItem {
                Name = td.Name,
                Color = TargetColors[i % TargetColors.Length]
            }).ToList();

            return new Result { Svg = sb.ToString(), Legend = legend };
        }

        private static string RemapLightToDark(string svg) => svg
            .Replace("#e8eef5", "#0d1117")
            .Replace("#c0c8d4", "#2d2d5e")
            .Replace("fill='#666'", "fill='#888'")
            .Replace("stroke='#2563b8'", "stroke='#7eb8f7'")
            .Replace("fill='#2563b8'", "fill='#7eb8f7'")
            .Replace("#7a8a9e", "#c0c0c0")
            .Replace("#c07a00", "#f59e0b")
            .Replace("opacity='0.75'", "opacity='0.45'")
            .Replace("#d0d4da", "#0f0f23")
            .Replace("#b04040", "#7a1a1a");

        private static double MapX(double x) => AltPadL + (x - AltPadL) * AltScaleX;

        private static string ScalePolylineX(string points) {
            var parts = points.Split(' ');
            var sb = new StringBuilder(points.Length * 2);
            foreach (var part in parts) {
                if (sb.Length > 0) sb.Append(' ');
                var comma = part.IndexOf(',');
                if (comma < 0) { sb.Append(part); continue; }
                if (double.TryParse(part.Substring(0, comma), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double x)) {
                    sb.Append(MapX(x).ToString("F1", CultureInfo.InvariantCulture));
                    sb.Append(part.Substring(comma));
                } else {
                    sb.Append(part);
                }
            }
            return sb.ToString();
        }

        private static string RemapSvgX(string element) {
            return Regex.Replace(element, @"x='([\d.]+)'", m => {
                if (double.TryParse(m.Groups[1].Value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double x) && x >= AltPadL) {
                    return $"x='{MapX(x).ToString("F1", CultureInfo.InvariantCulture)}'";
                }
                return m.Value;
            });
        }

        internal static string SanitizeId(string sessionId) {
            if (string.IsNullOrEmpty(sessionId)) return "x";
            var sb = new StringBuilder(sessionId.Length);
            foreach (var c in sessionId)
                sb.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            return sb.Length == 0 ? "x" : sb.ToString();
        }
    }
}
