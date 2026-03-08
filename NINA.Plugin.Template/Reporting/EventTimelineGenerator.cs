using NINA.Plugin.NightSummary.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace NINA.Plugin.NightSummary.Reporting {
    /// <summary>
    /// Generates an inline SVG session event timeline showing target imaging bands
    /// and markers for autofocus runs, safety monitor events, and meridian flips.
    /// Includes a ruler-style time axis and interactive hover tooltips.
    /// </summary>
    public static class EventTimelineGenerator {

        // Palette of distinct colors for target bands (cycles if more than 6 targets)
        private static readonly string[] TargetColors = {
            "#4e79a7", "#f28e2b", "#e15759", "#76b7b2", "#59a14f", "#edc948"
        };

        // Colors for event markers
        private const string ColorAutoFocus    = "#a78bfa"; // purple
        private const string ColorRoofOpen     = "#34d399"; // green
        private const string ColorRoofClosed   = "#f87171"; // red
        private const string ColorMeridianFlip = "#fbbf24"; // amber

        public static string GenerateTimeline(
            SessionRecord session,
            List<ImageRecord> images,
            List<SessionEvent> events) {

            if (!images.Any()) return string.Empty;

            const int svgWidth    = 760;
            const int trackHeight = 24;
            const int markerSize  = 10;
            const int legendRowH  = 20;
            const int topPad      = 10;
            const int leftPad     = 8;
            const int rightPad    = 8;
            const int barAreaW    = svgWidth - leftPad - rightPad;

            // Time span of the whole session
            var sessionStart = session.SessionStart;
            var sessionEnd   = session.SessionEnd;
            var totalSeconds = (sessionEnd - sessionStart).TotalSeconds;
            if (totalSeconds <= 0) return string.Empty;

            double TimeToX(DateTime t) =>
                leftPad + (t - sessionStart).TotalSeconds / totalSeconds * barAreaW;

            // Build target list in chronological order of first image
            var targets = images
                .GroupBy(i => i.TargetName)
                .OrderBy(g => g.Min(i => i.Timestamp))
                .Select((g, idx) => (Name: g.Key, Color: TargetColors[idx % TargetColors.Length], Images: g.ToList()))
                .ToList();

            var eventTypesPresent = events.Select(e => e.EventType).Distinct().ToList();

            int trackY    = topPad;
            int rulerH    = 28;  // tick lines (6px) + gap + label (~11px) + breathing room
            int legendTop = trackY + trackHeight + rulerH + 8;

            // Pre-calculate legend height so viewBox is correct
            int legendHeight = 18 + targets.Count * legendRowH;
            if (eventTypesPresent.Any())
                legendHeight += 6 + 18 + eventTypesPresent.Count * legendRowH;
            int svgHeight = legendTop + legendHeight + 10;

            var sb = new StringBuilder();
            sb.AppendLine("<h2>Session Timeline</h2>");
            sb.AppendLine("<div class='timeline-container' style='position:relative;'>");

            // Floating tooltip div — positioned by JS on mousemove
            sb.AppendLine("<div id='ns-tooltip' style='display:none;position:fixed;background:#1e1e2e;color:#e0e0e0;padding:6px 10px;border-radius:6px;font-size:12px;font-family:Arial,sans-serif;pointer-events:none;box-shadow:0 2px 8px rgba(0,0,0,0.6);z-index:9999;white-space:nowrap;'></div>");

            sb.AppendLine($"<svg viewBox='0 0 {svgWidth} {svgHeight}' xmlns='http://www.w3.org/2000/svg' style='width:100%;font-family:Arial,sans-serif;font-size:11px;'>");

            // Diagonal stripe pattern for idle (no imaging) periods
            sb.AppendLine("<defs>");
            sb.AppendLine("  <pattern id='ns-idle' patternUnits='userSpaceOnUse' width='8' height='8' patternTransform='rotate(45)'>");
            sb.AppendLine("    <rect width='8' height='8' fill='#0f0f23'/>");
            sb.AppendLine("    <line x1='0' y1='0' x2='0' y2='8' stroke='#7a1a1a' stroke-width='3'/>");
            sb.AppendLine("  </pattern>");
            sb.AppendLine("</defs>");

            // Build all imaging blocks across all targets before rendering,
            // so we can compute idle gaps and hatch only those regions.
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
                        if (gap <= 15) {
                            blockEnd = sorted[i].Timestamp;
                            continue;
                        }
                    }
                    allBlocks.Add((target.Name, target.Color, blockStart, blockEnd));
                    if (i < sorted.Count) {
                        blockStart = EstimatedStart(sorted[i]);
                        blockEnd   = sorted[i].Timestamp;
                    }
                }
            }
            allBlocks.Sort((a, b) => a.Start.CompareTo(b.Start));

            // Solid dark background track
            sb.AppendLine($"<rect x='{leftPad}' y='{trackY}' width='{barAreaW}' height='{trackHeight}' rx='4' fill='#0f0f23' />");

            // Hatch only the idle gaps (session start→first block, between blocks, last block→session end)
            var cursor = sessionStart;
            foreach (var block in allBlocks) {
                if (block.Start > cursor) {
                    double gx1 = TimeToX(cursor);
                    double gx2 = TimeToX(block.Start);
                    sb.AppendLine($"<rect x='{gx1:F1}' y='{trackY}' width='{(gx2 - gx1):F1}' height='{trackHeight}' fill='url(#ns-idle)' />");
                }
                if (block.End > cursor) cursor = block.End;
            }
            if (cursor < sessionEnd) {
                double gx1 = TimeToX(cursor);
                double gx2 = TimeToX(sessionEnd);
                sb.AppendLine($"<rect x='{gx1:F1}' y='{trackY}' width='{(gx2 - gx1):F1}' height='{trackHeight}' fill='url(#ns-idle)' />");
            }

            // Render colored imaging bands on top
            foreach (var block in allBlocks) {
                double x1 = TimeToX(block.Start);
                double x2 = TimeToX(block.End);
                double w  = Math.Max(x2 - x1, 2);
                sb.AppendLine($"<rect x='{x1:F1}' y='{trackY}' width='{w:F1}' height='{trackHeight}' fill='{block.Color}' opacity='0.85'>");
                sb.AppendLine($"  <title>{block.Name}: {block.Start:HH:mm} \u2013 {block.End:HH:mm}</title>");
                sb.AppendLine("</rect>");
            }

            // Event markers — triangles with data-tip for hover tooltips
            int markerY = trackY + trackHeight / 2;
            foreach (var evt in events) {
                if (evt.Timestamp < sessionStart || evt.Timestamp > sessionEnd) continue;

                double mx = TimeToX(evt.Timestamp);
                string markerColor = evt.EventType switch {
                    "AutoFocus"    => ColorAutoFocus,
                    "RoofOpen"     => ColorRoofOpen,
                    "RoofClosed"   => ColorRoofClosed,
                    "MeridianFlip" => ColorMeridianFlip,
                    _              => "#ffffff"
                };

                int half = markerSize / 2;
                // Escape single quotes in description to avoid breaking the attribute
                string tipText = $"{evt.Timestamp:HH:mm} \u2014 {evt.Description?.Replace("'", "\u2019") ?? ""}";
                sb.AppendLine($"<polygon points='{mx:F1},{markerY - half} {mx - half:F1},{markerY + half} {mx + half:F1},{markerY + half}' fill='{markerColor}' opacity='0.95' data-tip='{tipText}' style='cursor:pointer;'>");
                sb.AppendLine($"  <title>{evt.Timestamp:HH:mm} \u2013 {evt.Description}</title>");
                sb.AppendLine("</polygon>");
            }

            // ── Ruler-style time axis ────────────────────────────────────────────
            int rulerY     = trackY + trackHeight;
            int tickH      = 6;
            int tickLabelY = rulerY + 20;

            // Baseline rule
            sb.AppendLine($"<line x1='{leftPad}' y1='{rulerY}' x2='{svgWidth - rightPad}' y2='{rulerY}' stroke='#444' stroke-width='1'/>");

            // Adaptive tick interval
            double durationHours  = totalSeconds / 3600.0;
            int    tickIntervalMins = durationHours < 2 ? 15 : durationHours < 5 ? 30 : 60;

            // First aligned tick strictly after sessionStart
            var firstTick = new DateTime(sessionStart.Year, sessionStart.Month, sessionStart.Day, sessionStart.Hour, 0, 0);
            while (firstTick <= sessionStart)
                firstTick = firstTick.AddMinutes(tickIntervalMins);

            var tick = firstTick;
            while (tick < sessionEnd) {
                double tx = TimeToX(tick);
                // Suppress ticks that would collide with the start/end edge labels (within 40px)
                if (tx - leftPad > 40 && (svgWidth - rightPad) - tx > 40) {
                    sb.AppendLine($"<line x1='{tx:F1}' y1='{rulerY}' x2='{tx:F1}' y2='{rulerY + tickH}' stroke='#555' stroke-width='1'/>");
                    sb.AppendLine($"<text x='{tx:F1}' y='{tickLabelY}' fill='#888' text-anchor='middle'>{tick:HH:mm}</text>");
                }
                tick = tick.AddMinutes(tickIntervalMins);
            }

            // Session start / end edge labels
            sb.AppendLine($"<text x='{leftPad}' y='{tickLabelY}' fill='#888'>{sessionStart:HH:mm}</text>");
            sb.AppendLine($"<text x='{svgWidth - rightPad}' y='{tickLabelY}' fill='#888' text-anchor='end'>{sessionEnd:HH:mm}</text>");

            // ── Legend ───────────────────────────────────────────────────────────
            int ly = legendTop;

            // Targets section
            sb.AppendLine($"<text x='{leftPad}' y='{ly + 12}' fill='#aaa' font-weight='bold'>Targets</text>");
            ly += 18;
            foreach (var target in targets) {
                sb.AppendLine($"<rect x='{leftPad}' y='{ly}' width='14' height='12' fill='{target.Color}' rx='2'/>");
                sb.AppendLine($"<text x='{leftPad + 18}' y='{ly + 10}' fill='#e0e0e0'>{target.Name}</text>");
                ly += legendRowH;
            }

            // Events section
            if (eventTypesPresent.Any()) {
                ly += 6;
                sb.AppendLine($"<text x='{leftPad}' y='{ly + 12}' fill='#aaa' font-weight='bold'>Events</text>");
                ly += 18;
                foreach (var evtType in eventTypesPresent) {
                    string c = evtType switch {
                        "AutoFocus"    => ColorAutoFocus,
                        "RoofOpen"     => ColorRoofOpen,
                        "RoofClosed"   => ColorRoofClosed,
                        "MeridianFlip" => ColorMeridianFlip,
                        _              => "#ffffff"
                    };
                    string label = evtType switch {
                        "AutoFocus"    => "AutoFocus run",
                        "RoofOpen"     => "Roof opened (safe)",
                        "RoofClosed"   => "Roof closed (unsafe)",
                        "MeridianFlip" => "Meridian flip",
                        _              => evtType
                    };
                    int half = markerSize / 2;
                    int mx2  = leftPad + half;
                    sb.AppendLine($"<polygon points='{mx2},{ly} {mx2 - half},{ly + markerSize} {mx2 + half},{ly + markerSize}' fill='{c}'/>");
                    sb.AppendLine($"<text x='{leftPad + 18}' y='{ly + 10}' fill='#e0e0e0'>{label}</text>");
                    ly += legendRowH;
                }
            }

            sb.AppendLine("</svg>");

            // Inline JS — wires up hover tooltips for all [data-tip] SVG elements
            sb.AppendLine(@"<script>
(function() {
  var tip = document.getElementById('ns-tooltip');
  if (!tip) return;
  document.querySelectorAll('[data-tip]').forEach(function(el) {
    el.addEventListener('mousemove', function(e) {
      tip.textContent = el.getAttribute('data-tip');
      tip.style.display = 'block';
      tip.style.left = (e.clientX + 14) + 'px';
      tip.style.top  = (e.clientY - 36) + 'px';
    });
    el.addEventListener('mouseout', function() {
      tip.style.display = 'none';
    });
  });
})();
</script>");

            sb.AppendLine("</div>");
            return sb.ToString();
        }
    }
}
