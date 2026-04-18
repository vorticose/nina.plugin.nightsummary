// ── Night Summary Dashboard ──

// ── Logging ───────────────────────────────────────────────────────────────

var LOG_PREFIX = '[NightSummary]';

function logDebug() { console.log.apply(console, [LOG_PREFIX].concat(Array.prototype.slice.call(arguments))); }
function logInfo()  { console.info.apply(console, [LOG_PREFIX].concat(Array.prototype.slice.call(arguments))); }
function logWarn()  { console.warn.apply(console, [LOG_PREFIX].concat(Array.prototype.slice.call(arguments))); }
function logError() { console.error.apply(console, [LOG_PREFIX].concat(Array.prototype.slice.call(arguments))); }

// ── Utilities ──────────────────────────────────────────────────────────────

function fmt(seconds) {
  if (!seconds || seconds <= 0) return '--';
  var h = Math.floor(seconds / 3600);
  var m = Math.floor((seconds % 3600) / 60);
  var s = Math.floor(seconds % 60);
  if (h > 0) return m > 0 ? h + 'h ' + m + 'm' : h + 'h';
  if (m > 0) return s > 0 ? m + 'm ' + s + 's' : m + 'm';
  return s + 's';
}

function fmtNum(n, decimals) {
  if (n == null || n === 0) return '--';
  return Number(n).toFixed(decimals != null ? decimals : 2);
}

function fmtDate(iso) {
  // Parse YYYY-MM-DD without timezone offset (new Date('2026-03-30') parses as UTC, shifts day in local time)
  var parts = String(iso).match(/^(\d{4})-(\d{2})-(\d{2})/);
  if (parts) {
    return new Date(parseInt(parts[1]), parseInt(parts[2]) - 1, parseInt(parts[3]))
      .toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
  }
  return new Date(iso).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
}

function fmtTime(iso) {
  if (!iso) return '--';
  return new Date(iso).toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
}

// "Nov 2024" from YYYY-MM-DD (same timezone-safe parsing as fmtDate)
function fmtSinceDate(iso) {
  if (!iso) return '--';
  var parts = String(iso).match(/^(\d{4})-(\d{2})-(\d{2})/);
  var d = parts
    ? new Date(parseInt(parts[1]), parseInt(parts[2]) - 1, parseInt(parts[3]))
    : new Date(iso);
  return d.toLocaleDateString(undefined, { year: 'numeric', month: 'short' });
}

function fmtDateTime(iso) {
  return fmtDate(iso) + '  ' + fmtTime(iso);
}

function fmtRelativeTime(iso) {
  if (!iso) return '';
  var parts = String(iso).match(/^(\d{4})-(\d{2})-(\d{2})/);
  var d = parts
    ? new Date(parseInt(parts[1]), parseInt(parts[2]) - 1, parseInt(parts[3]))
    : new Date(iso);
  if (isNaN(d.getTime())) return '';
  var now = new Date();
  var today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  var then = new Date(d.getFullYear(), d.getMonth(), d.getDate());
  var days = Math.round((today - then) / (1000 * 60 * 60 * 24));
  if (days < 0) return 'in the future';
  if (days === 0) return 'today';
  if (days === 1) return 'yesterday';
  if (days < 7) return days + ' days ago';
  if (days < 30) {
    var weeks = Math.floor(days / 7);
    return weeks + (weeks === 1 ? ' week ago' : ' weeks ago');
  }
  if (days < 365) {
    var months = Math.floor(days / 30);
    return months + (months === 1 ? ' month ago' : ' months ago');
  }
  var years = Math.floor(days / 365);
  return years + (years === 1 ? ' year ago' : ' years ago');
}

function esc(str) {
  if (!str) return '';
  var d = document.createElement('div');
  d.textContent = str;
  return d.innerHTML;
}

// Target color palette — must match DashboardServer.TargetColors order
var TARGET_COLORS = ['#4e79a7', '#f28e2b', '#e15759', '#76b7b2', '#59a14f', '#edc948'];

// ── Filter type resolution system ────────────────────────────────────────────
// Mirrors FilterHelper.cs classification logic, extended with spectral colors.
//
// Resolution order for getFilterColor(name):
//   1. User type override (globalFilterTypeMap, from plugin settings)
//   2. Well-known name lookup (FILTER_KNOWN_TYPES, case-insensitive)
//   3. First-letter fallback (H/S/O/L/R/G/B only — mirrors FilterHelper.cs)
//   4. Unresolved → returns null (pill hidden)
//
// Colors are spectrally motivated:
//   Ha (656nm) vivid red < SII (672nm) deep crimson — longer λ = deeper red
//   R broadband is warmer (orange-red) than the narrowband reds
//   OIII (500nm) cyan-teal, NII (658nm) amber for visual separation from Ha

var FILTER_TYPE_COLORS = {
  'H': '#E53935', // H-alpha    — 656nm vivid red
  'S': '#C62828', // SII        — 672nm deep crimson (longer λ → deeper)
  'O': '#00ACC1', // OIII       — 500nm cyan-teal
  'N': '#FB8C00', // NII        — 658nm amber (near Ha; amber for visual separation)
  'L': '#90A4AE', // Luminance  — muted blue-silver (pill bg/border; letter uses white override)
  'R': '#FF7043', // Red BB     — warm orange-red (broadband = less spectrally pure)
  'G': '#66BB6A', // Green BB
  'B': '#42A5F5', // Blue BB
};

// Chart bar fill override: L renders as off-white so luminance segments read as
// "bright" rather than blending into the muted end of the palette. All other
// types use the same colors as the pills.
var FILTER_TYPE_CHART_COLORS = {
  'H': '#E53935',
  'S': '#C62828',
  'O': '#00ACC1',
  'N': '#FB8C00',
  'L': '#DCE1E8', // off-white for luminance bars
  'R': '#FF7043',
  'G': '#66BB6A',
  'B': '#42A5F5',
};

// Well-known filter names → canonical type. Keys are lowercase for O(1) lookup.
var FILTER_KNOWN_TYPES = {
  // H-alpha / Hβ variants
  'ha': 'H', 'h': 'H', 'h-alpha': 'H', 'halpha': 'H', 'h_alpha': 'H',
  'h-a': 'H', 'hydrogen': 'H', 'hb': 'H', 'hbeta': 'H', 'h-beta': 'H',
  // SII variants
  'sii': 'S', 's': 'S', 's2': 'S', 's-ii': 'S', 's_ii': 'S',
  'sulfur': 'S', 'sulphur': 'S', 'sulfur-ii': 'S', 'sulphur-ii': 'S',
  // OIII variants
  'oiii': 'O', 'o': 'O', 'o3': 'O', 'o-iii': 'O', 'o_iii': 'O',
  'oxygen': 'O', 'oxygen-iii': 'O',
  // NII variants
  'nii': 'N', 'n2': 'N', 'n-ii': 'N', 'n_ii': 'N', 'nitrogen': 'N', 'nitrogen-ii': 'N',
  // Luminance / filterless
  'l': 'L', 'luminance': 'L', 'lum': 'L', 'none': 'L',
  // Broadband
  'r': 'R', 'red': 'R',
  'g': 'G', 'green': 'G',
  'b': 'B', 'blue': 'B',
};

// First-letter fallback — only letters FilterHelper.cs recognizes.
// N intentionally absent: "ND" (neutral density) also starts with N.
var FILTER_FIRST_LETTER_TYPE = { 'H': 'H', 'S': 'S', 'O': 'O', 'L': 'L', 'R': 'R', 'G': 'G', 'B': 'B' };

// Populated from /api/settings filterTypeOverrides (set via Options.xaml)
var globalFilterTypeMap = {};

function resolveFilterType(name) {
  if (!name) return null;
  var lower = name.toLowerCase();
  if (globalFilterTypeMap[lower]) return globalFilterTypeMap[lower];       // 1. user override
  if (FILTER_KNOWN_TYPES[lower])  return FILTER_KNOWN_TYPES[lower];        // 2. known name
  var fl = name.charAt(0).toUpperCase();
  if (FILTER_FIRST_LETTER_TYPE[fl]) return FILTER_FIRST_LETTER_TYPE[fl];  // 3. first-letter
  return null;                                                              // 4. unresolved
}

function getFilterColor(name) {
  var type = resolveFilterType(name);
  return type ? FILTER_TYPE_COLORS[type] : null;
}

function hexToRgb(hex) {
  return parseInt(hex.slice(1,3),16)+','+parseInt(hex.slice(3,5),16)+','+parseInt(hex.slice(5,7),16);
}

function makeTargetBadge(name, idx) {
  var color = TARGET_COLORS[idx % TARGET_COLORS.length];
  var rgb = hexToRgb(color);
  return '<span class="card-target-badge" style="background:rgba('+rgb+',0.1);border-color:rgba('+rgb+',0.28);color:'+color+'">'+esc(name)+'</span>';
}

// ── API ────────────────────────────────────────────────────────────────────

function api(path) {
  var start = performance.now();
  logDebug('API', path);
  return fetch(path).then(function(r) {
    if (!r.ok) {
      logError('API', path, '->', r.status, '(' + Math.round(performance.now() - start) + 'ms)');
      throw new Error('HTTP ' + r.status);
    }
    return r.json().then(function(data) {
      var detail = Array.isArray(data) ? data.length + ' items' :
        data && data.targets ? data.targets.length + ' targets' :
        data && data.filters ? data.filters.length + ' filters' : 'ok';
      logDebug('API', path, '->', r.status, detail, '(' + Math.round(performance.now() - start) + 'ms)');
      return data;
    });
  });
}

// ── Theme ──────────────────────────────────────────────────────────────────

function initTheme() {
  var saved = localStorage.getItem('ns-theme');
  if (saved === 'light') document.documentElement.classList.add('light');
  updateThemeButton();
}

function toggleTheme() {
  document.documentElement.classList.toggle('light');
  var isLight = document.documentElement.classList.contains('light');
  localStorage.setItem('ns-theme', isLight ? 'light' : 'dark');
  updateThemeButton();
}

function updateThemeButton() {
  var btn = document.getElementById('theme-toggle');
  var isLight = document.documentElement.classList.contains('light');
  btn.textContent = isLight ? '\u2600' : '\u263E';
  btn.title = isLight ? 'Switch to dark mode' : 'Switch to light mode';
}

// ── Router ─────────────────────────────────────────────────────────────────

function route() {
  var hash = location.hash.slice(1) || '/sessions';
  logInfo('Navigate:', hash);
  var parts = hash.split('?');
  var path = parts[0];
  var params = new URLSearchParams(parts[1] || '');

  document.querySelectorAll('.nav-link').forEach(function(el) {
    el.classList.toggle('active', hash.startsWith('#' + el.getAttribute('href').slice(1)) ||
      path.startsWith('/' + el.dataset.page));
  });

  // Toggle report-view mode on body to kill outer scroll
  var isReport = path.match(/^\/sessions\/[^/]+$/);
  document.body.classList.toggle('report-view', !!isReport);

  if (path === '/sessions') {
    renderSessionList(params);
  } else if (isReport) {
    renderSessionDetail(path.split('/')[2]);
  } else if (path === '/stats') {
    renderStats();
  } else {
    renderSessionList(params);
  }
  repositionViewToggle();
}

function navigate(hash) {
  location.hash = hash;
}

// ── Components ─────────────────────────────────────────────────────────────

function statBox(value, label, cls) {
  return '<div class="stat-box' + (cls ? ' ' + cls : '') + '">' +
    '<div class="stat-value">' + esc(String(value != null ? value : '--')) + '</div>' +
    '<div class="stat-label">' + esc(label) + '</div>' +
    '</div>';
}

// ── Stats Tab Bar ──────────────────────────────────────────────────────────

function renderTabBar(tabs, activeTab) {
  var html = '<div class="stats-tabs" id="stats-tabs">';
  html += '<div class="stats-tab-thumb" id="stats-tab-thumb"></div>';
  for (var i = 0; i < tabs.length; i++) {
    var t = tabs[i];
    var cls = t.id === activeTab ? ' active' : '';
    html += '<button class="stats-tab-btn' + cls + '" data-tab="' + t.id + '">' + esc(t.label) + '</button>';
  }
  html += '</div>';
  return html;
}

function initTabBar(onSwitch) {
  var container = document.getElementById('stats-tabs');
  if (!container) return;
  var thumb = document.getElementById('stats-tab-thumb');
  var btns = container.querySelectorAll('.stats-tab-btn');

  function positionThumb(btn, animate) {
    if (!btn || !thumb) return;
    if (!animate) thumb.style.transition = 'none';
    thumb.style.left = btn.offsetLeft + 'px';
    thumb.style.width = btn.offsetWidth + 'px';
    if (!animate) {
      requestAnimationFrame(function() {
        requestAnimationFrame(function() {
          thumb.style.transition = '';
        });
      });
    }
  }

  // Position on active button without animation
  var active = container.querySelector('.stats-tab-btn.active');
  positionThumb(active, false);

  btns.forEach(function(btn) {
    btn.addEventListener('click', function() {
      btns.forEach(function(b) { b.classList.remove('active'); });
      btn.classList.add('active');
      positionThumb(btn, true);
      var tabId = btn.getAttribute('data-tab');
      localStorage.setItem('ns-stats-tab', tabId);
      if (onSwitch) onSwitch(tabId);
    });
  });
}

// ── Target Cards ──────────────────────────────────────────────────────────

function targetStatBox(value, label, unit) {
  return '<div class="target-card-stat">' +
    '<div class="target-card-stat-value">' + esc(String(value != null ? value : '--')) +
    (unit ? '<span class="target-card-stat-unit">' + esc(unit) + '</span>' : '') +
    '</div>' +
    '<div class="target-card-stat-label">' + esc(label) + '</div>' +
    '</div>';
}

function fmtCoord(raH, decD) {
  if (!raH && !decD) return '';
  var rH = Math.floor(raH);
  var rM = Math.floor((raH - rH) * 60);
  var dSign = decD >= 0 ? '+' : '-';
  var dAbs = Math.abs(decD);
  var dD = Math.floor(dAbs);
  var dM = Math.floor((dAbs - dD) * 60);
  return 'RA ' + rH + 'h' + (rM < 10 ? '0' : '') + rM + 'm  Dec ' + dSign + dD + '\u00b0' + (dM < 10 ? '0' : '') + dM + "'";
}

function renderTargetCard(t, index) {
  var initial = t.target ? t.target.charAt(0).toUpperCase() : '?';
  var sessionCount = t.sessionCount || 0;

  var html = '<div class="target-card" data-target="' + esc(t.target) + '" data-latest-session="' + esc(t.latestSessionId || '') + '">';

  // Header: name + type pill + badges + progress bar
  html += '<div class="target-card-header">';
  html += '<div class="target-card-header-left">';
  html += '<span class="target-card-name">' + esc(t.target) + '</span>';
  // Type pill — only show for TS-linked targets
  if (t.ts && t.ts.project) {
    var pType = projectType(!!t.ts.project.isMosaic, t.ts.project.targetCount);
    var typeLabel = pType === 'single' ? 'Single' : pType === 'multi' ? 'Multi' : 'Mosaic';
    html += '<span class="targets-project-type-badge">' + typeLabel + '</span>';
    var state = t.ts.project.state || 'Draft';
    var overridden = t.ts.project.stateSource === 'override';
    html += '<span class="target-card-ts-badge" data-state="' + esc(state) +
            '" data-project-guid="' + esc(t.ts.project.guid || '') +
            '" data-target="' + esc(t.target) + '" title="Click to override status">' +
            esc(state) +
            (overridden ? '<span class="override-mark" title="User override active"></span>' : '') +
            '</span>';
  }
  html += '</div>';

  // Progress bar
  if (t.ts && t.ts.project && t.ts.project.percentComplete != null && t.ts.project.percentComplete > 0) {
    var pct = t.ts.project.percentComplete;
    html += '<div class="targets-project-progress">';
    html += '<div class="targets-project-progress-overall">';
    html += '<div class="targets-project-progress-overall-track">' +
            '<div class="targets-project-progress-overall-fill" style="width:' + pct.toFixed(1) + '%"></div>' +
            '</div>';
    html += '<span class="targets-project-progress-overall-pct">' + pct.toFixed(0) + '%</span>';
    html += '</div>';
    html += '</div>';
  }

  html += '<div class="target-card-header-right">';
  html += '<button type="button" class="target-card-assign-btn" data-target="' + esc(t.target) + '" title="Assign to project">&#x1F4C1;</button>';
  html += '<button type="button" class="targets-project-collapse-btn" aria-label="Collapse"></button>';
  html += '</div>';
  html += '</div>'; // .target-card-header

  // Body: thumbnail left, stat boxes right
  html += '<div class="target-card-body">';

  // Thumbnail column
  html += '<div class="target-card-thumb-col">';
  html += '<div class="target-card-thumb" data-session-id="' + esc(t.latestSessionId || '') + '" data-target="' + esc(t.target) + '">';
  html += '<span class="thumb-placeholder">' + esc(initial) + '</span>';
  if (t.lastImaged) {
    html += '<div class="target-card-last-imaged">Last imaged ' + esc(fmtRelativeTime(t.lastImaged)) + '</div>';
  }
  html += '</div>'; // .target-card-thumb
  html += '</div>'; // .target-card-thumb-col

  // Stat boxes column — Sessions, Integration, Frames, Avg HFR
  var hours = t.totalIntegrationHours != null ? t.totalIntegrationHours.toFixed(1) : '--';
  var frames = t.acceptedFrames != null ? t.acceptedFrames : '--';
  html += '<div class="target-card-stat-boxes">';
  html += '<div class="stat-box"><div class="stat-value">' + sessionCount +
          '</div><div class="stat-label">Sessions</div></div>';
  html += '<div class="stat-box"><div class="stat-value">' + esc(String(hours)) +
          '<span class="unit">h</span></div><div class="stat-label">Integration</div></div>';
  html += '<div class="stat-box"><div class="stat-value">' + esc(String(frames)) +
          '</div><div class="stat-label">Frames</div></div>';
  html += '<div class="stat-box"><div class="stat-value">' + (t.avgHFR ? t.avgHFR.toFixed(2) : '--') +
          '<span class="unit">px</span></div><div class="stat-label">Avg HFR</div></div>';
  html += '</div>'; // .target-card-stat-boxes

  html += '</div></div>';
  return html;
}

// ── Targets tab sort controls ─────────────────────────────────────────────

var TARGET_SORT_OPTIONS = [
  { key: 'recent',   label: 'Most recent' },
  { key: 'sessions', label: 'Most sessions' },
  { key: 'hours',    label: 'Most hours' },
  { key: 'frames',   label: 'Most frames' },
  { key: 'name',     label: 'Name' },
  { key: 'type',     label: 'Type' }
];

function getTargetSortKey() {
  var k = localStorage.getItem('ns-targets-sort') || 'recent';
  var valid = TARGET_SORT_OPTIONS.some(function(o) { return o.key === k; });
  return valid ? k : 'recent';
}

function sortTargets(targets, key) {
  var sorted = targets.slice();
  switch (key) {
    case 'recent':
      sorted.sort(function(a, b) {
        var la = a.lastImaged || '';
        var lb = b.lastImaged || '';
        if (la === lb) return 0;
        return la < lb ? 1 : -1;
      });
      break;
    case 'sessions':
      sorted.sort(function(a, b) { return (b.sessionCount || 0) - (a.sessionCount || 0); });
      break;
    case 'hours':
      sorted.sort(function(a, b) { return (b.totalIntegrationHours || 0) - (a.totalIntegrationHours || 0); });
      break;
    case 'frames':
      sorted.sort(function(a, b) { return (b.acceptedFrames || 0) - (a.acceptedFrames || 0); });
      break;
    case 'name':
      sorted.sort(function(a, b) { return (a.target || '').localeCompare(b.target || ''); });
      break;
    case 'type': // in flat view, same as recent
      sorted.sort(function(a, b) {
        var la = a.lastImaged || '';
        var lb = b.lastImaged || '';
        if (la === lb) return 0;
        return la < lb ? 1 : -1;
      });
      break;
  }
  return sorted;
}

// ── Phase 3b: Grouping + Status Filters ──────────────────────────────────

var TS_STATE_ORDER  = ['Active', 'Completed', 'Draft', 'Inactive', 'Closed'];
var TS_STATE_COLORS = { Active: '#66BB6A', Completed: '#42A5F5', Draft: '#FFB74D', Inactive: '#EF5350', Closed: '#90A4AE' };
var DEFAULT_STATUS_FILTER = ['Active', 'Completed', 'Draft', 'Inactive'];

function getTargetGroupBy() {
  return localStorage.getItem('ns-targets-group') === 'project' ? 'project' : 'flat';
}

function getTargetStatusFilter() {
  try {
    var raw = localStorage.getItem('ns-targets-status-filter');
    if (raw) { var arr = JSON.parse(raw); if (Array.isArray(arr)) return arr; }
  } catch (e) {}
  return DEFAULT_STATUS_FILTER.slice();
}

function setTargetStatusFilter(arr) {
  localStorage.setItem('ns-targets-status-filter', JSON.stringify(arr));
}

var TARGET_TYPE_OPTIONS = [
  { key: 'single', label: 'Single' },
  { key: 'multi',  label: 'Multi' },
  { key: 'mosaic', label: 'Mosaic' }
];

function getTargetTypeFilter() {
  try {
    var raw = localStorage.getItem('ns-targets-type-filter');
    if (raw) { var arr = JSON.parse(raw); if (Array.isArray(arr)) return arr; }
  } catch (e) {}
  return ['single', 'multi', 'mosaic'];
}

function setTargetTypeFilter(arr) {
  localStorage.setItem('ns-targets-type-filter', JSON.stringify(arr));
}

// Returns 'single' | 'multi' | 'mosaic' for a TS project
function projectType(isMosaic, targetCount) {
  if (isMosaic) return 'mosaic';
  if ((targetCount || 1) > 1) return 'multi';
  return 'single';
}

// Renders the full control bar: sort pills + optional group toggle + optional filter row
function renderTargetsControlBar(sortKey, groupBy) {
  var tsAvail = statsTsStatus === 'available';
  var html = '<div class="targets-control-bar">';
  html += '<div class="targets-sort-bar"><span class="targets-sort-label">Sort</span>';
  // Group by project first — commonly used
  if (tsAvail || (statsTsProjects && statsTsProjects.some(function(p) { return p.isCustom; }))) {
    var grpCls = 'targets-group-pill' + (groupBy === 'project' ? ' active' : '');
    html += '<button type="button" class="' + grpCls + '" data-action="toggle-group">Group by project</button>';
  }
  TARGET_SORT_OPTIONS.forEach(function(opt) {
    var cls = 'targets-sort-pill' + (opt.key === sortKey ? ' active' : '');
    html += '<button type="button" class="' + cls + '" data-sort-key="' + opt.key + '">' + esc(opt.label) + '</button>';
  });
  html += '</div>';
  if (tsAvail) {
    var enabledStates = getTargetStatusFilter();
    var enabledTypes  = getTargetTypeFilter();
    var allStatesOn = TS_STATE_ORDER.every(function(s) { return enabledStates.indexOf(s) >= 0; });
    var allTypesOn  = TARGET_TYPE_OPTIONS.every(function(o) { return enabledTypes.indexOf(o.key) >= 0; });
    html += '<div class="targets-filter-row"><span class="targets-sort-label">Filter</span>';
    html += '<button type="button" class="targets-status-chip' + (allStatesOn && allTypesOn ? ' active' : '') + '" data-filter-state="__all__">All</button>';
    TS_STATE_ORDER.forEach(function(state) {
      var on = enabledStates.indexOf(state) >= 0;
      var color = TS_STATE_COLORS[state] || '#90A4AE';
      var cls = 'targets-status-chip' + (on ? ' active' : '');
      html += '<button type="button" class="' + cls + '" data-filter-state="' + esc(state) + '">' +
        '<span class="status-chip-dot" style="background:' + color + '"></span>' + esc(state) + '</button>';
    });
    html += '<span class="filter-row-divider"></span>';
    TARGET_TYPE_OPTIONS.forEach(function(opt) {
      var on = enabledTypes.indexOf(opt.key) >= 0;
      html += '<button type="button" class="targets-status-chip' + (on ? ' active' : '') + '" data-filter-type="' + esc(opt.key) + '">' + esc(opt.label) + '</button>';
    });
    html += '<span class="filter-row-divider"></span>';
    html += '<label class="target-check targets-fov-check" title="Show panel FOV overlays on mosaic thumbnails">' +
            '<input type="checkbox" id="targets-filter-fov"' + (showFovOverlay ? ' checked' : '') + '>' +
            '<span>Show FOV</span></label>';
    html += '</div>';
  }
  html += '</div>';
  return html;
}

function initTargetsControlBar() {
  document.querySelectorAll('.targets-sort-pill').forEach(function(pill) {
    pill.addEventListener('click', function() {
      var key = pill.getAttribute('data-sort-key');
      if (!key || key === getTargetSortKey()) return;
      localStorage.setItem('ns-targets-sort', key);
      renderStatsTabContent('targets');
    });
  });
  var grpBtn = document.querySelector('.targets-group-pill');
  if (grpBtn) {
    grpBtn.addEventListener('click', function() {
      localStorage.setItem('ns-targets-group', getTargetGroupBy() === 'project' ? 'flat' : 'project');
      renderStatsTabContent('targets');
    });
  }
  document.querySelectorAll('.targets-status-chip[data-filter-state]').forEach(function(chip) {
    chip.addEventListener('click', function() {
      var state = chip.getAttribute('data-filter-state');
      if (!state) return;
      if (state === '__all__') {
        setTargetStatusFilter(TS_STATE_ORDER.slice());
        setTargetTypeFilter(['single', 'multi', 'mosaic']);
        renderStatsTabContent('targets');
        return;
      }
      var enabled = getTargetStatusFilter();
      var idx = enabled.indexOf(state);
      if (idx >= 0) {
        if (enabled.length <= 1) return; // keep at least one enabled
        enabled.splice(idx, 1);
      } else {
        enabled.push(state);
      }
      setTargetStatusFilter(enabled);
      renderStatsTabContent('targets');
    });
  });
  document.querySelectorAll('.targets-status-chip[data-filter-type]').forEach(function(chip) {
    chip.addEventListener('click', function() {
      var type = chip.getAttribute('data-filter-type');
      if (!type) return;
      var enabled = getTargetTypeFilter();
      var idx = enabled.indexOf(type);
      if (idx >= 0) {
        if (enabled.length <= 1) return; // keep at least one enabled
        enabled.splice(idx, 1);
      } else {
        enabled.push(type);
      }
      setTargetTypeFilter(enabled);
      renderStatsTabContent('targets');
    });
  });

  // Show FOV overlay checkbox — shared setting with sessions tab
  var targetsFovEl = document.getElementById('targets-filter-fov');
  if (targetsFovEl) {
    targetsFovEl.addEventListener('change', function() {
      showFovOverlay = this.checked;
      localStorage.setItem('ns-show-fov', showFovOverlay ? 'true' : 'false');
      document.querySelectorAll('.mosaic-fov-svg, .card-thumb-wrap svg, .target-card-thumb svg, .tdp-hero-wrap svg').forEach(function(svg) {
        svg.style.display = showFovOverlay ? '' : 'none';
      });
    });
  }

  // Scroll fade: remove right-edge mask when scrolled to end
  ['.targets-sort-bar', '.targets-filter-row'].forEach(function(sel) {
    var el = document.querySelector(sel);
    if (!el) return;
    function updateFade() {
      var atEnd = el.scrollLeft + el.clientWidth >= el.scrollWidth - 4;
      el.classList.toggle('scrolled-end', atEnd);
    }
    el.addEventListener('scroll', updateFade, { passive: true });
    updateFade();
  });
}

// Flat-mode filter by state + type; targets with no TS link always pass through
function filterTargets(targets) {
  var enabledStates = getTargetStatusFilter();
  var enabledTypes  = getTargetTypeFilter();
  return targets.filter(function(t) {
    if (!t.ts || !t.ts.project) return true;
    var proj = t.ts.project;
    if (enabledStates.indexOf(proj.state) < 0) return false;
    if (enabledTypes.indexOf(projectType(!!proj.isMosaic, proj.targetCount)) < 0) return false;
    return true;
  });
}

function renderProjectContainer(info) {
  var allTargets = statsTargetData || [];
  var sorted = info.targets; // natural order — sort only applies between containers, not within
  var totalHours = 0, totalFrames = 0;
  info.targets.forEach(function(t) {
    totalHours += t.totalIntegrationHours || 0;
    totalFrames += t.acceptedFrames || 0;
  });
  var html = '<div class="targets-project-container" data-guid="' + esc(info.guid) +
    '" data-state="' + esc(info.state) + '">';
  html += '<div class="targets-project-header">';
  html += '<div class="targets-project-header-left">';
  html += '<span class="targets-project-name">' + esc(info.name) + '</span>';
  var containerType = info.isMosaic ? 'Mosaic' : (info.targetCount > 1 ? 'Multi' : 'Single');
  html += '<span class="targets-project-type-badge">' + containerType + '</span>';
  html += '<span class="target-card-ts-badge" data-state="' + esc(info.state) +
    '" data-project-guid="' + esc(info.guid) + '" title="Click to override status">' + esc(info.state) + '</span>';
  html += '</div>';

  // Overall progress bar only (no per-panel bars)
  var panelPcts = info.targets.map(function(t) {
    return (t.ts && t.ts.project && t.ts.project.percentComplete != null)
           ? t.ts.project.percentComplete : null;
  });
  var validPcts = panelPcts.filter(function(p) { return p !== null; });
  var hasAnyPct = validPcts.some(function(p) { return p > 0; });
  if (hasAnyPct) {
    var overallPct = validPcts.reduce(function(s, p) { return s + p; }, 0) / validPcts.length;
    html += '<div class="targets-project-progress">';
    html += '<div class="targets-project-progress-overall">';
    html += '<div class="targets-project-progress-overall-track">' +
            '<div class="targets-project-progress-overall-fill" style="width:' + overallPct.toFixed(1) + '%"></div>' +
            '</div>';
    html += '<span class="targets-project-progress-overall-pct">' + overallPct.toFixed(0) + '%</span>';
    html += '</div>';
    html += '</div>'; // .targets-project-progress
  }

  html += '<div class="targets-project-header-right">';
  html += '<button type="button" class="targets-project-collapse-btn" aria-label="Collapse"></button>';
  html += '</div>';
  html += '</div>'; // .targets-project-header

  // Aggregate across all panels
  var lastImaged = '';
  var totalSessions = 0;
  info.targets.forEach(function(t) {
    if (t.lastImaged && (!lastImaged || t.lastImaged > lastImaged)) lastImaged = t.lastImaged;
    totalSessions += t.sessionCount || 0;
  });

  html += '<div class="targets-project-body">';
  if (info.isMosaic && info.guid) {
    // Thumbnail fills all available horizontal space; stat boxes stretch to match height
    html += '<div class="targets-project-thumb-col">';
    html += '<div class="targets-project-thumb-wrap" data-guid="' + esc(info.guid) + '">' +
            '<img class="targets-project-thumb" src="/api/stats/projects/' + encodeURIComponent(info.guid) + '/mosaic-thumb" ' +
            'alt="Mosaic survey thumbnail" loading="lazy">';
    if (lastImaged) {
      html += '<div class="targets-project-last-imaged">Last imaged ' + fmtRelativeTime(lastImaged) + '</div>';
    }
    html += '</div>'; // .targets-project-thumb-wrap
    html += '</div>'; // .targets-project-thumb-col

    html += '<div class="targets-project-stat-boxes">';
    html += '<div class="stat-box"><div class="stat-value">' + info.targets.length +
            '</div><div class="stat-label">Panels</div></div>';
    html += '<div class="stat-box"><div class="stat-value">' + totalHours.toFixed(1) +
            '<span class="unit">h</span></div><div class="stat-label">Integration</div></div>';
    html += '<div class="stat-box"><div class="stat-value">' + totalFrames +
            '</div><div class="stat-label">Frames</div></div>';
    html += '<div class="stat-box"><div class="stat-value">' + totalSessions +
            '</div><div class="stat-label">Sessions</div></div>';
    html += '</div>'; // .targets-project-stat-boxes
  } else {
    // Non-mosaic grouped project — thumbnail from first target + stat boxes
    var firstTarget = info.targets[0];
    html += '<div class="targets-project-thumb-col">';
    html += '<div class="targets-project-thumb-wrap target-card-thumb" data-session-id="' +
            esc(firstTarget ? firstTarget.latestSessionId || '' : '') +
            '" data-target="' + esc(firstTarget ? firstTarget.target || '' : '') + '">';
    var initial = firstTarget && firstTarget.target ? firstTarget.target.charAt(0).toUpperCase() : '?';
    html += '<span class="thumb-placeholder">' + esc(initial) + '</span>';
    if (lastImaged) {
      html += '<div class="targets-project-last-imaged">Last imaged ' + fmtRelativeTime(lastImaged) + '</div>';
    }
    html += '</div>'; // .targets-project-thumb-wrap
    html += '</div>'; // .targets-project-thumb-col

    var avgHFR = 0, hfrCount = 0;
    info.targets.forEach(function(t) {
      if (t.avgHFR) { avgHFR += t.avgHFR; hfrCount++; }
    });

    html += '<div class="targets-project-stat-boxes">';
    html += '<div class="stat-box"><div class="stat-value">' + totalSessions +
            '</div><div class="stat-label">Sessions</div></div>';
    html += '<div class="stat-box"><div class="stat-value">' + totalHours.toFixed(1) +
            '<span class="unit">h</span></div><div class="stat-label">Integration</div></div>';
    html += '<div class="stat-box"><div class="stat-value">' + totalFrames +
            '</div><div class="stat-label">Frames</div></div>';
    html += '<div class="stat-box"><div class="stat-value">' + (hfrCount > 0 ? (avgHFR / hfrCount).toFixed(2) : '--') +
            '<span class="unit">px</span></div><div class="stat-label">Avg HFR</div></div>';
    html += '</div>'; // .targets-project-stat-boxes
  }
  html += '</div>'; // .targets-project-body

  html += '</div>';
  return html;
}

// Build grouped HTML: project containers + batched standalone cards + unassigned section
function renderGroupedTargets(targets, sortKey) {
  var enabled = getTargetStatusFilter();
  var allTargets = statsTargetData || [];
  var containerMap = {};
  var unassigned = [];

  targets.forEach(function(t) {
    if (!t.ts || !t.ts.project || !t.ts.project.guid) { unassigned.push(t); return; }
    var proj = t.ts.project;
    var guid = proj.guid;
    if (!containerMap[guid]) {
      containerMap[guid] = { guid: guid, name: proj.name || 'TS Project',
        state: proj.state || 'Draft', isMosaic: !!proj.isMosaic,
        targetCount: proj.targetCount || 1, targets: [] };
    }
    containerMap[guid].targets.push(t);
  });

  var enabledTypes = getTargetTypeFilter();
  var items = [];
  Object.keys(containerMap).forEach(function(guid) {
    var grp = containerMap[guid];
    if (enabled.indexOf(grp.state) < 0) return; // state filtered
    var pType = projectType(grp.isMosaic, grp.targetCount);
    if (enabledTypes.indexOf(pType) < 0) return; // type filtered
    if (!grp.isMosaic && grp.targetCount <= 1) {
      items.push({ type: 'standalone', pType: pType, target: grp.targets[0], state: grp.state });
    } else {
      items.push({ type: 'container', pType: pType, info: grp, state: grp.state });
    }
  });

  // Compute a sort value for a project item given the selected sort key.
  // Standalone items use their single target's values; containers aggregate.
  function projectSortValue(item) {
    var tgts = item.type === 'standalone' ? [item.target] : (item.info.targets || []);
    switch (sortKey) {
      case 'recent':
        var dates = tgts.map(function(t) { return t.lastImaged || ''; }).sort();
        return dates[dates.length - 1] || ''; // latest date string
      case 'sessions':
        return tgts.reduce(function(s, t) { return s + (t.sessionCount || 0); }, 0);
      case 'hours':
        return tgts.reduce(function(s, t) { return s + (t.totalIntegrationHours || 0); }, 0);
      case 'frames':
        return tgts.reduce(function(s, t) { return s + (t.acceptedFrames || 0); }, 0);
      case 'name':
        return item.type === 'standalone' ? (item.target.target || '') : (item.info.name || '');
      case 'type': // secondary sort is by recency within each type group
        var typeDates = tgts.map(function(t) { return t.lastImaged || ''; }).sort();
        return typeDates[typeDates.length - 1] || '';
      default:
        return 0;
    }
  }

  // Type order: mosaic → multi → single (default), reversed if user sorts differently
  var TYPE_ORDER = ['mosaic', 'multi', 'single'];

  // Sort items: primary by type group, secondary by state, tertiary by selected sort key
  items.sort(function(a, b) {
    // Type grouping
    var ta = TYPE_ORDER.indexOf(a.pType); if (ta < 0) ta = 99;
    var tb = TYPE_ORDER.indexOf(b.pType); if (tb < 0) tb = 99;
    if (ta !== tb) return ta - tb;
    // State within type
    var ia = TS_STATE_ORDER.indexOf(a.state); if (ia < 0) ia = 99;
    var ib = TS_STATE_ORDER.indexOf(b.state); if (ib < 0) ib = 99;
    if (ia !== ib) return ia - ib;
    // Value sort within state
    var sa = projectSortValue(a);
    var sb = projectSortValue(b);
    if (sortKey === 'name') return sa < sb ? -1 : sa > sb ? 1 : 0;
    if (sortKey === 'recent' || sortKey === 'type') return sa < sb ? 1 : sa > sb ? -1 : 0; // newest first
    return sb - sa; // numeric: higher first
  });

  var TYPE_LABELS = { mosaic: 'Mosaic Projects', multi: 'Multi-Target Projects', single: 'Single Target Projects' };

  var html = '';
  var currentType = null;

  function renderItem(item) {
    if (item.type === 'standalone') {
      return renderTargetCard(item.target, allTargets.indexOf(item.target));
    } else {
      return renderProjectContainer(item.info);
    }
  }

  // Group items by type, render each group in its own grid with a separator header
  var typeGroups = {};
  items.forEach(function(item) {
    if (!typeGroups[item.pType]) typeGroups[item.pType] = [];
    typeGroups[item.pType].push(item);
  });

  TYPE_ORDER.forEach(function(pType) {
    var group = typeGroups[pType];
    if (!group || !group.length) return;
    html += '<div class="targets-type-section">';
    html += '<div class="targets-type-header">' + (TYPE_LABELS[pType] || pType) + '</div>';
    html += '<div class="targets-grouped">';
    group.forEach(function(item) { html += renderItem(item); });
    html += '</div></div>';
  });

  if (unassigned.length > 0) {
    var sortedU = sortTargets(unassigned, sortKey);
    html += '<div class="targets-unassigned-section">';
    html += '<div class="targets-unassigned-header">Unassigned</div>';
    html += '<div class="target-grid">';
    sortedU.forEach(function(t) { html += renderTargetCard(t, allTargets.indexOf(t)); });
    html += '</div></div>';
  }

  if (!html) html = '<div class="empty" style="margin-top:32px">No targets match the current filter.</div>';
  return html;
}

function initProjectContainers() {
  // Collapse button toggles collapse
  document.querySelectorAll('.targets-project-collapse-btn').forEach(function(btn) {
    btn.addEventListener('click', function(e) {
      e.stopPropagation();
      var c = btn.closest('.targets-project-container');
      if (c) c.classList.toggle('collapsed');
    });
  });
  // Clicking the card (anywhere except TS badge and collapse btn) opens detail view
  document.querySelectorAll('.targets-project-container').forEach(function(container) {
    var guid = container.getAttribute('data-guid');
    var name = container.querySelector('.targets-project-name');
    if (!guid) return;
    container.addEventListener('click', function(e) {
      if (e.target.closest('.target-card-ts-badge')) return;
      if (e.target.closest('.targets-project-collapse-btn')) return;
      openProjectDetail(guid, name ? name.textContent : guid);
    });
  });
  // Load FOV overlays on card thumbnails
  loadCardMosaicOverlays();
}

function loadCardMosaicOverlays() {
  document.querySelectorAll('.targets-project-thumb-wrap[data-guid]').forEach(function(wrap) {
    var guid = wrap.getAttribute('data-guid');
    if (!guid) return;
    fetch('/api/stats/projects/' + encodeURIComponent(guid))
      .then(function(r) { return r.ok ? r.json() : null; })
      .then(function(data) {
        if (!data || !data.panels || !data.panels.length) return;
        loadMosaicThumbnail(data.panels, wrap, guid);
      })
      .catch(function() {});
  });
}

// Measure text width using a canvas — works even when parents clip with overflow:hidden.
function _measureTextWidth(text, fontSizePx, fontWeight, fontFamily, letterSpacingPx) {
  var canvas = _measureTextWidth._canvas || (_measureTextWidth._canvas = document.createElement('canvas'));
  var ctx = canvas.getContext('2d');
  ctx.font = fontWeight + ' ' + fontSizePx + 'px ' + fontFamily;
  var w = ctx.measureText(text).width;
  // Canvas measureText ignores CSS letter-spacing — approximate by adding it per gap
  var ls = parseFloat(letterSpacingPx) || 0;
  if (ls && text.length > 1) w += ls * (text.length - 1);
  return w;
}

// Shrink target-card-name-overlay font-size until the text fits on one line.
// Uses canvas measurement because the overlay is inside overflow:hidden parents.
function fitTargetNameOverlays() {
  var overlays = document.querySelectorAll('.target-card-name-overlay');
  overlays.forEach(function(el) {
    el.style.fontSize = '';
    var cs = window.getComputedStyle(el);
    var max = parseFloat(cs.fontSize) || 14;
    var min = 9;
    var family = cs.fontFamily;
    var weight = cs.fontWeight;
    var ls = cs.letterSpacing;
    var text = el.textContent || '';
    if (!text) return;
    var pL = parseFloat(cs.paddingLeft) || 0;
    var pR = parseFloat(cs.paddingRight) || 0;
    var avail = el.clientWidth - pL - pR;
    if (avail <= 0) return;
    var size = max;
    var guard = 40;
    while (size > min && _measureTextWidth(text, size, weight, family, ls) > avail && guard-- > 0) {
      size -= 0.5;
    }
    if (size !== max) el.style.fontSize = size + 'px';
  });
}

var _fitNamesDebounce = null;
window.addEventListener('resize', function() {
  if (_fitNamesDebounce) clearTimeout(_fitNamesDebounce);
  _fitNamesDebounce = setTimeout(fitTargetNameOverlays, 120);
});

// ── Target Detail Panel (Phase 2) ────────────────────────────────────────

// Filter ordering for stacked bars — consistent across all bars in a chart
var TDP_FILTER_STACK_ORDER = ['L', 'R', 'G', 'B', 'H', 'O', 'S', 'N'];

function tdpFmtDuration(mins) {
  if (!mins || mins <= 0) return '--';
  var h = Math.floor(mins / 60);
  var m = Math.round(mins % 60);
  if (h === 0) return m + 'm';
  if (m === 0) return h + 'h';
  return h + 'h ' + m + 'm';
}

function tdpFmtDate(iso) {
  if (!iso) return '--';
  var d = new Date(iso);
  if (isNaN(d.getTime())) return '--';
  return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric', year: 'numeric' });
}

// Shorter date format for narrow (mobile) layouts — drops the year since the
// date range header already covers it.
function tdpFmtDateShort(iso) {
  if (!iso) return '--';
  var d = new Date(iso);
  if (isNaN(d.getTime())) return '--';
  return d.toLocaleDateString(undefined, { month: 'short', day: 'numeric' });
}

// Build the same translucent circular pill used in the per-filter hover popups.
// Accepts either a filter name or a resolved type letter. Falls back to a neutral
// gray pill when the type is unresolved.
// Special case: L (luminance) uses a white letter since its muted blue-silver
// base color wouldn't read clearly on the translucent dark background.
function filterTypePill(filterNameOrType) {
  var fc = getFilterColor(filterNameOrType);
  var typeLetter = resolveFilterType(filterNameOrType) ||
    (filterNameOrType ? String(filterNameOrType).charAt(0).toUpperCase() : '?');
  var rgb = fc ? hexToRgb(fc) : null;
  var letterColor = typeLetter === 'L' ? '#FFFFFF' : fc;
  var dotStyle = rgb
    ? 'background:rgba(' + rgb + ',0.10);border-color:rgba(' + rgb + ',0.28);color:' + letterColor
    : 'background:rgba(128,128,128,0.10);border-color:rgba(128,128,128,0.28);color:var(--muted)';
  return '<span class="filter-type-dot" style="' + dotStyle + '">' + esc(typeLetter) + '</span>';
}

// Render the stacked-by-filter chart + cumulative line. Takes the raw sessions array
// from /api/stats/targets/{name}/sessions (newest-first; will sort ascending internally).
// widthPx: SVG viewBox width in pixels (pass the measured container width).
// Returns { svg: string, filtersUsed: ['H','O','S',...] } so the caller can render the
// HTML pill legend alongside the SVG.
function renderTargetChart(sessionsDesc, widthPx) {
  var W = Math.max(320, Math.round(widthPx || 540));
  var H = 160, PAD_L = 38, PAD_R = 20, PAD_T = 12, PAD_B = 22;
  var plotW = W - PAD_L - PAD_R;
  var plotH = H - PAD_T - PAD_B;

  var sorted = sessionsDesc.slice().sort(function(a, b) {
    return (a.sessionStart || '') < (b.sessionStart || '') ? -1 : 1;
  });
  if (sorted.length === 0) {
    return {
      svg: '<svg viewBox="0 0 ' + W + ' ' + H + '" xmlns="http://www.w3.org/2000/svg">' +
        '<text x="' + (W/2) + '" y="' + (H/2) + '" fill="#7a8394" font-size="11" text-anchor="middle">No session data</text></svg>',
      filtersUsed: []
    };
  }

  // Group each filter by resolved type letter so the stack is stable
  function filterType(name) { return resolveFilterType(name) || 'Unknown'; }
  var filtersUsed = {};
  sorted.forEach(function(s) {
    (s.filters || []).forEach(function(f) { filtersUsed[filterType(f.filter)] = true; });
  });

  // Per-session total hours for bar height scaling
  var maxNightHrs = Math.max.apply(null, sorted.map(function(s) { return s.integrationHours || 0; }));
  var totalHrs = sorted.reduce(function(acc, s) { return acc + (s.integrationHours || 0); }, 0);
  var yMaxBar = Math.max(0.5, Math.ceil(maxNightHrs * 1.1 * 2) / 2);
  var yMaxLine = Math.max(1, Math.ceil(totalHrs * 1.05));

  var barW = Math.max(8, Math.min(32, Math.floor(plotW / sorted.length) - 6));
  var xStep = plotW / sorted.length;

  var cum = 0;
  var linePts = [];
  var barsSvg = sorted.map(function(s, i) {
    var cx = PAD_L + i * xStep + xStep / 2;
    var barX = cx - barW / 2;
    var baseY = PAD_T + plotH;
    cum += s.integrationHours || 0;
    var lineY = PAD_T + plotH - (cum / yMaxLine) * plotH;
    linePts.push(cx + ',' + lineY);

    // Aggregate per-type (collapsing multiple filters that resolve to the same type)
    var byType = {};
    (s.filters || []).forEach(function(f) {
      var t = filterType(f.filter);
      byType[t] = (byType[t] || 0) + (f.integrationHours || 0);
    });

    var stackY = baseY;
    var segs = '';
    TDP_FILTER_STACK_ORDER.forEach(function(t) {
      var hrs = byType[t] || 0;
      if (hrs <= 0) return;
      var segH = (hrs / yMaxBar) * plotH;
      if (segH < 0.4) return;
      stackY -= segH;
      var color = FILTER_TYPE_CHART_COLORS[t] || '#808080';
      segs += '<rect x="' + barX + '" y="' + stackY + '" width="' + barW +
        '" height="' + segH + '" fill="' + color +
        '" opacity="0.85" stroke="rgba(0,0,0,0.3)" stroke-width="0.5"/>';
    });
    // Unknown type fallback (anything not in STACK_ORDER)
    Object.keys(byType).forEach(function(t) {
      if (TDP_FILTER_STACK_ORDER.indexOf(t) === -1) {
        var hrs = byType[t];
        var segH = (hrs / yMaxBar) * plotH;
        if (segH < 0.4) return;
        stackY -= segH;
        segs += '<rect x="' + barX + '" y="' + stackY + '" width="' + barW +
          '" height="' + segH + '" fill="#808080" opacity="0.7" stroke="rgba(0,0,0,0.3)" stroke-width="0.5"/>';
      }
    });
    return segs;
  }).join('');

  // Y axis labels
  var axisLeft =
    '<text x="' + (PAD_L - 4) + '" y="' + (PAD_T + plotH + 4) + '" fill="#7a8394" font-size="9" text-anchor="end">0</text>' +
    '<text x="' + (PAD_L - 4) + '" y="' + (PAD_T + plotH/2 + 3) + '" fill="#7a8394" font-size="9" text-anchor="end">' + (yMaxBar/2).toFixed(1) + 'h</text>' +
    '<text x="' + (PAD_L - 4) + '" y="' + (PAD_T + 7) + '" fill="#7a8394" font-size="9" text-anchor="end">' + yMaxBar.toFixed(1) + 'h</text>';
  var axisRight =
    '<text x="' + (W - PAD_R + 4) + '" y="' + (PAD_T + plotH + 4) + '" fill="#b8c0d0" font-size="9" text-anchor="start">0</text>' +
    '<text x="' + (W - PAD_R + 4) + '" y="' + (PAD_T + 7) + '" fill="#b8c0d0" font-size="9" text-anchor="start">' + yMaxLine.toFixed(0) + 'h</text>';

  // X axis labels: first, middle, last
  var xLabels = '';
  function monthDay(iso) {
    if (!iso) return '';
    var d = new Date(iso);
    if (isNaN(d.getTime())) return '';
    var m = d.getMonth() + 1, dd = d.getDate();
    return (m < 10 ? '0' : '') + m + '-' + (dd < 10 ? '0' : '') + dd;
  }
  if (sorted.length > 0) {
    var firstX = PAD_L + xStep/2;
    var lastX = PAD_L + (sorted.length - 1) * xStep + xStep/2;
    var midIdx = Math.floor(sorted.length / 2);
    var midX = PAD_L + midIdx * xStep + xStep/2;
    xLabels =
      '<text x="' + firstX + '" y="' + (PAD_T + plotH + 14) + '" fill="#7a8394" font-size="9" text-anchor="middle">' + monthDay(sorted[0].sessionStart) + '</text>' +
      '<text x="' + midX + '" y="' + (PAD_T + plotH + 14) + '" fill="#7a8394" font-size="9" text-anchor="middle">' + monthDay(sorted[midIdx].sessionStart) + '</text>' +
      '<text x="' + lastX + '" y="' + (PAD_T + plotH + 14) + '" fill="#7a8394" font-size="9" text-anchor="middle">' + monthDay(sorted[sorted.length-1].sessionStart) + '</text>';
  }

  // Cumulative line overlay
  var line = '<polyline points="' + linePts.join(' ') + '" fill="none" stroke="#b8c0d0" stroke-width="1.5" stroke-linejoin="round" opacity="0.9"/>';
  var dots = linePts.map(function(p) {
    var xy = p.split(',');
    return '<circle cx="' + xy[0] + '" cy="' + xy[1] + '" r="2" fill="#b8c0d0" stroke="#161a24" stroke-width="1"/>';
  }).join('');

  // Baseline
  var baseline = '<line x1="' + PAD_L + '" y1="' + (PAD_T + plotH) + '" x2="' + (W - PAD_R) + '" y2="' + (PAD_T + plotH) + '" stroke="rgba(255,255,255,0.12)" stroke-width="1"/>';

  var svg = '<svg viewBox="0 0 ' + W + ' ' + H + '" xmlns="http://www.w3.org/2000/svg">' +
    baseline + barsSvg + line + dots + axisLeft + axisRight + xLabels +
    '</svg>';

  var filtersUsedList = TDP_FILTER_STACK_ORDER.filter(function(k) { return filtersUsed[k]; });
  // Append any unresolved types
  Object.keys(filtersUsed).forEach(function(k) {
    if (filtersUsedList.indexOf(k) === -1) filtersUsedList.push(k);
  });
  return { svg: svg, filtersUsed: filtersUsedList };
}

function renderTargetDetailPanel(data, targetName, ts) {
  var initial = targetName ? targetName.charAt(0).toUpperCase() : '?';
  var totalHrs = data.totalIntegrationHours != null ? data.totalIntegrationHours.toFixed(1) : '--';
  var hrsLabel = '<div class="tdp-kpi-val">' + esc(totalHrs) + '<span class="unit">h</span></div>';
  var avgHFR = data.avgHFR != null ? data.avgHFR.toFixed(2) : '--';
  var avgGuide = data.avgGuidingRMS != null ? data.avgGuidingRMS.toFixed(2) + '"' : '--';

  var firstDate = tdpFmtDate(data.firstSession);
  var lastDate  = tdpFmtDate(data.lastSession);
  var dateRange = 'First captured ' + firstDate + ' \u00b7 Last imaged ' + lastDate;

  // Aggregate per-filter totals across all sessions so the Integration/Frames KPI
  // boxes can show the same per-filter breakdown popup as the target card stats.
  // Output shape matches statsTargetData[idx].filters: { filter, totalSeconds, acceptedCount }.
  var aggregated = {};
  (data.sessions || []).forEach(function(s) {
    (s.filters || []).forEach(function(f) {
      var name = f.filter || 'Unknown';
      if (!aggregated[name]) {
        aggregated[name] = { filter: name, totalSeconds: 0, acceptedCount: 0, frameCount: 0 };
      }
      aggregated[name].totalSeconds  += f.integrationSeconds || 0;
      aggregated[name].acceptedCount += f.frames             || 0;
      aggregated[name].frameCount    += f.totalFrames        || 0;
    });
  });
  tdpKpiFilters = Object.keys(aggregated).map(function(k) { return aggregated[k]; });

  var headerStats =
    '<div class="tdp-header-stats">' +
      '<div class="tdp-kpi target-stat-expandable" data-stat-type="integration" data-stat-source="tdp">' + hrsLabel + '<div class="tdp-kpi-label">Integration</div></div>' +
      '<div class="tdp-kpi target-stat-expandable" data-stat-type="frames" data-stat-source="tdp"><div class="tdp-kpi-val">' + (data.totalFrames || 0) + '</div><div class="tdp-kpi-label">Frames</div></div>' +
      '<div class="tdp-kpi"><div class="tdp-kpi-val">' + (data.sessionCount || 0) + '</div><div class="tdp-kpi-label">Sessions</div></div>' +
      '<div class="tdp-kpi"><div class="tdp-kpi-val">' + esc(avgHFR) + '<span class="unit">px</span></div><div class="tdp-kpi-label">Avg HFR</div></div>' +
    '</div>';

  // Session table rows with per-filter sub-rows (aligned to parent columns,
  // Moon at the end so blank filter-cell doesn't leave a visual gap)
  var sessions = data.sessions || [];
  var rows = sessions.map(function(s, idx) {
    var subRows = (s.filters || [])
      .slice()
      .sort(function(a, b) {
        // Sort sub-rows by same stack order
        var ta = resolveFilterType(a.filter) || 'Z';
        var tb = resolveFilterType(b.filter) || 'Z';
        var ia = TDP_FILTER_STACK_ORDER.indexOf(ta); if (ia === -1) ia = 99;
        var ib = TDP_FILTER_STACK_ORDER.indexOf(tb); if (ib === -1) ib = 99;
        return ia - ib;
      })
      .map(function(f) {
        var fHFR = f.avgHFR != null ? f.avgHFR.toFixed(2) : '--';
        var fGuide = f.avgGuidingRMS != null ? f.avgGuidingRMS.toFixed(2) + '"' : '--';
        var fMin = Math.round((f.integrationSeconds || 0) / 60);
        return '<tr class="tdp-filter-subrow" data-for="' + idx + '" style="display:none">' +
          '<td></td>' +
          '<td class="pdp-subrow-integration">' + filterTypePill(f.filter) + '<span>' + esc(tdpFmtDuration(fMin)) + '</span></td>' +
          '<td>' + (f.frames || 0) + '</td>' +
          '<td>' + esc(fHFR) + '</td>' +
          '<td>' + esc(fGuide) + '</td>' +
          '<td></td>' +
          '<td></td>' +
        '</tr>';
      }).join('');

    var sHFR = s.avgHFR != null ? s.avgHFR.toFixed(2) : '--';
    var sGuide = s.avgGuidingRMS != null ? s.avgGuidingRMS.toFixed(2) + '"' : '--';
    var sessionDurMin = Math.round((s.integrationSeconds || 0) / 60);
    var sessionDurationDisplay = tdpFmtDuration(sessionDurMin);

    return '<tr class="tdp-session-row" data-idx="' + idx + '" data-session-id="' + esc(s.sessionId || '') + '">' +
        '<td><span class="tdp-date-long">' + esc(tdpFmtDate(s.sessionStart)) + '</span>' +
             '<span class="tdp-date-short">' + esc(tdpFmtDateShort(s.sessionStart)) + '</span></td>' +
        '<td>' + esc(sessionDurationDisplay) + '</td>' +
        '<td>' + (s.frames || 0) + '</td>' +
        '<td>' + esc(sHFR) + '</td>' +
        '<td>' + esc(sGuide) + '</td>' +
        '<td>' + esc(s.moonPhase || '--') + '</td>' +
        '<td><span class="tdp-row-link" data-session-id="' + esc(s.sessionId || '') + '">View</span></td>' +
      '</tr>' + subRows;
  }).join('');

  // ── Title row pills ──────────────────────────────────────────────────────
  var titlePills = '';
  if (ts && ts.project) {
    var proj = ts.project;
    titlePills += '<span class="tdp-project-state-pill" data-state="' + esc(proj.state || 'Draft') +
      '" data-project-guid="' + esc(proj.guid || '') + '" title="Click to override status">' +
      esc(proj.state || 'Draft') +
      (proj.stateSource === 'override' ? ' \u00b7' : '') +
      '</span>';
    if (proj.isMosaic) {
      titlePills += '<span class="tdp-type-pill">Mosaic Panel</span>';
    }
  }

  // ── TS Progress bars ─────────────────────────────────────────────────────
  var tdpProgressHtml = '';
  if (statsTsStatus === 'available' && ts && ts.project) {
    var tsproj = ts.project;
    var tsgoals = ts.goals || [];

    var STACK_ORDER = ['L', 'R', 'G', 'B', 'H', 'S', 'O', 'N'];
    var sortedGoals = tsgoals.slice().sort(function(a, b) {
      var ai = STACK_ORDER.indexOf(resolveFilterType(a.filter) || '');
      var bi = STACK_ORDER.indexOf(resolveFilterType(b.filter) || '');
      if (ai < 0) ai = STACK_ORDER.length;
      if (bi < 0) bi = STACK_ORDER.length;
      if (ai !== bi) return ai - bi;
      return (b.exposureSec || 0) - (a.exposureSec || 0);
    });

    function extractExposureFromTemplate(name) {
      if (!name) return 0;
      var m = String(name).match(/(\d+)\s*s\b/i);
      return m ? parseInt(m[1], 10) : 0;
    }

    var goalRows = sortedGoals.map(function(g) {
      var pct = g.percentComplete;
      // effective = accepted when grading is active; falls back to acquired
      // (grading pending/disabled). Matches the server-side percentComplete calc.
      var effective = (g.effective != null) ? g.effective : g.accepted;
      var over = pct != null && effective > g.desired;
      var widthPct = pct != null ? Math.min(100, pct) : 0;
      var filterType = resolveFilterType(g.filter);
      var fillColor = (filterType && FILTER_TYPE_CHART_COLORS[filterType]) || '#66BB6A';
      var expSec = (g.exposureSec && g.exposureSec > 0)
        ? g.exposureSec
        : extractExposureFromTemplate(g.templateName);
      var labelText = expSec > 0 ? (expSec + 's') : (g.templateName || '');
      var labelHtml = labelText
        ? '<span class="tdp-progress-row-label-text">' + esc(labelText) + '</span>'
        : '';
      var tmplAttr = g.templateName ? ' data-template="' + esc(g.templateName) + '"' : '';
      return '<div class="tdp-progress-row">' +
        '<div class="tdp-progress-row-label"' + tmplAttr + '>' +
          filterTypePill(g.filter) + labelHtml +
        '</div>' +
        '<div class="tdp-progress-bar-wrap' + (over ? ' over' : '') + '" style="--fill-color:' + fillColor + '"' + tmplAttr + '>' +
          '<div class="tdp-progress-bar-fill" style="width:' + widthPct + '%"></div>' +
        '</div>' +
        '<div class="tdp-progress-row-count">' +
          effective + ' <span class="unit">/ ' + g.desired + '</span>' +
        '</div>' +
      '</div>';
    }).join('');

    var overallRow = '';
    if (tsproj.percentComplete != null) {
      var overallPct = tsproj.percentComplete;
      overallRow = '<div class="tdp-overall-separator"></div>' +
        '<div class="tdp-progress-row tdp-progress-row-overall">' +
          '<div class="tdp-progress-row-label"><span class="tdp-progress-row-label-text tdp-overall-label">Overall</span></div>' +
          '<div class="tdp-progress-bar-wrap tdp-overall-bar-wrap' + (overallPct > 100 ? ' over' : '') + '">' +
            '<div class="tdp-progress-bar-fill tdp-overall-bar-fill" style="width:' + Math.min(overallPct, 100).toFixed(1) + '%"></div>' +
          '</div>' +
          '<strong class="tdp-progress-row-count tdp-overall-count">' + overallPct.toFixed(1) + '%</strong>' +
        '</div>';
    }

    if (goalRows || overallRow) {
      tdpProgressHtml = '<div class="tdp-progress-section">' +
        '<div class="tdp-project-progress-grid">' + goalRows + overallRow + '</div>' +
      '</div>';
    }
  }

  // ── TS Actions row ───────────────────────────────────────────────────────
  var tdpActionsHtml = '';
  if (statsTsStatus === 'available') {
    if (!ts || !ts.project) {
      tdpActionsHtml = '<div class="tdp-ts-actions">' +
        '<button type="button" class="tdp-project-action-btn" data-action="link-ts">Link to TS target\u2026</button>' +
      '</div>';
    } else {
      tdpActionsHtml = '<div class="tdp-ts-actions">' +
        '<button type="button" class="tdp-project-action-btn" data-action="link-ts">Change TS link\u2026</button>' +
        (ts.matchedBy === 'manual'
          ? '<button type="button" class="tdp-project-action-btn" data-action="unlink-ts">Clear manual link</button>'
          : '') +
      '</div>';
    }
  }

  // Chart is injected after the panel is in the DOM (so we can measure width).
  // sessions data is stashed on the wrapper via a data attribute handled in JS.
  return '' +
    '<div class="tdp-modal" role="dialog" aria-label="Target detail">' +
      '<button class="tdp-close" aria-label="Close">\u2715</button>' +

      '<div class="tdp-title-section">' +
        '<div class="tdp-title-row">' +
          '<h2>' + esc(targetName) + '</h2>' +
          titlePills +
        '</div>' +
        '<div class="tdp-daterange">' + esc(dateRange) + '</div>' +
      '</div>' +

      '<div class="tdp-hero">' +
        '<div class="tdp-hero-wrap" id="tdp-hero-wrap">' +
          '<div class="tdp-thumb-placeholder">' + esc(initial) + '</div>' +
        '</div>' +
      '</div>' +

      '<div class="tdp-stats-section">' +
        headerStats +
      '</div>' +

      tdpProgressHtml +
      tdpActionsHtml +

      '<div class="tdp-body">' +
        '<div class="tdp-section-title">Integration Over Time</div>' +
        '<div class="tdp-chart-wrap">' +
          '<div class="tdp-chart-svg"></div>' +
          '<div class="tdp-chart-legend"></div>' +
        '</div>' +
        '<div class="tdp-section-title">Session History</div>' +
        '<table class="tdp-table">' +
          '<thead><tr><th>Date</th><th>Integration</th><th>Frames</th><th>HFR</th><th>Guide</th><th>Moon</th><th></th></tr></thead>' +
          '<tbody>' + rows + '</tbody>' +
        '</table>' +
      '</div>' +
    '</div>';
}

// ── Phase 3a: TS project section in target detail panel ───────────────────

function renderTsProjectSection(ts, targetName) {
  // Not shown when TS data isn't available at all
  if (statsTsStatus !== 'available') return '';

  // Unlinked target: show CTA to link manually
  if (!ts || !ts.project) {
    return '<div class="tdp-project-unlinked">' +
      '<div>No Target Scheduler project linked to <strong>' + esc(targetName) + '</strong>.</div>' +
      '<button type="button" class="tdp-project-action-btn" data-action="link-ts">Link to TS target\u2026</button>' +
    '</div>';
  }

  var proj = ts.project;
  var tgt  = ts.target || {};
  var goals = ts.goals || [];

  // Per-filter progress rows — primary sort by filter type (stack order),
  // secondary by exposure length descending so "R 300s" shows above "R 5s".
  var STACK_ORDER = ['L', 'R', 'G', 'B', 'H', 'S', 'O', 'N'];
  var sortedGoals = goals.slice().sort(function(a, b) {
    var ai = STACK_ORDER.indexOf(resolveFilterType(a.filter) || '');
    var bi = STACK_ORDER.indexOf(resolveFilterType(b.filter) || '');
    if (ai < 0) ai = STACK_ORDER.length;
    if (bi < 0) bi = STACK_ORDER.length;
    if (ai !== bi) return ai - bi;
    return (b.exposureSec || 0) - (a.exposureSec || 0);
  });

  // Regex fallback when exposureSec is missing from the API response.
  // Most TS users name templates with a trailing "300s" or "5s" pattern.
  function extractExposureFromTemplate(name) {
    if (!name) return 0;
    var m = String(name).match(/(\d+)\s*s\b/i);
    return m ? parseInt(m[1], 10) : 0;
  }

  var rows = sortedGoals.map(function(g) {
    var pct = g.percentComplete;
    var effective = (g.effective != null) ? g.effective : g.accepted;
    var over = pct != null && effective > g.desired;
    var widthPct = pct != null ? Math.min(100, pct) : 0;
    // Use the same color map as the stacked chart bars (L is off-white).
    var filterType = resolveFilterType(g.filter);
    var fillColor = (filterType && FILTER_TYPE_CHART_COLORS[filterType]) || '#66BB6A';
    // Always show the exposure length next to every filter pill. Prefer
    // exposureSec from the API, fall back to extracting from templateName,
    // and if we still can't parse it fall back to the raw template name.
    var expSec = (g.exposureSec && g.exposureSec > 0)
      ? g.exposureSec
      : extractExposureFromTemplate(g.templateName);
    var labelText = expSec > 0 ? (expSec + 's') : (g.templateName || '');
    var labelHtml = labelText
      ? '<span class="tdp-progress-row-label-text">' + esc(labelText) + '</span>'
      : '';
    var tmplAttr = g.templateName ? ' data-template="' + esc(g.templateName) + '"' : '';
    return '<div class="tdp-progress-row">' +
      '<div class="tdp-progress-row-label"' + tmplAttr + '>' +
        filterTypePill(g.filter) + labelHtml +
      '</div>' +
      '<div class="tdp-progress-bar-wrap' + (over ? ' over' : '') + '" style="--fill-color:' + fillColor + '"' + tmplAttr + '>' +
        '<div class="tdp-progress-bar-fill" style="width:' + widthPct + '%"></div>' +
      '</div>' +
      '<div class="tdp-progress-row-count">' +
        effective + ' <span class="unit">/ ' + g.desired + '</span>' +
      '</div>' +
    '</div>';
  }).join('');

  // Metadata row (priority, altitude, created, activated)
  var metaParts = [];
  if (proj.priority)        metaParts.push('<span>Priority <strong>' + esc(proj.priority) + '</strong></span>');
  if (proj.isMosaic)        metaParts.push('<span>Mosaic <strong>panel of ' + proj.targetCount + '</strong></span>');
  if (proj.minimumAltitude) metaParts.push('<span>Min alt <strong>' + proj.minimumAltitude + '\u00b0</strong></span>');
  if (proj.activeDate)      metaParts.push('<span>Started <strong>' + esc(fmtRelativeTime(proj.activeDate)) + '</strong></span>');
  else if (proj.createDate) metaParts.push('<span>Created <strong>' + esc(fmtRelativeTime(proj.createDate)) + '</strong></span>');

  var matchedByNote = ts.matchedBy === 'manual'
    ? '<span style="color:var(--accent);">manually linked</span>'
    : '';

  var overallPct = proj.percentComplete != null ? proj.percentComplete.toFixed(1) + '%' : '\u2014';

  return '<div class="tdp-project-section">' +
    '<div class="tdp-project-header">' +
      '<div class="tdp-project-name">' + esc(proj.name || 'TS Project') +
        (proj.isMosaic ? ' <span style="font-size:9px;color:var(--text-tertiary);text-transform:uppercase;letter-spacing:0.8px;font-weight:600;">\u00b7 Mosaic</span>' : '') +
      '</div>' +
      '<span class="tdp-project-state-pill" data-state="' + esc(proj.state || 'Draft') +
        '" data-project-guid="' + esc(proj.guid || '') + '" title="Click to override status">' +
        esc(proj.state || 'Draft') +
        (proj.stateSource === 'override' ? ' \u00b7' : '') +
      '</span>' +
    '</div>' +
    (metaParts.length ? '<div class="tdp-project-meta-row">' + metaParts.join('') + (matchedByNote ? '<span>' + matchedByNote + '</span>' : '') + '</div>' : '') +
    (rows
      ? '<div class="tdp-project-progress-grid">' + rows +
          (proj.percentComplete != null
            ? '<div class="tdp-overall-separator"></div>' +
              '<div class="tdp-progress-row tdp-progress-row-overall">' +
                '<div class="tdp-progress-row-label"><span class="tdp-progress-row-label-text tdp-overall-label">Overall</span></div>' +
                '<div class="tdp-progress-bar-wrap tdp-overall-bar-wrap' + (proj.percentComplete > 100 ? ' over' : '') + '">' +
                  '<div class="tdp-progress-bar-fill tdp-overall-bar-fill" style="width:' + Math.min(proj.percentComplete, 100).toFixed(1) + '%"></div>' +
                '</div>' +
                '<strong class="tdp-progress-row-count tdp-overall-count">' + overallPct + '</strong>' +
              '</div>'
            : '') +
        '</div>'
      : '<div style="color:var(--text-tertiary);font-size:11px;">No exposure plans defined for this target.</div>') +
    '<div class="tdp-project-actions">' +
      '<button type="button" class="tdp-project-action-btn" data-action="link-ts">Change TS link\u2026</button>' +
      (ts.matchedBy === 'manual'
        ? '<button type="button" class="tdp-project-action-btn" data-action="unlink-ts">Clear manual link</button>'
        : '') +
    '</div>' +
  '</div>';
}

// Measure the chart container and render the SVG + HTML legend into it.
// Called after the panel is inserted, and again on window resize.
function renderChartIntoPanel(backdrop, sessions) {
  if (!backdrop) return;
  var wrap = backdrop.querySelector('.tdp-chart-wrap');
  var svgHost = backdrop.querySelector('.tdp-chart-svg');
  var legendHost = backdrop.querySelector('.tdp-chart-legend');
  if (!wrap || !svgHost || !legendHost) return;

  var cs = window.getComputedStyle(wrap);
  var pL = parseFloat(cs.paddingLeft) || 0;
  var pR = parseFloat(cs.paddingRight) || 0;
  var innerWidth = Math.max(320, Math.floor(wrap.clientWidth - pL - pR));

  var chart = renderTargetChart(sessions, innerWidth);
  svgHost.innerHTML = chart.svg;

  // HTML legend: filter pills + cumulative line marker
  var pillsHtml = (chart.filtersUsed || []).map(function(k) { return filterTypePill(k); }).join('');
  var cumHtml = '<span class="tdp-chart-legend-cum"><span class="tdp-cum-line"></span>Cumulative</span>';
  legendHost.innerHTML = pillsHtml + cumHtml;
}

var _tdpKeyHandler = null;
var _tdpResizeHandler = null;
var _tdpResizeDebounce = null;
function closeTargetDetail() {
  var backdrop = document.getElementById('tdp-backdrop');
  if (!backdrop) return;
  backdrop.id = '';
  backdrop.classList.add('tdp-hiding');
  setTimeout(function() { if (backdrop.parentNode) backdrop.parentNode.removeChild(backdrop); }, 160);
  document.body.style.overflow = '';
  if (_tdpKeyHandler) {
    document.removeEventListener('keydown', _tdpKeyHandler);
    _tdpKeyHandler = null;
  }
  if (_tdpResizeHandler) {
    window.removeEventListener('resize', _tdpResizeHandler);
    _tdpResizeHandler = null;
  }
  if (_tdpResizeDebounce) {
    clearTimeout(_tdpResizeDebounce);
    _tdpResizeDebounce = null;
  }
  tdpKpiFilters = null;
  // Dismiss any stat expand popup that was anchored to a KPI box
  if (typeof hideStatExpand === 'function') hideStatExpand();
}

function bindTargetDetailEvents(backdrop, targetName) {
  // Click outside modal = close
  backdrop.addEventListener('click', function(e) {
    if (e.target === backdrop) closeTargetDetail();
  });
  // Close button
  var closeBtn = backdrop.querySelector('.tdp-close');
  if (closeBtn) closeBtn.addEventListener('click', closeTargetDetail);

  // Escape key closes
  _tdpKeyHandler = function(e) { if (e.key === 'Escape') closeTargetDetail(); };
  document.addEventListener('keydown', _tdpKeyHandler);

  // Expand/collapse session rows
  backdrop.querySelectorAll('tr.tdp-session-row').forEach(function(row) {
    row.addEventListener('click', function(e) {
      if (e.target.classList.contains('tdp-row-link')) return; // let the link handler run
      var idx = row.getAttribute('data-idx');
      var subs = backdrop.querySelectorAll('tr.tdp-filter-subrow[data-for="' + idx + '"]');
      if (!subs.length) return;
      var isOpen = row.classList.toggle('tdp-expanded');
      subs.forEach(function(sub) { sub.style.display = isOpen ? '' : 'none'; });
    });
  });

  // View report link → opens the session report in a new tab
  backdrop.querySelectorAll('.tdp-row-link').forEach(function(link) {
    link.addEventListener('click', function(e) {
      e.stopPropagation();
      var sid = link.getAttribute('data-session-id');
      if (!sid) return;
      window.open('/api/sessions/' + encodeURIComponent(sid) + '/report', '_blank', 'noopener');
    });
  });

  // Phase 3a: project state pill (override dropdown), link-ts + unlink buttons
  var statePill = backdrop.querySelector('.tdp-project-state-pill');
  if (statePill) {
    statePill.addEventListener('click', function(e) {
      e.stopPropagation();
      openTsOverrideDropdown(statePill);
    });
  }
  backdrop.querySelectorAll('.tdp-project-action-btn').forEach(function(btn) {
    btn.addEventListener('click', function(e) {
      e.stopPropagation();
      var action = btn.getAttribute('data-action');
      if (action === 'link-ts') {
        openTsLinkPicker(targetName);
      } else if (action === 'unlink-ts') {
        applyTsTargetLink(targetName, '', function() {
          // Close and re-open the panel with refreshed data
          closeTargetDetail();
          renderStats();
        });
      }
    });
  });
}

// Load the thumbnail for the panel header from the latest session's thumbnails
// (case-insensitive target match). Reuses thumbnailCache.
function loadTargetDetailThumb(targetName, latestSessionId) {
  if (!latestSessionId) return;
  var thumbEl = document.getElementById('tdp-hero-wrap');
  if (!thumbEl) return;

  function apply(thumbs) {
    if (!Array.isArray(thumbs)) return;
    var lower = (targetName || '').toLowerCase();
    var match = null;
    for (var i = 0; i < thumbs.length; i++) {
      if (thumbs[i].target === targetName) { match = thumbs[i]; break; }
    }
    if (!match) {
      for (var j = 0; j < thumbs.length; j++) {
        if ((thumbs[j].target || '').toLowerCase() === lower) { match = thumbs[j]; break; }
      }
    }
    if (match && match.dataUri) {
      var svg = '';
      if (match.fovSvg) {
        svg = match.fovSvg
          .replace(/width='\d+'/, "width='100%'")
          .replace(/height='\d+'/, "height='100%'")
          .replace("<svg ", "<svg viewBox='0 0 200 200' " + (showFovOverlay ? '' : "style='display:none' "));
      }
      thumbEl.innerHTML = '<img src="' + match.dataUri + '" alt="' + esc(targetName) + '">' + svg;
    }
  }

  if (thumbnailCache[latestSessionId]) {
    apply(thumbnailCache[latestSessionId]);
    return;
  }
  api('/api/sessions/' + latestSessionId + '/thumbnails').then(function(thumbs) {
    if (Array.isArray(thumbs) && thumbs.length > 0) thumbnailCache[latestSessionId] = thumbs;
    apply(thumbs);
  }).catch(function() { /* leave placeholder */ });
}

// Look up the TS payload for a target by name from the current statsTargetData cache.
function findTsForTarget(targetName) {
  if (!statsTargetData || !targetName) return null;
  var lower = targetName.toLowerCase();
  for (var i = 0; i < statsTargetData.length; i++) {
    var t = statsTargetData[i];
    if (t && t.target && t.target.toLowerCase() === lower) return t.ts || null;
  }
  return null;
}

function openTargetDetail(targetName, latestSessionId) {
  if (!targetName) return;
  // Close any existing panel first
  closeTargetDetail();

  // Loading placeholder so the user gets immediate feedback
  var backdrop = document.createElement('div');
  backdrop.id = 'tdp-backdrop';
  backdrop.className = 'tdp-backdrop';
  backdrop.innerHTML = '<div class="tdp-modal" style="padding:40px;text-align:center;color:var(--text-tertiary);">Loading \u2026</div>';
  document.body.appendChild(backdrop);
  document.body.style.overflow = 'hidden';
  backdrop.addEventListener('touchmove', function(e) { if (e.target === backdrop) e.preventDefault(); }, { passive: false });

  // Tentative close on backdrop click while loading
  var loadClickHandler = function(e) { if (e.target === backdrop) closeTargetDetail(); };
  backdrop.addEventListener('click', loadClickHandler);
  _tdpKeyHandler = function(e) { if (e.key === 'Escape') closeTargetDetail(); };
  document.addEventListener('keydown', _tdpKeyHandler);

  var ts = findTsForTarget(targetName);

  api('/api/stats/targets/' + encodeURIComponent(targetName) + '/sessions').then(function(data) {
    // If the user closed it while loading, bail out
    var current = document.getElementById('tdp-backdrop');
    if (!current || current !== backdrop) return;
    backdrop.removeEventListener('click', loadClickHandler);
    backdrop.innerHTML = renderTargetDetailPanel(data, targetName, ts);
    bindTargetDetailEvents(backdrop, targetName);
    loadTargetDetailThumb(targetName, latestSessionId);
    // Chart renders after the panel is in the DOM so we can measure available width.
    // Use rAF to ensure layout has settled (kpi grid, etc.).
    var sessions = data.sessions || [];
    requestAnimationFrame(function() { renderChartIntoPanel(backdrop, sessions); });
    // Re-render chart on window resize (debounced) so it stays full-width.
    _tdpResizeHandler = function() {
      if (_tdpResizeDebounce) clearTimeout(_tdpResizeDebounce);
      _tdpResizeDebounce = setTimeout(function() {
        renderChartIntoPanel(backdrop, sessions);
      }, 120);
    };
    window.addEventListener('resize', _tdpResizeHandler);
  }).catch(function(err) {
    logError('Failed to load target detail:', err && err.message);
    var current = document.getElementById('tdp-backdrop');
    if (!current || current !== backdrop) return;
    backdrop.innerHTML = '<div class="tdp-modal" style="padding:40px;text-align:center;">' +
      '<div style="color:#e15759;font-weight:600;margin-bottom:10px;">Failed to load</div>' +
      '<div style="color:var(--text-tertiary);font-size:12px;">' + esc(err && err.message ? err.message : 'unknown error') + '</div>' +
      '<button class="tdp-close" aria-label="Close">\u2715</button></div>';
    var c = backdrop.querySelector('.tdp-close');
    if (c) c.addEventListener('click', closeTargetDetail);
  });
}

// ── Project Detail Panel (Phase 3c) ──────────────────────────────────────────
// Shows combined mosaic HiPS thumbnail with per-panel FOV overlay rectangles,
// plus per-panel stats. Opens when user clicks "View Details" on a project container.

var _pdpKeyHandler = null;
var _pdpResizeHandler = null;
var _pdpResizeDebounce = null;

function closeProjectDetail() {
  var backdrop = document.getElementById('pdp-backdrop');
  if (!backdrop) return;
  backdrop.id = '';
  backdrop.classList.add('pdp-hiding');
  setTimeout(function() { if (backdrop.parentNode) backdrop.parentNode.removeChild(backdrop); }, 160);
  document.body.style.overflow = '';
  tdpKpiFilters = null;
  if (_pdpKeyHandler) {
    document.removeEventListener('keydown', _pdpKeyHandler);
    _pdpKeyHandler = null;
  }
  if (_pdpResizeHandler) {
    window.removeEventListener('resize', _pdpResizeHandler);
    _pdpResizeHandler = null;
  }
  if (_pdpResizeDebounce) {
    clearTimeout(_pdpResizeDebounce);
    _pdpResizeDebounce = null;
  }
}

function openProjectDetail(projectGuid, projectName) {
  if (!projectGuid) return;
  closeProjectDetail();

  var backdrop = document.createElement('div');
  backdrop.id = 'pdp-backdrop';
  backdrop.className = 'pdp-backdrop';
  backdrop.innerHTML = '<div class="pdp-modal pdp-modal--loading"><span style="color:var(--text-tertiary)">Loading \u2026</span></div>';
  document.body.appendChild(backdrop);
  document.body.style.overflow = 'hidden';
  backdrop.addEventListener('touchmove', function(e) { if (e.target === backdrop) e.preventDefault(); }, { passive: false });

  var loadClickHandler = function(e) { if (e.target === backdrop) closeProjectDetail(); };
  backdrop.addEventListener('click', loadClickHandler);
  _pdpKeyHandler = function(e) { if (e.key === 'Escape') closeProjectDetail(); };
  document.addEventListener('keydown', _pdpKeyHandler);

  api('/api/stats/projects/' + encodeURIComponent(projectGuid)).then(function(data) {
    var current = document.getElementById('pdp-backdrop');
    if (!current || current !== backdrop) return;
    backdrop.removeEventListener('click', loadClickHandler);
    backdrop.innerHTML = renderProjectDetailPanel(data);

    backdrop.addEventListener('click', function(e) { if (e.target === backdrop) closeProjectDetail(); });
    var closeBtn = backdrop.querySelector('.pdp-close');
    if (closeBtn) closeBtn.addEventListener('click', closeProjectDetail);

    var pdpImagedPanels = (data.panels || []).filter(function(p) {
      return (p.sessionCount || 0) > 0 || (p.acceptedFrames || 0) > 0;
    });
    var pdpIsMosaic = !!(data.project || {}).isMosaic;
    if (!pdpIsMosaic && pdpImagedPanels.length >= 2) {
      loadPdpMultiThumbs(backdrop, pdpImagedPanels);
    } else if (!pdpIsMosaic && pdpImagedPanels.length === 1) {
      loadPdpSingleThumb(backdrop, pdpImagedPanels[0]);
    } else {
      loadMosaicThumbnail(data.panels || [], backdrop, projectGuid);
    }

    // Panel card click → drill-down
    bindPdpPanelCardClicks(backdrop, data, projectGuid);

    // Fetch project sessions for chart + table
    api('/api/stats/projects/' + encodeURIComponent(projectGuid) + '/sessions').then(function(sessData) {
      var cur = document.getElementById('pdp-backdrop');
      if (!cur || cur !== backdrop) return;
      var sessions = sessData.sessions || [];
      if (!sessions.length) return;

      // Show and populate chart
      var chartSection = backdrop.querySelector('.pdp-chart-section');
      if (chartSection) {
        chartSection.style.display = '';
        renderPdpChart(backdrop, sessions);
      }

      // Show and populate session table
      var sessSection = backdrop.querySelector('.pdp-sessions-section');
      var tableWrap = backdrop.querySelector('.pdp-sessions-table-wrap');
      if (sessSection && tableWrap) {
        sessSection.style.display = '';
        var panelNames = sessData.panelNames || [];
        tableWrap.innerHTML = buildPdpSessionTable(sessions, panelNames.length > 1);
        bindPdpSessionTableEvents(backdrop);
      }

      // Resize handler for chart reflow
      if (_pdpResizeHandler) window.removeEventListener('resize', _pdpResizeHandler);
      _pdpResizeHandler = function() {
        if (_pdpResizeDebounce) clearTimeout(_pdpResizeDebounce);
        _pdpResizeDebounce = setTimeout(function() { renderPdpChart(backdrop, sessions); }, 120);
      };
      window.addEventListener('resize', _pdpResizeHandler);
    }).catch(function() { /* session fetch failed — chart/table just stay hidden */ });
  }).catch(function(err) {
    var current = document.getElementById('pdp-backdrop');
    if (!current || current !== backdrop) return;
    backdrop.innerHTML = '<div class="pdp-modal pdp-modal--loading">' +
      '<div style="color:#e15759;font-weight:600;margin-bottom:10px;">Failed to load</div>' +
      '<div style="color:var(--text-tertiary);font-size:12px;">' + esc(err && err.message ? err.message : 'unknown error') + '</div>' +
      '<button class="pdp-close">\u2715</button></div>';
    var c = backdrop.querySelector('.pdp-close');
    if (c) c.addEventListener('click', closeProjectDetail);
  });
}

function renderProjectDetailPanel(data) {
  var proj  = data.project  || {};
  var panels = data.panels  || [];
  var agg   = data.aggregate || {};

  var html = '<div class="pdp-modal">';
  html += '<button type="button" class="pdp-close" aria-label="Close">\u2715</button>';

  // ── 1. Header: title + date only ─────────────────────────────────────────
  html += '<div class="pdp-header">';
  html += '<div class="pdp-header-title-row">';
  html += '<h2 class="pdp-title">' + esc(proj.name || 'Project') + '</h2>';
  html += '<span class="target-card-ts-badge" data-state="' + esc(proj.state || '') + '">' + esc(proj.state || '') + '</span>';
  if (proj.isMosaic) html += '<span class="targets-project-mosaic-badge">Mosaic</span>';
  html += '</div>';

  // Date row
  var pdpDateHtml = '';
  if (agg.firstImaged && agg.lastImaged) {
    pdpDateHtml = 'First captured ' + esc(fmtRelativeTime(agg.firstImaged)) + ' \u00b7 Last imaged ' + esc(fmtRelativeTime(agg.lastImaged));
  } else if (agg.lastImaged) {
    pdpDateHtml = 'Last imaged ' + esc(fmtRelativeTime(agg.lastImaged));
  }
  if (pdpDateHtml) {
    html += '<div class="pdp-daterange">' + pdpDateHtml + '</div>';
  }

  if (proj.description) {
    html += '<div class="pdp-description">' + esc(proj.description) + '</div>';
  }
  html += '</div>'; // end pdp-header

  // ── 2. Hero thumbnail(s) — multi-project gets a grid, everything else single wrap ─
  var imagedPanels = panels.filter(function(p) {
    return (p.sessionCount || 0) > 0 || (p.acceptedFrames || 0) > 0;
  });
  var isMultiGrid = !proj.isMosaic && imagedPanels.length >= 2;

  html += '<div class="pdp-mosaic-section">';
  if (isMultiGrid) {
    html += '<div class="pdp-multi-thumb-grid" data-count="' + imagedPanels.length + '">';
    imagedPanels.forEach(function(p, i) {
      html += '<div class="pdp-multi-thumb-cell" id="pdp-panel-thumb-' + i + '">';
      html += '<div class="pdp-cell-placeholder">' + esc((p.name || '').charAt(0).toUpperCase()) + '</div>';
      html += '<div class="pdp-cell-label">' + esc(p.name || '') + '</div>';
      html += '</div>';
    });
    html += '</div>';
  } else {
    html += '<div class="pdp-mosaic-thumb-wrap" id="pdp-thumb-wrap">';
    html += '<div class="pdp-mosaic-placeholder">\u2606</div>';
    html += '</div>';
  }
  html += '</div>';

  // ── 3. KPI stats section ─────────────────────────────────────────────────
  // Aggregate per-filter totals across all panels for hover breakdown popup.
  var pdpKpiAgg = {};
  panels.forEach(function(panel) {
    (panel.filters || []).forEach(function(f) {
      var name = f.filter || 'Unknown';
      if (!pdpKpiAgg[name]) pdpKpiAgg[name] = { filter: name, totalSeconds: 0, acceptedCount: 0 };
      pdpKpiAgg[name].totalSeconds  += (f.totalHours || 0) * 3600;
      pdpKpiAgg[name].acceptedCount += f.acceptedFrames || 0;
    });
  });
  tdpKpiFilters = Object.keys(pdpKpiAgg).map(function(k) { return pdpKpiAgg[k]; });

  html += '<div class="pdp-stats-section">';
  html += '<div class="pdp-kpi-row">';
  html += '<div class="pdp-kpi target-stat-expandable" data-stat-type="integration" data-stat-source="tdp">' +
    '<div class="pdp-kpi-val">' + (agg.totalIntegrationHours || 0).toFixed(1) + '<span class="unit">h</span></div>' +
    '<div class="pdp-kpi-label">Integration</div></div>';
  html += '<div class="pdp-kpi target-stat-expandable" data-stat-type="frames" data-stat-source="tdp">' +
    '<div class="pdp-kpi-val">' + (agg.acceptedFrames || 0) + '</div>' +
    '<div class="pdp-kpi-label">Frames</div></div>';
  html += '<div class="pdp-kpi"><div class="pdp-kpi-val">' + (agg.sessionCount || 0) +
    '</div><div class="pdp-kpi-label">Sessions</div></div>';
  html += '<div class="pdp-kpi"><div class="pdp-kpi-val">' + panels.length +
    '</div><div class="pdp-kpi-label">Panels</div></div>';
  html += '</div>';
  html += '</div>'; // end pdp-stats-section

  // ── 4. Cumulative TS progress bars — aggregate goals across all panels ────
  // Prefer tsGoals embedded in the project API response (works even for unimaged targets).
  // Fall back to statsTargetData lookup for older API responses.
  var cumulativeGoalsMap = {};
  panels.forEach(function(panel) {
    var goals = null;
    if (panel.tsGoals && panel.tsGoals.length) {
      goals = panel.tsGoals;
    } else {
      var tsTarget = (statsTargetData || []).find(function(t) {
        return t.target && panel.name && t.target.toLowerCase() === panel.name.toLowerCase();
      });
      if (tsTarget && tsTarget.ts && tsTarget.ts.goals) goals = tsTarget.ts.goals;
    }
    if (!goals || !goals.length) return;
    goals.forEach(function(g) {
      var key = (g.filter || '') + '|' + (g.exposureSec || 0);
      if (!cumulativeGoalsMap[key]) {
        cumulativeGoalsMap[key] = {
          filter: g.filter, exposureSec: g.exposureSec, templateName: g.templateName,
          accepted: 0, desired: 0
        };
      }
      // Grading-pending fallback: use acquired when accepted=0 but acquired>0
      var effective = (g.accepted || 0) > 0 ? (g.accepted || 0) : (g.acquired || 0);
      cumulativeGoalsMap[key].accepted += effective;
      cumulativeGoalsMap[key].desired  += (g.desired  || 0);
    });
  });
  var PDP_STACK = ['L', 'R', 'G', 'B', 'H', 'S', 'O', 'N'];
  var cumulativeGoals = Object.keys(cumulativeGoalsMap).map(function(k) {
    return cumulativeGoalsMap[k];
  }).sort(function(a, b) {
    var ai = PDP_STACK.indexOf(resolveFilterType(a.filter) || ''); if (ai < 0) ai = PDP_STACK.length;
    var bi = PDP_STACK.indexOf(resolveFilterType(b.filter) || ''); if (bi < 0) bi = PDP_STACK.length;
    if (ai !== bi) return ai - bi;
    return (b.exposureSec || 0) - (a.exposureSec || 0);
  });

  if (cumulativeGoals.length > 0) {
    var pdpTotalAcc = 0, pdpTotalDes = 0;
    var pdpProgressRows = cumulativeGoals.map(function(g) {
      pdpTotalAcc += g.accepted;
      pdpTotalDes += g.desired;
      var pct = g.desired > 0 ? g.accepted / g.desired * 100 : 0;
      var over = g.accepted > g.desired;
      var filterType = resolveFilterType(g.filter);
      var fillColor = (filterType && FILTER_TYPE_CHART_COLORS[filterType]) || '#66BB6A';
      var expSec = g.exposureSec && g.exposureSec > 0 ? g.exposureSec : 0;
      var labelText = expSec > 0 ? (expSec + 's') : (g.templateName || '');
      var labelHtml = labelText ? '<span class="tdp-progress-row-label-text">' + esc(labelText) + '</span>' : '';
      return '<div class="tdp-progress-row">' +
        '<div class="tdp-progress-row-label">' + filterTypePill(g.filter) + labelHtml + '</div>' +
        '<div class="tdp-progress-bar-wrap' + (over ? ' over' : '') + '" style="--fill-color:' + fillColor + '">' +
          '<div class="tdp-progress-bar-fill" style="width:' + Math.min(100, pct).toFixed(1) + '%"></div>' +
        '</div>' +
        '<div class="tdp-progress-row-count">' + g.accepted + ' <span class="unit">/ ' + g.desired + '</span></div>' +
      '</div>';
    }).join('');
    var pdpOverallPct = pdpTotalDes > 0 ? pdpTotalAcc / pdpTotalDes * 100 : null;
    var pdpOverallRow = pdpOverallPct !== null
      ? '<div class="tdp-overall-separator"></div>' +
        '<div class="tdp-progress-row tdp-progress-row-overall">' +
          '<div class="tdp-progress-row-label"><span class="tdp-progress-row-label-text tdp-overall-label">Overall</span></div>' +
          '<div class="tdp-progress-bar-wrap tdp-overall-bar-wrap' + (pdpOverallPct > 100 ? ' over' : '') + '">' +
            '<div class="tdp-progress-bar-fill tdp-overall-bar-fill" style="width:' + Math.min(pdpOverallPct, 100).toFixed(1) + '%"></div>' +
          '</div>' +
          '<strong class="tdp-progress-row-count tdp-overall-count">' + pdpOverallPct.toFixed(1) + '%</strong>' +
        '</div>'
      : '';
    html += '<div class="pdp-ts-progress-section">';
    html += '<div class="pdp-section-title">TS Progress</div>';
    html += '<div class="tdp-project-progress-grid">' + pdpProgressRows + pdpOverallRow + '</div>';
    html += '</div>';
  }

  // ── 5. Per-panel cards ────────────────────────────────────────────────────
  html += '<div class="pdp-panels-section">';
  html += '<div class="pdp-section-title">Panels (' + panels.length + ')</div>';
  html += '<div class="pdp-panels-grid">';
  panels.forEach(function(panel, i) {
    // Enrich with per-panel TS progress data from summary cache
    var tsTarget = (statsTargetData || []).find(function(t) {
      return t.target && panel.name && t.target.toLowerCase() === panel.name.toLowerCase();
    });
    html += renderPdpPanelCard(panel, i, tsTarget);
  });
  html += '</div>';
  html += '</div>';

  // ── 6. Integration Over Time chart (populated async after session fetch) ──
  html += '<div class="pdp-chart-section" style="display:none">';
  html += '<div class="pdp-section-title">Integration Over Time</div>';
  html += '<div class="tdp-chart-wrap">';
  html += '<div class="tdp-chart-svg"></div>';
  html += '<div class="tdp-chart-legend"></div>';
  html += '</div></div>';

  // ── 7. Session History table (populated async after session fetch) ────────
  html += '<div class="pdp-sessions-section" style="display:none">';
  html += '<div class="pdp-section-title">Session History</div>';
  html += '<div class="pdp-sessions-table-wrap"></div>';
  html += '</div>';

  // ── 8. Filter coverage matrix — only for mosaics with ≥2 panels ──────────
  html += '</div>';
  return html;
}

// Load session thumbnails for multi-project grid cells.
// Each imaged panel gets its own cell; we fetch the best thumbnail per target.
function loadPdpMultiThumbs(backdrop, imagedPanels) {
  imagedPanels.forEach(function(panel, i) {
    var cell = backdrop.querySelector('#pdp-panel-thumb-' + i);
    if (!cell) return;
    var targetName = panel.name;
    var sid = panel.latestSessionId;
    if (!sid) return;

    function applyThumb(thumbs) {
      if (!Array.isArray(thumbs)) return;
      var lower = (targetName || '').toLowerCase();
      var match = null;
      for (var j = 0; j < thumbs.length; j++) {
        var t = thumbs[j];
        if (t.target === targetName || (t.target || '').toLowerCase() === lower) {
          match = t; break;
        }
      }
      if (match && match.dataUri) {
        var placeholder = cell.querySelector('.pdp-cell-placeholder');
        if (placeholder) placeholder.remove();
        var img = document.createElement('img');
        img.src = match.dataUri;
        img.alt = esc(targetName);
        cell.insertBefore(img, cell.firstChild);
      }
    }

    if (thumbnailCache[sid]) {
      applyThumb(thumbnailCache[sid]);
    } else {
      api('/api/sessions/' + encodeURIComponent(sid) + '/thumbnails').then(function(thumbs) {
        if (Array.isArray(thumbs) && thumbs.length > 0) thumbnailCache[sid] = thumbs;
        applyThumb(thumbs);
      }).catch(function() { /* leave placeholder */ });
    }
  });
}

function loadPdpSingleThumb(backdrop, panel) {
  var wrap = backdrop.querySelector('#pdp-thumb-wrap');
  if (!wrap) return;
  var targetName = panel.name;
  var sid = panel.latestSessionId;
  if (!sid) return;

  function applyThumb(thumbs) {
    if (!Array.isArray(thumbs)) return;
    var lower = (targetName || '').toLowerCase();
    var match = null;
    for (var i = 0; i < thumbs.length; i++) {
      var t = thumbs[i];
      if (t.target === targetName || (t.target || '').toLowerCase() === lower) {
        match = t; break;
      }
    }
    if (match && match.dataUri) {
      var placeholder = wrap.querySelector('.pdp-mosaic-placeholder');
      if (placeholder) placeholder.remove();
      var existingImg = wrap.querySelector('img');
      if (existingImg) existingImg.remove();
      var img = document.createElement('img');
      img.className = 'pdp-mosaic-img';
      img.src = match.dataUri;
      img.alt = targetName || '';
      wrap.insertBefore(img, wrap.firstChild);
    }
  }

  if (thumbnailCache[sid]) {
    applyThumb(thumbnailCache[sid]);
  } else {
    api('/api/sessions/' + encodeURIComponent(sid) + '/thumbnails').then(function(thumbs) {
      if (Array.isArray(thumbs) && thumbs.length > 0) thumbnailCache[sid] = thumbs;
      applyThumb(thumbs);
    }).catch(function() { /* leave placeholder */ });
  }
}

function renderPdpPanelCard(panel, idx, tsTarget) {
  var palette = ['#90CAF9','#A5D6A7','#FFCC80','#EF9A9A','#CE93D8','#80DEEA','#BCAAA4','#B0BEC5'];
  var color = palette[idx % palette.length];
  var totalHrs = (panel.totalIntegrationHours || 0).toFixed(1);
  var frames   = panel.acceptedFrames || 0;
  var pct = tsTarget && tsTarget.ts && tsTarget.ts.project
    ? (tsTarget.ts.project.percentComplete || 0) : null;

  var html = '<div class="pdp-panel-card" style="--panel-color:' + color + '" data-panel-name="' + esc(panel.name || '') + '" data-panel-idx="' + idx + '">';
  html += '<div class="pdp-panel-header">';
  html += '<div class="pdp-panel-index">Panel ' + (idx + 1) + '</div>';
  if (pct !== null) {
    html += '<div class="pdp-panel-pct">' + pct.toFixed(0) + '%</div>';
  }
  html += '</div>';
  html += '<div class="pdp-panel-name">' + esc(panel.name || 'Panel ' + (idx + 1)) + '</div>';

  // Progress bar
  if (pct !== null) {
    html += '<div class="pdp-panel-progress-track">' +
            '<div class="pdp-panel-progress-fill" style="width:' + Math.min(100, pct).toFixed(1) + '%;background:' + color + '"></div>' +
            '</div>';
  }

  // Stats row
  html += '<div class="pdp-panel-stats">';
  html += '<span class="pdp-panel-stat-val">' + totalHrs + '<span class="unit">h</span></span>';
  html += '<span class="pdp-panel-stat-sep">\u00b7</span>';
  html += '<span class="pdp-panel-stat-val">' + frames + '\u00a0' + (frames === 1 ? 'frame' : 'frames') + '</span>';
  if (panel.sessionCount) {
    html += '<span class="pdp-panel-stat-sep">\u00b7</span>';
    html += '<span class="pdp-panel-stat-val">' + panel.sessionCount + '\u00a0' + (panel.sessionCount === 1 ? 'session' : 'sessions') + '</span>';
  }
  html += '</div>';

  // RA/Dec + Position Angle
  if (panel.ra != null && panel.dec != null) {
    var raH = panel.ra, raM = (raH % 1) * 60, raS = (raM % 1) * 60;
    var raStr = Math.floor(raH) + 'h ' + Math.floor(raM) + 'm ' + raS.toFixed(0) + 's';
    var decSign = panel.dec >= 0 ? '+' : '';
    var rotStr = panel.rotation != null ? '\u00a0\u00a0\u21bb\u00a0' + panel.rotation.toFixed(1) + '\u00b0' : '';
    html += '<div class="pdp-panel-coords"><span class="pdp-coord-label">RA</span>\u00a0' + raStr + '\u00a0\u00a0<span class="pdp-coord-label">Dec</span>\u00a0' + decSign + panel.dec.toFixed(2) + '\u00b0' + rotStr + '</div>';
  }
  // Last imaged
  if (panel.lastImaged) {
    html += '<div class="pdp-panel-last-imaged">Last imaged ' + fmtRelativeTime(panel.lastImaged) + '</div>';
  }

  // Filter pills
  if (panel.filters && panel.filters.length > 0) {
    html += '<div class="pdp-panel-filters">';
    panel.filters.forEach(function(f) {
      html += '<span class="pdp-panel-filter-item">' + filterTypePill(f.filter) +
        '<span class="pdp-panel-filter-hrs">' + (f.totalHours || 0).toFixed(1) + 'h</span></span>';
    });
    html += '</div>';
  }
  html += '</div>';
  return html;
}

// ── PDP chart + session table helpers ─────────────────────────────────────

function renderPdpChart(backdrop, sessions) {
  if (!backdrop) return;
  var wrap = backdrop.querySelector('.tdp-chart-wrap');
  var svgHost = backdrop.querySelector('.tdp-chart-svg');
  var legendHost = backdrop.querySelector('.tdp-chart-legend');
  if (!wrap || !svgHost || !legendHost) return;

  var cs = window.getComputedStyle(wrap);
  var pL = parseFloat(cs.paddingLeft) || 0;
  var pR = parseFloat(cs.paddingRight) || 0;
  var innerWidth = Math.max(320, Math.floor(wrap.clientWidth - pL - pR));

  var chart = renderTargetChart(sessions, innerWidth);
  svgHost.innerHTML = chart.svg;

  var pillsHtml = (chart.filtersUsed || []).map(function(k) { return filterTypePill(k); }).join('');
  var cumHtml = '<span class="tdp-chart-legend-cum"><span class="tdp-cum-line"></span>Cumulative</span>';
  legendHost.innerHTML = pillsHtml + cumHtml;
}

function buildPdpSessionTable(sessions, showTargetCol) {
  var rows = sessions.map(function(s, idx) {
    // Sort filters for sub-rows
    var sortedFilters = (s.filters || []).slice().sort(function(a, b) {
      var ta = resolveFilterType(a.filter) || 'Z';
      var tb = resolveFilterType(b.filter) || 'Z';
      var ia = TDP_FILTER_STACK_ORDER.indexOf(ta); if (ia === -1) ia = 99;
      var ib = TDP_FILTER_STACK_ORDER.indexOf(tb); if (ib === -1) ib = 99;
      return ia - ib;
    });
    var targets = showTargetCol ? (s.targets || []) : [];

    // Build sub-rows: target names fill the Targets cell on filter rows
    var subRows = sortedFilters.map(function(f, fi) {
      var fHFR = f.avgHFR != null ? f.avgHFR.toFixed(2) : '--';
      var fGuide = f.avgGuidingRMS != null ? f.avgGuidingRMS.toFixed(2) + '"' : '--';
      var fMin = Math.round((f.integrationSeconds || 0) / 60);
      var firstCell = '<td></td>';
      if (showTargetCol) {
        var tgtName = fi < targets.length ? targets[fi] : '';
        firstCell = '<td class="pdp-target-subrow-name" colspan="2">' + esc(tgtName) + '</td>';
      }
      return '<tr class="tdp-filter-subrow" data-for="' + idx + '" style="display:none">' +
        firstCell +
        '<td class="pdp-subrow-integration">' + filterTypePill(f.filter) + '<span>' + esc(tdpFmtDuration(fMin)) + '</span></td>' +
        '<td>' + (f.frames || 0) + '</td>' +
        '<td>' + esc(fHFR) + '</td>' +
        '<td>' + esc(fGuide) + '</td>' +
        '<td></td>' +
        '<td></td>' +
      '</tr>';
    }).join('');
    // Extra target names if more targets than filters
    if (showTargetCol && targets.length > sortedFilters.length) {
      for (var ti = sortedFilters.length; ti < targets.length; ti++) {
        subRows += '<tr class="tdp-filter-subrow" data-for="' + idx + '" style="display:none">' +
          '<td class="pdp-target-subrow-name" colspan="2">' + esc(targets[ti]) + '</td>' +
          '<td></td><td></td><td></td><td></td><td></td><td></td>' +
        '</tr>';
      }
    }

    var sHFR = s.avgHFR != null ? s.avgHFR.toFixed(2) : '--';
    var sGuide = s.avgGuidingRMS != null ? s.avgGuidingRMS.toFixed(2) + '"' : '--';
    var sessionDurMin = Math.round((s.integrationSeconds || 0) / 60);

    var targetCell = '';
    if (showTargetCol) {
      targetCell = '<td class="pdp-session-targets">' + targets.length + '</td>';
    }

    return '<tr class="tdp-session-row" data-idx="' + idx + '" data-session-id="' + esc(s.sessionId || '') + '">' +
        '<td><span class="tdp-date-long">' + esc(tdpFmtDate(s.sessionStart)) + '</span>' +
             '<span class="tdp-date-short">' + esc(tdpFmtDateShort(s.sessionStart)) + '</span></td>' +
        targetCell +
        '<td>' + esc(tdpFmtDuration(sessionDurMin)) + '</td>' +
        '<td>' + (s.frames || 0) + '</td>' +
        '<td>' + esc(sHFR) + '</td>' +
        '<td>' + esc(sGuide) + '</td>' +
        '<td>' + esc(s.moonPhase || '--') + '</td>' +
        '<td><span class="tdp-row-link" data-session-id="' + esc(s.sessionId || '') + '">View</span></td>' +
      '</tr>' + subRows;
  }).join('');

  var colgroup = '';

  return '<table class="tdp-table pdp-session-table">' +
    colgroup +
    '<thead><tr>' +
      '<th>Date</th>' +
      (showTargetCol ? '<th>Targets</th>' : '') +
      '<th>Integration</th><th>Frames</th><th>HFR</th><th>Guide</th><th>Moon</th><th></th>' +
    '</tr></thead>' +
    '<tbody>' + rows + '</tbody></table>';
}

function bindPdpSessionTableEvents(backdrop) {
  // Expand/collapse session rows
  backdrop.querySelectorAll('.pdp-session-table tr.tdp-session-row').forEach(function(row) {
    row.addEventListener('click', function(e) {
      if (e.target.classList.contains('tdp-row-link')) return;
      var idx = row.getAttribute('data-idx');
      var subs = backdrop.querySelectorAll('.pdp-session-table tr.tdp-filter-subrow[data-for="' + idx + '"]');
      if (!subs.length) return;
      var isOpen = row.classList.toggle('tdp-expanded');
      subs.forEach(function(sub) { sub.style.display = isOpen ? '' : 'none'; });
    });
  });
  // View report link
  backdrop.querySelectorAll('.pdp-session-table .tdp-row-link').forEach(function(link) {
    link.addEventListener('click', function(e) {
      e.stopPropagation();
      var sid = link.getAttribute('data-session-id');
      if (!sid) return;
      window.open('/api/sessions/' + encodeURIComponent(sid) + '/report', '_blank', 'noopener');
    });
  });
}

// ── PDP panel card drill-down ──────────────────────────────────────────────

function bindPdpPanelCardClicks(backdrop, projectData, projectGuid) {
  backdrop.querySelectorAll('.pdp-panel-card').forEach(function(card) {
    card.addEventListener('click', function() {
      var panelName = card.getAttribute('data-panel-name');
      var panelIdx = parseInt(card.getAttribute('data-panel-idx') || '0', 10);
      if (!panelName) return;
      var panel = (projectData.panels || []).find(function(p) { return p.name === panelName; });
      openPdpPanelDrillDown(backdrop, panelName, panel, projectData, projectGuid);
    });
  });
}

function openPdpPanelDrillDown(backdrop, panelName, panelData, projectData, projectGuid) {
  var modal = backdrop.querySelector('.pdp-modal');
  if (!modal) return;

  // Show loading state
  modal.innerHTML = '<button type="button" class="pdp-close" aria-label="Close">\u2715</button>' +
    '<div class="pdp-drilldown-header">' +
      '<span class="pdp-back-btn">\u2190 ' + esc((projectData.project || {}).name || 'Project') + '</span>' +
    '</div>' +
    '<div style="padding:40px;text-align:center;color:var(--text-tertiary)">Loading\u2026</div>';

  // Bind close + back immediately
  var closeBtn = modal.querySelector('.pdp-close');
  if (closeBtn) closeBtn.addEventListener('click', closeProjectDetail);
  var backBtn = modal.querySelector('.pdp-back-btn');
  if (backBtn) backBtn.addEventListener('click', function() {
    openProjectDetail(projectGuid, (projectData.project || {}).name);
  });

  // Scroll modal to top
  modal.scrollTop = 0;

  // Fetch per-target session data
  api('/api/stats/targets/' + encodeURIComponent(panelName) + '/sessions').then(function(data) {
    var cur = document.getElementById('pdp-backdrop');
    if (!cur || cur !== backdrop) return;

    modal.innerHTML = renderPdpPanelDrillDown(data, panelName, panelData, projectData, projectGuid);

    // Close + back handlers
    var closeBtn2 = modal.querySelector('.pdp-close');
    if (closeBtn2) closeBtn2.addEventListener('click', closeProjectDetail);
    var backBtn2 = modal.querySelector('.pdp-back-btn');
    if (backBtn2) backBtn2.addEventListener('click', function() {
      openProjectDetail(projectGuid, (projectData.project || {}).name);
    });

    // Session row expand/collapse + view links
    bindPdpSessionTableEvents(backdrop);

    // Load thumbnail
    if (panelData && panelData.latestSessionId) {
      loadTargetDetailThumb(panelName, panelData.latestSessionId);
    }

    // Chart — render synchronously (modal is already laid out after innerHTML set)
    renderPdpChart(backdrop, data.sessions || []);
    // Update resize handler for drill-down chart
    if (_pdpResizeHandler) window.removeEventListener('resize', _pdpResizeHandler);
    _pdpResizeHandler = function() {
      if (_pdpResizeDebounce) clearTimeout(_pdpResizeDebounce);
      _pdpResizeDebounce = setTimeout(function() { renderPdpChart(backdrop, data.sessions || []); }, 120);
    };
    window.addEventListener('resize', _pdpResizeHandler);
  }).catch(function(err) {
    modal.innerHTML = '<button type="button" class="pdp-close" aria-label="Close">\u2715</button>' +
      '<div class="pdp-drilldown-header">' +
        '<span class="pdp-back-btn">\u2190 ' + esc((projectData.project || {}).name || 'Project') + '</span>' +
      '</div>' +
      '<div style="padding:20px;color:#e15759">Failed to load: ' + esc(err && err.message ? err.message : 'unknown') + '</div>';
    var c = modal.querySelector('.pdp-close');
    if (c) c.addEventListener('click', closeProjectDetail);
    var b = modal.querySelector('.pdp-back-btn');
    if (b) b.addEventListener('click', function() {
      openProjectDetail(projectGuid, (projectData.project || {}).name);
    });
  });
}

function renderPdpPanelDrillDown(data, panelName, panelData, projectData, projectGuid) {
  var initial = panelName ? panelName.charAt(0).toUpperCase() : '?';
  var totalHrs = data.totalIntegrationHours != null ? data.totalIntegrationHours.toFixed(1) : '--';
  var avgHFR = data.avgHFR != null ? data.avgHFR.toFixed(2) : '--';

  var firstDate = tdpFmtDate(data.firstSession);
  var lastDate  = tdpFmtDate(data.lastSession);
  var dateRange = 'First captured ' + firstDate + ' \u00b7 Last imaged ' + lastDate;

  // Aggregate per-filter totals for KPI popup
  var aggregated = {};
  (data.sessions || []).forEach(function(s) {
    (s.filters || []).forEach(function(f) {
      var name = f.filter || 'Unknown';
      if (!aggregated[name]) {
        aggregated[name] = { filter: name, totalSeconds: 0, acceptedCount: 0, frameCount: 0 };
      }
      aggregated[name].totalSeconds  += f.integrationSeconds || 0;
      aggregated[name].acceptedCount += f.frames             || 0;
      aggregated[name].frameCount    += f.totalFrames        || 0;
    });
  });
  tdpKpiFilters = Object.keys(aggregated).map(function(k) { return aggregated[k]; });

  // ── Title pills ──────────────────────────────────────────────────────
  var titlePills = '';
  var ts = panelData ? findTsForTarget(panelName) : null;
  if (ts && ts.project) {
    var proj = ts.project;
    titlePills += '<span class="tdp-project-state-pill" data-state="' + esc(proj.state || 'Draft') +
      '" data-project-guid="' + esc(proj.guid || '') + '">' +
      esc(proj.state || 'Draft') +
      (proj.stateSource === 'override' ? ' \u00b7' : '') +
      '</span>';
    if (proj.isMosaic) {
      titlePills += '<span class="tdp-type-pill">Mosaic Panel</span>';
    }
  }

  // ── TS Progress bars ─────────────────────────────────────────────────
  var progressHtml = '';
  if (panelData && panelData.tsGoals && panelData.tsGoals.length && statsTsStatus === 'available') {
    var STACK_ORDER = ['L', 'R', 'G', 'B', 'H', 'S', 'O', 'N'];
    var sortedGoals = panelData.tsGoals.slice().sort(function(a, b) {
      var ai = STACK_ORDER.indexOf(resolveFilterType(a.filter) || '');
      var bi = STACK_ORDER.indexOf(resolveFilterType(b.filter) || '');
      if (ai < 0) ai = STACK_ORDER.length;
      if (bi < 0) bi = STACK_ORDER.length;
      if (ai !== bi) return ai - bi;
      return (b.exposureSec || 0) - (a.exposureSec || 0);
    });

    var goalRows = sortedGoals.map(function(g) {
      var effective = (g.accepted || 0) > 0 ? (g.accepted || 0) : (g.acquired || 0);
      var pct = g.desired > 0 ? effective / g.desired * 100 : 0;
      var over = effective > g.desired;
      var widthPct = Math.min(100, pct);
      var filterType = resolveFilterType(g.filter);
      var fillColor = (filterType && FILTER_TYPE_CHART_COLORS[filterType]) || '#66BB6A';
      var expSec = g.exposureSec && g.exposureSec > 0 ? g.exposureSec : 0;
      var labelText = expSec > 0 ? (expSec + 's') : '';
      var labelHtml = labelText ? '<span class="tdp-progress-row-label-text">' + esc(labelText) + '</span>' : '';
      return '<div class="tdp-progress-row">' +
        '<div class="tdp-progress-row-label">' + filterTypePill(g.filter) + labelHtml + '</div>' +
        '<div class="tdp-progress-bar-wrap' + (over ? ' over' : '') + '" style="--fill-color:' + fillColor + '">' +
          '<div class="tdp-progress-bar-fill" style="width:' + widthPct.toFixed(1) + '%"></div>' +
        '</div>' +
        '<div class="tdp-progress-row-count">' + effective + ' <span class="unit">/ ' + g.desired + '</span></div>' +
      '</div>';
    }).join('');

    // Overall
    var totalAcc = 0, totalDes = 0;
    sortedGoals.forEach(function(g) {
      totalAcc += (g.accepted || 0) > 0 ? (g.accepted || 0) : (g.acquired || 0);
      totalDes += g.desired || 0;
    });
    var overallRow = '';
    if (totalDes > 0) {
      var overallPct = totalAcc / totalDes * 100;
      overallRow = '<div class="tdp-overall-separator"></div>' +
        '<div class="tdp-progress-row tdp-progress-row-overall">' +
          '<div class="tdp-progress-row-label"><span class="tdp-progress-row-label-text tdp-overall-label">Overall</span></div>' +
          '<div class="tdp-progress-bar-wrap tdp-overall-bar-wrap' + (overallPct > 100 ? ' over' : '') + '">' +
            '<div class="tdp-progress-bar-fill tdp-overall-bar-fill" style="width:' + Math.min(overallPct, 100).toFixed(1) + '%"></div>' +
          '</div>' +
          '<strong class="tdp-progress-row-count tdp-overall-count">' + overallPct.toFixed(1) + '%</strong>' +
        '</div>';
    }
    if (goalRows || overallRow) {
      progressHtml = '<div class="tdp-progress-section"><div class="tdp-project-progress-grid">' + goalRows + overallRow + '</div></div>';
    }
  }

  // ── Session table ────────────────────────────────────────────────────
  var sessions = data.sessions || [];
  var tableHtml = buildPdpSessionTable(sessions, false);

  return '<button type="button" class="pdp-close" aria-label="Close">\u2715</button>' +
    '<div class="pdp-drilldown-header">' +
      '<span class="pdp-back-btn">\u2190 ' + esc((projectData.project || {}).name || 'Project') + '</span>' +
    '</div>' +
    '<div class="tdp-title-section">' +
      '<div class="tdp-title-row">' +
        '<h2>' + esc(panelName) + '</h2>' +
        titlePills +
      '</div>' +
      '<div class="tdp-daterange">' + esc(dateRange) + '</div>' +
    '</div>' +
    '<div class="tdp-hero">' +
      '<div class="tdp-hero-wrap" id="tdp-hero-wrap">' +
        '<div class="tdp-thumb-placeholder">' + esc(initial) + '</div>' +
      '</div>' +
    '</div>' +
    '<div class="tdp-stats-section">' +
      '<div class="tdp-header-stats">' +
        '<div class="tdp-kpi target-stat-expandable" data-stat-type="integration" data-stat-source="tdp"><div class="tdp-kpi-val">' + esc(totalHrs) + '<span class="unit">h</span></div><div class="tdp-kpi-label">Integration</div></div>' +
        '<div class="tdp-kpi target-stat-expandable" data-stat-type="frames" data-stat-source="tdp"><div class="tdp-kpi-val">' + (data.totalFrames || 0) + '</div><div class="tdp-kpi-label">Frames</div></div>' +
        '<div class="tdp-kpi"><div class="tdp-kpi-val">' + (data.sessionCount || 0) + '</div><div class="tdp-kpi-label">Sessions</div></div>' +
        '<div class="tdp-kpi"><div class="tdp-kpi-val">' + esc(avgHFR) + '<span class="unit">px</span></div><div class="tdp-kpi-label">Avg HFR</div></div>' +
      '</div>' +
    '</div>' +
    progressHtml +
    '<div class="tdp-body">' +
      '<div class="tdp-section-title">Integration Over Time</div>' +
      '<div class="tdp-chart-wrap">' +
        '<div class="tdp-chart-svg"></div>' +
        '<div class="tdp-chart-legend"></div>' +
      '</div>' +
      '<div class="tdp-section-title">Session History</div>' +
      '<div class="pdp-sessions-table-wrap">' + tableHtml + '</div>' +
    '</div>';
}

// Fetch the combined HiPS survey image via the server's disk-cached endpoint and draw
// per-panel FOV rectangles as an SVG overlay.
// SVG rotation convention: position angle (degrees E of N, CCW) → SVG rotate(-PA) because
// astronomical images are N-up, E-left which mirrors the x-axis relative to SVG.
function loadMosaicThumbnail(panels, wrapOrBackdrop, projectGuid) {
  // Accept either a direct wrap element or a backdrop containing #pdp-thumb-wrap
  var wrap = (wrapOrBackdrop && (wrapOrBackdrop.id === 'pdp-thumb-wrap' ||
              wrapOrBackdrop.classList.contains('targets-project-thumb-wrap')))
    ? wrapOrBackdrop
    : (wrapOrBackdrop ? wrapOrBackdrop.querySelector('#pdp-thumb-wrap') : null);
  if (!wrap) return;

  var validPanels = panels.filter(function(p) {
    return p.ra != null && p.dec != null && !(p.ra === 0 && p.dec === 0);
  });
  if (!validPanels.length) return;

  // Use only imaged panels (those with FOV data) for center/maxReach calculation.
  // This prevents unshot mosaic placeholders from pulling the center off the actual target.
  // Fall back to all valid panels if none have been imaged yet.
  var imagedPanels = validPanels.filter(function(p) {
    return p.fovWidthDeg != null && p.fovHeightDeg != null;
  });
  var centerPanels = imagedPanels.length ? imagedPanels : validPanels;

  var raDegArr  = centerPanels.map(function(p) { return p.ra * 15; });
  var decDegArr = centerPanels.map(function(p) { return p.dec; });

  var centerDec = decDegArr.reduce(function(s, d) { return s + d; }, 0) / decDegArr.length;
  var centerRA  = raDegArr.reduce(function(s, r) { return s + r; }, 0) / raDegArr.length;
  var cosCenter = Math.cos(centerDec * Math.PI / 180);

  // Find minimum HiPS FOV that contains all panel footprints with 15% padding.
  // Use all valid panels for FOV extent so planned-but-unshot panels are still shown.
  var maxReach = 0;
  validPanels.forEach(function(p) {
    var dRA  = (p.ra * 15 - centerRA) * cosCenter;
    var dDec = p.dec - centerDec;
    var halfDiag = 0;
    if (p.fovWidthDeg != null && p.fovHeightDeg != null) {
      halfDiag = Math.sqrt(p.fovWidthDeg * p.fovWidthDeg + p.fovHeightDeg * p.fovHeightDeg) / 2;
    }
    maxReach = Math.max(maxReach, Math.sqrt(dRA * dRA + dDec * dDec) + halfDiag);
  });

  // Fallback for single-panel case where maxReach ≈ halfDiag only
  if (maxReach < 0.5) {
    var p0 = centerPanels[0];
    maxReach = (p0.fovWidthDeg && p0.fovHeightDeg)
      ? Math.sqrt(p0.fovWidthDeg * p0.fovWidthDeg + p0.fovHeightDeg * p0.fovHeightDeg) / 2
      : 1.0;
  }

  var hipsFov  = maxReach * 2 * 1.15;
  var imgSize  = 1024;
  var scale    = hipsFov / imgSize; // degrees per pixel

  // Image served via server's disk-cached endpoint — server handles HiPS fetch + caching
  var hipsUrl = '/api/stats/projects/' + encodeURIComponent(projectGuid) + '/mosaic-thumb';

  // Build SVG overlay rects + smart-positioned labels (labels rendered last so they sit on top)
  var palette = ['rgba(144,202,249,0.9)','rgba(165,214,167,0.9)','rgba(255,204,128,0.9)',
                 'rgba(239,154,154,0.9)','rgba(206,147,216,0.9)','rgba(128,222,234,0.9)',
                 'rgba(188,170,164,0.9)','rgba(176,190,197,0.9)'];

  // Precompute image-space geometry for every panel
  var pGeo = validPanels.map(function(p, i) {
    // Use trailing number from TS target name (e.g. "Spaghetti Nebula Panel 2" → 2),
    // fall back to sequential 1-based index.
    var nameMatch = p.name && p.name.match(/(\d+)\s*$/);
    return {
      cx:    imgSize / 2 + (-(p.ra * 15 - centerRA) * cosCenter / scale),
      cy:    imgSize / 2 + (-(p.dec - centerDec) / scale),
      wPx:   p.fovWidthDeg  != null ? p.fovWidthDeg  / scale : 0,
      hPx:   p.fovHeightDeg != null ? p.fovHeightDeg / scale : 0,
      pa:    p.positionAngle != null ? p.positionAngle : (p.rotation || 0),
      label: nameMatch ? nameMatch[1] : String(i + 1)
    };
  });

  // True if image-space point (px,py) lies inside panel j's rotated footprint.
  // Inverse of SVG rotate(-pa): apply rotate(+pa) to bring point into panel's local frame.
  function inPanel(px, py, j) {
    var g = pGeo[j];
    if (!g.wPx || !g.hPx) return false;
    var r = g.pa * Math.PI / 180;
    var dx = px - g.cx, dy = py - g.cy;
    var lx = dx * Math.cos(r) - dy * Math.sin(r);
    var ly = dx * Math.sin(r) + dy * Math.cos(r);
    return Math.abs(lx) < g.wPx / 2 && Math.abs(ly) < g.hPx / 2;
  }

  var svgRects = '', svgLabels = '';
  pGeo.forEach(function(g, i) {
    if (!g.wPx || !g.hPx) return;
    var color = palette[i % palette.length];
    var r = g.pa * Math.PI / 180;
    var cosR = Math.cos(r), sinR = Math.sin(r);

    // Rect (inside rotated group)
    svgRects += '<g transform="translate(' + g.cx.toFixed(1) + ',' + g.cy.toFixed(1) + ')' +
                ' rotate(' + (-g.pa).toFixed(1) + ')">' +
                '<rect x="' + (-(g.wPx/2)).toFixed(1) + '" y="' + (-(g.hPx/2)).toFixed(1) +
                '" width="' + g.wPx.toFixed(1) + '" height="' + g.hPx.toFixed(1) +
                '" fill="none" stroke="' + color + '" stroke-width="5"/></g>';

    // Label placement: score 4 corners + 4 edge midpoints.
    // Pick the candidate that is (1) inside the fewest other panels, then
    // (2) furthest from the image center — i.e. the most "outer" uncontested spot.
    var hw = g.wPx / 2, hh = g.hPx / 2;
    var locals = [[-hw,-hh],[hw,-hh],[-hw,hh],[hw,hh],[0,-hh],[0,hh],[-hw,0],[hw,0]];
    var bestPt = null, bestOvlp = 999, bestDist = -1;
    locals.forEach(function(lc) {
      // local → image space: apply SVG rotate(-pa), i.e. [cos(pa), sin(pa); -sin(pa), cos(pa)]
      var ix = g.cx + lc[0] * cosR + lc[1] * sinR;
      var iy = g.cy - lc[0] * sinR + lc[1] * cosR;
      var ovlp = 0;
      for (var j = 0; j < pGeo.length; j++) { if (j !== i && inPanel(ix, iy, j)) ovlp++; }
      var dist = Math.sqrt(Math.pow(ix - imgSize/2, 2) + Math.pow(iy - imgSize/2, 2));
      if (ovlp < bestOvlp || (ovlp === bestOvlp && dist > bestDist)) {
        bestPt = [ix, iy]; bestOvlp = ovlp; bestDist = dist;
      }
    });

    // Inset from chosen corner toward panel center so label sits clearly inside.
    // Inset distance scales with font size to keep the number off the border.
    var dx = g.cx - bestPt[0], dy = g.cy - bestPt[1];
    var d = Math.sqrt(dx*dx + dy*dy) || 1;
    var lbx = bestPt[0] + dx/d * 54, lby = bestPt[1] + dy/d * 54;

    svgLabels += '<text x="' + lbx.toFixed(1) + '" y="' + lby.toFixed(1) +
                 '" text-anchor="middle" dominant-baseline="central"' +
                 ' font-size="36" font-weight="700" fill="' + color +
                 '" stroke="rgba(0,0,0,0.85)" stroke-width="4.5" paint-order="stroke">' + g.label + '</text>';
  });

  var svgMarkup = '<svg viewBox="0 0 ' + imgSize + ' ' + imgSize + '"' +
    ' xmlns="http://www.w3.org/2000/svg"' +
    ' style="position:absolute;inset:0;width:100%;height:100%;pointer-events:none">' +
    svgRects + svgLabels + '</svg>';

  function injectSvg() {
    // Remove any prior overlay so re-renders don't stack
    var old = wrap.querySelector('.mosaic-fov-svg');
    if (old) old.parentNode.removeChild(old);
    var svgContainer = document.createElement('div');
    svgContainer.innerHTML = svgMarkup;
    var svgEl = svgContainer.firstChild;
    svgEl.classList.add('mosaic-fov-svg');
    if (!showFovOverlay) svgEl.style.display = 'none';
    wrap.appendChild(svgEl);
  }

  // If the wrap already has a loaded image (card thumbnail), inject SVG immediately.
  // Otherwise (detail panel), create + insert the image first.
  var existingImg = wrap.querySelector('img');
  if (existingImg) {
    if (existingImg.complete && existingImg.naturalWidth > 0) {
      injectSvg();
    } else {
      existingImg.addEventListener('load', injectSvg);
      existingImg.addEventListener('error', function() {}); // silently ignore
    }
  } else {
    var img = new Image();
    img.className = 'pdp-mosaic-img';
    img.alt = 'Mosaic survey + FOV overlay';
    img.onload = function() {
      var placeholder = wrap.querySelector('.pdp-mosaic-placeholder');
      if (placeholder) placeholder.style.display = 'none';
      wrap.insertBefore(img, wrap.firstChild);
      injectSvg();
    };
    img.onerror = function() {
      var placeholder = wrap.querySelector('.pdp-mosaic-placeholder');
      if (placeholder) {
        placeholder.textContent = 'Survey image unavailable';
        placeholder.style.fontSize = '13px';
      }
    };
    img.src = hipsUrl;
  }
}

function loadTargetThumbnails() {
  var thumbEls = document.querySelectorAll('.target-card-thumb[data-session-id]');
  var sessionMap = {};
  thumbEls.forEach(function(el) {
    var sid = el.getAttribute('data-session-id');
    if (!sid) return;
    if (!sessionMap[sid]) sessionMap[sid] = [];
    sessionMap[sid].push(el);
  });

  function applyThumbs(sid, thumbs) {
    if (!Array.isArray(thumbs)) return;
    sessionMap[sid].forEach(function(el) {
      var target = el.getAttribute('data-target');
      var match = null;
      // Exact match first, then case-insensitive fallback
      for (var i = 0; i < thumbs.length; i++) {
        if (thumbs[i].target === target) { match = thumbs[i]; break; }
      }
      if (!match) {
        var lower = target.toLowerCase();
        for (var i = 0; i < thumbs.length; i++) {
          if (thumbs[i].target.toLowerCase() === lower) { match = thumbs[i]; break; }
        }
      }
      if (match && match.dataUri) {
        // Remove placeholder but preserve overlay + last-imaged chip (children of the thumb)
        var placeholder = el.querySelector('.thumb-placeholder');
        if (placeholder) placeholder.remove();
        var existingImg = el.querySelector('img');
        if (existingImg) existingImg.remove();
        var imgEl = document.createElement('img');
        imgEl.src = match.dataUri;
        imgEl.alt = target;
        // Insert as first child so overlay/badge (absolute, higher z-index) stack above
        el.insertBefore(imgEl, el.firstChild);
        el.classList.add('has-image');
        // FOV overlay — simple rectangle, no color or labels
        if (match.fovSvg) {
          var oldSvg = el.querySelector('svg');
          if (oldSvg) oldSvg.remove();
          var fovHtml = match.fovSvg
            .replace(/width='\d+'/, "width='100%'")
            .replace(/height='\d+'/, "height='100%'")
            .replace("<svg ", "<svg viewBox='0 0 200 200' " + (showFovOverlay ? '' : "style='display:none' "));
          var fovDiv = document.createElement('div');
          fovDiv.innerHTML = fovHtml;
          var svgEl = fovDiv.firstChild;
          el.appendChild(svgEl);
        }
      }
    });
  }

  Object.keys(sessionMap).forEach(function(sid) {
    // Use cached thumbnails if already fetched (e.g. from sessions tab)
    if (thumbnailCache[sid]) {
      applyThumbs(sid, thumbnailCache[sid]);
      return;
    }
    api('/api/sessions/' + sid + '/thumbnails').then(function(thumbs) {
      if (Array.isArray(thumbs) && thumbs.length > 0) thumbnailCache[sid] = thumbs;
      applyThumbs(sid, thumbs);
    }).catch(function() { /* leave placeholder */ });
  });
}

// ── Sessions List Page ─────────────────────────────────────────────────────

var sessionsCache = [];
var initialLoadDone = false; // true after first successful render; skip fade on subsequent renders
var selectedTargets = {}; // target name -> boolean (true = selected)
var showEmptySessions = false; // hide 0-image sessions by default
var showFovOverlay = localStorage.getItem('ns-show-fov') !== 'false'; // on by default
var showAltitude = localStorage.getItem('ns-show-altitude') !== 'false'; // on by default
var cardViewMode = localStorage.getItem('ns-card-view') || 'expanded'; // 'expanded' or 'compact'
var hiddenSessions = JSON.parse(localStorage.getItem('ns-hidden-sessions') || '{}'); // sessionId -> true
var showHidden = false;
var dropdownOpen = false; // persists across re-renders so pill clicks don't close the menu
var targetSearch = '';   // persists across re-renders so search text survives pill clicks
var sortDropdownOpen = false;
var currentSort = localStorage.getItem('ns-sort') || 'date-desc';
var SORT_LABELS = { 'date-desc': 'Newest first', 'date-asc': 'Oldest first', 'integration': 'Most integration', 'images': 'Most images', 'targets': 'Most targets' };
var livestackMap = {}; // sessionId -> { targetName -> [{filter, url, label, isComposite}] }
var thumbnailCache = {}; // sessionId -> thumbnails array
var altitudeChartCache = {}; // sessionId -> {svg, legend}
var altitudeChartFetching = {}; // sessionId -> true while request is in flight
var altitudeObserver = null; // IntersectionObserver for lazy-loading uncached charts
var thumbnailFetching = {}; // sessionId -> true while request is in flight
var detailCache = {}; // sessionId -> detail JSON (for stat expand)
var statExpandTimer = null;
var statExpandActiveEl = null;

function getAllTargets() {
  var targets = {};
  sessionsCache.forEach(function(s) {
    s.targets.forEach(function(t) { targets[t] = true; });
  });
  return Object.keys(targets).sort();
}

function renderSessionList(params) {
  var el = document.getElementById('content');
  var sub = document.getElementById('page-subtitle');

  var fromVal = params ? (params.get('from') || '') : '';
  var toVal = params ? (params.get('to') || '') : '';
  var sortVal = params ? (params.get('sort') || 'date-desc') : 'date-desc';

  if (sessionsCache.length === 0) {
    el.innerHTML = '<div class="loading">Loading sessions...</div>';
    api('/api/sessions').then(function(data) {
      sessionsCache = data;
      logInfo('Sessions loaded:', data.length);
      // Initialize: all targets selected
      getAllTargets().forEach(function(t) { selectedTargets[t] = true; });
      doRenderList(el, sub, fromVal, toVal, sortVal);
    }).catch(function(err) {
      logError('Failed to load sessions:', err.message);
      el.innerHTML = '<div class="error">Failed to load sessions: ' + esc(err.message) + '</div>';
    });
  } else {
    doRenderList(el, sub, fromVal, toVal, sortVal);
  }
}

function doRenderList(el, sub, fromFilter, toFilter, sortBy) {
  // Build target dropdown filter
  var allTargets = getAllTargets();
  var activeCount = allTargets.filter(function(t) { return selectedTargets[t] !== false; }).length;
  var targetLabel = activeCount === allTargets.length ? 'All targets' :
    activeCount === 0 ? 'No targets' : activeCount + ' target' + (activeCount > 1 ? 's' : '');

  var targetDropHtml = '';
  if (allTargets.length > 0) {
    targetDropHtml = '<div class="target-dropdown" id="target-dropdown">' +
      '<button class="target-dropdown-btn" id="target-dropdown-btn">' + esc(targetLabel) + ' \u25BC</button>' +
      '<div class="target-dropdown-menu" id="target-dropdown-menu">' +
        '<input type="text" class="target-search" placeholder="Filter targets\u2026" value="' + esc(targetSearch) + '">' +
        '<div class="target-dropdown-actions">' +
          '<button id="targets-all" class="filter-link">All</button>' +
          '<button id="targets-none" class="filter-link">None</button>' +
        '</div>' +
        '<div class="target-pill-list">';
    allTargets.forEach(function(t) {
      var checked = selectedTargets[t] !== false ? 'checked' : '';
      targetDropHtml += '<label class="target-check">' +
        '<input type="checkbox" data-target="' + esc(t) + '" ' + checked + '>' +
        '<span>' + esc(t) + '</span></label>';
    });
    targetDropHtml += '</div></div></div>';
  }

  var filterHtml = '<div class="filter-bar">' +
    targetDropHtml +
    '<div class="filter-dates">' +
      '<div class="date-input-wrap">' +
        '<span class="date-label' + (fromFilter ? '' : ' empty') + '">' + (fromFilter ? fmtDate(fromFilter) : 'From') + '</span>' +
        '<input type="date" id="filter-from" value="' + esc(fromFilter) + '" tabindex="-1">' +
        (fromFilter ? '<button class="date-clear" data-target="filter-from" title="Clear">\u00d7</button>' : '') +
      '</div>' +
      '<div class="date-input-wrap">' +
        '<span class="date-label' + (toFilter ? '' : ' empty') + '">' + (toFilter ? fmtDate(toFilter) : 'To') + '</span>' +
        '<input type="date" id="filter-to" value="' + esc(toFilter) + '" tabindex="-1">' +
        (toFilter ? '<button class="date-clear" data-target="filter-to" title="Clear">\u00d7</button>' : '') +
      '</div>' +
    '</div>' +
    '<div class="filter-sort" id="sort-dropdown">' +
      '<button class="sort-dropdown-btn" id="sort-dropdown-btn">' + esc(SORT_LABELS[currentSort]) + ' \u25be</button>' +
      '<div class="sort-dropdown-menu" id="sort-dropdown-menu">' +
        Object.keys(SORT_LABELS).map(function(v) {
          return '<button class="sort-option' + (currentSort === v ? ' active' : '') + '" data-sort="' + v + '">' + esc(SORT_LABELS[v]) + '</button>';
        }).join('') +
      '</div>' +
    '</div>' +
    '<button id="filter-clear" class="filter-link">Clear filters</button>' +
    '<div class="view-toggle ' + (cardViewMode === 'compact' ? 'is-compact' : 'is-expanded') + '">' +
      '<div class="view-toggle-thumb"></div>' +
      '<button class="view-toggle-btn' + (cardViewMode === 'compact' ? ' active' : '') + '" data-view="compact">Compact</button>' +
      '<button class="view-toggle-btn' + (cardViewMode === 'expanded' ? ' active' : '') + '" data-view="expanded">Expanded</button>' +
    '</div>' +
    '<label class="target-check" title="Include sessions with 0 captured images"><input type="checkbox" id="filter-empty"' + (showEmptySessions ? ' checked' : '') + '><span>Show empty</span></label>' +
    '<label class="target-check' + (cardViewMode === 'compact' ? ' disabled' : '') + '" title="Show camera FOV rectangle on thumbnails"><input type="checkbox" id="filter-fov"' + (showFovOverlay ? ' checked' : '') + (cardViewMode === 'compact' ? ' disabled' : '') + '><span>Show FOV</span></label>' +
    '<label class="target-check' + (cardViewMode === 'compact' ? ' disabled' : '') + '" title="Show altitude chart on each card"><input type="checkbox" id="filter-altitude"' + (showAltitude ? ' checked' : '') + (cardViewMode === 'compact' ? ' disabled' : '') + '><span>Altitude</span></label>';

  // Add unhide-all button if any sessions are hidden
  var tempHiddenCount = sessionsCache.filter(function(s) { return hiddenSessions[s.sessionId]; }).length;
  if (tempHiddenCount > 0) {
    filterHtml +=
      '<button id="unhide-all" class="filter-link">Unhide all (' + tempHiddenCount + ')</button>';
  }

  filterHtml += '</div>';

  // Filter sessions
  var activeTargets = {};
  allTargets.forEach(function(t) {
    if (selectedTargets[t] !== false) activeTargets[t] = true;
  });
  var allSelected = Object.keys(activeTargets).length === allTargets.length;

  var hiddenCount = sessionsCache.filter(function(s) { return hiddenSessions[s.sessionId]; }).length;

  var filtered = sessionsCache.filter(function(s) {
    if (!showHidden && hiddenSessions[s.sessionId]) return false;
    if (!showEmptySessions && s.imageCount === 0) return false;
    if (!allSelected) {
      var match = s.targets.some(function(t) { return activeTargets[t]; });
      if (!match) return false;
    }
    if (fromFilter && s.sessionStart.substring(0, 10) < fromFilter) return false;
    if (toFilter && s.sessionStart.substring(0, 10) > toFilter) return false;
    return true;
  });

  // Sort
  filtered.sort(function(a, b) {
    if (sortBy === 'date-asc') return a.sessionStart.localeCompare(b.sessionStart);
    if (sortBy === 'integration') return (b.totalIntegrationSeconds || 0) - (a.totalIntegrationSeconds || 0);
    if (sortBy === 'images') return (b.imageCount || 0) - (a.imageCount || 0);
    if (sortBy === 'targets') return (b.targets.length || 0) - (a.targets.length || 0);
    return b.sessionStart.localeCompare(a.sessionStart); // date-desc default
  });

  sub.textContent = filtered.length + ' of ' + sessionsCache.length + ' sessions';

  if (sessionsCache.length === 0) {
    el.innerHTML = filterHtml + '<div class="empty">No sessions recorded yet.</div>';
    bindListEvents();
    return;
  }

  if (filtered.length === 0) {
    el.innerHTML = filterHtml + '<div class="empty">No sessions match the current filters.</div>';
    bindListEvents();
    return;
  }

  var cards = filtered.map(function(s) {
    var targetsText = s.targets.length > 0
      ? s.targets.map(function(t, i) { return makeTargetBadge(t, i); }).join('')
      : '<span style="color:var(--text-quaternary);font-size:12px">No targets</span>';

    var badge = s.hasReport ? '' : '<span class="badge badge-red">No report</span>';

    var sessionTimes = fmtTime(s.sessionStart) + ' \u2013 ' + fmtTime(s.sessionEnd);

    var statsLine = '<span class="stat-val">' + s.imageCount + '</span> imgs' +
      ' &middot; <span class="stat-val">' + fmt(s.totalIntegrationSeconds) + '</span>' +
      ' &middot; HFR <span class="stat-val">' + fmtNum(s.avgHfr) + '</span>px' +
      ' &middot; FWHM <span class="stat-val">' + fmtNum(s.avgFwhm) + '</span>&Prime;' +
      ' &middot; <span class="stat-val">' + fmtNum(s.avgGuiding) + '&Prime;</span> guiding';

    var statBoxes = '<div class="card-stats">' +
      '<div class="card-stat card-stat-expandable stat-images" data-stat-type="images" data-session-id="' + s.sessionId + '"><div class="card-stat-value">' + s.imageCount + '</div><div class="card-stat-label">Images</div></div>' +
      '<div class="card-stat card-stat-expandable stat-integration" data-stat-type="integration" data-session-id="' + s.sessionId + '"><div class="card-stat-value">' + fmt(s.totalIntegrationSeconds) + '</div><div class="card-stat-label">Integration</div></div>' +
      '<div class="card-stat stat-hfr"><div class="card-stat-value">' + fmtNum(s.avgHfr) + 'px</div><div class="card-stat-label">HFR</div></div>' +
      '<div class="card-stat stat-fwhm"><div class="card-stat-value">' + fmtNum(s.avgFwhm) + '&Prime;</div><div class="card-stat-label">FWHM</div></div>' +
      '<div class="card-stat stat-guiding"><div class="card-stat-value">' + fmtNum(s.avgGuiding) + '&Prime;</div><div class="card-stat-label">Guiding</div></div>' +
      '<div class="card-stat stat-moon">' + (s.moonPhase ? '<div class="card-stat-value">' + esc(s.moonPhase) + '</div><div class="card-stat-label">Moon</div>' : '') + '</div>' +
      '</div>';

    return '<div class="session-card" onclick="navigate(\'#/sessions/' + s.sessionId + '\')">' +
      '<button class="hide-btn" data-session="' + s.sessionId + '" onclick="event.stopPropagation();hideSession(this.dataset.session)" title="Hide this session">\u2715</button>' +
      '<div class="card-header">' +
        '<span class="session-date">' + fmtDate(s.sessionStart) + '</span>' +
        '<span class="session-times">' + sessionTimes + '</span>' +
        '<span class="card-targets-line" id="targets-' + s.sessionId + '">' + targetsText + '</span>' +
        badge +
      '</div>' +
      '<div class="card-body">' +
        '<div class="card-content">' +
          '<div class="card-thumbs" id="thumbs-' + s.sessionId + '"></div>' +
          '<div class="card-stats-line">' + statsLine + '</div>' +
          statBoxes +
        '</div>' +
        '<div class="card-altitude" id="altitude-' + s.sessionId + '"' + (showAltitude ? '' : ' style="display:none"') + '></div>' +
      '</div>' +
    '</div>';
  }).join('');

  var modeClass = cardViewMode === 'compact' ? ' cards-compact' : '';
  // Only animate fade-in on the very first page load — filter toggles reveal instantly
  var fadeStyle = (!initialLoadDone && cardViewMode === 'expanded')
    ? 'opacity:0;transition:opacity 400ms cubic-bezier(0.22,1,0.36,1)'
    : 'opacity:1';

  el.innerHTML = filterHtml + '<div class="cards-container' + modeClass + '" style="' + fadeStyle + '">' + cards + '</div>';
  bindListEvents();

  loadLiveStacks(filtered);

  if (!initialLoadDone && cardViewMode === 'expanded') {
    // First load only: hold opacity:0 until all assets are fetched, then reveal together
    function revealContainer() {
      var container = el.querySelector('.cards-container');
      if (container) container.style.opacity = '1';
    }
    var pending = loadThumbnails(filtered).concat(loadAltitudeCharts(filtered));
    if (pending.length === 0) {
      requestAnimationFrame(revealContainer);
    } else {
      var safetyTimer = setTimeout(revealContainer, 5000);
      Promise.all(pending).then(function() {
        clearTimeout(safetyTimer);
        revealContainer();
      });
    }
    initialLoadDone = true;
  } else {
    loadThumbnails(filtered);
    // Re-render cached charts directly (works even after navigation destroyed the old divs)
    filtered.forEach(function(s) {
      if (altitudeChartCache[s.sessionId]) {
        renderAltitudeChart(s, altitudeChartCache[s.sessionId]);
      }
    });
    // IO observer for any uncached charts (lazy-loads as they scroll into view)
    setupAltitudeObserver(filtered);
  }
}

function renderThumbnails(s, thumbs) {
  var el = document.getElementById('thumbs-' + s.sessionId);
  if (!el) return;
  el.innerHTML = thumbs.map(function(t) {
    var img = '<img class="card-thumb" src="' + t.dataUri + '" alt="' + esc(t.target) + '" loading="lazy" onerror="this.style.display=\'none\'">';
    var svg = '';
    if (t.fovSvg) {
      svg = t.fovSvg
        .replace(/width='\d+'/, "width='100%'")
        .replace(/height='\d+'/, "height='100%'")
        .replace("<svg ", "<svg viewBox='0 0 200 200' " + (showFovOverlay ? '' : "style='display:none' "));
    }
    var labelName = t.target.length > 30 ? t.target.substring(0, 29) + '\u2026' : t.target;
    var labelFontStyle = labelName.length <= 14 ? '' :
      labelName.length <= 20 ? ' style="font-size:7px"' : ' style="font-size:6px"';
    return '<div class="card-thumb-wrap" data-target="' + esc(t.target) + '" data-session="' + esc(s.sessionId) + '">' +
      '<div class="thumb-label"' + labelFontStyle + '>' + esc(labelName) + '</div>' +
      img + svg + '</div>';
  }).join('');
  var targetsEl = document.getElementById('targets-' + s.sessionId);
  if (targetsEl && thumbs.length > 0) {
    var thumbOrder = thumbs.map(function(t) { return t.target; });
    var remaining = s.targets.filter(function(t) { return thumbOrder.indexOf(t) === -1; });
    var ordered = thumbOrder.concat(remaining);
    targetsEl.innerHTML = ordered.map(function(t, i) { return makeTargetBadge(t, i); }).join('');
  }
  setupMobileThumbnailZoom(el);
}

function loadThumbnails(sessions) {
  var promises = [];
  sessions.forEach(function(s) {
    if (!s.hasReport) return;
    if (thumbnailCache[s.sessionId]) {
      var el = document.getElementById('thumbs-' + s.sessionId);
      if (el) renderThumbnails(s, thumbnailCache[s.sessionId]);
      return;
    }
    if (thumbnailFetching[s.sessionId]) return; // already in flight
    if (!document.getElementById('thumbs-' + s.sessionId)) return;
    thumbnailFetching[s.sessionId] = true;
    promises.push(api('/api/sessions/' + s.sessionId + '/thumbnails').then(function(thumbs) {
      delete thumbnailFetching[s.sessionId];
      if (!thumbs || thumbs.length === 0) return;
      thumbnailCache[s.sessionId] = thumbs;
      renderThumbnails(s, thumbs);
    }).catch(function(err) {
      delete thumbnailFetching[s.sessionId];
      logDebug('Thumb load failed for', s.sessionId, err.message);
    }));
  });
  return promises;
}

function wireLiveStackBadges(s, data) {
  var thumbsEl = document.getElementById('thumbs-' + s.sessionId);
  if (!thumbsEl) return;
  var wraps = thumbsEl.querySelectorAll('.card-thumb-wrap');
  for (var i = 0; i < wraps.length; i++) {
    var target = wraps[i].getAttribute('data-target');
    if (target && data[target]) {
      // Don't add a second badge if already wired (e.g. cache hit on re-render)
      if (wraps[i].querySelector('.livestack-badge')) continue;
      var count = data[target].length;
      var badge = document.createElement('span');
      badge.className = 'livestack-badge';
      badge.textContent = count;
      badge.title = count + ' live stack image' + (count !== 1 ? 's' : '');
      wraps[i].appendChild(badge);
      setupLiveStackHover(wraps[i], s.sessionId, target);
    }
  }
}

function loadLiveStacks(sessions) {
  sessions.forEach(function(s) {
    if (!s.hasReport) return;
    if (livestackMap[s.sessionId]) {
      wireLiveStackBadges(s, livestackMap[s.sessionId]);
      return;
    }
    api('/api/sessions/' + s.sessionId + '/livestack').then(function(data) {
      // data is { targetName: [{target, filter, url, label, isComposite}] }
      if (!data || Object.keys(data).length === 0) return;
      livestackMap[s.sessionId] = data;
      wireLiveStackBadges(s, data);
    }).catch(function(err) {
      logDebug('LiveStack load failed for', s.sessionId, err.message);
    });
  });
}

function setupLiveStackHover(thumbWrap, sessionId, targetName) {
  var hoverTimer = null;
  var shelf = null;
  var shelfLeaveTimer = null;

  function showShelf() {
    if (shelf) return;
    var images = livestackMap[sessionId] && livestackMap[sessionId][targetName];
    if (!images || images.length === 0) return;

    shelf = document.createElement('div');
    shelf.className = 'livestack-shelf';

    var imagesDiv = document.createElement('div');
    imagesDiv.className = 'livestack-shelf-images';

    images.forEach(function(img, idx) {
      var item = document.createElement('div');
      item.className = 'livestack-shelf-item';
      item.style.animationDelay = (idx * 40) + 'ms';

      var imgEl = document.createElement('img');
      imgEl.className = 'livestack-shelf-img';
      imgEl.src = img.url;
      imgEl.alt = img.label;
      imgEl.loading = 'lazy';
      imgEl.style.cursor = 'pointer';
      imgEl.addEventListener('click', function(e) {
        e.stopPropagation();
        var overlay = document.createElement('div');
        overlay.className = 'livestack-zoom-overlay';
        var zoomImg = document.createElement('img');
        zoomImg.src = img.url;
        zoomImg.alt = img.label;
        overlay.appendChild(zoomImg);
        overlay.addEventListener('click', function() {
          overlay.remove();
        });
        document.body.appendChild(overlay);
      });

      var label = document.createElement('div');
      label.className = 'livestack-shelf-label';
      label.textContent = img.label;

      item.appendChild(imgEl);
      item.appendChild(label);
      imagesDiv.appendChild(item);
    });

    shelf.appendChild(imagesDiv);

    // Position relative to the thumb — append to .card-thumbs container
    var thumbsContainer = thumbWrap.closest('.card-thumbs');
    if (!thumbsContainer) { shelf = null; return; }
    thumbWrap.classList.add('shelf-active');
    thumbsContainer.appendChild(shelf);

    // Calculate position: center shelf below the hovered thumb
    // Account for the transform:scale(1.67) on hover — the visual size is larger
    var wrapRect = thumbWrap.getBoundingClientRect();
    var containerRect = thumbsContainer.getBoundingClientRect();
    var centerX = (wrapRect.left + wrapRect.width / 2) - containerRect.left;
    var topY = wrapRect.bottom - containerRect.top + 75;

    shelf.style.left = centerX + 'px';
    shelf.style.top = topY + 'px';

    // Clamp so shelf doesn't overflow past left edge of viewport
    requestAnimationFrame(function() {
      if (!shelf) return;
      var shelfRect = shelf.getBoundingClientRect();
      if (shelfRect.left < 12) {
        var shift = 12 - shelfRect.left;
        shelf.style.left = (centerX + shift) + 'px';
      }
    });

    // Shelf hover: keep alive when mouse enters shelf
    shelf.addEventListener('mouseenter', function() {
      clearTimeout(shelfLeaveTimer);
    });
    shelf.addEventListener('mouseleave', function(e) {
      // Hide unless mouse went back to the thumb
      if (thumbWrap.contains(e.relatedTarget)) return;
      hideShelf();
    });
  }

  function hideShelf() {
    clearTimeout(shelfLeaveTimer);
    thumbWrap.classList.remove('shelf-active');
    if (shelf) {
      shelf.classList.add('shelf-hiding');
      var s = shelf;
      setTimeout(function() { if (s.parentNode) s.parentNode.removeChild(s); }, 150);
      shelf = null;
    }
  }

  thumbWrap.addEventListener('mouseenter', function() {
    clearTimeout(shelfLeaveTimer);
    hoverTimer = setTimeout(showShelf, 200);
  });

  thumbWrap.addEventListener('mouseleave', function(e) {
    clearTimeout(hoverTimer);
    // Don't hide immediately if mouse moved into the shelf — give a grace period
    if (shelf && shelf.contains(e.relatedTarget)) return;
    shelfLeaveTimer = setTimeout(hideShelf, 100);
  });
}

function hideSession(sessionId) {
  hiddenSessions[sessionId] = true;
  localStorage.setItem('ns-hidden-sessions', JSON.stringify(hiddenSessions));

  var btn = document.querySelector('.hide-btn[data-session="' + sessionId + '"]');
  var card = btn ? btn.closest('.session-card') : null;

  function afterRemove() {
    // Update subtitle
    var sub = document.getElementById('page-subtitle');
    if (sub) {
      var visible = document.querySelectorAll('.session-card').length;
      sub.textContent = visible + ' of ' + sessionsCache.length + ' sessions';
    }

    // Update or create unhide-all button in the filter bar
    var hiddenCount = sessionsCache.filter(function(s) { return hiddenSessions[s.sessionId]; }).length;
    var unhideBtn = document.getElementById('unhide-all');
    if (unhideBtn) {
      unhideBtn.textContent = 'Unhide all (' + hiddenCount + ')';
    } else {
      var filterBar = document.querySelector('.filter-bar');
      if (!filterBar) return;
      var unhideBtn2 = document.createElement('button');
      unhideBtn2.id = 'unhide-all';
      unhideBtn2.className = 'filter-link';
      unhideBtn2.textContent = 'Unhide all (' + hiddenCount + ')';
      filterBar.appendChild(unhideBtn2);
      unhideBtn2.addEventListener('click', function() {
        hiddenSessions = {};
        showHidden = false;
        localStorage.setItem('ns-hidden-sessions', '{}');
        var from = document.getElementById('filter-from');
        var to = document.getElementById('filter-to');
        var sort = document.getElementById('filter-sort');
        doRenderList(document.getElementById('content'), document.getElementById('page-subtitle'),
          from ? from.value : '', to ? to.value : '', sort ? sort.value : 'date-desc');
      });
    }
  }

  if (card) {
    // Phase 1: fade out + slight scale (GPU-composited)
    card.style.transition = 'opacity 0.2s, transform 0.2s';
    card.style.opacity = '0';
    card.style.transform = 'scale(0.97)';
    setTimeout(function() {
      // Phase 2: slide siblings up using translateY (GPU-composited, 60fps)
      var gap = card.offsetHeight + parseFloat(getComputedStyle(card).marginBottom || 0);
      card.style.visibility = 'hidden';
      card.style.position = 'absolute';
      card.style.width = '100%';
      card.style.pointerEvents = 'none';

      // Siblings jump up when card goes absolute — offset them back, then animate to 0
      var siblings = [];
      var next = card.nextElementSibling;
      while (next) {
        next.style.transition = 'none';
        next.style.transform = 'translateY(' + gap + 'px)'; // counteract the instant jump
        siblings.push(next);
        next = next.nextElementSibling;
      }
      // Force layout, then animate to natural position
      if (siblings.length) siblings[0].offsetHeight;
      siblings.forEach(function(s) {
        s.style.transition = 'transform 0.5s cubic-bezier(0.22, 1, 0.36, 1)';
        s.style.transform = 'translateY(0)';
      });

      setTimeout(function() {
        // Clean up: remove card and clear transforms
        siblings.forEach(function(s) { s.style.transition = ''; s.style.transform = ''; });
        if (card.parentNode) card.parentNode.removeChild(card);
        afterRemove();
      }, 500);
    }, 200);
  } else {
    afterRemove();
  }
}

// ── Stat box hover expansion ─────────────────────────────────────────────

function hideStatExpand() {
  clearTimeout(statExpandTimer);
  statExpandTimer = null;
  statExpandActiveEl = null;
  var popup = document.getElementById('stat-expand-popup');
  if (popup) {
    popup.classList.add('stat-expand-hiding');
    setTimeout(function() { if (popup.parentNode) popup.parentNode.removeChild(popup); }, 180);
  }
}

function showStatExpand(el, sessionId, type) {
  function render(detail) {
    var targets = detail.targets || [];
    if (targets.length === 0) return;

    // Sort by the relevant metric descending
    var sorted = targets.slice().sort(function(a, b) {
      return type === 'images'
        ? (b.imageCount || 0) - (a.imageCount || 0)
        : (b.integrationSeconds || 0) - (a.integrationSeconds || 0);
    });

    var rows = sorted.map(function(t) {
      var val = type === 'images'
        ? (t.imageCount || 0)
        : fmt(t.integrationSeconds);
      return '<div class="stat-expand-row">' +
        '<span class="stat-expand-filter">' + esc(t.target) + '</span>' +
        '<span class="stat-expand-val">' + esc(String(val)) + '</span>' +
        '</div>';
    }).join('');

    // Remove existing popup
    var old = document.getElementById('stat-expand-popup');
    if (old) old.parentNode.removeChild(old);

    var popup = document.createElement('div');
    popup.id = 'stat-expand-popup';
    popup.className = 'stat-expand-popup';
    popup.innerHTML =
      '<div class="stat-expand-header">' + (type === 'images' ? 'images by target' : 'integration by target') + '</div>' +
      rows;
    document.body.appendChild(popup);

    // Position below (or above if near bottom) the stat box
    var rect = el.getBoundingClientRect();
    var popupH = popup.offsetHeight;
    var spaceBelow = window.innerHeight - rect.bottom;
    var top, left;
    if (spaceBelow >= popupH + 8 || spaceBelow >= 100) {
      top = rect.bottom + window.scrollY + 6;
    } else {
      top = rect.top + window.scrollY - popupH - 6;
    }
    left = rect.left + window.scrollX + (rect.width / 2);
    popup.style.top = top + 'px';
    popup.style.left = left + 'px';

    requestAnimationFrame(function() {
      if (!document.getElementById('stat-expand-popup')) return;
      var pr = popup.getBoundingClientRect();
      var pad = 12;
      if (pr.left < pad) {
        popup.style.left = (left + (pad - pr.left)) + 'px';
      } else if (pr.right > window.innerWidth - pad) {
        popup.style.left = (left - (pr.right - (window.innerWidth - pad))) + 'px';
      }
      popup.classList.add('stat-expand-visible');
    });
  }

  if (detailCache[sessionId]) {
    render(detailCache[sessionId]);
  } else {
    fetch('/api/sessions/' + sessionId)
      .then(function(r) { return r.json(); })
      .then(function(d) {
        detailCache[sessionId] = d;
        // Only render if this stat box is still hovered
        if (statExpandActiveEl === el) render(d);
      })
      .catch(function() {});
  }
}

function showTargetStatExpand(el, filters, type) {
  if (!filters || filters.length === 0) return;

  var SORT_ORDER = ['L', 'R', 'G', 'B', 'H', 'S', 'O', 'N'];
  var sorted = filters.slice().filter(function(f) {
    return type === 'integration' ? (f.totalSeconds || 0) >= 1 : (f.acceptedCount || 0) > 0;
  }).sort(function(a, b) {
    var ai = SORT_ORDER.indexOf(resolveFilterType(a.filter) || '');
    var bi = SORT_ORDER.indexOf(resolveFilterType(b.filter) || '');
    if (ai < 0) ai = SORT_ORDER.length;
    if (bi < 0) bi = SORT_ORDER.length;
    return ai - bi;
  });
  if (sorted.length === 0) return;

  var rows = sorted.map(function(f) {
    var val = type === 'integration' ? fmt(f.totalSeconds) : (f.acceptedCount || 0);
    return '<div class="stat-expand-row">' +
      '<span class="stat-expand-filter">' + filterTypePill(f.filter) + '</span>' +
      '<span class="stat-expand-val">' + esc(String(val)) + '</span>' +
      '</div>';
  }).join('');

  var old = document.getElementById('stat-expand-popup');
  if (old) old.parentNode.removeChild(old);

  var popup = document.createElement('div');
  popup.id = 'stat-expand-popup';
  popup.className = 'stat-expand-popup';
  popup.innerHTML =
    '<div class="stat-expand-header">' + (type === 'integration' ? 'integration by filter' : 'frames by filter') + '</div>' +
    rows;
  document.body.appendChild(popup);

  var rect = el.getBoundingClientRect();
  var popupH = popup.offsetHeight;
  var spaceBelow = window.innerHeight - rect.bottom;
  var top = (spaceBelow >= popupH + 8 || spaceBelow >= 100)
    ? rect.bottom + window.scrollY + 6
    : rect.top + window.scrollY - popupH - 6;
  var left = rect.left + window.scrollX + (rect.width / 2);
  popup.style.top = top + 'px';
  popup.style.left = left + 'px';

  requestAnimationFrame(function() {
    if (!document.getElementById('stat-expand-popup')) return;
    var pr = popup.getBoundingClientRect();
    var pad = 12;
    if (pr.left < pad) {
      popup.style.left = (left + (pad - pr.left)) + 'px';
    } else if (pr.right > window.innerWidth - pad) {
      popup.style.left = (left - (pr.right - (window.innerWidth - pad))) + 'px';
    }
    popup.classList.add('stat-expand-visible');
  });
}

// Detect touch device once
var isTouchDevice = 'ontouchstart' in window;

// ── TS progress bar template name tooltip ────────────────────────────────
(function() {
  var tip = document.createElement('div');
  tip.className = 'ts-bar-tip';
  document.body.appendChild(tip);
  var autoHide;

  function show(text, target) {
    clearTimeout(autoHide);
    tip.textContent = text;
    // Pre-position off-screen so we can measure dimensions before revealing
    tip.style.top = '-9999px';
    tip.style.left = '-9999px';
    tip.classList.add('visible');
    var r = target.getBoundingClientRect();
    var tw = tip.offsetWidth;
    var th = tip.offsetHeight;
    var left = r.left + r.width / 2 - tw / 2;
    left = Math.max(8, Math.min(left, window.innerWidth - tw - 8));
    var top = r.top - th - 8;
    if (top < 8) top = r.bottom + 8; // flip below if too close to top
    tip.style.left = left + 'px';
    tip.style.top = top + 'px';
  }

  function hide() {
    clearTimeout(autoHide);
    tip.classList.remove('visible');
  }

  // Desktop: show on hover
  document.addEventListener('mouseover', function(e) {
    if (isTouchDevice) return;
    var el = e.target.closest('[data-template]');
    if (el) show(el.dataset.template, el);
  });
  document.addEventListener('mouseout', function(e) {
    if (isTouchDevice) return;
    var el = e.target.closest('[data-template]');
    if (el && !el.contains(e.relatedTarget)) hide();
  });

  // Mobile: tap to show; retap same element or tap elsewhere to hide
  var activeEl = null;
  document.addEventListener('click', function(e) {
    if (!isTouchDevice) return;
    var el = e.target.closest('[data-template]');
    if (!el) { hide(); activeEl = null; return; }
    if (tip.classList.contains('visible') && el === activeEl) {
      hide(); activeEl = null; return;
    }
    activeEl = el;
    show(el.dataset.template, el);
    autoHide = setTimeout(function() { hide(); activeEl = null; }, 2500);
  });
})();

// Event delegation for stat box hover expansion (desktop only)
document.addEventListener('mouseenter', function(e) {
  if (isTouchDevice) return;
  var el = e.target.closest('.card-stat-expandable');
  if (!el) return;
  var sessionId = el.dataset.sessionId;
  var type = el.dataset.statType;
  if (!sessionId || !type) return;
  clearTimeout(statExpandTimer);
  statExpandActiveEl = el;
  statExpandTimer = setTimeout(function() {
    showStatExpand(el, sessionId, type);
  }, 350);
}, true);

document.addEventListener('mouseleave', function(e) {
  if (isTouchDevice) return;
  var el = e.target.closest('.card-stat-expandable');
  if (!el) return;
  var popup = document.getElementById('stat-expand-popup');
  if (popup && popup.contains(e.relatedTarget)) return;
  hideStatExpand();
}, true);

// Hide popup when mouse leaves it (desktop only)
document.addEventListener('mouseleave', function(e) {
  if (isTouchDevice) return;
  if (!e.target || e.target.id !== 'stat-expand-popup') return;
  if (statExpandActiveEl && statExpandActiveEl.contains(e.relatedTarget)) return;
  hideStatExpand();
}, true);

// Mobile: tap stat boxes to toggle expand/collapse
// Uses click (capture phase) + CSS touch-action:manipulation for instant first-tap
// Capture phase fires BEFORE the card's inline onclick can navigate
document.addEventListener('click', function(e) {
  var el = e.target.closest('.card-stat-expandable');
  if (el && 'ontouchstart' in window) {
    e.stopPropagation(); // block card onclick navigation
    e.preventDefault();
    var sessionId = el.dataset.sessionId;
    var type = el.dataset.statType;
    if (statExpandActiveEl === el) {
      hideStatExpand(); // tap same → collapse (sets statExpandActiveEl = null)
    } else {
      statExpandActiveEl = el; // track which box is expanded
      showStatExpand(el, sessionId, type);
    }
    return;
  }
  // Target card stat box tap (mobile)
  var tel = e.target.closest('.target-stat-expandable');
  if (tel && 'ontouchstart' in window) {
    e.stopPropagation();
    e.preventDefault();
    var ttype = tel.dataset.statType;
    if (statExpandActiveEl === tel) {
      hideStatExpand();
    } else {
      statExpandActiveEl = tel;
      var tfilters = resolveTargetStatFilters(tel);
      if (tfilters) showTargetStatExpand(tel, tfilters, ttype);
    }
    return;
  }
  // Tap outside — dismiss if open
  var popup = document.getElementById('stat-expand-popup');
  if (popup && !popup.contains(e.target) &&
      !e.target.closest('.card-stat-expandable') &&
      !e.target.closest('.target-stat-expandable')) {
    hideStatExpand();
  }
}, true); // capture phase

// Resolve the filters array for a `.target-stat-expandable` element.
// Target cards use data-target-idx → statsTargetData lookup.
// Detail panel KPI boxes use data-stat-source="tdp" → lookup in tdpKpiFilters.
function resolveTargetStatFilters(el) {
  var source = el.dataset.statSource;
  if (source === 'tdp') {
    return tdpKpiFilters || null;
  }
  var targetIdx = parseInt(el.dataset.targetIdx, 10);
  if (isNaN(targetIdx)) return null;
  var t = statsTargetData && statsTargetData[targetIdx];
  return (t && t.filters) || null;
}

// Filters for the currently open detail panel (used by KPI box popups).
var tdpKpiFilters = null;

// Event delegation for target card stat box hover expansion (desktop only)
document.addEventListener('mouseenter', function(e) {
  if (isTouchDevice) return;
  var el = e.target.closest('.target-stat-expandable');
  if (!el) return;
  var type = el.dataset.statType;
  if (!type) return;
  var filters = resolveTargetStatFilters(el);
  if (!filters) return;
  clearTimeout(statExpandTimer);
  statExpandActiveEl = el;
  statExpandTimer = setTimeout(function() {
    showTargetStatExpand(el, filters, type);
  }, 350);
}, true);

document.addEventListener('mouseleave', function(e) {
  if (isTouchDevice) return;
  var el = e.target.closest('.target-stat-expandable');
  if (!el) return;
  var popup = document.getElementById('stat-expand-popup');
  if (popup && popup.contains(e.relatedTarget)) return;
  hideStatExpand();
}, true);

// ── Altitude chart crosshair ──────────────────────────────────────────────

function setupChartCrosshair(container) {
  var svg = container.querySelector('svg');
  if (!svg) return;

  var ns = 'http://www.w3.org/2000/svg';
  var viewBox = svg.getAttribute('viewBox').split(' ').map(Number);
  var vbMinX = viewBox[0], vbMinY = viewBox[1], vbW = viewBox[2];
  // Plot area bounds in viewBox coordinates
  var plotL = 38, plotR = vbMinX + vbW - 10, plotT = 20, plotB = 220;

  // Extract time labels from the SVG
  var timeLabels = [];
  svg.querySelectorAll('text').forEach(function(t) {
    if (t.getAttribute('fill') === '#888' && /^\d{2}:\d{2}$/.test(t.textContent.trim())) {
      timeLabels.push({ x: parseFloat(t.getAttribute('x')), time: t.textContent.trim() });
    }
  });
  timeLabels.sort(function(a, b) { return a.x - b.x; });

  // Extract target polylines (colored ones inside <g> with <title>)
  var targets = [];
  svg.querySelectorAll('g').forEach(function(g) {
    var title = g.querySelector('title');
    if (!title || title.textContent === 'Moon Position') return;
    var polys = g.querySelectorAll('polyline');
    var poly = polys.length > 1 ? polys[1] : polys[0]; // second is colored
    if (!poly || poly.getAttribute('stroke') === 'transparent') poly = polys.length > 1 ? polys[1] : null;
    if (!poly) return;
    var color = poly.getAttribute('stroke');
    if (color === 'transparent') return;
    var pts = poly.getAttribute('points').split(' ').map(function(p) {
      var c = p.split(','); return { x: parseFloat(c[0]), y: parseFloat(c[1]) };
    }).filter(function(p) { return !isNaN(p.x) && !isNaN(p.y); });
    targets.push({ name: title.textContent, color: color, points: pts });
  });

  if (targets.length === 0) return;

  // Extract imaging window rects (colored rects with opacity='0.15') and their border lines
  var imagingWindows = [];
  svg.querySelectorAll("rect[opacity='0.15']").forEach(function(r) {
    var x = parseFloat(r.getAttribute('x'));
    var w = parseFloat(r.getAttribute('width'));
    var color = r.getAttribute('fill');
    // Find which target this rect belongs to by matching color
    var targetIdx = -1;
    for (var i = 0; i < targets.length; i++) {
      if (targets[i].color === color) { targetIdx = i; break; }
    }
    if (targetIdx >= 0) {
      imagingWindows.push({ x: x, w: w, targetIdx: targetIdx, rect: r });
    }
  });

  // Collect all imaging window border lines (colored lines with opacity='0.6')
  var windowLines = [];
  svg.querySelectorAll("line[opacity='0.6']").forEach(function(l) {
    windowLines.push(l);
  });

  // Collect target groups for opacity control
  var targetGroups = [];
  svg.querySelectorAll('g').forEach(function(g) {
    var title = g.querySelector('title');
    if (title && title.textContent !== 'Moon Position') {
      targetGroups.push(g);
    }
  });

  // Create persistent SVG elements (update positions on mousemove, avoid DOM churn)
  var crossLine = document.createElementNS(ns, 'line');
  crossLine.setAttribute('stroke', isMobile ? '#d4a06a' : '#ffffff');
  crossLine.setAttribute('stroke-width', isMobile ? '1.5' : '0.5');
  crossLine.setAttribute('stroke-dasharray', isMobile ? '6,4' : '3,3');
  crossLine.setAttribute('opacity', isMobile ? '1' : '0.5');
  crossLine.setAttribute('vector-effect', 'non-scaling-stroke');
  crossLine.style.display = 'none';
  crossLine.style.pointerEvents = 'none';
  svg.appendChild(crossLine);

  var tooltip = document.createElementNS(ns, 'g');
  tooltip.style.display = 'none';
  tooltip.style.pointerEvents = 'none';
  svg.appendChild(tooltip);

  // Time label element
  var isMobile = window.innerWidth <= 700;
  var timeFontSize = isMobile ? '18' : '9';
  var altFontSize = isMobile ? '16' : '8';
  var dotRadius = isMobile ? '5' : '3';

  var timeText = document.createElementNS(ns, 'text');
  timeText.setAttribute('fill', '#fff');
  timeText.setAttribute('font-size', timeFontSize);
  timeText.setAttribute('text-anchor', 'middle');
  timeText.setAttribute('font-weight', 'bold');
  tooltip.appendChild(timeText);

  // Pre-create dot + label for each target
  var markers = targets.map(function(t) {
    var dot = document.createElementNS(ns, 'circle');
    dot.setAttribute('r', dotRadius);
    dot.setAttribute('fill', t.color);
    dot.setAttribute('stroke', '#fff');
    dot.setAttribute('stroke-width', '0.8');
    tooltip.appendChild(dot);
    var label = document.createElementNS(ns, 'text');
    label.setAttribute('fill', t.color);
    label.setAttribute('font-size', altFontSize);
    label.setAttribute('font-weight', 'bold');
    tooltip.appendChild(label);
    return { dot: dot, label: label };
  });

  function interpolateY(points, x) {
    for (var i = 0; i < points.length - 1; i++) {
      if (x >= points[i].x && x <= points[i + 1].x) {
        var t = (x - points[i].x) / (points[i + 1].x - points[i].x);
        return points[i].y + t * (points[i + 1].y - points[i].y);
      }
    }
    return null;
  }

  function xToTime(x) {
    for (var i = 0; i < timeLabels.length - 1; i++) {
      if (x >= timeLabels[i].x && x <= timeLabels[i + 1].x) {
        var t = (x - timeLabels[i].x) / (timeLabels[i + 1].x - timeLabels[i].x);
        var p1 = timeLabels[i].time.split(':').map(Number);
        var p2 = timeLabels[i + 1].time.split(':').map(Number);
        var m1 = p1[0] * 60 + p1[1], m2 = p2[0] * 60 + p2[1];
        if (m2 < m1) m2 += 1440;
        var m = (m1 + t * (m2 - m1)) % 1440;
        var hh = Math.floor(m / 60), mm = Math.floor(m % 60);
        return (hh < 10 ? '0' : '') + hh + ':' + (mm < 10 ? '0' : '') + mm;
      }
    }
    return '';
  }

  function yToAlt(y) { return Math.max(0, Math.min(90, 90 * (plotB - y) / (plotB - plotT))); }

  function updateCrosshair(clientX, clientY) {
    // Map client coords to SVG viewBox coordinates using CTM (handles preserveAspectRatio=none)
    var pt = svg.createSVGPoint();
    pt.x = clientX; pt.y = clientY;
    var ctm = svg.getScreenCTM();
    var svgPt = pt.matrixTransform(ctm.inverse());
    var sx = svgPt.x;

    // Counter-transform for text: undo horizontal squash from preserveAspectRatio=none
    var scaleRatio = ctm.d / ctm.a; // yScale / xScale
    var textTransform = 'scale(' + scaleRatio.toFixed(3) + ', 1)';

    if (sx < plotL || sx > plotR) {
      hideCrosshair();
      return;
    }

    crossLine.setAttribute('x1', sx); crossLine.setAttribute('y1', plotT);
    crossLine.setAttribute('x2', sx); crossLine.setAttribute('y2', plotB);
    crossLine.style.display = '';
    tooltip.style.display = '';

    // Time at top — position just inside visible viewBox area
    var time = xToTime(sx);
    var timeY = vbMinY + (isMobile ? 16 : 8);
    timeText.setAttribute('x', sx);
    timeText.setAttribute('y', timeY);
    timeText.setAttribute('transform', 'translate(' + sx + ',' + timeY + ') ' + textTransform + ' translate(' + (-sx) + ',' + (-timeY) + ')');
    timeText.textContent = time;

    // Detect which imaging window the crosshair is inside
    var activeTarget = -1;
    for (var w = 0; w < imagingWindows.length; w++) {
      var iw = imagingWindows[w];
      if (sx >= iw.x && sx <= iw.x + iw.w) {
        activeTarget = iw.targetIdx;
        break;
      }
    }

    // Highlight active target, dim others (curves, shading, border lines)
    if (targets.length > 1) {
      for (var g = 0; g < targetGroups.length; g++) {
        targetGroups[g].style.opacity = (activeTarget === -1 || g === activeTarget) ? '1' : '0.15';
      }
      for (var r = 0; r < imagingWindows.length; r++) {
        var isActive = activeTarget === -1 || imagingWindows[r].targetIdx === activeTarget;
        imagingWindows[r].rect.style.opacity = isActive ? '0.15' : '0.04';
      }
      for (var l = 0; l < windowLines.length; l++) {
        // Match border lines to targets by color
        var lineColor = windowLines[l].getAttribute('stroke');
        var lineActive = activeTarget === -1;
        if (!lineActive) {
          lineActive = targets[activeTarget] && targets[activeTarget].color === lineColor;
        }
        windowLines[l].style.opacity = lineActive ? '0.6' : '0.1';
      }
    }

    // Per-target markers — only show for the active target's imaging window
    for (var i = 0; i < targets.length; i++) {
      if (activeTarget !== -1 && i !== activeTarget) {
        markers[i].dot.style.display = 'none';
        markers[i].label.style.display = 'none';
        continue;
      }
      var y = (activeTarget === -1) ? null : interpolateY(targets[i].points, sx);
      if (y === null || y < plotT || y > plotB) {
        markers[i].dot.style.display = 'none';
        markers[i].label.style.display = 'none';
        continue;
      }
      markers[i].dot.setAttribute('cx', sx);
      markers[i].dot.setAttribute('cy', y);
      // Counter-transform dot to stay circular despite preserveAspectRatio=none stretch
      markers[i].dot.setAttribute('transform', 'translate(' + sx + ',' + y + ') ' + textTransform + ' translate(' + (-sx) + ',' + (-y) + ')');
      markers[i].dot.style.display = '';
      var alt = yToAlt(y).toFixed(0) + '\u00b0';
      markers[i].label.textContent = alt;
      // Position label to the right, offset to avoid overlap; counter-transform text
      var labelGap = isMobile ? 10 : 5;
      var labelSpacing = isMobile ? 20 : 10;
      var lx = sx + labelGap, ly2 = y - 4 - i * labelSpacing;
      markers[i].label.setAttribute('x', lx);
      markers[i].label.setAttribute('y', ly2);
      markers[i].label.setAttribute('transform', 'translate(' + lx + ',' + ly2 + ') ' + textTransform + ' translate(' + (-lx) + ',' + (-ly2) + ')');
      markers[i].label.style.display = '';
    }
  }

  function hideCrosshair() {
    crossLine.style.display = 'none';
    tooltip.style.display = 'none';
    // Restore all opacities
    for (var g = 0; g < targetGroups.length; g++) targetGroups[g].style.opacity = '1';
    for (var r = 0; r < imagingWindows.length; r++) imagingWindows[r].rect.style.opacity = '0.15';
    for (var l = 0; l < windowLines.length; l++) windowLines[l].style.opacity = '0.6';
  }

  // Mouse events (desktop)
  svg.addEventListener('mousemove', function(e) {
    updateCrosshair(e.clientX, e.clientY);
  });
  svg.addEventListener('mouseleave', hideCrosshair);

  // Touch events (mobile) — horizontal drag scrubs crosshair, vertical scrolls page
  var touchStartX = 0, touchStartY = 0, touchLocked = null; // 'crosshair' or 'scroll'
  svg.addEventListener('touchstart', function(e) {
    var t = e.touches[0];
    touchStartX = t.clientX;
    touchStartY = t.clientY;
    touchLocked = null;
    updateCrosshair(t.clientX, t.clientY);
  }, { passive: true });
  svg.addEventListener('touchmove', function(e) {
    var t = e.touches[0];
    // Lock direction on first significant movement
    if (!touchLocked) {
      var dx = Math.abs(t.clientX - touchStartX);
      var dy = Math.abs(t.clientY - touchStartY);
      if (dx > 6 || dy > 6) {
        touchLocked = dx > dy ? 'crosshair' : 'scroll';
        if (touchLocked === 'scroll') hideCrosshair();
      }
    }
    if (touchLocked === 'crosshair') {
      updateCrosshair(t.clientX, t.clientY);
      e.preventDefault(); // only block scroll for horizontal drags
    }
  }, { passive: false });
  svg.addEventListener('touchend', function() { touchLocked = null; hideCrosshair(); });
  svg.addEventListener('touchcancel', function() { touchLocked = null; hideCrosshair(); });
}

// ── Mobile thumbnail zoom (scroll-to-center) ────────────────────────────

function setupMobileThumbnailZoom(thumbsContainer) {
  if (!thumbsContainer || window.innerWidth > 700) return;

  var preview = null;
  var activeThumb = null; // currently expanded thumbnail

  function showPreview(thumbWrap) {
    var img = thumbWrap.querySelector('.card-thumb');
    if (!img) return;
    activeThumb = thumbWrap;

    if (!preview) {
      preview = document.createElement('div');
      preview.className = 'mobile-thumb-preview';
      preview.innerHTML = '<div class="mobile-thumb-preview-img-wrap"><img></div><div class="mobile-thumb-preview-label"></div>';
    }

    var imgWrap = preview.querySelector('.mobile-thumb-preview-img-wrap');
    imgWrap.querySelector('img').src = img.src;
    imgWrap.querySelector('img').alt = img.alt;

    // Copy FOV SVG overlay if present and enabled
    var existingSvg = imgWrap.querySelector('svg');
    if (existingSvg) existingSvg.remove();
    var fovSvg = thumbWrap.querySelector('svg');
    if (fovSvg && showFovOverlay) {
      var clone = fovSvg.cloneNode(true);
      clone.style.display = '';
      imgWrap.appendChild(clone);
    }

    var label = thumbWrap.querySelector('.thumb-label');
    preview.querySelector('.mobile-thumb-preview-label').textContent = label ? label.textContent : '';

    var card = thumbsContainer.closest('.session-card');
    if (!card) return;
    if (!preview.parentNode) card.appendChild(preview);

    var cardRect = card.getBoundingClientRect();
    var thumbRect = thumbWrap.getBoundingClientRect();
    var thumbsRect = thumbsContainer.getBoundingClientRect();

    var centerX = thumbRect.left + thumbRect.width / 2 - cardRect.left;
    var previewW = 200;
    centerX = Math.max(previewW / 2 + 4, Math.min(cardRect.width - previewW / 2 - 4, centerX));
    preview.style.left = centerX + 'px';

    // Position above or below thumbs depending on available space
    // Show preview, measure it, then decide placement
    preview.style.bottom = 'auto';
    preview.style.top = 'auto';
    preview.style.display = '';
    preview.style.visibility = 'hidden';
    var previewH = preview.offsetHeight;
    preview.style.visibility = '';

    // Account for sticky header when measuring available space
    var headerEl = document.querySelector('header');
    var headerBottom = headerEl ? headerEl.getBoundingClientRect().bottom : 0;
    var spaceAbove = thumbsRect.top - headerBottom;
    var spaceBelow = window.innerHeight - thumbsRect.bottom;

    if (spaceAbove >= previewH + 10) {
      // Show above
      preview.style.bottom = (cardRect.bottom - thumbsRect.top + 6) + 'px';
      preview.style.top = 'auto';
    } else {
      // Show below
      preview.style.top = (thumbsRect.bottom - cardRect.top + 6) + 'px';
      preview.style.bottom = 'auto';
    }
  }

  function hidePreview() {
    if (preview) preview.style.display = 'none';
    activeThumb = null;
  }

  // Track touch movement to distinguish taps from scroll drags
  var touchStartX = 0, touchStartY = 0, touchMoved = false;
  thumbsContainer.addEventListener('touchstart', function(e) {
    var t = e.touches[0];
    touchStartX = t.clientX;
    touchStartY = t.clientY;
    touchMoved = false;
  }, { passive: true });
  thumbsContainer.addEventListener('touchmove', function(e) {
    var t = e.touches[0];
    if (Math.abs(t.clientX - touchStartX) > 8 || Math.abs(t.clientY - touchStartY) > 8) {
      touchMoved = true;
    }
  }, { passive: true });

  // Use touchend so zoom fires on the very first tap (bypasses iOS sticky hover)
  thumbsContainer.addEventListener('touchend', function(e) {
    if (touchMoved) return; // was a scroll, not a tap
    var thumbWrap = e.target.closest('.card-thumb-wrap');
    if (!thumbWrap) return;
    e.preventDefault(); // prevent the delayed click from firing card navigation

    if (activeThumb === thumbWrap) {
      hidePreview();
    } else {
      showPreview(thumbWrap);
    }
  });

  // Block click on thumbnails from navigating to report (covers any remaining click events)
  thumbsContainer.addEventListener('click', function(e) {
    if (e.target.closest('.card-thumb-wrap')) {
      e.stopPropagation();
    }
  });

  // Dismiss when tapping outside the thumbs row
  document.addEventListener('touchend', function(e) {
    if (!activeThumb) return;
    if (!thumbsContainer.contains(e.target) && !(preview && preview.contains(e.target))) {
      hidePreview();
    }
  });
}

// ── Animated curve drawing on scroll ──────────────────────────────────────

function setupCurveAnimation(container) {
  var svg = container.querySelector('svg');
  if (!svg) return;

  // Find all visible target polylines (colored, not transparent)
  var polylines = [];
  svg.querySelectorAll('polyline').forEach(function(p) {
    var stroke = p.getAttribute('stroke');
    if (stroke && stroke !== 'transparent' && stroke !== '#c0c0c0') {
      polylines.push(p);
    }
  });
  if (polylines.length === 0) return;

  // Cache lengths and set initial hidden state
  var lengths = polylines.map(function(p) { return p.getTotalLength(); });
  polylines.forEach(function(p, i) {
    p.style.strokeDasharray = lengths[i];
    p.style.strokeDashoffset = lengths[i];
  });

  // Animate in once when first scrolled into view — never reset, never replay
  var observer = new IntersectionObserver(function(entries) {
    entries.forEach(function(entry) {
      if (!entry.isIntersecting) return;
      polylines.forEach(function(p) {
        p.style.transition = 'stroke-dashoffset 0.5s ease-out';
        p.style.strokeDashoffset = '0';
      });
      observer.disconnect();
    });
  }, { threshold: 0.3 });

  observer.observe(container);
}

function fixChartTextDistortion(container) {
  var svg = container.querySelector('svg');
  if (!svg) return;
  requestAnimationFrame(function() {
    var ctm = svg.getScreenCTM();
    if (!ctm || ctm.a === 0) return;
    var ratio = ctm.d / ctm.a; // yScale / xScale
    if (Math.abs(ratio - 1) < 0.02) return; // Already uniform, skip
    svg.querySelectorAll('text').forEach(function(t) {
      var x = parseFloat(t.getAttribute('x') || '0');
      var y = parseFloat(t.getAttribute('y') || '0');
      t.setAttribute('transform',
        'translate(' + x + ',' + y + ') scale(' + ratio.toFixed(4) + ',1) translate(' + (-x) + ',' + (-y) + ')');
    });
  });
}

function renderAltitudeChart(s, data) {
  var el = document.getElementById('altitude-' + s.sessionId);
  if (!el) return;
  var legendHtml = '';
  if (data.legend && data.legend.length > 0) {
    legendHtml = '<div class="chart-legend">' + data.legend.map(function(l) {
      return '<div class="chart-legend-item">' +
        '<span class="chart-legend-swatch" style="background:' + l.color + '"></span>' +
        '<span style="color:' + l.color + '">' + esc(l.name) + '</span></div>';
    }).join('') + '</div>';
  }
  el.innerHTML = legendHtml + '<div class="chart-svg-wrap">' + data.svg + '</div>';
  setupCurveAnimation(el);
  setupChartCrosshair(el);
  fixChartTextDistortion(el);
  // Dynamically extend chart upward based on header height
  var card = el.closest('.session-card');
  var header = card ? card.querySelector('.card-header') : null;
  if (header) {
    var headerH = header.offsetHeight;
    var headerMargin = 4;
    var cardPadTop = 8;
    var lastChild = header.lastElementChild;
    var textRight = lastChild ? lastChild.getBoundingClientRect().right : 0;
    var svgWrap = el.querySelector('.chart-svg-wrap');
    var chartLeft = svgWrap ? svgWrap.getBoundingClientRect().left : el.getBoundingClientRect().left;
    var clearance = (textRight > chartLeft - 15) ? 18 : 0;
    var pullUp = Math.max(0, headerH + headerMargin - cardPadTop - clearance);
    el.style.marginTop = '-' + pullUp + 'px';
  }
  var body = el.parentElement;
  if (body) body.classList.add('has-chart');
}

function fetchAltitudeChart(s) {
  if (altitudeChartCache[s.sessionId]) {
    renderAltitudeChart(s, altitudeChartCache[s.sessionId]);
    return Promise.resolve();
  }
  if (altitudeChartFetching[s.sessionId]) return Promise.resolve(); // already in flight
  altitudeChartFetching[s.sessionId] = true;
  return api('/api/sessions/' + s.sessionId + '/altitude-chart').then(function(data) {
    delete altitudeChartFetching[s.sessionId];
    if (!data || !data.svg) return;
    altitudeChartCache[s.sessionId] = data;
    renderAltitudeChart(s, data);
  }).catch(function(err) {
    delete altitudeChartFetching[s.sessionId];
    logDebug('Altitude chart load failed for', s.sessionId, err.message);
  });
}

function loadAltitudeCharts(sessions) {
  var visible = [];
  var offscreen = [];
  sessions.forEach(function(s) {
    if (!s.hasReport) return;
    // Skip DOM lookup entirely if already cached — just re-render directly
    if (altitudeChartCache[s.sessionId]) {
      renderAltitudeChart(s, altitudeChartCache[s.sessionId]);
      return;
    }
    var el = document.getElementById('altitude-' + s.sessionId);
    if (!el) return;
    var rect = el.getBoundingClientRect();
    if (rect.top < window.innerHeight + 100) {
      visible.push(s);
    } else {
      offscreen.push(s);
    }
  });
  // Load visible charts immediately; offscreen after a short head start
  var promises = visible.map(fetchAltitudeChart);
  offscreen.forEach(function(s) {
    promises.push(new Promise(function(resolve) {
      setTimeout(function() { fetchAltitudeChart(s).then(resolve, resolve); }, 150);
    }));
  });
  return promises;
}

function setupAltitudeObserver(sessions) {
  if (altitudeObserver) {
    altitudeObserver.disconnect();
    altitudeObserver = null;
  }
  var unloaded = sessions.filter(function(s) {
    return s.hasReport && !altitudeChartCache[s.sessionId];
  });
  if (unloaded.length === 0) return;

  altitudeObserver = new IntersectionObserver(function(entries) {
    entries.forEach(function(entry) {
      if (!entry.isIntersecting) return;
      var id = entry.target.dataset.altId;
      var s = unloaded.find(function(x) { return x.sessionId === id; });
      if (s) {
        fetchAltitudeChart(s);
        altitudeObserver.unobserve(entry.target);
      }
    });
  }, { rootMargin: '200px' });

  unloaded.forEach(function(s) {
    var el = document.getElementById('altitude-' + s.sessionId);
    if (el) {
      el.dataset.altId = s.sessionId;
      altitudeObserver.observe(el);
    }
  });
}

function applyTargetSearch(query) {
  var q = query.toLowerCase();
  document.querySelectorAll('.target-pill-list .target-check').forEach(function(label) {
    var name = label.querySelector('span').textContent.toLowerCase();
    label.style.display = (!q || name.indexOf(q) !== -1) ? '' : 'none';
  });
}

function bindListEvents() {
  var fromEl = document.getElementById('filter-from');
  var toEl = document.getElementById('filter-to');
  var clearEl = document.getElementById('filter-clear');
  var allBtn = document.getElementById('targets-all');
  var noneBtn = document.getElementById('targets-none');

  function getFilters() {
    return {
      from: fromEl ? fromEl.value : '',
      to: toEl ? toEl.value : '',
      sort: currentSort
    };
  }

  function refresh() {
    var f = getFilters();
    var el = document.getElementById('content');
    var sub = document.getElementById('page-subtitle');
    doRenderList(el, sub, f.from, f.to, f.sort);
  }

  // Target dropdown toggle
  var dropBtn = document.getElementById('target-dropdown-btn');
  var dropMenu = document.getElementById('target-dropdown-menu');
  if (dropBtn && dropMenu) {
    // Restore open state and search after re-render
    if (dropdownOpen) {
      dropMenu.classList.add('open');
      if (targetSearch) applyTargetSearch(targetSearch);
    }
    dropBtn.addEventListener('click', function(e) {
      e.stopPropagation();
      dropdownOpen = !dropdownOpen;
      dropMenu.classList.toggle('open');
    });
    // Close on click outside — also clear search
    document.addEventListener('click', function closeDropdown(e) {
      var dropdown = document.getElementById('target-dropdown');
      if (dropdown && !dropdown.contains(e.target)) {
        dropdownOpen = false;
        targetSearch = '';
        dropMenu.classList.remove('open');
      }
    });
    // Prevent menu clicks from closing
    dropMenu.addEventListener('click', function(e) { e.stopPropagation(); });

    // Search input — filter pills in-place, no API call
    var searchEl = dropMenu.querySelector('.target-search');
    if (searchEl) {
      searchEl.addEventListener('input', function() {
        targetSearch = this.value;
        applyTargetSearch(targetSearch);
      });
    }
  }

  // Date picker — click anywhere on wrapper opens the picker
  if (fromEl) {
    fromEl.addEventListener('change', refresh);
    fromEl.parentElement.addEventListener('click', function(e) {
      if (e.target.classList.contains('date-clear')) return;
      fromEl.showPicker && fromEl.showPicker();
    });
  }
  if (toEl) {
    toEl.addEventListener('change', refresh);
    toEl.parentElement.addEventListener('click', function(e) {
      if (e.target.classList.contains('date-clear')) return;
      toEl.showPicker && toEl.showPicker();
    });
  }
  // Sort dropdown
  var sortBtn = document.getElementById('sort-dropdown-btn');
  var sortMenu = document.getElementById('sort-dropdown-menu');
  if (sortBtn && sortMenu) {
    if (sortDropdownOpen) sortMenu.classList.add('open');
    sortBtn.addEventListener('click', function(e) {
      e.stopPropagation();
      sortDropdownOpen = !sortDropdownOpen;
      sortMenu.classList.toggle('open');
    });
    document.addEventListener('click', function closeSortDropdown(e) {
      var dropdown = document.getElementById('sort-dropdown');
      if (dropdown && !dropdown.contains(e.target)) {
        sortDropdownOpen = false;
        sortMenu.classList.remove('open');
        document.removeEventListener('click', closeSortDropdown);
      }
    });
    sortMenu.addEventListener('click', function(e) { e.stopPropagation(); });
    sortMenu.querySelectorAll('.sort-option').forEach(function(btn) {
      btn.addEventListener('click', function() {
        currentSort = this.dataset.sort;
        localStorage.setItem('ns-sort', currentSort);
        sortDropdownOpen = false;
        refresh();
      });
    });
  }

  // Clear (×) buttons on date inputs
  document.querySelectorAll('.date-clear').forEach(function(btn) {
    btn.addEventListener('click', function() {
      var input = document.getElementById(btn.dataset.target);
      if (input) {
        input.value = '';
      }
      refresh();
    });
  });

  // Show empty sessions checkbox
  var emptyEl = document.getElementById('filter-empty');
  if (emptyEl) {
    emptyEl.addEventListener('change', function() {
      showEmptySessions = this.checked;
      refresh();
    });
  }

  // Show FOV overlay checkbox — toggle visibility without re-render
  var fovEl = document.getElementById('filter-fov');
  if (fovEl) {
    fovEl.addEventListener('change', function() {
      showFovOverlay = this.checked;
      localStorage.setItem('ns-show-fov', showFovOverlay ? 'true' : 'false');
      document.querySelectorAll('.mosaic-fov-svg, .card-thumb-wrap svg, .target-card-thumb svg, .tdp-hero-wrap svg').forEach(function(svg) {
        svg.style.display = showFovOverlay ? '' : 'none';
      });
    });
  }

  // Show altitude chart checkbox — toggle visibility without re-render
  var altEl = document.getElementById('filter-altitude');
  if (altEl) {
    altEl.addEventListener('change', function() {
      showAltitude = this.checked;
      localStorage.setItem('ns-show-altitude', showAltitude ? 'true' : 'false');
      document.querySelectorAll('.card-altitude').forEach(function(el) {
        el.style.display = showAltitude ? '' : 'none';
      });
    });
  }

  // Show hidden / unhide all
  var hiddenEl = document.getElementById('filter-hidden');
  var unhideEl = document.getElementById('unhide-all');
  if (hiddenEl) {
    hiddenEl.addEventListener('change', function() {
      showHidden = this.checked;
      refresh();
    });
  }
  if (unhideEl) {
    unhideEl.addEventListener('click', function() {
      hiddenSessions = {};
      showHidden = false;
      localStorage.setItem('ns-hidden-sessions', '{}');
      refresh();
    });
  }

  if (clearEl) {
    clearEl.addEventListener('click', function() {
      getAllTargets().forEach(function(t) { selectedTargets[t] = true; });
      showEmptySessions = false;
      showHidden = false;
      var el = document.getElementById('content');
      var sub = document.getElementById('page-subtitle');
      doRenderList(el, sub, '', '', 'date-desc');
    });
  }

  // Target checkboxes
  document.querySelectorAll('.target-check input[data-target]').forEach(function(cb) {
    cb.addEventListener('change', function() {
      selectedTargets[this.dataset.target] = this.checked;
      refresh();
    });
  });

  if (allBtn) {
    allBtn.addEventListener('click', function() {
      getAllTargets().forEach(function(t) { selectedTargets[t] = true; });
      refresh();
    });
  }

  if (noneBtn) {
    noneBtn.addEventListener('click', function() {
      getAllTargets().forEach(function(t) { selectedTargets[t] = false; });
      refresh();
    });
  }

  // View mode toggle
  document.querySelectorAll('.view-toggle-btn').forEach(function(btn) {
    btn.addEventListener('click', function() {
      if (cardViewMode === this.dataset.view) return;
      cardViewMode = this.dataset.view;
      localStorage.setItem('ns-card-view', cardViewMode);
      var toggle = this.closest('.view-toggle');
      toggle.classList.toggle('is-compact', cardViewMode === 'compact');
      toggle.classList.toggle('is-expanded', cardViewMode === 'expanded');
      toggle.querySelectorAll('.view-toggle-btn').forEach(function(b) {
        b.classList.toggle('active', b.dataset.view === cardViewMode);
      });
      setTimeout(refresh, 230);
    });
  });

  // On mobile, move the view toggle into the header area
  repositionViewToggle();
}

function repositionViewToggle() {
  var toggles = document.querySelectorAll('.view-toggle');
  if (toggles.length === 0) return;
  var headerRight = document.querySelector('.header-right');
  var filterBar = document.querySelector('.filter-bar');
  // Pick the freshest toggle (last in DOM, just rendered in filter bar)
  var keep = toggles[toggles.length - 1];
  var onSessionsPage = !location.hash || location.hash === '#/sessions' || location.hash.slice(1) === '/sessions';
  if (window.innerWidth <= 700) {
    if (headerRight && onSessionsPage) {
      headerRight.appendChild(keep);
      keep.style.display = '';
    } else if (headerRight) {
      // On non-session pages, hide it from header
      if (keep.parentNode === headerRight) keep.style.display = 'none';
    }
  } else {
    if (filterBar && keep.parentNode !== filterBar) {
      var clearBtn = document.getElementById('filter-clear');
      if (clearBtn && clearBtn.nextSibling) {
        filterBar.insertBefore(keep, clearBtn.nextSibling);
      } else {
        filterBar.appendChild(keep);
      }
    }
    keep.style.display = '';
  }
  // Remove any duplicates
  for (var i = 0; i < toggles.length; i++) {
    if (toggles[i] !== keep) toggles[i].remove();
  }
}

// ── Session Detail Page (Report-First) ────────────────────────────────────

var currentSettings = null;

// X-axis options: Time, Frame Index, then all metrics (matches plugin's ChartXAxisMetric index)
var XAXIS_OPTIONS = [
  'Time', 'Frame Index', 'HFR', 'FWHM', 'Guiding RMS', 'Focuser Temp',
  'Ambient Temp', 'Eccentricity', 'Altitude', 'Airmass', 'Humidity',
  'Focuser Position', 'Sky Quality', 'Cloud Cover', 'Camera Temp',
  'Dew Point', 'Wind Speed', 'Pressure', 'Star Count', 'Azimuth', 'Seeing FWHM'
];

// Primary/secondary options: metrics only, no Time/Frame Index
// (matches plugin's ChartPrimaryMetric / ChartSecondaryMetric index)
var PRIMARY_OPTIONS = [
  'HFR', 'FWHM', 'Guiding RMS', 'Focuser Temp',
  'Ambient Temp', 'Eccentricity', 'Altitude', 'Airmass', 'Humidity',
  'Focuser Position', 'Sky Quality', 'Cloud Cover', 'Camera Temp',
  'Dew Point', 'Wind Speed', 'Pressure', 'Star Count', 'Azimuth', 'Seeing FWHM'
];

var EQUIPMENT_FIELDS = [
  'Camera', 'Telescope', 'Mount', 'Filter Wheel', 'Focuser', 'Rotator',
  'Guider', 'Dome', 'Flat Panel', 'Safety Monitor', 'Weather', 'Switch'
];

var CLASSIFICATION_OPTIONS = ['Auto', 'Broadband', 'Narrowband', 'Exclude'];
var CLASSIFICATION_CODES = ['A', 'B', 'N', 'X'];

var cachedFilters = null;

function buildSelect(idOrClass, isClass, options, value, includeNone) {
  var attr = isClass ? 'class="' + idOrClass + ' settings-select"' : 'id="' + idOrClass + '" class="settings-select"';
  var html = '<select ' + attr + '>';
  if (includeNone) html += '<option value="0"' + (value === 0 ? ' selected' : '') + '>None</option>';
  options.forEach(function(m, i) {
    var val = includeNone ? i + 1 : i;
    html += '<option value="' + val + '"' + (value === val ? ' selected' : '') + '>' + esc(m) + '</option>';
  });
  html += '</select>';
  return html;
}

function xAxisSelect(id, value) { return buildSelect(id, false, XAXIS_OPTIONS, value, false); }
function primarySelect(id, value) { return buildSelect(id, false, PRIMARY_OPTIONS, value, false); }
function secondarySelect(id, value) { return buildSelect(id, false, PRIMARY_OPTIONS, value, true); }

function xAxisSelectClass(cls, value) { return buildSelect(cls, true, XAXIS_OPTIONS, value, false); }
function primarySelectClass(cls, value) { return buildSelect(cls, true, PRIMARY_OPTIONS, value, false); }
function secondarySelectClass(cls, value) { return buildSelect(cls, true, PRIMARY_OPTIONS, value, true); }

function settingsCheckbox(id, label, checked) {
  return '<label class="settings-check"><input type="checkbox" id="' + id + '"' +
    (checked ? ' checked' : '') + '><span>' + esc(label) + '</span></label>';
}

function parseChartConfigs(raw) {
  if (!raw) return [];
  return raw.split('|').map(function(part) {
    var t = part.split(':');
    return { primary: parseInt(t[0]) || 0, secondary: parseInt(t[1]) || 0, xAxis: parseInt(t[2]) || 0 };
  }).filter(function(c) { return !isNaN(c.primary); });
}

function parseFilterClassifications(raw) {
  var result = {};
  if (!raw) return result;
  raw.split(',').forEach(function(pair) {
    var parts = pair.split('=');
    if (parts.length === 2) result[parts[0].trim()] = parts[1].trim();
  });
  return result;
}

function parseEquipmentOverrides(raw) {
  var result = {};
  if (!raw) return result;
  raw.split(',').forEach(function(pair) {
    var parts = pair.split(':');
    if (parts.length >= 2) result[parts[0].trim()] = parts.slice(1).join(':').trim();
  });
  return result;
}

function buildSettingsPanel(settings, filters) {
  var s = settings;
  var visibleFields = (s.equipmentVisibleFields || '').split(',').map(function(f) { return f.trim(); });
  var additionalCharts = parseChartConfigs(s.additionalChartConfigs);
  var filterClass = parseFilterClassifications(s.filterClassifications);
  var filterTypes = parseFilterClassifications(s.filterTypeOverrides || '');
  var eqOverrides = parseEquipmentOverrides(s.equipmentOverrides);

  var html = '<div id="settings-panel" class="settings-panel" style="display:none">';

  // Row 1: Detail level + theme
  html += '<div class="settings-row">' +
    '<div class="settings-group"><label class="settings-label">Detail Level</label>' +
      '<select id="s-detailLevel" class="settings-select">' +
        '<option value="0"' + (s.reportDetailLevel === 0 ? ' selected' : '') + '>Snapshot</option>' +
        '<option value="1"' + (s.reportDetailLevel === 1 ? ' selected' : '') + '>Standard</option>' +
        '<option value="2"' + (s.reportDetailLevel === 2 ? ' selected' : '') + '>Full</option>' +
      '</select></div>' +
    '<div class="settings-group"><label class="settings-label">Theme</label>' +
      '<select id="s-lightMode" class="settings-select">' +
        '<option value="false"' + (!s.reportLightMode ? ' selected' : '') + '>Dark</option>' +
        '<option value="true"' + (s.reportLightMode ? ' selected' : '') + '>Light</option>' +
      '</select></div>' +
  '</div>';

  // Row 2: Section toggles
  html += '<div class="settings-row"><div class="settings-group"><label class="settings-label">Sections</label>' +
    '<div class="settings-checks">' +
      settingsCheckbox('s-overhead', 'Overhead Breakdown', s.showOverheadBreakdown) +
      settingsCheckbox('s-skyThumb', 'Sky Thumbnails', s.showSkyThumbnails) +
      settingsCheckbox('s-altitude', 'Altitude Chart', s.showAltitudeChart) +
      settingsCheckbox('s-moon', 'Moon Curve', s.showMoonCurve) +
      settingsCheckbox('s-minAlt', 'Min Altitude', s.showMinAltitude) +
      settingsCheckbox('s-livestack', 'Live Stack Images', s.showLiveStackImages) +
      settingsCheckbox('s-history', 'Session History', s.showSessionHistory) +
      settingsCheckbox('s-tsProgress', 'TS Progress Bars', s.showTSProgressBars) +
      settingsCheckbox('s-starCV', 'Star Count CV', s.showStarCountCV) +
      settingsCheckbox('s-hfr', 'Metric Chart', s.showHFRGraph) +
      settingsCheckbox('s-afMarkers', 'AF Markers', s.showChartAfMarkers) +
      settingsCheckbox('s-flipMarkers', 'Flip Markers', s.showChartFlipMarkers) +
      settingsCheckbox('s-roofMarkers', 'Roof Markers', s.showChartRoofMarkers) +
      settingsCheckbox('s-perTargetIQ', 'Per-Target IQ', s.showPerTargetIQ) +
      settingsCheckbox('s-equipment', 'Equipment Profile', s.showEquipmentProfile) +
      settingsCheckbox('s-expand', 'Expand Sections', s.expandSectionsDefault) +
    '</div></div></div>';

  // Row 3: Charts (main + additional in aligned grid)
  html += '<div class="settings-row"><div class="settings-group" style="width:100%">' +
    '<label class="settings-label">Charts</label>' +
    '<div class="chart-grid">' +
      '<div class="chart-grid-headers">' +
        '<span></span><span>X-Axis</span><span>Primary</span><span>Secondary</span><span></span>' +
      '</div>' +
      '<div class="chart-row">' +
        '<span class="chart-row-label">Chart 1</span>' +
        xAxisSelect('s-xAxis', s.chartXAxisMetric) +
        primarySelect('s-primary', s.chartPrimaryMetric) +
        secondarySelect('s-secondary', s.chartSecondaryMetric) +
        '<span></span>' +
      '</div>';
  html += '<div id="additional-charts">';
  additionalCharts.forEach(function(c, i) {
    html += '<div class="chart-row" data-idx="' + i + '">' +
      '<span class="chart-row-label">Chart ' + (i + 2) + '</span>' +
      xAxisSelectClass('ac-xAxis', c.xAxis) +
      primarySelectClass('ac-primary', c.primary) +
      secondarySelectClass('ac-secondary', c.secondary) +
      '<button class="remove-chart-btn" data-idx="' + i + '">\u2715</button>' +
    '</div>';
  });
  html += '</div>' +
    '<button id="btn-add-chart" class="filter-link" style="margin-top:6px">+ Add Chart</button>' +
    '</div></div></div>';

  // Row 5: Filter classifications + types
  if (filters && filters.length > 0) {
    var TYPE_OPTIONS = ['Auto', 'L', 'R', 'G', 'B', 'H', 'S', 'O'];
    var TYPE_CODES   = ['A',    'L', 'R', 'G', 'B', 'H', 'S', 'O'];
    html += '<div class="settings-row"><div class="settings-group">' +
      '<label class="settings-label">Filter Classifications &amp; Types</label>' +
      '<div class="filter-class-headers">' +
        '<span class="filter-class-name"></span>' +
        '<span class="filter-class-col-hdr">Classification</span>' +
        '<span class="filter-class-col-hdr">Type</span>' +
      '</div>' +
      '<div class="filter-class-grid">';
    filters.forEach(function(f) {
      var code    = filterClass[f] || 'A';
      var clsIdx  = CLASSIFICATION_CODES.indexOf(code); if (clsIdx < 0) clsIdx = 0;
      var typeCode = filterTypes[f] || 'A';
      var typeIdx  = TYPE_CODES.indexOf(typeCode); if (typeIdx < 0) typeIdx = 0;
      html += '<div class="filter-class-row">' +
        '<span class="filter-class-name">' + esc(f) + '</span>' +
        '<select class="fc-select settings-select" data-filter="' + esc(f) + '">';
      CLASSIFICATION_OPTIONS.forEach(function(opt, oi) {
        html += '<option value="' + CLASSIFICATION_CODES[oi] + '"' + (oi === clsIdx ? ' selected' : '') + '>' + esc(opt) + '</option>';
      });
      html += '</select>' +
        '<select class="ft-select settings-select" data-filter="' + esc(f) + '">';
      TYPE_OPTIONS.forEach(function(opt, ti) {
        html += '<option value="' + TYPE_CODES[ti] + '"' + (ti === typeIdx ? ' selected' : '') + '>' + esc(opt) + '</option>';
      });
      html += '</select></div>';
    });
    html += '</div></div></div>';
  }

  // Row 6: Equipment (checkbox + override per field)
  html += '<div class="settings-row"><div class="settings-group" style="width:100%">' +
    '<label class="settings-label">Equipment</label>' +
    '<div class="equipment-grid">';
  EQUIPMENT_FIELDS.forEach(function(f) {
    var visible = visibleFields.indexOf(f) >= 0;
    var override = eqOverrides[f] || '';
    var fid = f.replace(/\s/g, '');
    html += '<div class="equipment-row">' +
      '<label class="settings-check"><input type="checkbox" id="s-eq-' + fid + '"' + (visible ? ' checked' : '') + '>' +
        '<span>' + esc(f) + '</span></label>' +
      '<input type="text" class="eq-override settings-input-sm" data-field="' + esc(f) + '" value="' + esc(override) + '" placeholder="Override name">' +
    '</div>';
  });
  html += '</div></div></div>';

  // Regenerate buttons
  html += '<div class="settings-actions">' +
    '<button id="btn-regenerate" class="report-btn regen-btn">Regenerate Report</button>' +
    '<button id="btn-regenerate-all" class="report-btn regen-all-btn">Regenerate All Reports</button>' +
    '<span id="regen-status" class="regen-status"></span>' +
  '</div>';

  html += '</div>';
  return html;
}

function collectSettings() {
  var visibleFields = [];
  EQUIPMENT_FIELDS.forEach(function(f) {
    var cb = document.getElementById('s-eq-' + f.replace(/\s/g, ''));
    if (cb && cb.checked) visibleFields.push(f);
  });

  // Collect additional charts (only from the additional-charts container, not Chart 1)
  var chartRows = document.querySelectorAll('#additional-charts .chart-row');
  var additionalParts = [];
  chartRows.forEach(function(row) {
    var selects = row.querySelectorAll('select');
    if (selects.length >= 3) {
      additionalParts.push(selects[1].value + ':' + selects[2].value + ':' + selects[0].value);
    }
  });

  // Collect filter classifications (only non-Auto)
  var fcParts = [];
  document.querySelectorAll('.fc-select').forEach(function(sel) {
    if (sel.value !== 'A') fcParts.push(sel.dataset.filter + '=' + sel.value);
  });

  // Collect filter type overrides (only non-Auto)
  var ftParts = [];
  document.querySelectorAll('.ft-select').forEach(function(sel) {
    if (sel.value !== 'A') ftParts.push(sel.dataset.filter + '=' + sel.value);
  });

  // Collect equipment overrides (only non-empty)
  var eqParts = [];
  document.querySelectorAll('.eq-override').forEach(function(inp) {
    if (inp.value.trim()) {
      eqParts.push(inp.dataset.field + ':' + inp.value.trim());
    }
  });

  return {
    reportDetailLevel:     parseInt(document.getElementById('s-detailLevel').value),
    reportLightMode:       document.getElementById('s-lightMode').value === 'true',
    expandSectionsDefault: document.getElementById('s-expand').checked,
    showOverheadBreakdown: document.getElementById('s-overhead').checked,
    showSkyThumbnails:     document.getElementById('s-skyThumb').checked,
    showAltitudeChart:     document.getElementById('s-altitude').checked,
    showMoonCurve:         document.getElementById('s-moon').checked,
    showMinAltitude:       document.getElementById('s-minAlt').checked,
    showLiveStackImages:   document.getElementById('s-livestack').checked,
    showSessionHistory:    document.getElementById('s-history').checked,
    showTSProgressBars:    document.getElementById('s-tsProgress').checked,
    showStarCountCV:       document.getElementById('s-starCV').checked,
    showHFRGraph:          document.getElementById('s-hfr').checked,
    showChartAfMarkers:    document.getElementById('s-afMarkers').checked,
    showChartFlipMarkers:  document.getElementById('s-flipMarkers').checked,
    showChartRoofMarkers:  document.getElementById('s-roofMarkers').checked,
    showPerTargetIQ:       document.getElementById('s-perTargetIQ').checked,
    showEquipmentProfile:  document.getElementById('s-equipment').checked,
    chartXAxisMetric:      parseInt(document.getElementById('s-xAxis').value),
    chartPrimaryMetric:    parseInt(document.getElementById('s-primary').value),
    chartSecondaryMetric:  parseInt(document.getElementById('s-secondary').value),
    additionalChartConfigs: additionalParts.join('|'),
    equipmentVisibleFields: visibleFields.join(','),
    filterClassifications: fcParts.join(','),
    filterTypeOverrides:   ftParts.join(','),
    equipmentOverrides:    eqParts.join(',')
  };
}

function loadReportIntoShadow(sessionId) {
  var host = document.getElementById('report-shadow-host');
  if (!host) return;

  fetch('/api/sessions/' + sessionId + '/report')
    .then(function(r) { return r.text(); })
    .then(function(html) {
      // Render report at its designed width (800px) and CSS-scale to fit the
      // viewport — identical to how Safari renders it in a new tab (980px
      // default width scaled down). This preserves the report's layout exactly.
      var hostWidth = host.offsetWidth;
      var padding = 8;
      var designWidth = 800;
      var scale = Math.min((hostWidth - padding * 2) / designWidth, 1);

      var shadow = host.shadowRoot || host.attachShadow({ mode: 'open' });
      shadow.innerHTML = '';

      var wrapper = document.createElement('div');
      wrapper.style.cssText = 'width:' + designWidth + 'px;transform:scale(' + scale + ');transform-origin:top left;margin:0 ' + padding + 'px;';
      wrapper.innerHTML = html;
      shadow.appendChild(wrapper);

      // Set host height to match scaled content
      requestAnimationFrame(function() {
        host.style.height = (wrapper.offsetHeight * scale) + 'px';
      });

      logInfo('Report loaded into shadow DOM (scale=' + scale.toFixed(3) + '):', sessionId);
    })
    .catch(function(err) {
      logError('Failed to load report into shadow DOM:', err.message);
      host.innerHTML = '<div style="color:#f85149;padding:20px;">Failed to load report: ' + err.message + '</div>';
    });
}

function renderSessionDetail(sessionId) {
  var el = document.getElementById('content');
  var sub = document.getElementById('page-subtitle');

  el.innerHTML = '<div class="loading">Loading report...</div>';

  Promise.all([
    api('/api/sessions/' + sessionId),
    api('/api/sessions/' + sessionId + '/settings'),
    cachedFilters ? Promise.resolve({ filters: cachedFilters }) : api('/api/filters')
  ]).then(function(results) {
    var detail = results[0];
    currentSettings = results[1];
    cachedFilters = results[2].filters || [];
    logInfo('Session detail loaded:', sessionId);
    logDebug('Settings received:', JSON.stringify(currentSettings, null, 2));

    var targets = detail.targets.map(function(t) { return t.target; }).join(', ') || 'Unknown';
    sub.textContent = fmtDate(detail.sessionStart) + ' \u2014 ' + targets;

    var html = '<div class="report-nav">' +
      '<a class="back-btn" href="#/sessions">\u2190 Sessions</a>' +
      '<div class="report-nav-info">' +
        '<span class="report-nav-date">' + fmtDate(detail.sessionStart) + '</span>' +
        '<span class="report-nav-targets">' + esc(targets) + '</span>' +
      '</div>' +
      '<div class="report-nav-actions">' +
        '<button class="report-btn" id="btn-settings">\u2699 Settings</button>';

    if (detail.hasReport) {
      html += '<a href="/api/sessions/' + sessionId + '/report" target="_blank" class="report-btn">Open in New Tab \u2192</a>';
    }

    html += '</div></div>';

    html += buildSettingsPanel(currentSettings, cachedFilters);

    var isMobile = window.innerWidth <= 700;

    if (detail.hasReport) {
      if (isMobile) {
        // Mobile: use shadow DOM to render report inline — iframes don't
        // respect viewport constraints on mobile, causing squished content
        html += '<div class="report-viewer"><div id="report-shadow-host" class="report-shadow-host"></div></div>';
      } else {
        html += '<div class="report-viewer">' +
          '<iframe id="report-iframe" class="report-iframe" src="/api/sessions/' + sessionId + '/report" sandbox="allow-same-origin"></iframe>' +
        '</div>';
      }
    } else {
      html += '<div class="report-viewer">' +
        '<div class="empty">No report generated for this session. Click "Regenerate Report" to generate one.</div>' +
      '</div>';
    }

    el.innerHTML = html;

    if (detail.hasReport && isMobile) {
      loadReportIntoShadow(sessionId);
    }

    bindDetailEvents(sessionId);
  }).catch(function(err) {
    logError('Failed to load session detail:', sessionId, err.message);
    el.innerHTML = '<a class="back-btn" href="#/sessions">\u2190 Sessions</a>' +
      '<div class="error">Failed to load session: ' + esc(err.message) + '</div>';
  });
}

function bindDetailEvents(sessionId) {
  var settingsBtn = document.getElementById('btn-settings');
  var panel = document.getElementById('settings-panel');
  var regenBtn = document.getElementById('btn-regenerate');
  var regenAllBtn = document.getElementById('btn-regenerate-all');
  var status = document.getElementById('regen-status');

  if (settingsBtn && panel) {
    settingsBtn.addEventListener('click', function() {
      var visible = panel.style.display !== 'none';
      panel.style.display = visible ? 'none' : 'block';
    });
  }

  // Additional chart add/remove
  var addChartBtn = document.getElementById('btn-add-chart');
  if (addChartBtn) {
    addChartBtn.addEventListener('click', function() {
      var container = document.getElementById('additional-charts');
      var idx = container.querySelectorAll('.chart-row').length;
      var row = document.createElement('div');
      row.className = 'chart-row';
      row.dataset.idx = idx;
      row.innerHTML = '<span class="chart-row-label">Chart ' + (idx + 2) + '</span>' +
        xAxisSelectClass('ac-xAxis', 0) +
        primarySelectClass('ac-primary', 0) +
        secondarySelectClass('ac-secondary', 0) +
        '<button class="remove-chart-btn" style="justify-self:center">\u2715</button>';
      container.appendChild(row);
      row.querySelector('.remove-chart-btn').addEventListener('click', function() {
        row.remove();
        renumberChartRows();
      });
    });
  }

  document.querySelectorAll('.remove-chart-btn').forEach(function(btn) {
    btn.addEventListener('click', function() {
      this.closest('.chart-row').remove();
      renumberChartRows();
    });
  });

  if (regenBtn) {
    regenBtn.addEventListener('click', function() {
      var settings = collectSettings();
      logInfo('Regenerate report:', sessionId);
      logDebug('Settings sent for regeneration:', JSON.stringify(settings, null, 2));
      status.textContent = 'Generating...';
      status.className = 'regen-status';
      regenBtn.disabled = true;
      var regenStart = performance.now();

      fetch('/api/sessions/' + sessionId + '/regenerate', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(settings)
      }).then(function(r) { return r.json(); }).then(function(data) {
        if (data.status === 'ok') {
          logInfo('Regenerate complete:', sessionId, '(' + Math.round(performance.now() - regenStart) + 'ms)');
          status.textContent = 'Done';
          status.className = 'regen-status regen-ok';
          // Reload report — iframe on desktop, shadow DOM on mobile
          var iframe = document.getElementById('report-iframe');
          var shadowHost = document.getElementById('report-shadow-host');
          if (iframe) {
            iframe.src = '/api/sessions/' + sessionId + '/report?t=' + Date.now();
          } else if (shadowHost) {
            loadReportIntoShadow(sessionId);
          } else {
            // Report didn't exist before — re-render the whole page
            sessionsCache = []; initialLoadDone = false; // Clear cache to refresh hasReport
            renderSessionDetail(sessionId);
          }
        } else {
          logError('Regenerate failed:', sessionId, data.error);
          status.textContent = data.error || 'Failed';
          status.className = 'regen-status regen-err';
        }
      }).catch(function(err) {
        logError('Regenerate error:', sessionId, err.message);
        status.textContent = err.message;
        status.className = 'regen-status regen-err';
      }).finally(function() {
        regenBtn.disabled = false;
      });
    });
  }

  if (regenAllBtn) {
    regenAllBtn.addEventListener('click', function() {
      if (!confirm('This will regenerate ALL session reports with the current settings, overwriting any existing reports.\n\nThis may take a while for many sessions. Continue?')) {
        logInfo('Regenerate-all cancelled by user');
        return;
      }
      logInfo('Regenerate-all started');
      var settings = collectSettings();
      status.textContent = 'Regenerating all...';
      status.className = 'regen-status';
      regenAllBtn.disabled = true;
      if (regenBtn) regenBtn.disabled = true;

      fetch('/api/regenerate-all', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(settings)
      }).then(function(r) { return r.json(); }).then(function(data) {
        if (data.status === 'started') {
          logInfo('Regenerate-all accepted:', data.total, 'sessions');
          pollRegenAllProgress(sessionId, regenBtn, regenAllBtn, status);
        } else {
          logError('Regenerate-all rejected:', data.error);
          status.textContent = data.error || 'Failed to start';
          status.className = 'regen-status regen-err';
          regenAllBtn.disabled = false;
          if (regenBtn) regenBtn.disabled = false;
        }
      }).catch(function(err) {
        logError('Regenerate-all error:', err.message);
        status.textContent = err.message;
        status.className = 'regen-status regen-err';
        regenAllBtn.disabled = false;
        if (regenBtn) regenBtn.disabled = false;
      });
    });
  }
}

function renumberChartRows() {
  document.querySelectorAll('#additional-charts .chart-row').forEach(function(row, i) {
    var label = row.querySelector('.chart-row-label');
    if (label) label.textContent = 'Chart ' + (i + 2);
  });
}

function pollRegenAllProgress(sessionId, regenBtn, regenAllBtn, statusEl) {
  var poll = setInterval(function() {
    fetch('/api/regenerate-all/status').then(function(r) { return r.json(); }).then(function(data) {
      if (data.status === 'running') {
        statusEl.textContent = 'Regenerating ' + data.current + '/' + data.total + '...';
        statusEl.className = 'regen-status';
      } else if (data.status === 'done') {
        clearInterval(poll);
        logInfo('Regenerate-all complete:', data.generated, 'generated,', data.failed, 'failed');
        statusEl.textContent = 'Done \u2014 ' + data.generated + ' generated' + (data.failed > 0 ? ', ' + data.failed + ' failed' : '');
        statusEl.className = 'regen-status regen-ok';
        regenAllBtn.disabled = false;
        if (regenBtn) regenBtn.disabled = false;
        sessionsCache = []; initialLoadDone = false;
        var iframe = document.getElementById('report-iframe');
        if (iframe) iframe.src = iframe.src.split('?')[0] + '?t=' + Date.now();
      } else if (data.status === 'error') {
        clearInterval(poll);
        logError('Regenerate-all error:', data.error);
        statusEl.textContent = data.error || 'Failed';
        statusEl.className = 'regen-status regen-err';
        regenAllBtn.disabled = false;
        if (regenBtn) regenBtn.disabled = false;
      }
    }).catch(function() {
      // Network error during poll — keep trying
    });
  }, 1000);
}

// ── Stats Page ─────────────────────────────────────────────────────────────

var statsTargetData = null;
// Phase 3a: Target Scheduler integration state (populated by renderStats on each load)
var statsTsStatus   = null;   // "available" | "not_installed" | "error" | null
var statsTsError    = null;   // string or null
var statsTsProjects = null;   // array of { guid, name, state, isMosaic, isCustom, targetCount, targets: [{guid,name}] }
var statsProjectAssignments = null; // { "target name (lowercase)": "project-guid" }
var statsTargetExclusions  = null; // { "project-guid": ["target name (lowercase)", ...] }

function renderStatsTabContent(tabId) {
  var container = document.getElementById('stats-tab-content');
  if (!container) return;

  if (tabId === 'targets') {
    var targets = statsTargetData || [];
    if (targets.length === 0) {
      container.innerHTML = '<div class="empty">No target data available yet.</div>';
      return;
    }
    // Preserve scroll positions of sort/filter bars across re-render
    var prevSortScroll = 0, prevFilterScroll = 0;
    var prevSortBar = container.querySelector('.targets-sort-bar');
    var prevFilterRow = container.querySelector('.targets-filter-row');
    if (prevSortBar) prevSortScroll = prevSortBar.scrollLeft;
    if (prevFilterRow) prevFilterScroll = prevFilterRow.scrollLeft;

    var sortKey = getTargetSortKey();
    var groupBy = getTargetGroupBy();
    var tsAvail = statsTsStatus === 'available';
    var allTargets = statsTargetData || [];
    var html = renderTsStatusBanner();
    html += renderTargetsControlBar(sortKey, groupBy);
    if (groupBy === 'project' && tsAvail) {
      html += '<div class="targets-grouped">' + renderGroupedTargets(targets, sortKey) + '</div>';
    } else {
      var filtered = tsAvail ? filterTargets(targets) : targets;
      var sorted = sortTargets(filtered, sortKey);
      html += '<div class="target-grid">';
      sorted.forEach(function(t) { html += renderTargetCard(t, allTargets.indexOf(t)); });
      html += '</div>';
      if (filtered.length === 0 && targets.length > 0) {
        html += '<div class="empty" style="margin-top:40px">No targets match the current filter.</div>';
      }
    }
    container.innerHTML = html;
    // Restore sort/filter bar scroll positions
    var newSortBar = container.querySelector('.targets-sort-bar');
    var newFilterRow = container.querySelector('.targets-filter-row');
    if (newSortBar && prevSortScroll) newSortBar.scrollLeft = prevSortScroll;
    if (newFilterRow && prevFilterScroll) newFilterRow.scrollLeft = prevFilterScroll;

    loadTargetThumbnails();
    initTargetsControlBar();
    initTargetCardClicks();
    initTsBadgeClicks();
    if (groupBy === 'project' && tsAvail) initProjectContainers();
    requestAnimationFrame(fitTargetNameOverlays);
  } else if (tabId === 'tonight') {
    renderTonightTab(container);
  }

  // Manage Projects button: only relevant on Targets tab
  var manageBtn = document.querySelector('.targets-manage-projects-btn');
  if (manageBtn) manageBtn.style.display = (tabId === 'targets') ? '' : 'none';
}

// ── Tonight Tab ───────────────────────────────────────────────────────────

var TONIGHT_COLORS = ['#5b9cf6','#66c2a5','#fc8d62','#e78ac3','#a6d854','#ffd92f','#e5c494','#b3b3b3'];
var tonightPreviewCache = null;
var tonightPreviewCacheTime = 0;

function fmtDuration(totalSec) {
  var s = Math.round(totalSec);
  var h = Math.floor(s / 3600);
  var m = Math.floor((s % 3600) / 60);
  if (h > 0) return h + 'h ' + m + 'm';
  if (m > 0) return m + 'm';
  return s + 's';
}

function fmtTimeHHMM(d) {
  var h = d.getHours().toString().padStart(2, '0');
  var m = d.getMinutes().toString().padStart(2, '0');
  return h + ':' + m;
}

function renderTonightTab(container) {
  container.innerHTML = '<div class="tonight-loading"><div class="tonight-spinner"></div>Fetching tonight\'s schedule from Target Scheduler\u2026 (may take ~30s)</div>';

  // Cache for 5 minutes
  var now = Date.now();
  if (tonightPreviewCache && (now - tonightPreviewCacheTime) < 5 * 60 * 1000) {
    renderTonightContent(container, tonightPreviewCache);
    return;
  }

  fetch('/api/tonight/preview')
    .then(function(r) { return r.json(); })
    .then(function(data) {
      if (data.error) {
        container.innerHTML = '<div class="tonight-error">' + esc(data.error) +
          '<br><button class="tonight-retry-btn" onclick="tonightPreviewCache=null;renderTonightTab(this.closest(\'#stats-tab-content\'))">Retry</button></div>';
        return;
      }
      tonightPreviewCache = data;
      tonightPreviewCacheTime = Date.now();
      renderTonightContent(container, data);
    })
    .catch(function(err) {
      container.innerHTML = '<div class="tonight-error">Failed to load tonight\'s preview: ' + esc(err.message) +
        '<br><button class="tonight-retry-btn" onclick="tonightPreviewCache=null;renderTonightTab(this.closest(\'#stats-tab-content\'))">Retry</button></div>';
    });
}

function renderTonightContent(container, data) {
  var entries = data.entries || [];

  // Targets = non-wait-period entries with a name
  var targets = entries.filter(function(e) { return !e.waitPeriod && e.name; });
  if (!targets.length) {
    container.innerHTML = '<div class="tonight-error">No targets scheduled for tonight.</div>';
    return;
  }

  // Trim leading wait periods so the timeline starts at the first target block
  var firstTargetStart = new Date(targets[0].startTime);
  entries = entries.filter(function(e) { return new Date(e.endTime) > firstTargetStart; });

  var timelineStart = firstTargetStart;
  var timelineEnd   = new Date(entries[entries.length - 1].endTime);
  var totalMs       = timelineEnd - timelineStart;
  if (totalMs <= 0) {
    container.innerHTML = '<div class="tonight-error">Invalid timeline data.</div>';
    return;
  }

  // Assign colors to unique target names (in order of first appearance)
  var uniqueNames = [];
  targets.forEach(function(t) { if (uniqueNames.indexOf(t.name) === -1) uniqueNames.push(t.name); });
  var colorMap = {};
  uniqueNames.forEach(function(name, i) { colorMap[name] = TONIGHT_COLORS[i % TONIGHT_COLORS.length]; });

  // Date header
  var pd = new Date(targets[0].startTime);
  var dateStr = pd.toLocaleDateString(undefined, {month: 'long', day: 'numeric', year: 'numeric'});
  var startStr = fmtTimeHHMM(timelineStart);
  var endStr   = fmtTimeHHMM(timelineEnd);

  var html = '<div class="tonight-section">';
  html += '<div class="tonight-header">';
  html += '<h3 class="tonight-title">Tonight\'s Preview</h3>';
  html += '<p class="tonight-subtitle">Planned schedule for ' + dateStr + ' \u2014 ' + startStr + ' to ' + endStr + '</p>';
  html += '<p class="tonight-disclaimer">Generated by Target Scheduler \u2014 actual imaging may differ based on conditions</p>';
  html += '</div>';

  // Timeline SVG
  html += buildTonightTimeline(entries, targets, colorMap, uniqueNames, timelineStart, totalMs);

  // Altitude chart
  html += '<h4 class="tonight-section-heading">Altitude</h4>';
  var altSvg = buildTonightAltitudeChart(data, uniqueNames, colorMap, entries, timelineStart, totalMs);
  if (altSvg.indexOf('<svg') !== -1) {
    html += '<div class="tonight-altitude-wrap">' + altSvg + '</div>';
  } else {
    html += altSvg;
  }

  // Summary table
  html += '<table class="tonight-summary-table">';
  html += '<thead><tr><th>Target</th><th>Window</th><th>Images</th><th>Total Time</th></tr></thead>';
  html += '<tbody>';
  targets.forEach(function(t) {
    var totalFrames = (t.exposurePlan || []).reduce(function(s, ep) { return s + (ep.count || 0); }, 0);
    var totalSec    = (t.exposurePlan || []).reduce(function(s, ep) { return s + (ep.exposure || 0) * (ep.count || 0); }, 0);
    var tStart = fmtTimeHHMM(new Date(t.startTime));
    var tEnd   = fmtTimeHHMM(new Date(t.endTime));
    html += '<tr>';
    html += '<td><span class="tonight-color-dot" style="background:' + colorMap[t.name] + '"></span>' + esc(t.name) + '</td>';
    html += '<td>' + tStart + ' \u2013 ' + tEnd + '</td>';
    html += '<td>' + totalFrames + '</td>';
    html += '<td>' + fmtDuration(totalSec) + '</td>';
    html += '</tr>';
  });
  html += '</tbody></table>';

  // Collapsible filter breakdowns per target (aggregate across all timeline blocks)
  var targetGroups = {};
  targets.forEach(function(t) {
    if (!targetGroups[t.name]) targetGroups[t.name] = [];
    (t.exposurePlan || []).forEach(function(ep) { targetGroups[t.name].push(ep); });
  });

  uniqueNames.forEach(function(name) {
    var exposures = targetGroups[name] || [];
    if (!exposures.length) return;

    // Group by filterName + exposure length
    var groups = {};
    exposures.forEach(function(ep) {
      var key = (ep.filterName || 'Unknown') + '|' + (ep.exposure || 0);
      if (!groups[key]) groups[key] = {filterName: ep.filterName || 'Unknown', exposure: ep.exposure || 0, count: 0};
      groups[key].count += (ep.count || 0);
    });

    var sorted = Object.keys(groups).map(function(k) { return groups[k]; }).sort(function(a, b) {
      return a.filterName.localeCompare(b.filterName) || a.exposure - b.exposure;
    });

    html += '<details class="tonight-details">';
    html += '<summary><span class="tonight-color-dot" style="background:' + colorMap[name] + '"></span>' + esc(name) + ' \u2014 Filter Breakdown</summary>';
    html += '<table class="tonight-filter-table">';
    html += '<thead><tr><th>Filter</th><th>Images</th><th>Exposure</th><th>Total Time</th></tr></thead>';
    html += '<tbody>';
    sorted.forEach(function(g) {
      var intSec = g.exposure * g.count;
      html += '<tr><td>' + esc(g.filterName) + '</td><td>' + g.count + '</td><td>' + g.exposure.toFixed(0) + 's</td><td>' + fmtDuration(intSec) + '</td></tr>';
    });
    html += '</tbody></table>';
    html += '</details>';
  });

  html += '</div>';
  container.innerHTML = html;

  // Wire crosshair on the altitude chart (must be after innerHTML is set)
  var altWrap = container.querySelector('.tonight-altitude-wrap');
  if (altWrap && altWrap.querySelector('svg')) {
    setupChartCrosshair(altWrap);
  }
}

// ── Altitude maths (port of AltitudeCalculator.cs) ────────────────────────

function toJulianDate(d) {
  // JS Date.getTime() is ms since Unix epoch; Unix epoch = JD 2440587.5
  return d.getTime() / 86400000.0 + 2440587.5;
}

function calcAltitudeDeg(raHours, decDeg, latDeg, lonDeg, dateUTC) {
  var jd       = toJulianDate(dateUTC);
  var gmstDeg  = ((280.46061837 + 360.98564736629 * (jd - 2451545.0)) % 360 + 360) % 360;
  var lstDeg   = ((gmstDeg + lonDeg)        % 360 + 360) % 360;
  var haDeg    = ((lstDeg - raHours * 15.0) % 360 + 360) % 360;
  if (haDeg > 180) haDeg -= 360;
  var decRad   = decDeg * Math.PI / 180;
  var latRad   = latDeg * Math.PI / 180;
  var haRad    = haDeg  * Math.PI / 180;
  var sinAlt   = Math.sin(decRad)*Math.sin(latRad) + Math.cos(decRad)*Math.cos(latRad)*Math.cos(haRad);
  return Math.asin(Math.max(-1.0, Math.min(1.0, sinAlt))) * 180.0 / Math.PI;
}

// Build an altitude-vs-time SVG compatible with setupChartCrosshair.
// Uses the same plotL/plotT/plotB/plotR constants as the session altitude charts.
function buildTonightAltitudeChart(data, uniqueNames, colorMap, targets, timelineStart, totalMs) {
  var observerLat = (data.observerLat != null) ? data.observerLat : 0;
  var observerLon = (data.observerLon != null) ? data.observerLon : 0;

  if (observerLat === 0 && observerLon === 0) {
    return '<div class="tonight-altitude-note">Altitude curves unavailable (observer coordinates not set in NINA profile).</div>';
  }

  // Collect RA/Dec from entry data; deduplicate by name (first occurrence wins)
  var targetCoords = {};
  targets.forEach(function(e) {
    if (!e.waitPeriod && e.name && !(e.ra === 0 && e.dec === 0) && !targetCoords[e.name]) {
      targetCoords[e.name] = { ra: e.ra || 0, dec: e.dec || 0 };
    }
  });
  // Fallback: try statsTargetData by TS target name match
  if (statsTargetData) {
    uniqueNames.forEach(function(name) {
      if (targetCoords[name]) return;
      for (var i = 0; i < statsTargetData.length; i++) {
        var t = statsTargetData[i];
        var tsDec = t.ts && t.ts.target && t.ts.target.dec;
        var tsRa  = t.ts && t.ts.target && t.ts.target.ra;
        var tsName = t.ts && t.ts.target && (t.ts.target.name || '');
        if (tsName.toLowerCase() === name.toLowerCase() && !(tsRa === 0 && tsDec === 0)) {
          targetCoords[name] = { ra: tsRa || 0, dec: tsDec || 0 };
          break;
        }
        if ((t.target || '').toLowerCase() === name.toLowerCase() && t.ts && t.ts.target &&
            !(tsRa === 0 && tsDec === 0)) {
          targetCoords[name] = { ra: tsRa || 0, dec: tsDec || 0 };
          break;
        }
      }
    });
  }

  var charted = uniqueNames.filter(function(n) { return targetCoords[n]; });
  if (!charted.length) {
    return '<div class="tonight-altitude-note">Altitude curves unavailable (no RA/Dec found for tonight\'s targets).</div>';
  }

  // SVG layout — matches setupChartCrosshair's hardcoded plot bounds
  var svgW = 760, svgH = 250;
  var plotL = 38, plotR = svgW - 10, plotT = 20, plotB = 220;
  var timelineEnd = new Date(timelineStart.getTime() + totalMs);

  function timeToX(d) {
    return plotL + (d.getTime() - timelineStart.getTime()) / totalMs * (plotR - plotL);
  }
  function altToY(alt) {
    return plotB - (alt / 90) * (plotB - plotT);
  }

  // Sample altitude every 5 min across the timeline
  var stepMs  = 5 * 60 * 1000;
  var steps   = [];
  for (var ts = timelineStart.getTime(); ts <= timelineEnd.getTime(); ts += stepMs) {
    steps.push(new Date(Math.min(ts, timelineEnd.getTime())));
    if (ts >= timelineEnd.getTime()) break;
  }
  if (!steps.length || steps[steps.length - 1].getTime() < timelineEnd.getTime()) {
    steps.push(timelineEnd);
  }

  var s = '';
  s += '<svg viewBox="0 0 ' + svgW + ' ' + svgH + '" xmlns="http://www.w3.org/2000/svg"';
  s += ' style="width:100%;font-family:Arial,sans-serif;font-size:11px;" preserveAspectRatio="none">';

  // Clip path — curves and shading must not overflow below the horizon or above the plot
  s += '<defs><clipPath id="altPlotClip"><rect x="' + plotL + '" y="' + plotT + '" width="' + (plotR - plotL) + '" height="' + (plotB - plotT) + '"/></clipPath></defs>';

  // Plot background
  s += '<rect x="' + plotL + '" y="' + plotT + '" width="' + (plotR - plotL) + '" height="' + (plotB - plotT) + '" fill="#111827"/>';

  // Gridlines + Y-axis labels at 0°, 30°, 60°, 90°
  [0, 30, 60, 90].forEach(function(alt) {
    var gy = altToY(alt).toFixed(1);
    var isHorizon = (alt === 0);
    s += '<line x1="' + plotL + '" y1="' + gy + '" x2="' + plotR + '" y2="' + gy + '"';
    s += ' stroke="#2d3748" stroke-width="' + (isHorizon ? '1.5' : '1') + '"';
    if (!isHorizon) s += ' stroke-dasharray="4,4"';
    s += '/>';
    s += '<text x="' + (plotL - 4) + '" y="' + (parseFloat(gy) + 4).toFixed(1) + '" fill="#888" text-anchor="end" font-size="9">' + alt + '</text>';
  });

  // Imaging window shading — one rect per target entry block (matching colors for crosshair)
  targets.forEach(function(entry) {
    if (entry.waitPeriod || !entry.name || !targetCoords[entry.name]) return;
    var x1 = timeToX(new Date(entry.startTime));
    var x2 = timeToX(new Date(entry.endTime));
    var w  = Math.max(x2 - x1, 1);
    var c  = colorMap[entry.name];
    s += '<rect x="' + x1.toFixed(1) + '" y="' + plotT + '" width="' + w.toFixed(1) + '" height="' + (plotB - plotT) + '" fill="' + c + '" opacity="0.15" clip-path="url(#altPlotClip)"/>';
    s += '<line x1="' + x1.toFixed(1) + '" y1="' + plotT + '" x2="' + x1.toFixed(1) + '" y2="' + plotB + '" stroke="' + c + '" opacity="0.6" stroke-width="1"/>';
    s += '<line x1="' + x2.toFixed(1) + '" y1="' + plotT + '" x2="' + x2.toFixed(1) + '" y2="' + plotB + '" stroke="' + c + '" opacity="0.6" stroke-width="1"/>';
  });

  // One altitude curve per unique target name (wrapped in <g><title>…</title>)
  charted.forEach(function(name) {
    var coords = targetCoords[name];
    var color  = colorMap[name];
    var pts = steps.map(function(d) {
      var alt = calcAltitudeDeg(coords.ra, coords.dec, observerLat, observerLon, d);
      return timeToX(d).toFixed(1) + ',' + altToY(alt).toFixed(1);
    }).join(' ');
    s += '<g clip-path="url(#altPlotClip)">';
    s += '<title>' + esc(name) + '</title>';
    s += '<polyline points="' + pts + '" fill="none" stroke="' + color + '" stroke-width="1.5"/>';
    s += '</g>';
  });

  // Time-axis ruler at y=plotB (required by crosshair for time interpolation)
  var durationHours    = totalMs / 3600000;
  var tickIntervalMin  = durationHours < 2 ? 15 : durationHours < 5 ? 30 : 60;
  var tickLabelY       = plotB + 14;

  s += '<line x1="' + plotL + '" y1="' + plotB + '" x2="' + plotR + '" y2="' + plotB + '" stroke="#555" stroke-width="1"/>';
  s += '<text x="' + plotL + '" y="' + tickLabelY + '" fill="#888">' + fmtTimeHHMM(timelineStart) + '</text>';
  s += '<text x="' + plotR  + '" y="' + tickLabelY + '" fill="#888" text-anchor="end">' + fmtTimeHHMM(timelineEnd) + '</text>';

  var tickT = new Date(timelineStart);
  tickT.setMinutes(0, 0, 0);
  tickT = new Date(tickT.getTime() + tickIntervalMin * 60000);
  while (tickT <= timelineStart) tickT = new Date(tickT.getTime() + tickIntervalMin * 60000);
  while (tickT < timelineEnd) {
    var tx = timeToX(tickT);
    if (tx - plotL > 40 && plotR - tx > 40) {
      s += '<line x1="' + tx.toFixed(1) + '" y1="' + plotB + '" x2="' + tx.toFixed(1) + '" y2="' + (plotB + 5) + '" stroke="#555" stroke-width="1"/>';
      s += '<text x="' + tx.toFixed(1) + '" y="' + tickLabelY + '" fill="#888" text-anchor="middle">' + fmtTimeHHMM(tickT) + '</text>';
    }
    tickT = new Date(tickT.getTime() + tickIntervalMin * 60000);
  }

  s += '</svg>';
  return s;
}

function buildTonightTimeline(entries, targets, colorMap, uniqueNames, timelineStart, totalMs) {
  var svgW     = 760;
  var trackH   = 24;
  var topPad   = 10;
  var leftPad  = 8;
  var rightPad = 8;
  var barAreaW = svgW - leftPad - rightPad;
  var legendRowH = 20;

  function timeToX(d) {
    return leftPad + (d - timelineStart) / totalMs * barAreaW;
  }

  var rulerH    = 28;
  var legendTop = topPad + trackH + rulerH + 8;
  var legendH   = 18 + uniqueNames.length * legendRowH;
  var svgH      = legendTop + legendH + 10;

  var s = '<div class="tonight-timeline">';
  s += '<svg viewBox="0 0 ' + svgW + ' ' + svgH + '" xmlns="http://www.w3.org/2000/svg" style="width:100%;font-family:Arial,sans-serif;font-size:11px;">';

  // Background track
  s += '<rect x="' + leftPad + '" y="' + topPad + '" width="' + barAreaW + '" height="' + trackH + '" rx="4" fill="#1e1f3c"/>';

  // Hatch pattern for wait periods
  s += '<defs>';
  s += '<pattern id="tonight-idle" patternUnits="userSpaceOnUse" width="8" height="8" patternTransform="rotate(45)">';
  s += '<rect width="8" height="8" fill="#1e1f3c"/>';
  s += '<line x1="0" y1="0" x2="0" y2="8" stroke="#2d2d5e" stroke-width="3"/>';
  s += '</pattern>';
  s += '</defs>';

  // Wait period segments
  entries.filter(function(e) { return e.waitPeriod; }).forEach(function(e) {
    var x1 = timeToX(new Date(e.startTime));
    var x2 = timeToX(new Date(e.endTime));
    var w  = Math.max(x2 - x1, 1);
    s += '<rect x="' + x1.toFixed(1) + '" y="' + topPad + '" width="' + w.toFixed(1) + '" height="' + trackH + '" fill="url(#tonight-idle)"/>';
  });

  // Target blocks
  targets.forEach(function(e) {
    var x1 = timeToX(new Date(e.startTime));
    var x2 = timeToX(new Date(e.endTime));
    var w  = Math.max(x2 - x1, 2);
    s += '<rect x="' + x1.toFixed(1) + '" y="' + topPad + '" width="' + w.toFixed(1) + '" height="' + trackH + '" fill="' + colorMap[e.name] + '" opacity="0.85"/>';
  });

  // Ruler
  var rulerY     = topPad + trackH;
  var tickLabelY = rulerY + 20;
  var timelineEnd = new Date(entries[entries.length - 1].endTime);
  var durationHours = totalMs / 3600000;
  var tickIntervalMin = durationHours < 2 ? 15 : durationHours < 5 ? 30 : 60;

  s += '<line x1="' + leftPad + '" y1="' + rulerY + '" x2="' + (svgW - rightPad) + '" y2="' + rulerY + '" stroke="#555" stroke-width="1"/>';

  // Start / end time labels
  s += '<text x="' + leftPad + '" y="' + tickLabelY + '" fill="#888">' + fmtTimeHHMM(timelineStart) + '</text>';
  s += '<text x="' + (svgW - rightPad) + '" y="' + tickLabelY + '" fill="#888" text-anchor="end">' + fmtTimeHHMM(timelineEnd) + '</text>';

  // Tick marks
  var tickMs = new Date(timelineStart);
  tickMs.setMinutes(0, 0, 0);
  tickMs = new Date(tickMs.getTime() + tickIntervalMin * 60000);
  while (tickMs <= timelineStart) tickMs = new Date(tickMs.getTime() + tickIntervalMin * 60000);

  while (tickMs < timelineEnd) {
    var tx = timeToX(tickMs);
    if (tx - leftPad > 40 && (svgW - rightPad) - tx > 40) {
      s += '<line x1="' + tx.toFixed(1) + '" y1="' + rulerY + '" x2="' + tx.toFixed(1) + '" y2="' + (rulerY + 6) + '" stroke="#555" stroke-width="1"/>';
      s += '<text x="' + tx.toFixed(1) + '" y="' + tickLabelY + '" fill="#888" text-anchor="middle">' + fmtTimeHHMM(tickMs) + '</text>';
    }
    tickMs = new Date(tickMs.getTime() + tickIntervalMin * 60000);
  }

  // Legend
  var ly = legendTop;
  s += '<text x="' + leftPad + '" y="' + (ly + 12) + '" fill="#888" font-weight="bold">Targets</text>';
  ly += 18;
  uniqueNames.forEach(function(name) {
    s += '<rect x="' + leftPad + '" y="' + ly + '" width="14" height="12" fill="' + colorMap[name] + '" rx="2"/>';
    s += '<text x="' + (leftPad + 18) + '" y="' + (ly + 10) + '" fill="#888">' + esc(name) + '</text>';
    ly += legendRowH;
  });

  s += '</svg></div>';
  return s;
}

// ── Phase 3a: TS status banner ─────────────────────────────────────────────

function renderTsStatusBanner() {
  if (statsTsStatus === 'available' || statsTsStatus == null) return '';
  if (statsTsStatus === 'not_installed') {
    // Silent when TS isn't installed — no banner, no clutter
    return '';
  }
  if (statsTsStatus === 'error') {
    var msg = statsTsError ? ' — ' + esc(statsTsError) : '';
    return '<div class="ts-status-banner">Target Scheduler data unavailable' + msg + '. Project badges and goals will not appear until this is resolved.</div>';
  }
  return '';
}

// Card-level click handler: opens the target detail panel when a card is clicked
// anywhere except the expandable Hours/Frames stat boxes (which have their own
// per-filter hover popup and shouldn't trigger the panel) and the TS state badge
// (which opens its own override dropdown).
function initTargetCardClicks() {
  var cards = document.querySelectorAll('.target-card[data-target]');
  cards.forEach(function(card) {
    // Collapse button
    var collapseBtn = card.querySelector('.targets-project-collapse-btn');
    if (collapseBtn) {
      collapseBtn.addEventListener('click', function(e) {
        e.stopPropagation();
        card.classList.toggle('collapsed');
      });
    }
    // Assign-to-project button
    var assignBtn = card.querySelector('.target-card-assign-btn');
    if (assignBtn) {
      assignBtn.addEventListener('click', function(e) {
        e.stopPropagation();
        openProjectAssignPicker(assignBtn, assignBtn.getAttribute('data-target'));
      });
    }
    card.addEventListener('click', function(e) {
      if (e.target.closest('.target-stat-expandable')) return;
      if (e.target.closest('.target-card-ts-badge')) return;
      if (e.target.closest('.targets-project-collapse-btn')) return;
      if (e.target.closest('.target-card-assign-btn')) return;
      var name = card.getAttribute('data-target');
      var sid = card.getAttribute('data-latest-session');
      openTargetDetail(name, sid);
    });
    card.style.cursor = 'pointer';
  });
}

// ── Phase 3a: TS state badge click \u2192 override dropdown ────────────────────

var TS_STATES = ['Active', 'Completed', 'Draft', 'Inactive', 'Closed'];

function initTsBadgeClicks() {
  var badges = document.querySelectorAll('.target-card-ts-badge');
  badges.forEach(function(badge) {
    badge.addEventListener('click', function(e) {
      e.stopPropagation();
      openTsOverrideDropdown(badge);
    });
  });
}

function closeTsOverrideDropdown() {
  var existing = document.getElementById('ts-override-dropdown');
  if (existing) {
    existing.classList.remove('visible');
    setTimeout(function() { if (existing.parentNode) existing.parentNode.removeChild(existing); }, 150);
  }
  document.removeEventListener('click', _tsOverrideOutsideHandler, true);
  document.removeEventListener('keydown', _tsOverrideKeyHandler);
}

var _tsOverrideOutsideHandler = function(e) {
  var dropdown = document.getElementById('ts-override-dropdown');
  if (!dropdown) return;
  if (dropdown.contains(e.target)) return;
  if (e.target.closest('.target-card-ts-badge')) return;
  closeTsOverrideDropdown();
};
var _tsOverrideKeyHandler = function(e) {
  if (e.key === 'Escape') closeTsOverrideDropdown();
};

function openTsOverrideDropdown(anchorBadge) {
  closeTsOverrideDropdown();
  var projectGuid = anchorBadge.getAttribute('data-project-guid');
  var currentState = anchorBadge.getAttribute('data-state');
  if (!projectGuid) return;

  var options = TS_STATES.map(function(state) {
    var cls = 'ts-override-option' + (state === currentState ? ' selected' : '');
    return '<div class="' + cls + '" data-state="' + esc(state) + '">' +
      '<span class="state-dot"></span>' + esc(state) +
      '</div>';
  }).join('');

  var html =
    '<div class="ts-override-dropdown-header">Project status</div>' +
    options +
    '<div class="ts-override-reset" data-action="reset">Reset to TS value</div>';

  var dropdown = document.createElement('div');
  dropdown.id = 'ts-override-dropdown';
  dropdown.className = 'ts-override-dropdown';
  dropdown.setAttribute('data-project-guid', projectGuid);
  dropdown.innerHTML = html;
  document.body.appendChild(dropdown);

  // Position below the badge, clamp to viewport
  var rect = anchorBadge.getBoundingClientRect();
  var top = rect.bottom + window.scrollY + 6;
  var left = rect.left + window.scrollX;
  dropdown.style.top = top + 'px';
  dropdown.style.left = left + 'px';

  requestAnimationFrame(function() {
    var dr = dropdown.getBoundingClientRect();
    if (dr.right > window.innerWidth - 12) {
      dropdown.style.left = (window.innerWidth - dr.width - 12 + window.scrollX) + 'px';
    }
    if (dr.bottom > window.innerHeight - 12) {
      // flip above if no room below
      dropdown.style.top = (rect.top + window.scrollY - dr.height - 6) + 'px';
    }
    dropdown.classList.add('visible');
  });

  // Wire up click handlers
  dropdown.querySelectorAll('.ts-override-option').forEach(function(opt) {
    opt.addEventListener('click', function() {
      applyTsStatusOverride(projectGuid, opt.getAttribute('data-state'));
    });
  });
  var reset = dropdown.querySelector('.ts-override-reset');
  if (reset) reset.addEventListener('click', function() {
    applyTsStatusOverride(projectGuid, '');
  });

  // Dismiss on outside click or Escape
  setTimeout(function() {
    document.addEventListener('click', _tsOverrideOutsideHandler, true);
    document.addEventListener('keydown', _tsOverrideKeyHandler);
  }, 0);
}

function applyTsStatusOverride(projectGuid, status) {
  closeTsOverrideDropdown();
  fetch('/api/stats/ts/override', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ projectGuid: projectGuid, status: status || '' })
  }).then(function(r) {
    if (!r.ok) throw new Error('HTTP ' + r.status);
    return r.json();
  }).then(function() {
    // Reload stats data so all cards reflect the new state
    logInfo('TS override applied:', projectGuid, '->', status || '(cleared)');
    renderStats();
  }).catch(function(err) {
    logError('TS override failed:', err.message);
  });
}

function applyTsTargetLink(sessionTargetName, tsTargetGuid, onDone) {
  fetch('/api/stats/ts/link', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ sessionTargetName: sessionTargetName, tsTargetGuid: tsTargetGuid || '' })
  }).then(function(r) {
    if (!r.ok) throw new Error('HTTP ' + r.status);
    return r.json();
  }).then(function() {
    logInfo('TS link applied:', sessionTargetName, '->', tsTargetGuid || '(cleared)');
    if (onDone) onDone();
  }).catch(function(err) {
    logError('TS link failed:', err.message);
  });
}

// ── Manage Projects modal ─────────────────────────────────────────────────

function closeManageProjectsModal() {
  var bd = document.getElementById('manage-projects-backdrop');
  if (bd && bd.parentNode) bd.parentNode.removeChild(bd);
  document.removeEventListener('keydown', _manageProjectsKeyHandler);
  document.body.style.overflow = '';
}

var _manageProjectsKeyHandler = function(e) {
  if (e.key === 'Escape') closeManageProjectsModal();
};

// Check which project GUIDs have at least one NS target match
function getMatchedProjectGuids() {
  var matched = {};
  (statsTargetData || []).forEach(function(d) {
    if (d.ts && d.ts.project && d.ts.project.guid) matched[d.ts.project.guid] = true;
  });
  // Also include custom projects that have assignments
  Object.keys(statsProjectAssignments || {}).forEach(function(k) {
    matched[statsProjectAssignments[k]] = true;
  });
  return matched;
}

function openManageProjectsModal() {
  closeManageProjectsModal();
  var projects = statsTsProjects || [];
  var matchedGuids = getMatchedProjectGuids();

  // Build target list for each project
  function projectTargetList(p) {
    var targets = [];
    // TS targets from the project itself (custom project targets are all assigned)
    var srcForBuiltin = p.isCustom ? 'assigned' : 'ts';
    if (p.targets) {
      var projExclusions = (!p.isCustom && statsTargetExclusions) ? (statsTargetExclusions[p.guid] || []) : [];
      p.targets.forEach(function(t) {
        if (projExclusions.indexOf((t.name || '').toLowerCase()) >= 0) return;
        targets.push({ name: t.name, source: srcForBuiltin });
      });
    }
    // Manually assigned targets (only add if not already listed from p.targets)
    Object.keys(statsProjectAssignments || {}).forEach(function(k) {
      if (statsProjectAssignments[k] === p.guid) {
        var alreadyListed = targets.some(function(t) { return t.name.toLowerCase() === k; });
        if (!alreadyListed) targets.push({ name: k, source: 'assigned' });
      }
    });
    return targets;
  }

  function renderProjectRow(p) {
    var targets = projectTargetList(p);
    var subtitle = p.isCustom
      ? (targets.length > 0 ? targets.length + ' assigned target' + (targets.length > 1 ? 's' : '') : 'No targets assigned')
      : p.targetCount + ' TS target' + (p.targetCount !== 1 ? 's' : '');
    var typeTag = p.isMosaic ? 'Mosaic' : (p.isCustom ? 'Custom' : (p.targetCount > 1 ? 'Multi' : 'Single'));
    var hasTargets = targets.length > 0;
    var hasExclusions = !p.isCustom && statsTargetExclusions && (statsTargetExclusions[p.guid] || []).length > 0;

    var targetsHtml = '';
    if (hasTargets) {
      targetsHtml = '<div class="manage-project-targets" data-guid="' + esc(p.guid) + '" style="display:none">';
      targets.forEach(function(t) {
        targetsHtml += '<div class="manage-project-target">' +
          '<span class="manage-project-target-name">' + esc(t.name) + '</span>' +
          '<button type="button" class="manage-project-target-remove" data-target="' + esc(t.name) + '" data-project="' + esc(p.guid) + '" data-source="' + t.source + '" title="Remove from project">\u00d7</button>' +
        '</div>';
      });
      targetsHtml += '</div>';
    }

    return '<div class="manage-project-row">' +
      '<div class="manage-project-item' + (hasTargets ? ' expandable' : '') + '" data-guid="' + esc(p.guid) + '">' +
        (hasTargets ? '<span class="manage-project-chevron">\u25b8</span>' : '') +
        '<div class="manage-project-info">' +
          '<span class="manage-project-name">' + esc(p.name) + '</span>' +
          '<span class="manage-project-meta">' + esc(typeTag) + ' \u00b7 ' + esc(subtitle) + '</span>' +
        '</div>' +
        (hasExclusions ? '<button type="button" class="manage-project-proj-reset" data-guid="' + esc(p.guid) + '" title="Restore hidden targets for this project">\u21ba</button>' : '') +
        (p.isCustom ? '<button type="button" class="manage-project-delete" data-guid="' + esc(p.guid) + '" title="Delete project">\u00d7</button>' : '') +
      '</div>' +
      targetsHtml +
    '</div>';
  }

  // Split projects into matched (have NS data) and unmatched
  var matchedProjects = [];
  var unmatchedProjects = [];
  projects.forEach(function(p) {
    if (p.isCustom || matchedGuids[p.guid]) matchedProjects.push(p);
    else unmatchedProjects.push(p);
  });

  var matchedHtml = matchedProjects.map(renderProjectRow).join('');
  var unmatchedHtml = '';
  if (unmatchedProjects.length > 0) {
    unmatchedHtml = '<div class="manage-projects-other">' +
      '<div class="manage-projects-other-header" data-action="toggle-other">' +
        '<span class="manage-project-chevron">\u25b8</span> Other TS Projects (' + unmatchedProjects.length + ')' +
      '</div>' +
      '<div class="manage-projects-other-list" style="display:none">' +
        unmatchedProjects.map(renderProjectRow).join('') +
      '</div>' +
    '</div>';
  }

  var listContent = matchedHtml + unmatchedHtml;

  var html =
    '<div class="manage-projects-modal" role="dialog" aria-label="Manage Projects">' +
      '<h3>Manage Projects</h3>' +
      '<div class="manage-projects-list">' + (listContent || '<div class="empty">No projects available</div>') + '</div>' +
      '<div class="manage-projects-create">' +
        '<input type="text" class="manage-projects-input" placeholder="New project name\u2026" maxlength="80">' +
        '<button type="button" class="manage-projects-add-btn">Create</button>' +
      '</div>' +
      '<div class="manage-projects-footer">' +
        '<button type="button" class="manage-projects-reset-btn" data-action="reset">Reset to TS</button>' +
        '<button type="button" data-action="close">Close</button>' +
      '</div>' +
    '</div>';

  var backdrop = document.createElement('div');
  backdrop.id = 'manage-projects-backdrop';
  backdrop.className = 'ts-link-picker-backdrop';
  backdrop.innerHTML = html;
  document.body.appendChild(backdrop);
  document.body.style.overflow = 'hidden';
  backdrop.addEventListener('touchmove', function(e) { if (e.target === backdrop) e.preventDefault(); }, { passive: false });

  backdrop.addEventListener('click', function(e) {
    if (e.target === backdrop) closeManageProjectsModal();
  });
  document.addEventListener('keydown', _manageProjectsKeyHandler);

  // Close button
  backdrop.querySelector('[data-action="close"]').addEventListener('click', closeManageProjectsModal);

  // ── In-place helpers ─────────────────────────────────────────────────────

  // Update a project row's subtitle and ↺ button in-place after an exclusion change.
  function updateProjectRowMeta(projectGuid) {
    var item = backdrop.querySelector('.manage-project-item[data-guid="' + projectGuid + '"]');
    if (!item) return;
    var metaEl = item.querySelector('.manage-project-meta');
    if (!metaEl) return;
    var proj = (statsTsProjects || []).find(function(p) { return p.guid === projectGuid; });
    if (!proj) return;

    var excludedCount = ((statsTargetExclusions || {})[projectGuid] || []).length;
    var typeTag = proj.isMosaic ? 'Mosaic' : (proj.isCustom ? 'Custom' : (proj.targetCount > 1 ? 'Multi' : 'Single'));
    var subtitle;
    if (proj.isCustom) {
      var assignedCount = 0;
      Object.keys(statsProjectAssignments || {}).forEach(function(k) {
        if ((statsProjectAssignments || {})[k] === projectGuid) assignedCount++;
      });
      subtitle = assignedCount > 0
        ? assignedCount + ' assigned target' + (assignedCount !== 1 ? 's' : '')
        : 'No targets assigned';
    } else {
      var visible = proj.targetCount - excludedCount;
      subtitle = visible + ' TS target' + (visible !== 1 ? 's' : '');
      if (excludedCount > 0) subtitle += ' \u00b7 ' + excludedCount + ' hidden';
    }
    metaEl.textContent = typeTag + ' \u00b7 ' + subtitle;

    var existingReset = item.querySelector('.manage-project-proj-reset');
    if (!proj.isCustom && excludedCount > 0 && !existingReset) {
      var rb = document.createElement('button');
      rb.type = 'button';
      rb.className = 'manage-project-proj-reset';
      rb.setAttribute('data-guid', projectGuid);
      rb.title = 'Restore hidden targets for this project';
      rb.textContent = '\u21ba';
      rb.addEventListener('click', function(e) { e.stopPropagation(); handleProjectReset(projectGuid); });
      var deleteBtn = item.querySelector('.manage-project-delete');
      if (deleteBtn) item.insertBefore(rb, deleteBtn);
      else item.appendChild(rb);
    } else if (excludedCount === 0 && existingReset) {
      existingReset.remove();
    }
  }

  // Remove a target row in-place and persist.
  function handleTargetRemove(btn) {
    var targetName  = btn.getAttribute('data-target');
    var projectGuid = btn.getAttribute('data-project');
    var source      = btn.getAttribute('data-source');
    var url, body;
    if (source === 'ts') {
      if (!statsTargetExclusions) statsTargetExclusions = {};
      var list = statsTargetExclusions[projectGuid] || [];
      var key = targetName.toLowerCase();
      if (list.indexOf(key) < 0) list.push(key);
      statsTargetExclusions[projectGuid] = list;
      url  = '/api/stats/ts/exclude';
      body = { targetName: targetName, projectGuid: projectGuid, exclude: true };
    } else {
      if (statsProjectAssignments) delete statsProjectAssignments[targetName.toLowerCase()];
      url  = '/api/stats/ts/assign';
      body = { targetName: targetName, projectGuid: '' };
    }
    var row = btn.closest('.manage-project-target');
    if (row) row.remove();
    updateProjectRowMeta(projectGuid);
    fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
  }

  // Restore all hidden targets for one project in-place.
  function handleProjectReset(projectGuid) {
    var proj = (statsTsProjects || []).find(function(p) { return p.guid === projectGuid; });
    if (!proj) return;
    var excluded = ((statsTargetExclusions || {})[projectGuid] || []).slice();
    var toRestore = (proj.targets || []).filter(function(t) {
      return excluded.indexOf((t.name || '').toLowerCase()) >= 0;
    });
    var targetList = backdrop.querySelector('.manage-project-targets[data-guid="' + projectGuid + '"]');
    toRestore.forEach(function(t) {
      var row = document.createElement('div');
      row.className = 'manage-project-target';
      var nameSpan = document.createElement('span');
      nameSpan.className = 'manage-project-target-name';
      nameSpan.textContent = t.name || '';
      var removeBtn = document.createElement('button');
      removeBtn.type = 'button';
      removeBtn.className = 'manage-project-target-remove';
      removeBtn.setAttribute('data-target', t.name || '');
      removeBtn.setAttribute('data-project', projectGuid);
      removeBtn.setAttribute('data-source', 'ts');
      removeBtn.title = 'Remove from project';
      removeBtn.textContent = '\u00d7';
      removeBtn.addEventListener('click', function(e) { e.stopPropagation(); handleTargetRemove(removeBtn); });
      row.appendChild(nameSpan);
      row.appendChild(removeBtn);
      if (targetList) targetList.appendChild(row);
    });
    if (statsTargetExclusions) delete statsTargetExclusions[projectGuid];
    updateProjectRowMeta(projectGuid);
    fetch('/api/stats/projects/' + encodeURIComponent(projectGuid) + '/reset', {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({})
    });
  }

  // Rebuild the project list in-place (for structural changes: create, delete, global reset).
  function rebuildList() {
    var listEl = backdrop.querySelector('.manage-projects-list');
    if (!listEl) return;
    var curProjects = statsTsProjects || [];
    var curMatched = getMatchedProjectGuids();
    var matchedP = [], unmatchedP = [];
    curProjects.forEach(function(p) {
      if (p.isCustom || curMatched[p.guid]) matchedP.push(p);
      else unmatchedP.push(p);
    });
    var html = matchedP.map(renderProjectRow).join('');
    if (unmatchedP.length > 0) {
      html += '<div class="manage-projects-other">' +
        '<div class="manage-projects-other-header" data-action="toggle-other">' +
          '<span class="manage-project-chevron">\u25b8</span> Other TS Projects (' + unmatchedP.length + ')' +
        '</div>' +
        '<div class="manage-projects-other-list" style="display:none">' +
          unmatchedP.map(renderProjectRow).join('') +
        '</div>' +
      '</div>';
    }
    listEl.innerHTML = html || '<div class="empty">No projects available</div>';
    wireListHandlers(listEl);
  }

  // Wire all dynamic handlers inside the list element.
  function wireListHandlers(listEl) {
    // Expand/collapse project rows
    listEl.querySelectorAll('.manage-project-item.expandable').forEach(function(item) {
      item.addEventListener('click', function(e) {
        if (e.target.closest('.manage-project-delete')) return;
        if (e.target.closest('.manage-project-proj-reset')) return;
        var guid = item.getAttribute('data-guid');
        var targetListEl = listEl.querySelector('.manage-project-targets[data-guid="' + guid + '"]');
        var chevron = item.querySelector('.manage-project-chevron');
        if (!targetListEl) return;
        var open = targetListEl.style.display === 'none' || targetListEl.style.display === '';
        targetListEl.style.display = open ? 'block' : 'none';
        if (chevron) chevron.textContent = open ? '\u25b8' : '\u25be';
        if (!open) {
          var scrollParent = item.closest('.manage-projects-list');
          if (scrollParent) setTimeout(function() { targetListEl.scrollIntoView({ behavior: 'smooth', block: 'nearest' }); }, 50);
        }
      });
    });

    // Other TS Projects toggle
    var otherHeader = listEl.querySelector('.manage-projects-other-header');
    if (otherHeader) {
      otherHeader.addEventListener('click', function() {
        var otherList = listEl.querySelector('.manage-projects-other-list');
        var chevron = otherHeader.querySelector('.manage-project-chevron');
        if (!otherList) return;
        var open = otherList.style.display === 'none' || otherList.style.display === '';
        otherList.style.display = open ? 'block' : 'none';
        if (chevron) chevron.textContent = open ? '\u25b8' : '\u25be';
        if (!open) setTimeout(function() { otherList.scrollIntoView({ behavior: 'smooth', block: 'nearest' }); }, 50);
      });
    }

    // Remove target buttons
    listEl.querySelectorAll('.manage-project-target-remove').forEach(function(btn) {
      btn.addEventListener('click', function(e) { e.stopPropagation(); handleTargetRemove(btn); });
    });

    // Per-project reset buttons (those initially rendered with exclusions)
    listEl.querySelectorAll('.manage-project-proj-reset').forEach(function(btn) {
      btn.addEventListener('click', function(e) { e.stopPropagation(); handleProjectReset(btn.getAttribute('data-guid')); });
    });

    // Delete custom project buttons
    listEl.querySelectorAll('.manage-project-delete').forEach(function(btn) {
      btn.addEventListener('click', function(e) {
        e.stopPropagation();
        var guid = btn.getAttribute('data-guid');
        if (!confirm('Delete this custom project? Any target assignments will be cleared.')) return;
        fetch('/api/stats/projects/custom', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ action: 'delete', guid: guid })
        }).then(function(r) { return r.json(); }).then(function() {
          // Remove from memory and rebuild list
          if (statsTsProjects) statsTsProjects = statsTsProjects.filter(function(p) { return p.guid !== guid; });
          Object.keys(statsProjectAssignments || {}).forEach(function(k) {
            if (statsProjectAssignments[k] === guid) delete statsProjectAssignments[k];
          });
          rebuildList();
        });
      });
    });
  }

  // ── Wire initial list ─────────────────────────────────────────────────────
  wireListHandlers(backdrop.querySelector('.manage-projects-list'));

  // Create button
  var input = backdrop.querySelector('.manage-projects-input');
  var addBtn = backdrop.querySelector('.manage-projects-add-btn');
  function doCreate() {
    var name = input.value.trim();
    if (!name) return;
    addBtn.disabled = true;
    fetch('/api/stats/projects/custom', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ action: 'create', name: name })
    }).then(function(r) { return r.json(); }).then(function(d) {
      if (d.guid && d.name) {
        if (!statsTsProjects) statsTsProjects = [];
        statsTsProjects.push({ guid: d.guid, name: d.name, state: 'Active', isMosaic: false, isCustom: true, targetCount: 0, targets: [] });
      }
      input.value = '';
      addBtn.disabled = false;
      rebuildList();
    }).catch(function() { addBtn.disabled = false; });
  }
  addBtn.addEventListener('click', doCreate);
  input.addEventListener('keydown', function(e) { if (e.key === 'Enter') doCreate(); });

  // Reset to TS button
  backdrop.querySelector('[data-action="reset"]').addEventListener('click', function() {
    if (!confirm('Reset all project assignments?\n\nThis will remove all custom projects and target assignments, restoring the default TS project grouping.')) return;
    fetch('/api/stats/projects/reset', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({})
    }).then(function(r) { return r.json(); }).then(function() {
      if (statsTsProjects) statsTsProjects = statsTsProjects.filter(function(p) { return !p.isCustom; });
      statsProjectAssignments = {};
      statsTargetExclusions   = {};
      rebuildList();
    });
  });
}

// ── Project assignment picker (per target card) ──────────────────────────

function closeProjectAssignPicker() {
  var bd = document.getElementById('project-assign-backdrop');
  if (bd && bd.parentNode) bd.parentNode.removeChild(bd);
  var dd = document.getElementById('project-assign-dropdown');
  if (dd && dd.parentNode) dd.parentNode.removeChild(dd);
  document.removeEventListener('keydown', _projectAssignKeyHandler);
  document.body.style.overflow = '';
}

var _projectAssignOutsideHandler = function(e) {
  var dd = document.getElementById('project-assign-dropdown');
  if (dd && !dd.contains(e.target)) closeProjectAssignPicker();
};
var _projectAssignKeyHandler = function(e) {
  if (e.key === 'Escape') closeProjectAssignPicker();
};

function openProjectAssignPicker(anchorEl, targetName) {
  closeProjectAssignPicker();
  var projects = statsTsProjects || [];
  var matchedGuids = getMatchedProjectGuids();
  var currentGuid = (statsProjectAssignments || {})[targetName.toLowerCase()] || null;
  // Also check if target is auto-matched to a TS project
  var targetRow = (statsTargetData || []).filter(function(t) { return t.target === targetName; })[0];
  var autoProjectGuid = (targetRow && targetRow.ts && targetRow.ts.project) ? targetRow.ts.project.guid : null;
  var effectiveGuid = currentGuid || autoProjectGuid;

  function renderOption(p) {
    var cls = 'project-assign-option' + (p.guid === effectiveGuid ? ' selected' : '');
    var tag = p.isCustom ? 'Custom' : (p.isMosaic ? 'Mosaic' : 'TS');
    return '<div class="' + cls + '" data-guid="' + esc(p.guid) + '">' +
      '<span class="project-assign-name">' + esc(p.name) + '</span>' +
      '<span class="project-assign-tag">' + esc(tag) + '</span>' +
    '</div>';
  }

  var matched = [];
  var unmatched = [];
  projects.forEach(function(p) {
    if (p.isCustom || matchedGuids[p.guid]) matched.push(p);
    else unmatched.push(p);
  });

  var options = matched.map(renderOption).join('');
  if (unmatched.length > 0) {
    options += '<div class="project-assign-other-header" data-action="toggle-other">' +
      '<span class="manage-project-chevron">\u25b8</span> Other TS Projects</div>' +
      '<div class="project-assign-other-list" style="display:none">' +
        unmatched.map(renderOption).join('') +
      '</div>';
  }

  var html =
    '<div class="project-assign-header">Assign to project</div>' +
    '<div class="project-assign-list">' + options + '</div>' +
    '<div class="project-assign-footer">' +
      '<div class="project-assign-reset" data-action="clear">Remove from project</div>' +
    '</div>';

  // Measure anchor position before locking scroll
  var rect = anchorEl.getBoundingClientRect();

  // Transparent fixed backdrop to block background scroll
  var backdrop = document.createElement('div');
  backdrop.id = 'project-assign-backdrop';
  backdrop.style.cssText = 'position:fixed;inset:0;z-index:1099;';
  backdrop.addEventListener('click', function() { closeProjectAssignPicker(); });
  backdrop.addEventListener('touchmove', function(e) { e.preventDefault(); }, { passive: false });
  document.body.appendChild(backdrop);
  document.body.style.overflow = 'hidden';

  var dropdown = document.createElement('div');
  dropdown.id = 'project-assign-dropdown';
  dropdown.className = 'ts-override-dropdown';
  dropdown.innerHTML = html;
  document.body.appendChild(dropdown);

  // Position below the anchor using fixed positioning (viewport-relative)
  dropdown.style.position = 'fixed';
  dropdown.style.top = (rect.bottom + 6) + 'px';
  dropdown.style.left = rect.left + 'px';

  requestAnimationFrame(function() {
    var dr = dropdown.getBoundingClientRect();
    if (dr.right > window.innerWidth - 12) {
      dropdown.style.left = (window.innerWidth - dr.width - 12) + 'px';
    }
    if (dr.bottom > window.innerHeight - 12) {
      dropdown.style.top = (rect.top - dr.height - 6) + 'px';
    }
    dropdown.classList.add('visible');
  });

  // Toggle "Other TS Projects" expander
  var otherHdr = dropdown.querySelector('.project-assign-other-header');
  if (otherHdr) {
    otherHdr.addEventListener('click', function() {
      var list = dropdown.querySelector('.project-assign-other-list');
      var chevron = otherHdr.querySelector('.manage-project-chevron');
      if (!list) return;
      var open = list.style.display !== 'none';
      list.style.display = open ? 'none' : 'block';
      if (chevron) chevron.textContent = open ? '\u25b8' : '\u25be';
      if (!open) {
        var scrollParent = otherHdr.closest('.project-assign-list');
        if (scrollParent) setTimeout(function() { scrollParent.scrollTop = scrollParent.scrollHeight; }, 50);
      }
    });
  }

  // Click handlers
  dropdown.querySelectorAll('.project-assign-option').forEach(function(opt) {
    opt.addEventListener('click', function() {
      var guid = opt.getAttribute('data-guid');
      closeProjectAssignPicker();
      fetch('/api/stats/ts/assign', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ targetName: targetName, projectGuid: guid })
      }).then(function(r) { return r.json(); }).then(function() {
        renderStats();
      });
    });
  });
  dropdown.querySelector('[data-action="clear"]').addEventListener('click', function() {
    closeProjectAssignPicker();
    fetch('/api/stats/ts/assign', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ targetName: targetName, projectGuid: '' })
    }).then(function(r) { return r.json(); }).then(function() {
      renderStats();
    });
  });

  setTimeout(function() {
    document.addEventListener('click', _projectAssignOutsideHandler, true);
    document.addEventListener('keydown', _projectAssignKeyHandler);
  }, 0);
}

// ── TS Link picker modal ──────────────────────────────────────────────────

function closeTsLinkPicker() {
  var bd = document.getElementById('ts-link-picker-backdrop');
  if (bd && bd.parentNode) bd.parentNode.removeChild(bd);
  document.removeEventListener('keydown', _tsLinkPickerKeyHandler);
  document.body.style.overflow = '';
}

var _tsLinkPickerKeyHandler = function(e) {
  if (e.key === 'Escape') closeTsLinkPicker();
};

function openTsLinkPicker(sessionTargetName) {
  if (!statsTsProjects || statsTsProjects.length === 0) {
    logError('No TS projects available for linking');
    return;
  }

  // Figure out current linked target guid (if any) for visual highlighting
  var currentTs = findTsForTarget(sessionTargetName);
  var currentGuid = currentTs && currentTs.target ? currentTs.target.guid : null;

  // Flatten all targets across all projects with their project context
  var items = [];
  statsTsProjects.forEach(function(p) {
    (p.targets || []).forEach(function(t) {
      items.push({
        guid: t.guid,
        targetName: t.name,
        projectName: p.name,
        projectState: p.state,
        isMosaic: p.isMosaic
      });
    });
  });

  // Sort: current match first, then alphabetical
  items.sort(function(a, b) {
    if (a.guid === currentGuid && b.guid !== currentGuid) return -1;
    if (b.guid === currentGuid && a.guid !== currentGuid) return 1;
    return (a.targetName || '').localeCompare(b.targetName || '');
  });

  var list = items.map(function(it) {
    var cls = 'ts-link-picker-item' + (it.guid === currentGuid ? ' current' : '');
    return '<div class="' + cls + '" data-target-guid="' + esc(it.guid || '') + '">' +
      '<span class="target-name">' + esc(it.targetName || 'Unnamed') + '</span>' +
      '<span class="project-name">' + esc(it.projectName || '') + (it.isMosaic ? ' \u00b7 mosaic' : '') + '</span>' +
    '</div>';
  }).join('');

  var html =
    '<div class="ts-link-picker" role="dialog" aria-label="Link to TS target">' +
      '<h3>Link to Target Scheduler target</h3>' +
      '<div class="ts-link-picker-sub">Session target: <strong>' + esc(sessionTargetName) + '</strong></div>' +
      '<div class="ts-link-picker-list">' + list + '</div>' +
      '<div class="ts-link-picker-footer">' +
        '<button type="button" data-action="cancel">Cancel</button>' +
        '<button type="button" class="danger" data-action="clear">Clear manual link</button>' +
      '</div>' +
    '</div>';

  var backdrop = document.createElement('div');
  backdrop.id = 'ts-link-picker-backdrop';
  backdrop.className = 'ts-link-picker-backdrop';
  backdrop.innerHTML = html;
  document.body.appendChild(backdrop);
  document.body.style.overflow = 'hidden';
  backdrop.addEventListener('touchmove', function(e) { if (e.target === backdrop) e.preventDefault(); }, { passive: false });

  backdrop.addEventListener('click', function(e) {
    if (e.target === backdrop) closeTsLinkPicker();
  });
  document.addEventListener('keydown', _tsLinkPickerKeyHandler);

  backdrop.querySelectorAll('.ts-link-picker-item').forEach(function(item) {
    item.addEventListener('click', function() {
      var guid = item.getAttribute('data-target-guid');
      closeTsLinkPicker();
      applyTsTargetLink(sessionTargetName, guid, function() {
        closeTargetDetail();
        renderStats();
      });
    });
  });
  backdrop.querySelector('[data-action="cancel"]').addEventListener('click', closeTsLinkPicker);
  backdrop.querySelector('[data-action="clear"]').addEventListener('click', function() {
    closeTsLinkPicker();
    applyTsTargetLink(sessionTargetName, '', function() {
      closeTargetDetail();
      renderStats();
    });
  });
}

// ── Activity heatmap (GitHub contribution-grid style) ──────────────────────
//
// Scales organically with user history:
//   - no sessions OR < 14 days of history → skip entirely (return empty string)
//   - 14–365 days → grid spans firstSession → today (width grows as user images more)
//   - ≥ 365 days → rolling 365-day window
//
// Cells bucket by local date portion of sessionStart (YYYY-MM-DD).  Intensity
// buckets (hours): 0 / 0<h<1 / 1–3 / 3–6 / ≥6.
function buildActivityHeatmap(sessions, firstSessionIso) {
  if (!firstSessionIso || !sessions || !sessions.length) return '';

  var firstParts = String(firstSessionIso).match(/^(\d{4})-(\d{2})-(\d{2})/);
  if (!firstParts) return '';
  var firstDate = new Date(parseInt(firstParts[1]), parseInt(firstParts[2]) - 1, parseInt(firstParts[3]));
  firstDate.setHours(0, 0, 0, 0);

  var today = new Date();
  today.setHours(0, 0, 0, 0);

  var dayMs = 86400000;
  var historyDays = Math.floor((today - firstDate) / dayMs);
  if (historyDays < 14) return '';

  // Bucket sessions by local YYYY-MM-DD
  var byDay = {};
  sessions.forEach(function(s) {
    if (!s.sessionStart) return;
    var m = String(s.sessionStart).match(/^(\d{4})-(\d{2})-(\d{2})/);
    if (!m) return;
    var key = m[1] + '-' + m[2] + '-' + m[3];
    byDay[key] = (byDay[key] || 0) + (s.totalIntegrationSeconds || 0);
  });

  // Date range — cap at rolling 365 days
  var startDate = historyDays >= 365
    ? new Date(today.getTime() - 364 * dayMs)
    : new Date(firstDate);

  // GitHub-style grid: rows = days-of-week (Sun=0 .. Sat=6), cols = weeks.
  // Snap gridStart back to the preceding Sunday for clean column alignment.
  var gridStart = new Date(startDate);
  gridStart.setDate(gridStart.getDate() - gridStart.getDay());

  function bucketFor(hrs) {
    if (hrs <= 0) return 0;
    if (hrs < 1) return 1;
    if (hrs < 3) return 2;
    if (hrs < 6) return 3;
    return 4;
  }

  var cells = [];
  var d = new Date(gridStart);
  while (d <= today) {
    var y = d.getFullYear();
    var mo = String(d.getMonth() + 1).padStart(2, '0');
    var da = String(d.getDate()).padStart(2, '0');
    var key = y + '-' + mo + '-' + da;
    var secs = byDay[key] || 0;
    var hrs = secs / 3600;
    var preHistory = d < firstDate;
    cells.push({ date: new Date(d), key: key, hours: hrs, intensity: bucketFor(hrs), pre: preHistory });
    d.setDate(d.getDate() + 1);
  }

  var cellSize = 11, gap = 2;
  var step = cellSize + gap;
  var totalCols = Math.ceil(cells.length / 7);
  var width = totalCols * step - gap;
  var monthLabelH = 14;
  var height = monthLabelH + 7 * step - gap;

  var MONTHS = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];

  var svg = '<svg class="lifetime-heatmap" viewBox="0 0 ' + width + ' ' + height + '" ';
  svg += 'preserveAspectRatio="xMinYMid meet" ';
  svg += 'style="height:' + height + 'px;max-width:' + width + 'px;width:100%">';

  // Month labels (top) — once per column where the month first appears
  var lastMonth = -1;
  for (var col = 0; col < totalCols; col++) {
    var firstCellOfCol = cells[col * 7];
    if (!firstCellOfCol) break;
    var m = firstCellOfCol.date.getMonth();
    if (m !== lastMonth) {
      var lx = col * step;
      svg += '<text class="lifetime-heatmap-month" x="' + lx + '" y="10">' + MONTHS[m] + '</text>';
      lastMonth = m;
    }
  }

  // Cells — skip anything past today
  cells.forEach(function(c, i) {
    if (c.date > today) return;
    var col = Math.floor(i / 7);
    var row = i % 7;
    var x = col * step;
    var y = monthLabelH + row * step;
    var cls = 'lifetime-heatmap-cell intensity-' + c.intensity;
    if (c.pre) cls += ' pre-history';
    var tooltip = c.pre
      ? c.key + ' \u00b7 before first session'
      : c.key + ' \u00b7 ' + (c.hours > 0 ? c.hours.toFixed(1) + 'h' : 'no session');
    svg += '<rect class="' + cls + '" x="' + x + '" y="' + y + '" ';
    svg += 'width="' + cellSize + '" height="' + cellSize + '" rx="2"><title>';
    svg += esc(tooltip) + '</title></rect>';
  });

  svg += '</svg>';
  return svg;
}

function renderStats() {
  var el = document.getElementById('content');
  var sub = document.getElementById('page-subtitle');
  sub.textContent = 'Lifetime Statistics';

  el.innerHTML = '<div class="loading">Loading stats...</div>';

  Promise.all([
    api('/api/stats/targets'),
    api('/api/stats/summary'),
    api('/api/settings'),
    api('/api/sessions')
  ]).then(function(results) {
    var targetData = results[0];
    var summary    = results[1];
    var settings   = results[2];
    var sessions   = results[3] || [];
    var targets = targetData.targets || [];
    statsTargetData = targets;
    statsTsStatus   = targetData.tsStatus   || null;
    statsTsError    = targetData.tsError    || null;
    statsTsProjects = targetData.tsProjects || null;
    statsProjectAssignments = targetData.projectAssignments || {};
    statsTargetExclusions  = targetData.targetExclusions  || {};

    // Populate globalFilterTypeMap from plugin settings (case-insensitive)
    globalFilterTypeMap = {};
    var rawTypes = (settings && settings.filterTypeOverrides) || '';
    rawTypes.split(',').forEach(function(pair) {
      var parts = pair.split('=');
      if (parts.length === 2 && parts[0].trim() && parts[1].trim())
        globalFilterTypeMap[parts[0].trim().toLowerCase()] = parts[1].trim();
    });

    logInfo('Stats loaded:', summary.totalSessions, 'sessions,', targets.length, 'targets');

    sub.textContent = targets.length + ' target' + (targets.length !== 1 ? 's' : '') +
      ' \u00b7 ' + summary.totalSessions + ' session' + (summary.totalSessions !== 1 ? 's' : '');

    var html = '';

    // Lifetime trophy case — three-column grid: compact stats | activity heatmap (future) | filter ring (future)
    html += '<div class="lifetime-strip">';
    html +=   '<div class="lifetime-stats">';
    html +=     '<div class="lifetime-stat">' +
                  '<span class="lifetime-value">' + summary.totalSessions + '</span>' +
                  '<span class="lifetime-label">Sessions</span>' +
                '</div>';
    html +=     '<div class="lifetime-stat">' +
                  '<span class="lifetime-value">' + summary.totalIntegrationHours.toFixed(1) +
                    '<span class="unit">h</span></span>' +
                  '<span class="lifetime-label">Integration</span>' +
                '</div>';
    html +=     '<div class="lifetime-stat">' +
                  '<span class="lifetime-value">' + summary.targetCount + '</span>' +
                  '<span class="lifetime-label">Targets</span>' +
                '</div>';
    if (summary.firstSession) {
      html +=   '<div class="lifetime-stat lifetime-stat--date">' +
                  '<span class="lifetime-value">' + esc(fmtSinceDate(summary.firstSession)) + '</span>' +
                  '<span class="lifetime-label">Imaging Since</span>' +
                '</div>';
    }
    html +=   '</div>';
    html +=   '<div class="lifetime-heatmap-slot">' +
                buildActivityHeatmap(sessions, summary.firstSession) +
              '</div>';
    html +=   '<div class="lifetime-ring-slot" aria-hidden="true"></div>';
    html += '</div>';

    // Tab bar + content
    var tabs = [{id: 'targets', label: 'Targets'}, {id: 'tonight', label: 'Tonight'}];
    var activeTab = localStorage.getItem('ns-stats-tab') || 'targets';
    if (!tabs.some(function(t) { return t.id === activeTab; })) activeTab = 'targets';

    html += '<div class="stats-tab-row">';
    html += renderTabBar(tabs, activeTab);
    html += '<button type="button" class="targets-manage-projects-btn" data-action="manage-projects">Manage Projects</button>';
    html += '</div>';
    html += '<div id="stats-tab-content"></div>';

    el.innerHTML = html;

    initTabBar(renderStatsTabContent);

    // Manage Projects button — lives in the tab row, not the targets control bar
    var manageBtn = el.querySelector('.targets-manage-projects-btn');
    if (manageBtn) {
      manageBtn.addEventListener('click', function() { openManageProjectsModal(); });
    }

    renderStatsTabContent(activeTab);
  }).catch(function(err) {
    logError('Failed to load stats:', err.message);
    el.innerHTML = '<div class="error">Failed to load stats: ' + esc(err.message) + '</div>';
  });
}

// ── Init ───────────────────────────────────────────────────────────────────

logInfo('Dashboard initializing');
initTheme();
document.getElementById('theme-toggle').addEventListener('click', toggleTheme);
window.addEventListener('scroll', function() {
  var h = document.querySelector('header');
  if (h) h.classList.toggle('scrolled', window.scrollY > 4);
}, { passive: true });
window.addEventListener('hashchange', route);
window.addEventListener('resize', repositionViewToggle);
route();
logInfo('Dashboard ready');
