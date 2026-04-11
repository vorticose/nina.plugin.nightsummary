// Night Summary — Metric Chart Renderer
//
// Consumes a ChartModel JSON object (produced by ChartGenerator.BuildChartModel
// in C#) and renders an SVG line chart visually matching what the legacy C#
// SVG generator produced. The same renderer is used by the static HTML report
// (embedded as a script tag + data-chart attributes) and will power the v3
// dashboard (served the same JSON shape via API).
//
// Entry points:
//   NSMetricChart.initAll()  — scans document for [data-chart] containers and renders them
//   NSMetricChart.render(container, model, filter)  — renders one chart
//
// Filter selection is dynamic: the y-axes recompute from the visible subset
// each time the user picks a filter, so per-filter trends use the full chart
// height (option B from the design discussion).

(function () {
    'use strict';

    // ── Palette ─────────────────────────────────────────────────────────────
    // Mirrors the static readonly colors in ChartGenerator.cs. Selected by
    // model.lightMode at render time.
    const PALETTE_DARK = {
        background:   '#1a1a2e',
        grid:         '#2a2a4a',
        axis:         '#555577',
        primary:      '#7eb8f7',
        primaryDot:   '#a8d4ff',
        secondary:    '#f7a87e',
        secondaryDot: '#ffd4a8',
        label:        '#aaaacc',
        warning:      '#f7a87e',
        warningBg:    '#3a1e00',
        afMarker:     '#a78bfa',
        flipMarker:   '#fbbf24',
        safeMarker:   '#34d399',
        unsafeMarker: '#f87171'
    };

    const PALETTE_LIGHT = {
        background:   '#f5f5f5',
        grid:         '#c8cdd4',
        axis:         '#666688',
        primary:      '#2563b8',
        primaryDot:   '#1a4f9e',
        secondary:    '#d47020',
        secondaryDot: '#b85c10',
        label:        '#555577',
        warning:      '#d47020',
        warningBg:    '#fff3cd',
        afMarker:     '#7c3aed',
        flipMarker:   '#d97706',
        safeMarker:   '#059669',
        unsafeMarker: '#dc2626'
    };

    // Layout constants match ChartGenerator.cs
    const PAD_LEFT       = 55;
    const PAD_RIGHT      = 20;
    const PAD_RIGHT_DUAL = 62;
    const PAD_TOP        = 20;
    const PAD_BOTTOM     = 45;

    // ── Number formatting ───────────────────────────────────────────────────
    // Emulates C#'s ToString("F0") / ToString("F1").
    function fmt(value, format) {
        if (format === 'F0') return value.toFixed(0);
        return value.toFixed(1);
    }

    // ── Nice-scale computation (port of ComputeNiceScale in C#) ─────────────
    function computeNiceScale(values, minSpan) {
        let rawMin = Math.min.apply(null, values);
        let rawMax = Math.max.apply(null, values);
        if (rawMax - rawMin < minSpan) {
            const mid = (rawMin + rawMax) / 2.0;
            rawMin = mid - minSpan / 2;
            rawMax = mid + minSpan / 2;
        }
        const range = rawMax - rawMin;
        const rough = range / 4.0;
        const mag   = Math.pow(10, Math.floor(Math.log10(Math.max(rough, 1e-10))));
        const norm  = rough / mag;
        let niceStep;
        if      (norm < 1.5) niceStep = mag;
        else if (norm < 3.5) niceStep = 2 * mag;
        else if (norm < 7.5) niceStep = 5 * mag;
        else                 niceStep = 10 * mag;
        const niceMin = Math.floor(rawMin / niceStep) * niceStep;
        const niceMax = Math.ceil(rawMax  / niceStep) * niceStep;
        return { min: niceMin, max: niceMax, step: niceStep };
    }

    // ── X-axis formatting ───────────────────────────────────────────────────
    // minTime is a Date; xVal is the axis value at that tick.
    function formatXAxisValue(xVal, xAxis, minTime) {
        if (xAxis.mode === 0) {
            // Time — xVal is seconds since minTime
            const t = new Date(minTime.getTime() + xVal * 1000);
            const hh = String(t.getHours()).padStart(2, '0');
            const mm = String(t.getMinutes()).padStart(2, '0');
            return hh + ':' + mm;
        }
        if (xAxis.mode === 1) {
            return String(Math.round(xVal));
        }
        return fmt(xVal, xAxis.format);
    }

    function formatTooltipX(point, xAxis) {
        if (xAxis.mode === 0) {
            const t = new Date(point.timestamp);
            const hh = String(t.getHours()).padStart(2, '0');
            const mm = String(t.getMinutes()).padStart(2, '0');
            const ss = String(t.getSeconds()).padStart(2, '0');
            return hh + ':' + mm + ':' + ss;
        }
        if (xAxis.mode === 1) {
            return '#' + Math.round(point.x);
        }
        return fmt(point.x, xAxis.format) + xAxis.unit;
    }

    // ── XML escaping (for SVG text nodes and title attributes) ──────────────
    function escapeXml(s) {
        if (s == null) return '';
        return String(s)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;');
    }

    // ── Placeholder SVG (matches GeneratePlaceholderSvg in C#) ──────────────
    function renderPlaceholder(width, height, messages, palette) {
        const cx = width / 2;
        const iconY = height / 2 - (messages.length > 1 ? 24 : 18);
        let svg = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ' + width + ' ' + height
            + '" style="width:100%;max-width:' + width + 'px;display:block;margin:0 auto 16px;font-family:sans-serif">';
        svg += '<rect width="' + width + '" height="' + height + '" fill="' + palette.background + '" rx="6"/>';
        svg += '<text x="' + cx + '" y="' + iconY + '" fill="' + palette.warning + '" font-size="22" text-anchor="middle">&#x26A0;</text>';
        for (let i = 0; i < messages.length; i++) {
            const y = iconY + 28 + i * 18;
            svg += '<text x="' + cx + '" y="' + y + '" fill="' + palette.label + '" font-size="12" text-anchor="middle">' + escapeXml(messages[i]) + '</text>';
        }
        svg += '</svg>';
        return svg;
    }

    // ── Main renderer ───────────────────────────────────────────────────────
    // model: ChartModel JSON object
    // activeFilter: null/'' for all filters, or a filter name string
    function renderSvg(model, activeFilter) {
        const palette = model.lightMode ? PALETTE_LIGHT : PALETTE_DARK;
        const W = model.width  || 800;
        const H = model.height || 300;

        // Subset points to the active filter (if any). Empty filter string = All.
        const filterFn = (activeFilter && activeFilter.length > 0)
            ? function (p) { return p.filter === activeFilter; }
            : function () { return true; };

        const primaryPts   = (model.primaryPoints   || []).filter(filterFn);
        const secondaryPts = (model.secondaryPoints || []).filter(filterFn);

        const wantSecondary = model.secondary != null;
        const hasPrimary    = primaryPts.length   >= 2;
        const hasSecondary  = secondaryPts.length >= 2;

        // Both empty → placeholder
        if (!hasPrimary && !hasSecondary) {
            const msgs = [];
            if (model.primary && model.primary.noDataMessage) {
                msgs.push(model.primary.noDataMessage);
            } else {
                msgs.push(filterForMessage(activeFilter, model.primary ? model.primary.label : 'data'));
            }
            if (wantSecondary) {
                if (model.secondary.noDataMessage) {
                    msgs.push(model.secondary.noDataMessage);
                } else {
                    msgs.push(filterForMessage(activeFilter, model.secondary.label));
                }
            }
            return renderPlaceholder(W, H, msgs, palette);
        }

        // Swap: if primary has no data but secondary does, draw secondary on the left axis
        const swapped = !hasPrimary && hasSecondary;
        const leftPts  = swapped ? secondaryPts : primaryPts;
        const rightPts = (!swapped && hasSecondary) ? secondaryPts : [];
        const hasDual  = rightPts.length >= 2;

        const leftMetric = swapped ? model.secondary : model.primary;
        const leftColor      = swapped ? palette.secondary    : palette.primary;
        const leftDotColor   = swapped ? palette.secondaryDot : palette.primaryDot;
        const leftAxisLabel  = leftMetric.axisLabel;

        // Warning badge text (one axis missing)
        let badgeText    = null;
        let badgeSubtext = null;
        if (swapped) {
            badgeText    = model.primary.label + ': no data';
            badgeSubtext = model.primary.noDataHint || filterForMessage(activeFilter, model.primary.label);
        } else if (wantSecondary && !hasSecondary) {
            badgeText    = model.secondary.label + ': no data';
            badgeSubtext = model.secondary.noDataHint || filterForMessage(activeFilter, model.secondary.label);
        }

        const padRight = hasDual ? PAD_RIGHT_DUAL : PAD_RIGHT;
        const plotW    = W - PAD_LEFT - padRight;
        const plotH    = H - PAD_TOP  - PAD_BOTTOM;

        // X range — union of visible points
        const allX = leftPts.map(function (p) { return p.x; }).concat(rightPts.map(function (p) { return p.x; }));
        const minX = Math.min.apply(null, allX);
        const maxX = Math.max.apply(null, allX);
        const xRangeMin = model.xAxis.mode === 1 ? 1 : 0.001;
        const xRange = Math.max(maxX - minX, xRangeMin);

        // Y scales (from visible points only → rescale on filter change)
        const leftMinSpan = leftMetric.minSpan;
        const leftScale   = computeNiceScale(leftPts.map(function (p) { return p.y; }), leftMinSpan);
        const rangeL      = leftScale.max - leftScale.min;

        let rightScale = null;
        let rangeR = 1;
        if (hasDual) {
            rightScale = computeNiceScale(rightPts.map(function (p) { return p.y; }), model.secondary.minSpan);
            rangeR = rightScale.max - rightScale.min;
        }

        function toXPx(v) { return PAD_LEFT + ((v - minX) / xRange) * plotW; }
        function toYL(v)  { return PAD_TOP + plotH - ((v - leftScale.min) / rangeL) * plotH; }
        function toYR(v)  { return PAD_TOP + plotH - ((v - rightScale.min) / rangeR) * plotH; }

        // minTime for time-axis label + event marker formatting
        let minTime = new Date(0);
        if (leftPts.length > 0) {
            minTime = new Date(leftPts[0].timestamp);
            // minTime should correspond to xValue = 0, not the first visible point's time.
            // For Time mode, xValue is seconds from the first unfiltered point — adjust.
            if (model.xAxis.mode === 0) {
                minTime = new Date(minTime.getTime() - leftPts[0].x * 1000);
            }
        }

        let svg = '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ' + W + ' ' + H
            + '" style="width:100%;max-width:' + W + 'px;display:block;margin:0 auto 16px;font-family:sans-serif">';
        svg += '<style>circle, .evt-hit { cursor: pointer; }</style>';
        svg += '<rect width="' + W + '" height="' + H + '" fill="' + palette.background + '" rx="6"/>';

        // Horizontal grid lines + left Y labels
        const leftFmt = leftMetric.format;
        for (let v = leftScale.min; v <= leftScale.max + leftScale.step * 0.001; v += leftScale.step) {
            const y = toYL(v);
            svg += '<line x1="' + PAD_LEFT + '" y1="' + y.toFixed(1) + '" x2="' + (W - padRight) + '" y2="' + y.toFixed(1) + '" stroke="' + palette.grid + '" stroke-width="1"/>';
            svg += '<text x="' + (PAD_LEFT - 6) + '" y="' + (y + 4).toFixed(1) + '" fill="' + palette.label + '" font-size="11" text-anchor="end">' + fmt(v, leftFmt) + '</text>';
        }

        // Right Y axis
        if (hasDual) {
            const rightFmt  = model.secondary.format;
            const rightLineX  = W - padRight;
            const rightLabelX = rightLineX + 6;
            const rightTitleX = W - 10;
            svg += '<line x1="' + rightLineX + '" y1="' + PAD_TOP + '" x2="' + rightLineX + '" y2="' + (PAD_TOP + plotH) + '" stroke="' + palette.axis + '" stroke-width="1"/>';
            for (let v = rightScale.min; v <= rightScale.max + rightScale.step * 0.001; v += rightScale.step) {
                const y = toYR(v);
                svg += '<text x="' + rightLabelX + '" y="' + (y + 4).toFixed(1) + '" fill="' + palette.secondary + '" font-size="11" text-anchor="start">' + fmt(v, rightFmt) + '</text>';
            }
            svg += '<text x="' + rightTitleX + '" y="' + (H / 2) + '" fill="' + palette.secondary + '" font-size="11" text-anchor="middle" transform="rotate(90,' + rightTitleX + ',' + (H / 2) + ')">' + escapeXml(model.secondary.axisLabel) + '</text>';
        }

        // X axis labels
        const pointCount = Math.max(leftPts.length, hasDual ? rightPts.length : 0);
        const xSteps = Math.max(1, Math.min(6, pointCount - 1));
        for (let i = 0; i <= xSteps; i++) {
            const xVal = minX + (xRange / xSteps * i);
            const xPx  = toXPx(xVal);
            svg += '<line x1="' + xPx.toFixed(1) + '" y1="' + PAD_TOP + '" x2="' + xPx.toFixed(1) + '" y2="' + (PAD_TOP + plotH) + '" stroke="' + palette.grid + '" stroke-width="1"/>';
            const xLabel = formatXAxisValue(xVal, model.xAxis, minTime);
            svg += '<text x="' + xPx.toFixed(1) + '" y="' + (H - 10) + '" fill="' + palette.label + '" font-size="11" text-anchor="middle">' + escapeXml(xLabel) + '</text>';
        }

        // Left and bottom axes
        svg += '<line x1="' + PAD_LEFT + '" y1="' + PAD_TOP + '" x2="' + PAD_LEFT + '" y2="' + (PAD_TOP + plotH) + '" stroke="' + palette.axis + '" stroke-width="1"/>';
        svg += '<line x1="' + PAD_LEFT + '" y1="' + (PAD_TOP + plotH) + '" x2="' + (W - padRight) + '" y2="' + (PAD_TOP + plotH) + '" stroke="' + palette.axis + '" stroke-width="1"/>';

        // Left Y axis title
        const leftTitleColor = swapped ? palette.secondary : palette.label;
        svg += '<text x="14" y="' + (H / 2) + '" fill="' + leftTitleColor + '" font-size="11" text-anchor="middle" transform="rotate(-90,14,' + (H / 2) + ')">' + escapeXml(leftAxisLabel) + '</text>';

        // X axis title (for non-time axes)
        if (model.xAxis.mode !== 0 && model.xAxis.axisLabel) {
            svg += '<text x="' + (PAD_LEFT + plotW / 2) + '" y="' + (H - 2) + '" fill="' + palette.label + '" font-size="10" text-anchor="middle">' + escapeXml(model.xAxis.axisLabel) + '</text>';
        }

        // Event markers (Time x-axis only, drawn before data lines so points render on top)
        if (model.xAxis.mode === 0 && model.eventMarkers && model.eventMarkers.length > 0) {
            for (const evt of model.eventMarkers) {
                const evtX = evt.xValue;
                if (evtX < minX || evtX > maxX) continue;
                const xPx = toXPx(evtX);
                let color, label;
                if (evt.type === 'AutoFocus')     { color = palette.afMarker;   label = 'AF'; }
                else if (evt.type === 'MeridianFlip') { color = palette.flipMarker; label = 'MF'; }
                else if (evt.type === 'RoofOpen')     { color = palette.safeMarker; label = 'S';  }
                else                                  { color = palette.unsafeMarker; label = 'US'; }

                const tsStr = new Date(evt.timestamp).toTimeString().slice(0, 8);
                const tip   = label + ': ' + escapeXml(evt.description || evt.type) + ' @ ' + tsStr;
                svg += '<line x1="' + xPx.toFixed(1) + '" y1="' + PAD_TOP + '" x2="' + xPx.toFixed(1) + '" y2="' + (PAD_TOP + plotH) + '" stroke="' + color + '" stroke-width="1" stroke-dasharray="4,3" opacity="0.7"/>';
                svg += '<line class="evt-hit" x1="' + xPx.toFixed(1) + '" y1="' + PAD_TOP + '" x2="' + xPx.toFixed(1) + '" y2="' + (PAD_TOP + plotH) + '" stroke="transparent" stroke-width="8"><title>' + tip + '</title></line>';
                svg += '<text x="' + xPx.toFixed(1) + '" y="' + (PAD_TOP - 4) + '" fill="' + color + '" font-size="8" text-anchor="middle" opacity="0.85">' + label + '</text>';
            }
        }

        // Secondary line (drawn first so primary renders on top)
        if (hasDual) {
            const rightPoly = rightPts.map(function (p) { return toXPx(p.x).toFixed(1) + ',' + toYR(p.y).toFixed(1); }).join(' ');
            svg += '<polyline points="' + rightPoly + '" fill="none" stroke="' + palette.secondary + '" stroke-width="2" stroke-linejoin="round" stroke-dasharray="6,3"/>';
            const secUnit = model.secondary.unit;
            const secFmt  = model.secondary.format;
            for (const p of rightPts) {
                const tip = formatTooltipX(p, model.xAxis) + ' \u2014 ' + fmt(p.y, secFmt) + secUnit;
                svg += '<circle cx="' + toXPx(p.x).toFixed(1) + '" cy="' + toYR(p.y).toFixed(1) + '" r="3" fill="' + palette.secondaryDot + '"><title>' + escapeXml(tip) + '</title></circle>';
            }
        }

        // Primary line
        const leftPoly = leftPts.map(function (p) { return toXPx(p.x).toFixed(1) + ',' + toYL(p.y).toFixed(1); }).join(' ');
        svg += '<polyline points="' + leftPoly + '" fill="none" stroke="' + leftColor + '" stroke-width="2" stroke-linejoin="round"/>';
        const leftUnit = leftMetric.unit;
        const leftTipFmt = leftMetric.format;
        for (const p of leftPts) {
            const tip = formatTooltipX(p, model.xAxis) + ' \u2014 ' + fmt(p.y, leftTipFmt) + leftUnit;
            svg += '<circle cx="' + toXPx(p.x).toFixed(1) + '" cy="' + toYL(p.y).toFixed(1) + '" r="3" fill="' + leftDotColor + '"><title>' + escapeXml(tip) + '</title></circle>';
        }

        // Warning badge
        if (badgeText) {
            const bx = PAD_LEFT + 8;
            const by = PAD_TOP  + 6;
            const neededW = Math.max(
                badgeText.length * 6.5 + 34,
                (badgeSubtext ? badgeSubtext.length : 0) * 5.7 + 14);
            const bw = Math.min(plotW - 16, Math.max(neededW, 180));
            const bh = badgeSubtext ? 32 : 20;
            svg += '<rect x="' + bx + '" y="' + by + '" width="' + bw + '" height="' + bh + '" rx="3" fill="' + palette.warningBg + '" stroke="' + palette.warning + '" stroke-width="1" opacity="0.92"/>';
            svg += '<text x="' + (bx + 7) + '" y="' + (by + 14) + '" fill="' + palette.warning + '" font-size="11">&#x26A0; ' + escapeXml(badgeText) + '</text>';
            if (badgeSubtext)
                svg += '<text x="' + (bx + 7) + '" y="' + (by + 27) + '" fill="' + palette.warning + '" font-size="10" opacity="0.8">' + escapeXml(badgeSubtext) + '</text>';
        }

        svg += '</svg>';
        return svg;
    }

    function filterForMessage(activeFilter, metricLabel) {
        if (activeFilter && activeFilter.length > 0)
            return 'No ' + metricLabel + ' data for filter ' + activeFilter;
        return 'No ' + metricLabel + ' data available';
    }

    // ── Filter selector UI ──────────────────────────────────────────────────
    function buildFilterSelector(container, model, onChange) {
        const filters = model.filters || [];
        if (filters.length < 2) return null;  // No selector when there's only one filter (or none)

        const bar = document.createElement('div');
        bar.className = 'ns-chart-filter-bar';

        const allBtn = makeFilterButton('All', '');
        allBtn.classList.add('active');
        bar.appendChild(allBtn);

        for (const f of filters) {
            bar.appendChild(makeFilterButton(f, f));
        }

        bar.addEventListener('click', function (e) {
            const btn = e.target.closest('.ns-chart-filter-btn');
            if (!btn) return;
            const filter = btn.getAttribute('data-filter') || '';
            // Toggle active class
            bar.querySelectorAll('.ns-chart-filter-btn').forEach(function (b) { b.classList.remove('active'); });
            btn.classList.add('active');
            onChange(filter);
        });

        return bar;
    }

    function makeFilterButton(label, value) {
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'ns-chart-filter-btn';
        btn.setAttribute('data-filter', value);
        btn.textContent = label;
        return btn;
    }

    // ── Public API ──────────────────────────────────────────────────────────
    function render(container, model) {
        container.innerHTML = '';
        let currentFilter = '';

        const onFilterChange = function (filter) {
            currentFilter = filter;
            svgHost.innerHTML = renderSvg(model, currentFilter);
        };

        const filterBar = buildFilterSelector(container, model, onFilterChange);
        if (filterBar) container.appendChild(filterBar);

        const svgHost = document.createElement('div');
        svgHost.className = 'ns-chart-svg';
        svgHost.innerHTML = renderSvg(model, currentFilter);
        container.appendChild(svgHost);
    }

    function initAll(root) {
        root = root || document;
        const nodes = root.querySelectorAll('[data-chart]');
        nodes.forEach(function (node) {
            const raw = node.getAttribute('data-chart');
            if (!raw) return;
            let model;
            try {
                model = JSON.parse(raw);
            } catch (err) {
                node.innerHTML = '<div style="color:#f87171;font-family:sans-serif;padding:8px;">Chart data error: ' + escapeXml(err.message) + '</div>';
                return;
            }
            render(node, model);
        });
    }

    // Expose
    window.NSMetricChart = {
        render: render,
        initAll: initAll,
        // Exposed for tests / debugging
        _renderSvg: renderSvg,
        _computeNiceScale: computeNiceScale
    };

    // Auto-init on DOMContentLoaded (and on immediate call if DOM is already ready)
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { initAll(); });
    } else {
        initAll();
    }
})();
