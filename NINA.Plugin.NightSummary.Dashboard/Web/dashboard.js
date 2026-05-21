// ── Night Summary Dashboard ──

// ── Logging ───────────────────────────────────────────────────────────────

var LOG_PREFIX = '[NightSummary]';

function _argsToString(args) {
  return Array.prototype.slice.call(args).map(function(a) {
    if (a instanceof Error) return a.message + (a.stack ? '\n' + a.stack : '');
    return (typeof a === 'object' && a !== null) ? JSON.stringify(a) : String(a);
  }).join(' ');
}
function _postClientLog(level, args) {
  try {
    fetch('/api/clientlog', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ level: level, message: _argsToString(args), url: window.location.hash || '/' })
    }).catch(function() {});
  } catch (_) {}
}
function logDebug() { console.log.apply(console, [LOG_PREFIX].concat(Array.prototype.slice.call(arguments))); }
function logInfo()  { console.info.apply(console, [LOG_PREFIX].concat(Array.prototype.slice.call(arguments))); }
function logWarn()  { console.warn.apply(console, [LOG_PREFIX].concat(Array.prototype.slice.call(arguments))); _postClientLog('warn', arguments); }
function logError() { console.error.apply(console, [LOG_PREFIX].concat(Array.prototype.slice.call(arguments))); _postClientLog('error', arguments); }
window.addEventListener('error', function(e) {
  _postClientLog('error', ['Uncaught: ' + e.message + ' (' + e.filename + ':' + e.lineno + ')']);
});
window.addEventListener('unhandledrejection', function(e) {
  var reason = e.reason ? (e.reason.message || String(e.reason)) : 'unknown rejection';
  _postClientLog('error', ['UnhandledRejection: ' + reason]);
});

// ── Capability detection ──────────────────────────────────────────────────

// True for any device with touch input — phones, tablets, touch-screen
// laptops. Used to gate touch-specific UX (long-press scrubber, tap-to-zoom
// thumbnails) so it kicks in regardless of viewport size, and to suppress
// hover-driven UI that fights with tap interactions on the same surfaces.
// `'ontouchstart' in window` covers iOS Safari; `maxTouchPoints > 0` is the
// reliable signal on Windows touch devices.
var IS_TOUCH = !window.matchMedia('(hover: hover)').matches;

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
  var d = new Date(iso);
  return String(d.getHours()).padStart(2, '0') + ':' + String(d.getMinutes()).padStart(2, '0');
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

// Defense-in-depth scrub of SVG fragments parsed via DOMParser before they enter
// the live DOM. Strips <script> elements, on*-handler attributes, and javascript:
// hrefs so a compromised backend (or future Phase 2 cloud surface) can't smuggle
// JS through API-supplied SVG content.
// localStorage.setItem can throw on quota-exceeded or in privacy modes that
// disable storage. Wrap writes so a benign settings flip doesn't bubble an
// uncaught exception out of an event handler.
function safeSetItem(key, value) {
  try { localStorage.setItem(key, value); } catch (_) { /* storage unavailable */ }
}

function sanitizeSvgInPlace(root) {
  if (!root) return;
  var nodes = [root].concat(Array.prototype.slice.call(root.querySelectorAll('*')));
  for (var i = 0; i < nodes.length; i++) {
    var el = nodes[i];
    if (el.tagName && el.tagName.toLowerCase() === 'script') {
      if (el.parentNode) el.parentNode.removeChild(el);
      continue;
    }
    if (!el.attributes) continue;
    for (var j = el.attributes.length - 1; j >= 0; j--) {
      var attr = el.attributes[j];
      if (/^on/i.test(attr.name)) el.removeAttribute(attr.name);
      else if ((attr.name === 'href' || attr.name === 'xlink:href') &&
               /^\s*javascript:/i.test(attr.value || '')) {
        el.removeAttribute(attr.name);
      }
    }
  }
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

// Canonical imaging stack order — L, R, G, B then narrowband Ha, Sii, Oiii,
// other narrowband. Unresolved filters fall to the end, alphabetical.
var FILTER_STACK_ORDER = ['L', 'R', 'G', 'B', 'H', 'S', 'O', 'N'];

// Compare two filter names by stack order. Use anywhere filters need to be
// listed in a consistent imaging-meaningful order (frames gallery, charts,
// per-filter tables, etc).
function compareFilterStackOrder(a, b) {
  var ta = resolveFilterType(a), tb = resolveFilterType(b);
  var ia = ta ? FILTER_STACK_ORDER.indexOf(ta) : -1;
  var ib = tb ? FILTER_STACK_ORDER.indexOf(tb) : -1;
  if (ia < 0) ia = FILTER_STACK_ORDER.length;
  if (ib < 0) ib = FILTER_STACK_ORDER.length;
  if (ia !== ib) return ia - ib;
  return (a || '').localeCompare(b || '');
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

// Defer a "Loading…" placeholder so cache-hot navigations don't flash one.
// Call before kicking off the fetch and invoke the returned cancel fn once
// data arrives (before paint). If 200ms elapses without cancel, the
// placeholder paints so the user still sees progress on slow loads.
function deferLoader(el, msg) {
  var timer = setTimeout(function() {
    el.innerHTML = '<div class="loading">' + msg + '</div>';
  }, 200);
  return function cancelLoader() { clearTimeout(timer); };
}

// Strip report-view chrome (body class, header pills, --header-h CSS var).
// Call this in each non-report destination renderer at the paint moment so
// the prior report doesn't collapse before the new page is ready. Idempotent.
function exitReportView() {
  document.body.classList.remove('report-view');
  var existingNav = document.getElementById('header-report-nav');
  if (existingNav) existingNav.remove();
  document.documentElement.style.removeProperty('--header-h');
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
  safeSetItem('ns-theme', isLight ? 'light' : 'dark');
  updateThemeButton();
  syncReportTheme();
}

var REPORT_THEME_LIGHT = ':root { --bg: #f5f5f5; --text: #1a1a2e; --accent: #2563b8; --accent-light: #3b7dd8; --accent-lighter: #5a9ae6; --surface: #e8ecf1; --border: #c0c8d4; --muted: #666; --dim: #888; --chart-bg: #e0e4ea; --chart-dark: #d0d4da; --bar-acquired: #8bb0d4; --warn-bg: #fff3cd; --warn-border: #d4a850; --warn-text: #856404; --warn-item: #6d5200; --skip-color: #cc3333; }' +
  /* altitude chart SVG — hardcoded dark hex overrides */
  'svg rect[fill="#0d1117"] { fill: #e8eef5; }' +
  'svg [stroke="#2d2d5e"] { stroke: #c0c8d4; }' +
  'svg [fill="#2d2d5e"] { fill: #c0c8d4; }' +
  'svg text[fill="#888"] { fill: #666; }' +
  'svg [stroke="#c0c0c0"] { stroke: #7a8a9e; }' +
  'svg [stroke="#7eb8f7"] { stroke: #2563b8; }' +
  /* metric chart SVG — hardcoded dark hex overrides */
  'svg rect[fill="#1a1a2e"] { fill: #f5f5f5; }' +
  'svg [stroke="#2a2a4a"] { stroke: #c8cdd4; }' +
  'svg [stroke="#555577"] { stroke: #666688; }' +
  'svg text[fill="#aaaacc"] { fill: #555577; }' +
  'svg circle[fill="#a8d4ff"] { fill: #1a4f9e; }' +
  'svg circle[fill="#ffd4a8"] { fill: #b85c10; }' +
  'svg rect[fill="#3a1e00"] { fill: #fff3cd; }' +
  /* timeline legend text — dark-generated reports viewed in light mode */
  'svg text[fill="#e0e0e0"] { fill: #1a1a2e; }';
var REPORT_THEME_DARK  = ':root { --bg: #1a1a2e; --text: #e0e0e0; --accent: #7eb8f7; --accent-light: #a0c4ff; --accent-lighter: #c0d8ff; --surface: #16213e; --border: #2d2d5e; --muted: #888; --dim: #555; --chart-bg: #0d1117; --chart-dark: #0f0f23; --bar-acquired: #3a5a7a; --warn-bg: #3a2a00; --warn-border: #b8860b; --warn-text: #f0c040; --warn-item: #d4a850; --skip-color: #cc6666; }' +
  /* restore dark chart colors when switching back */
  'svg rect[fill="#e8eef5"] { fill: #0d1117; }' +
  'svg rect[fill="#f5f5f5"] { fill: #1a1a2e; }' +
  /* timeline legend text — light-generated reports viewed in dark mode */
  'svg text[fill="#1a1a2e"] { fill: #e0e0e0; }';

function syncReportTheme() {
  var isLight = document.documentElement.classList.contains('light');
  // Desktop/tablet: iframe path
  var iframe = document.getElementById('report-iframe');
  if (iframe) {
    try {
      var d = iframe.contentDocument;
      if (d && d.head) {
        d.documentElement.setAttribute('data-theme', isLight ? 'light' : 'dark');
        var existing = d.getElementById('ns-theme-override');
        if (existing) existing.remove();
        var style = d.createElement('style');
        style.id = 'ns-theme-override';
        style.textContent = isLight ? REPORT_THEME_LIGHT : REPORT_THEME_DARK;
        d.head.appendChild(style);
        d.documentElement.style.backgroundColor = isLight ? '#f5f5f5' : '#1a1a2e';
        d.documentElement.style.overscrollBehavior = 'none';
      }
    } catch(e) {}
  }
  // Mobile: shadow DOM path
  var host = document.getElementById('report-shadow-host');
  if (host && host.shadowRoot) {
    var shadowStyle = host.shadowRoot.getElementById('ns-theme-override');
    if (shadowStyle) shadowStyle.textContent = isLight ? REPORT_THEME_LIGHT : REPORT_THEME_DARK;
  }
}

function updateThemeButton() {
  var btn = document.getElementById('theme-toggle');
  var isLight = document.documentElement.classList.contains('light');
  btn.textContent = isLight ? '\u2600' : '\u263E';
  btn.title = isLight ? 'Switch to dark mode' : 'Switch to light mode';
}

// ── Router ─────────────────────────────────────────────────────────────────

function route() {
  try {
    var hash = location.hash.slice(1) || '/sessions';
    logInfo('Navigate:', hash);
    var parts = hash.split('?');
    var path = parts[0];
    var params = new URLSearchParams(parts[1] || '');

    document.querySelectorAll('.nav-link').forEach(function(el) {
      el.classList.toggle('active', hash.startsWith('#' + el.getAttribute('href').slice(1)) ||
        path.startsWith('/' + el.dataset.page));
    });
    updateStatsNavLabel();

    // Toggle report-view mode on body to kill outer scroll. Frames view
    // (/sessions/{sid}/frames) is its own page, not a report iframe.
    var isReport      = path.match(/^\/sessions\/[^/]+$/);
    var isFrames      = path.match(/^\/sessions\/([^/]+)\/frames$/);
    var isTargetFrames= path.match(/^\/targets\/([^/]+)\/frames$/);
    var isProjectFrames=path.match(/^\/projects\/([^/]+)\/frames$/);
    if (isReport) {
      document.body.classList.add('report-view');
      // Shell is the scroll container in report-view; reset it so content
      // always starts at top regardless of prior body scroll position.
      // window.scrollTo is unreliable on iOS Safari after a hashchange.
      var shellEl = document.querySelector('.shell');
      if (shellEl) shellEl.scrollTop = 0;
    }
    // Leaving report view: chrome cleanup (body class, header pills,
    // --header-h) is deferred to the destination renderer's first paint via
    // exitReportView() so the prior report doesn't visibly collapse while
    // the destination data is still loading.

    if (path === '/sessions') {
      renderSessionList(params);
    } else if (isFrames) {
      renderFramesGallery({ kind: 'session', id: decodeURIComponent(isFrames[1]), params: params });
    } else if (isTargetFrames) {
      renderFramesGallery({ kind: 'target', id: decodeURIComponent(isTargetFrames[1]), params: params });
    } else if (isProjectFrames) {
      renderFramesGallery({ kind: 'project', id: decodeURIComponent(isProjectFrames[1]), params: params });
    } else if (isReport) {
      renderSessionDetail(path.split('/')[2], params);
    } else if (path === '/stats') {
      renderStats(params);
    } else if (path === '/settings') {
      renderSettingsPage();
    } else {
      renderSessionList(params);
    }
    repositionViewToggle();
  } catch (e) { logError('route() crashed at', location.hash, e); }
}

function updateStatsNavLabel() {
  var link = document.querySelector('.nav-link[data-page="stats"]');
  if (!link) return;
  if (statsTsStatus === 'available') link.textContent = 'Projects';
  else if (statsTsStatus !== null) link.textContent = 'Targets';
  // statsTsStatus === null means not yet loaded — leave as HTML default ("Targets")
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
    var gated = t.disabled ? ' ts-gated' : '';
    html += '<button class="stats-tab-btn' + cls + gated + '" data-tab="' + t.id + '"' + (t.disabled ? ' title="No projects yet"' : '') + '>' + esc(t.label) + '</button>';
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
      safeSetItem('ns-stats-tab', tabId);
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

function plural(n, word) { return n === 1 ? word : word + 's'; }

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
  // Additional project badges (multi-project assignment)
  if (t.additionalProjects && t.additionalProjects.length > 0) {
    t.additionalProjects.forEach(function(ap) {
      html += '<span class="target-card-project-pill" title="' + esc(ap.name || '') + '">' +
              esc(ap.name || 'Project') + '</span>';
    });
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
  if (statsTsStatus === 'available') html += '<button type="button" class="target-card-assign-btn' + (!statsTsProjects || statsTsProjects.length === 0 ? ' ts-gated' : '') + '" data-target="' + esc(t.target) + '" title="Assign to project">&#x1F4C1;</button>';
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
          '</div><div class="stat-label">' + plural(sessionCount, 'Session') + '</div></div>';
  html += '<div class="stat-box"><div class="stat-value">' + esc(String(hours)) +
          '<span class="unit">h</span></div><div class="stat-label">Integration</div></div>';
  html += '<div class="stat-box"><div class="stat-value">' + esc(String(frames)) +
          '</div><div class="stat-label">' + plural(frames, 'Frame') + '</div></div>';
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
  safeSetItem('ns-targets-status-filter', JSON.stringify(arr));
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
  safeSetItem('ns-targets-type-filter', JSON.stringify(arr));
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
  var tsNoProjects = tsAvail && (!statsTsProjects || statsTsProjects.length === 0);

  var html = '<div class="targets-control-bar">';
  html += '<div class="targets-sort-bar"><span class="targets-sort-label">Sort</span>';
  // Group by project first — commonly used
  if (tsAvail || (statsTsProjects && statsTsProjects.some(function(p) { return p.isCustom; }))) {
    var grpCls = 'targets-group-pill' + (groupBy === 'project' ? ' active' : '') + (tsNoProjects ? ' ts-gated' : '');
    html += '<button type="button" class="' + grpCls + '" data-action="toggle-group">Group by project</button>';
  }
  TARGET_SORT_OPTIONS.forEach(function(opt) {
    if (opt.key === 'type' && !tsAvail) return;
    var cls = 'targets-sort-pill' + (opt.key === sortKey ? ' active' : '') + (opt.key === 'type' && tsNoProjects ? ' ts-gated' : '');
    html += '<button type="button" class="' + cls + '" data-sort-key="' + opt.key + '">' + esc(opt.label) + '</button>';
  });
  html += '</div>';
  if (tsAvail) {
    var enabledStates = getTargetStatusFilter();
    var enabledTypes  = getTargetTypeFilter();
    var allStatesOn = TS_STATE_ORDER.every(function(s) { return enabledStates.indexOf(s) >= 0; });
    var allTypesOn  = TARGET_TYPE_OPTIONS.every(function(o) { return enabledTypes.indexOf(o.key) >= 0; });
    html += '<div class="targets-filter-row' + (tsNoProjects ? ' ts-gated' : '') + '"><span class="targets-sort-label">Filter</span>';
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
      safeSetItem('ns-targets-sort', key);
      renderStatsTabContent('targets');
    });
  });
  var grpBtn = document.querySelector('.targets-group-pill');
  if (grpBtn) {
    grpBtn.addEventListener('click', function() {
      safeSetItem('ns-targets-group', getTargetGroupBy() === 'project' ? 'flat' : 'project');
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
      safeSetItem('ns-show-fov', showFovOverlay ? 'true' : 'false');
      document.querySelectorAll('.mosaic-fov-svg').forEach(function(svg) {
        svg.style.display = showFovOverlay ? '' : 'none';
      });
      document.querySelectorAll('.card-thumb-wrap svg, .target-card-thumb svg, .pdp-multi-thumb-cell svg, #pdp-thumb-wrap svg, #tdp-hero-wrap svg').forEach(function(svg) {
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
  var containerType = info.isMosaic ? 'Mosaic' : (Math.max(info.targetCount, info.targets.length) > 1 ? 'Multi' : 'Single');
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
            '</div><div class="stat-label">' + plural(totalFrames, 'Frame') + '</div></div>';
    html += '<div class="stat-box"><div class="stat-value">' + totalSessions +
            '</div><div class="stat-label">' + plural(totalSessions, 'Session') + '</div></div>';
    html += '</div>'; // .targets-project-stat-boxes
  } else if (info.targets.length >= 2) {
    // Non-mosaic multi-target — 2x2 grid of target thumbnails inside the standard thumb-wrap
    html += '<div class="targets-project-thumb-col">';
    html += '<div class="targets-project-thumb-wrap">';
    html += '<div class="targets-project-thumb-grid">';
    info.targets.forEach(function(t) {
      var tInitial = t.target ? t.target.charAt(0).toUpperCase() : '?';
      html += '<div class="targets-project-thumb-cell target-card-thumb" data-session-id="' +
              esc(t.latestSessionId || '') + '" data-target="' + esc(t.target || '') + '">';
      html += '<span class="thumb-placeholder">' + esc(tInitial) + '</span>';
      html += '</div>';
    });
    html += '</div>'; // .targets-project-thumb-grid
    if (lastImaged) {
      html += '<div class="targets-project-last-imaged">Last imaged ' + fmtRelativeTime(lastImaged) + '</div>';
    }
    html += '</div>'; // .targets-project-thumb-wrap
    html += '</div>'; // .targets-project-thumb-col
  } else {
    // Non-mosaic single target — single thumbnail
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
  }

  if (!info.isMosaic) {
    var avgHFR = 0, hfrCount = 0;
    info.targets.forEach(function(t) {
      if (t.avgHFR) { avgHFR += t.avgHFR; hfrCount++; }
    });

    html += '<div class="targets-project-stat-boxes">';
    html += '<div class="stat-box"><div class="stat-value">' + totalSessions +
            '</div><div class="stat-label">' + plural(totalSessions, 'Session') + '</div></div>';
    html += '<div class="stat-box"><div class="stat-value">' + totalHours.toFixed(1) +
            '<span class="unit">h</span></div><div class="stat-label">Integration</div></div>';
    html += '<div class="stat-box"><div class="stat-value">' + totalFrames +
            '</div><div class="stat-label">' + plural(totalFrames, 'Frame') + '</div></div>';
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
    // Skip targets excluded from their native project
    var excl = (statsTargetExclusions || {})[guid] || [];
    var isExcluded = excl.indexOf((t.target || '').toLowerCase()) >= 0;
    if (!isExcluded) {
      if (!containerMap[guid]) {
        containerMap[guid] = { guid: guid, name: proj.name || 'TS Project',
          state: proj.state || 'Draft', isMosaic: !!proj.isMosaic,
          targetCount: proj.targetCount || 1, targets: [] };
      }
      containerMap[guid].targets.push(t);
    }
    // Multi-project: also add to additional project containers
    var addedElsewhere = false;
    if (t.additionalProjects) {
      t.additionalProjects.forEach(function(ap) {
        if (!ap.guid) return;
        if (!containerMap[ap.guid]) {
          containerMap[ap.guid] = { guid: ap.guid, name: ap.name || 'Project',
            state: ap.state || 'Draft', isMosaic: !!ap.isMosaic,
            targetCount: ap.targetCount || 1, targets: [],
            isCustom: !!ap.isCustom };
        }
        containerMap[ap.guid].targets.push(t);
        addedElsewhere = true;
      });
    }
    // Excluded from native project and not assigned elsewhere → unassigned
    if (isExcluded && !addedElsewhere) unassigned.push(t);
  });

  var enabledTypes = getTargetTypeFilter();
  var items = [];
  Object.keys(containerMap).forEach(function(guid) {
    var grp = containerMap[guid];
    if (enabled.indexOf(grp.state) < 0) return; // state filtered
    // Use actual target count (may exceed TS targetCount due to multi-project assignments)
    var effectiveCount = Math.max(grp.targetCount, grp.targets.length);
    var pType = projectType(grp.isMosaic, effectiveCount);
    if (enabledTypes.indexOf(pType) < 0) return; // type filtered
    if (!grp.isMosaic && effectiveCount <= 1) {
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
  // Collapse button toggles nearest collapsible ancestor (target card wins
  // over its enclosing project container when both are present).
  document.querySelectorAll('.targets-project-collapse-btn').forEach(function(btn) {
    btn.addEventListener('click', function(e) {
      e.stopPropagation();
      var c = btn.closest('.target-card, .targets-project-container');
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
var TDP_FILTER_STACK_ORDER = ['L', 'R', 'G', 'B', 'H', 'S', 'O', 'N'];

function tdpFmtDuration(mins) {
  if (!mins || mins <= 0) return '--';
  var h = Math.floor(mins / 60);
  var m = Math.round(mins % 60);
  if (h === 0) return m + 'm';
  if (m === 0) return h + 'h';
  return h + 'h ' + m + 'm';
}
function tdpFmtFilterSecs(secs) {
  if (secs == null || isNaN(secs) || secs < 0) return '--';
  if (secs < 60) return Math.round(secs) + 's';
  if (secs < 3600) return Math.round(secs / 60) + 'm';
  return (secs / 3600).toFixed(1) + 'h';
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
        return '<tr class="tdp-filter-subrow" data-for="' + idx + '" style="display:none">' +
          '<td></td>' +
          '<td class="pdp-subrow-integration">' + filterTypePill(f.filter) + '<span>' + esc(tdpFmtFilterSecs(f.integrationSeconds || 0)) + '</span></td>' +
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
    var pType = projectType(!!proj.isMosaic, proj.targetCount);
    var typeLabel = pType === 'single' ? 'Single' : pType === 'multi' ? 'Multi' : 'Mosaic';
    titlePills += '<span class="targets-project-type-badge">' + typeLabel + '</span>';
    titlePills += '<span class="tdp-project-state-pill" data-state="' + esc(proj.state || 'Draft') +
      '" data-project-guid="' + esc(proj.guid || '') + '" title="Click to override status">' +
      esc(proj.state || 'Draft') +
      (proj.stateSource === 'override' ? '<span class="override-mark" title="User override active"></span>' : '') +
      '</span>';
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
        '<div class="tdp-progress-hint">TS-reported progress · may include frames captured before Night Summary was active</div>' +
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
        '<div class="tdp-table-wrap">' +
          '<table class="tdp-table">' +
            '<thead><tr><th>Date</th><th>Integration</th><th>Frames</th><th>HFR</th><th>Guide</th><th>Moon</th><th></th></tr></thead>' +
            '<tbody>' + rows + '</tbody>' +
          '</table>' +
        '</div>' +
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

  // On narrow viewports, scroll the session table to its right edge so the
  // Moon column isn't hidden under the sticky View pill at scrollLeft=0.
  if (window.innerWidth <= 600) {
    var tableWrap = backdrop.querySelector('.tdp-table-wrap');
    if (tableWrap && tableWrap.scrollWidth > tableWrap.clientWidth) {
      tableWrap.scrollLeft = tableWrap.scrollWidth - tableWrap.clientWidth;
    }
  }

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

  // View report link → load session detail in shell with TDP context
  // so the back-button returns to this TDP modal (preserves Frames tab access).
  backdrop.querySelectorAll('.tdp-row-link').forEach(function(link) {
    link.addEventListener('click', function(e) {
      e.stopPropagation();
      var sid = link.getAttribute('data-session-id');
      if (!sid) return;
      // Don't close yet — modal stays visible until the report paints,
      // then renderSessionDetail dismisses it. Avoids a 1-frame gap where
      // the user would see the Stats page behind the closing modal.
      navigate('#/sessions/' + encodeURIComponent(sid) +
        '?from=tdp&target=' + encodeURIComponent(targetName));
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
      // Build the image and SVG via the DOM API so attribute-context injection
      // from API-controlled values (dataUri / fovSvg) is impossible. The earlier
      // string-concatenation approach was an XSS vector if the backend ever
      // returned crafted content (relevant for the Phase 2 cloud move).
      thumbEl.innerHTML = '';
      var img = document.createElement('img');
      img.setAttribute('src', match.dataUri);
      img.setAttribute('alt', targetName || '');
      thumbEl.appendChild(img);

      if (match.fovSvg) {
        try {
          var doc = new DOMParser().parseFromString(match.fovSvg, 'image/svg+xml');
          var svgEl = doc.documentElement;
          if (svgEl && svgEl.tagName && svgEl.tagName.toLowerCase() === 'svg') {
            sanitizeSvgInPlace(svgEl);
            svgEl.setAttribute('width', '100%');
            svgEl.setAttribute('height', '100%');
            svgEl.setAttribute('viewBox', '0 0 200 200');
            if (!showFovOverlay) svgEl.setAttribute('style', 'display:none');
            thumbEl.appendChild(document.importNode(svgEl, true));
          }
        } catch (_) { /* malformed SVG — drop silently */ }
      }
    }
  }

  if (thumbnailCache[latestSessionId]) {
    apply(thumbnailCache[latestSessionId]);
    return;
  }
  api('/api/sessions/' + latestSessionId + '/thumbnails').then(function(thumbs) {
    if (Array.isArray(thumbs) && thumbs.length > 0) thumbnailCache[latestSessionId] = thumbs;
    apply(thumbs);
  }).catch(function(e) { logWarn('loadTargetDetailThumb: thumbnail fetch failed', e); });
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

// Paint the TDP panel into an existing backdrop. Extracted so the cold path
// (fetch -> paint) and the preloaded path (paint now) share one impl.
function paintTargetDetailPanel(backdrop, data, targetName, latestSessionId, ts) {
  backdrop.innerHTML = renderTargetDetailPanel(data, targetName, ts);
  bindTargetDetailEvents(backdrop, targetName);
  loadTargetDetailThumb(targetName, latestSessionId);
  // Chart renders after the panel is in the DOM so we can measure available width.
  // Use rAF to ensure layout has settled (kpi grid, etc.).
  var sessions = data.sessions || [];
  requestAnimationFrame(function() { renderChartIntoPanel(backdrop, sessions); });
  // Re-render chart on window resize (debounced) so it stays full-width.
  // Detach any prior handler first; opening the panel twice would otherwise
  // attach a second listener and the close-time remove only catches the latest.
  if (_tdpResizeHandler) window.removeEventListener('resize', _tdpResizeHandler);
  _tdpResizeHandler = function() {
    if (_tdpResizeDebounce) clearTimeout(_tdpResizeDebounce);
    _tdpResizeDebounce = setTimeout(function() {
      renderChartIntoPanel(backdrop, sessions);
    }, 120);
  };
  window.addEventListener('resize', _tdpResizeHandler);
}

function openTargetDetail(targetName, latestSessionId, preloadedData) {
  if (!targetName) return;
  // Close any existing panel first
  closeTargetDetail();

  var backdrop = document.createElement('div');
  backdrop.id = 'tdp-backdrop';
  backdrop.className = 'tdp-backdrop';
  document.body.appendChild(backdrop);
  document.body.style.overflow = 'hidden';
  backdrop.addEventListener('touchmove', function(e) { if (e.target === backdrop) e.preventDefault(); }, { passive: false });
  _tdpKeyHandler = function(e) { if (e.key === 'Escape') closeTargetDetail(); };
  document.addEventListener('keydown', _tdpKeyHandler);

  var ts = findTsForTarget(targetName);

  // Preloaded path: paint full panel immediately. Used by back-navigation
  // from session detail where the caller pre-fetched the sessions list in
  // parallel with the stats page load. Skip fade-in so the modal is fully
  // opaque on the same paint cycle as Stats — otherwise the user sees a
  // 180ms flash of Stats underneath the still-transparent backdrop.
  if (preloadedData) {
    backdrop.classList.add('tdp-no-anim');
    paintTargetDetailPanel(backdrop, preloadedData, targetName, latestSessionId, ts);
    return;
  }

  // Cold path: show loading stub while fetching.
  backdrop.innerHTML = '<div class="tdp-modal" style="padding:40px;text-align:center;color:var(--text-tertiary);">Loading \u2026</div>';
  // Tentative close on backdrop click while loading
  var loadClickHandler = function(e) { if (e.target === backdrop) closeTargetDetail(); };
  backdrop.addEventListener('click', loadClickHandler);

  api('/api/stats/targets/' + encodeURIComponent(targetName) + '/sessions').then(function(data) {
    // If the user closed it while loading, bail out
    var current = document.getElementById('tdp-backdrop');
    if (!current || current !== backdrop) return;
    backdrop.removeEventListener('click', loadClickHandler);
    paintTargetDetailPanel(backdrop, data, targetName, latestSessionId, ts);
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
        bindPdpSessionTableEvents(backdrop, projectGuid, (data.project || {}).name);
      }

      // Resize handler for chart reflow
      if (_pdpResizeHandler) window.removeEventListener('resize', _pdpResizeHandler);
      _pdpResizeHandler = function() {
        if (_pdpResizeDebounce) clearTimeout(_pdpResizeDebounce);
        _pdpResizeDebounce = setTimeout(function() { renderPdpChart(backdrop, sessions); }, 120);
      };
      window.addEventListener('resize', _pdpResizeHandler);
    }).catch(function(e) { logWarn('openProjectDetail: session fetch failed', e); });
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
  var pType = projectType(!!proj.isMosaic, panels.length);
  var typeLabel = pType === 'single' ? 'Single' : pType === 'multi' ? 'Multi' : 'Mosaic';
  html += '<span class="targets-project-type-badge">' + typeLabel + '</span>';
  html += '<span class="target-card-ts-badge" data-state="' + esc(proj.state || '') + '">' + esc(proj.state || '') + '</span>';
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
    '</div><div class="pdp-kpi-label">' + (proj.isMosaic ? 'Panels' : 'Targets') + '</div></div>';
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
    html += '<div class="tdp-progress-hint">TS-reported progress · may include frames captured before Night Summary was active</div>';
    html += '</div>';
  }

  // ── 5. Per-panel cards ────────────────────────────────────────────────────
  html += '<div class="pdp-panels-section">';
  html += '<div class="pdp-section-title">' + (proj.isMosaic ? 'Panels' : 'Targets') + ' (' + panels.length + ')</div>';
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
        if (match.fovSvg) {
          var oldSvg = cell.querySelector('svg');
          if (oldSvg) oldSvg.remove();
          var fovHtml = match.fovSvg
            .replace(/width='\d+'/, "width='100%'")
            .replace(/height='\d+'/, "height='100%'")
            .replace("<svg ", "<svg viewBox='0 0 200 200' " + (showFovOverlay ? '' : "style='display:none' "));
          var fovDiv = document.createElement('div');
          fovDiv.innerHTML = fovHtml;
          cell.appendChild(fovDiv.firstChild);
        }
      }
    }

    if (thumbnailCache[sid]) {
      applyThumb(thumbnailCache[sid]);
    } else {
      api('/api/sessions/' + encodeURIComponent(sid) + '/thumbnails').then(function(thumbs) {
        if (Array.isArray(thumbs) && thumbs.length > 0) thumbnailCache[sid] = thumbs;
        applyThumb(thumbs);
      }).catch(function(e) { logWarn('loadPdpMultiThumbs: thumbnail fetch failed', e); });
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
      if (match.fovSvg) {
        var oldSvg = wrap.querySelector('svg');
        if (oldSvg) oldSvg.remove();
        var fovHtml = match.fovSvg
          .replace(/width='\d+'/, "width='100%'")
          .replace(/height='\d+'/, "height='100%'")
          .replace("<svg ", "<svg viewBox='0 0 200 200' " + (showFovOverlay ? '' : "style='display:none' "));
        var fovDiv = document.createElement('div');
        fovDiv.innerHTML = fovHtml;
        wrap.appendChild(fovDiv.firstChild);
      }
    }
  }

  if (thumbnailCache[sid]) {
    applyThumb(thumbnailCache[sid]);
  } else {
    api('/api/sessions/' + encodeURIComponent(sid) + '/thumbnails').then(function(thumbs) {
      if (Array.isArray(thumbs) && thumbs.length > 0) thumbnailCache[sid] = thumbs;
      applyThumb(thumbs);
    }).catch(function(e) { logWarn('loadPdpSingleThumb: thumbnail fetch failed', e); });
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
        '<span class="pdp-panel-filter-hrs">' + tdpFmtFilterSecs(f.totalSeconds != null ? f.totalSeconds : (f.totalHours || 0) * 3600) + '</span></span>';
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
      var firstCell = '<td></td>';
      if (showTargetCol) {
        var tgtName = fi < targets.length ? targets[fi] : '';
        firstCell = '<td class="pdp-target-subrow-name" colspan="2">' + esc(tgtName) + '</td>';
      }
      return '<tr class="tdp-filter-subrow" data-for="' + idx + '" style="display:none">' +
        firstCell +
        '<td class="pdp-subrow-integration">' + filterTypePill(f.filter) + '<span>' + esc(tdpFmtFilterSecs(f.integrationSeconds || 0)) + '</span></td>' +
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

function bindPdpSessionTableEvents(backdrop, projectGuid, projectName) {
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
  // View report link → load session detail in shell with PDP context
  // so the back-button returns to this PDP modal (preserves Frames tab access).
  backdrop.querySelectorAll('.pdp-session-table .tdp-row-link').forEach(function(link) {
    link.addEventListener('click', function(e) {
      e.stopPropagation();
      var sid = link.getAttribute('data-session-id');
      if (!sid) return;
      var qs = '?from=pdp';
      if (projectGuid) qs += '&pid=' + encodeURIComponent(projectGuid);
      if (projectName) qs += '&pname=' + encodeURIComponent(projectName);
      closeProjectDetail();
      navigate('#/sessions/' + encodeURIComponent(sid) + qs);
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
    bindPdpSessionTableEvents(backdrop, projectGuid, (projectData.project || {}).name);

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
    var pType = projectType(!!proj.isMosaic, proj.targetCount);
    var typeLabel = pType === 'single' ? 'Single' : pType === 'multi' ? 'Multi' : 'Mosaic';
    titlePills += '<span class="targets-project-type-badge">' + typeLabel + '</span>';
    titlePills += '<span class="tdp-project-state-pill" data-state="' + esc(proj.state || 'Draft') +
      '" data-project-guid="' + esc(proj.guid || '') + '">' +
      esc(proj.state || 'Draft') +
      (proj.stateSource === 'override' ? '<span class="override-mark" title="User override active"></span>' : '') +
      '</span>';
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
      progressHtml = '<div class="tdp-progress-section"><div class="tdp-project-progress-grid">' + goalRows + overallRow + '</div><div class="tdp-progress-hint">TS-reported progress · may include frames captured before Night Summary was active</div></div>';
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
    }).catch(function(e) { logWarn('loadTargetThumbnails: thumbnail fetch failed for', sid, e); });
  });
}

// ── Sessions List Page ─────────────────────────────────────────────────────

var sessionsCache = [];
var SESSION_PAGE_SIZE = 25;
var visibleSessionCount = SESSION_PAGE_SIZE;
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
var fpFrom = null, fpTo = null; // Flatpickr instances — destroyed/recreated on each list render
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
var sessionsV2Mode = true;  // false when ?sessionsV1=1
var heroSessionId = null;   // excluded from the historical list in V2

function getAllTargets() {
  var targets = {};
  sessionsCache.forEach(function(s) {
    s.targets.forEach(function(t) { targets[t] = true; });
  });
  return Object.keys(targets).sort();
}

function getSubtitleText() {
  var visible = sessionsCache.filter(function(s) { return !hiddenSessions[s.sessionId]; });
  var targets = {};
  visible.forEach(function(s) { s.targets.forEach(function(t) { targets[t] = true; }); });
  var tc = Object.keys(targets).length;
  var sc = visible.length;
  return tc + ' ' + plural(tc, 'target') + ' · ' + sc + ' ' + plural(sc, 'session');
}

function updateSubtitle() {
  var sub = document.getElementById('page-subtitle');
  if (sub) sub.textContent = getSubtitleText();
}

// ── Sessions V2: trophy case + hero card + historical expander ──────────────

function buildActivityWaveform(sessions) {
  if (!sessions || sessions.length < 2) return '';
  var dates = sessions.map(function(s) { return new Date(s.sessionStart).getTime(); });
  var minDRaw = Math.min.apply(null, dates), maxDRaw = Math.max.apply(null, dates);
  var minDObj = new Date(minDRaw), maxDObj = new Date(maxDRaw);
  var minD = new Date(minDObj.getFullYear(), minDObj.getMonth(), minDObj.getDate()).getTime();
  var maxD = new Date(maxDObj.getFullYear(), maxDObj.getMonth(), maxDObj.getDate()).getTime();
  var dateSpan = maxD - minD || 86400000;
  var maxInteg = Math.max.apply(null, sessions.map(function(s) { return s.totalIntegrationSeconds || 0; }));
  if (!maxInteg) return '';

  // Calendar-day granularity: same-night sessions collapse to one slot for bar-width calculation
  var uniqueDayMs = dates.map(function(t) {
    var d = new Date(t);
    return new Date(d.getFullYear(), d.getMonth(), d.getDate()).getTime();
  }).filter(function(t, i, arr) { return arr.indexOf(t) === i; }).sort(function(a, b) { return a - b; });
  var minGapMs = Infinity;
  for (var gi = 1; gi < uniqueDayMs.length; gi++) {
    var g = uniqueDayMs[gi] - uniqueDayMs[gi - 1];
    if (g > 0 && g < minGapMs) minGapMs = g;
  }
  // Extend span to today so right edge always = now
  var DAY_MS = 86400000;
  var todayObj = new Date();
  var todayMs = new Date(todayObj.getFullYear(), todayObj.getMonth(), todayObj.getDate()).getTime();
  if (todayMs > maxD) { maxD = todayMs; dateSpan = maxD - minD || DAY_MS; }
  var spanDays = Math.ceil(dateSpan / DAY_MS);

  var isMobile = window.innerWidth < 720;
  // Desktop: stretch waveform to fill the strip (shell max-width 1800, ~100px chrome for shell+strip padding)
  var availW = isMobile ? 680 : Math.max(680, Math.min(window.innerWidth, 1800) - 100);
  var W = Math.max(availW, spanDays * 8);
  var BAR_W = 6;
  var CHART_H = isMobile ? 90 : 64, LABEL_H = isMobile ? 33 : 28, H = CHART_H + LABEL_H;

  // Brightness ramp: near-black navy → bright sky blue (wide contrast)
  function barHeatColor(t) {
    return 'hsl(213,' + Math.round(50 + t * 40) + '%,' + Math.round(15 + t * 55) + '%)';
  }

  var barData = [];

  // Most-recent session (by start time) gets a gold "latest" highlight matching the latest-session card
  var latestStart = Math.max.apply(null, sessions.filter(function(s){return s.imageCount;}).map(function(s){return new Date(s.sessionStart).getTime();}));

  var svg = '<svg class="lifetime-waveform" viewBox="0 0 ' + W + ' ' + H + '" ';
  svg += 'width="' + W + '" height="' + H + '" style="display:block">';

  // Today marker
  var todayX = ((todayMs - minD) / dateSpan) * (W - BAR_W) + BAR_W / 2;
  svg += '<line x1="' + todayX.toFixed(1) + '" y1="0" x2="' + todayX.toFixed(1) + '" y2="' + CHART_H + '" stroke="rgba(120,170,255,0.2)" stroke-width="1"/>';

  svg += '<line x1="0" y1="' + CHART_H + '" x2="' + W + '" y2="' + CHART_H + '" stroke="rgba(255,255,255,0.1)" stroke-width="1"/>';

  // Adaptive x-axis: daily ticks on short spans, weekly on medium, monthly on long
  var tickEveryDays = 1; // MOCKUP: force daily ticks
  var tickLabelH = isMobile ? 12 : 12;  // labeled ticks
  var tickMajH   = isMobile ? 8  : 8;   // month-start unlabeled
  var tickMinH   = isMobile ? 3  : 3;   // minor unlabeled ticks
  var MIN_LABEL_GAP = isMobile ? 60 : 36;
  var MNAMES = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];
  var axD = new Date(minD);
  var prevLabelX = -99;
  while (axD.getTime() <= minD + dateSpan + DAY_MS) {
    var axX = ((axD.getTime() - minD) / dateSpan) * (W - BAR_W) + BAR_W / 2;
    if (axX < -4 || axX > W + 4) { axD.setDate(axD.getDate() + 1); continue; }
    var isMonthStart = axD.getDate() === 1;
    var daysIn = Math.round((axD.getTime() - minD) / DAY_MS);
    var isTick = isMonthStart || (daysIn % tickEveryDays === 0);
    if (!isTick) { axD.setDate(axD.getDate() + 1); continue; }
    if (isMonthStart) {
      svg += '<line x1="' + axX.toFixed(1) + '" y1="0" x2="' + axX.toFixed(1) + '" y2="' + CHART_H + '" stroke="rgba(255,255,255,0.05)" stroke-width="1" stroke-dasharray="3,4"/>';
    }
    // Compute label before tick so we can size the tick appropriately
    var labelText = null;
    if (isMonthStart || (daysIn % 7 === 0 && spanDays <= 300)) {
      labelText = MNAMES[axD.getMonth()] + ' ' + (isMonthStart ? 1 : axD.getDate());
      if (spanDays > 300 && isMonthStart && axD.getMonth() === 0)
        labelText += ' \'' + String(axD.getFullYear()).slice(2);
    }
    var willLabel = labelText && axX - prevLabelX > MIN_LABEL_GAP;
    var tickH = willLabel ? tickLabelH : (isMonthStart ? tickMajH : tickMinH);
    svg += '<line x1="' + axX.toFixed(1) + '" y1="' + CHART_H + '" x2="' + axX.toFixed(1) + '" y2="' + (CHART_H + tickH) + '"'
      + ' stroke="' + (willLabel ? 'rgba(120,170,255,0.5)' : 'rgba(120,170,255,0.38)') + '" stroke-width="1"/>';
    if (willLabel) {
      var anchor = axX < W * 0.07 ? 'start' : (axX > W * 0.93 ? 'end' : 'middle');
      svg += '<text class="lw-label" x="' + axX.toFixed(1) + '" y="' + (H - 3) + '" text-anchor="' + anchor + '">' + esc(labelText) + '</text>';
      prevLabelX = axX;
    }
    axD.setDate(axD.getDate() + 1);
  }

  // Bars — midnight-snapped position so ticks and bars align
  sessions.forEach(function(s) {
    if (!s.imageCount) return;
    var sd = new Date(s.sessionStart);
    var dayMs = new Date(sd.getFullYear(), sd.getMonth(), sd.getDate()).getTime();
    var t = (dayMs - minD) / dateSpan;
    var x = t * (W - BAR_W);
    var hours = (s.totalIntegrationSeconds || 0) / 3600;
    var normInteg = maxInteg > 0 ? hours / (maxInteg / 3600) : 0;
    var barH = Math.max(2, normInteg * (CHART_H - 4));
    var y = CHART_H - barH;
    var tgtStr = (s.targets && s.targets.length) ? s.targets.join(', ') : '';
    var isLatest = new Date(s.sessionStart).getTime() === latestStart;
    var hColor = isLatest ? 'rgb(212,160,106)' : barHeatColor(normInteg);
    var glowOpacity = (0.05 + normInteg * 0.25).toFixed(2);
    barData.push({x: (x + BAR_W / 2).toFixed(1), rx: x.toFixed(1), d: (s.sessionStart || '').substring(0, 10), i: s.totalIntegrationSeconds || 0, n: s.imageCount || 0, t: (s.targets || []).join(', '), sid: s.sessionId || '', hr: !!s.hasReport});
    if (isLatest) {
      // Layered gold glow matching .session-card--latest
      svg += '<rect x="' + (x - 6).toFixed(1) + '" y="' + (y - 4).toFixed(1) + '" width="' + (BAR_W + 12) + '" height="' + (barH + 8).toFixed(1) + '" fill="rgb(212,160,106)" opacity="0.12" rx="3"/>';
      svg += '<rect x="' + (x - 3).toFixed(1) + '" y="' + (y - 2).toFixed(1) + '" width="' + (BAR_W + 6) + '" height="' + (barH + 4).toFixed(1) + '" fill="rgb(212,160,106)" opacity="0.28" rx="2.5"/>';
    } else {
      svg += '<rect x="' + (x - 2).toFixed(1) + '" y="' + y.toFixed(1) + '" width="' + (BAR_W + 4) + '" height="' + barH.toFixed(1) + '" fill="' + hColor + '" opacity="' + glowOpacity + '" rx="2"/>';
    }
    var barClass = isLatest ? 'lw-bar lw-bar-latest' : 'lw-bar';
    var tipMeta = fmtDate(s.sessionStart) + ' \u00b7 ' + fmt(s.totalIntegrationSeconds || 0) + ' \u00b7 ' + (s.imageCount || 0) + ' images' + (isLatest ? ' \u00b7 latest' : '');
    var bar = '<rect class="' + barClass + '" x="' + x.toFixed(1) + '" y="' + y.toFixed(1) + '" width="' + BAR_W + '" height="' + barH.toFixed(1) + '" fill="' + hColor + '" rx="2" data-lw-tgt="' + esc(tgtStr) + '" data-lw-meta="' + esc(tipMeta) + '" data-lw-latest="' + (isLatest ? '1' : '0') + '"/>';
    if (!IS_TOUCH && s.sessionId && s.hasReport) {
      // In-app session view (settings panel + inline report), matching the
      // session-card click behavior. Previously opened the static report in
      // a new tab, which felt jarringly different from clicking a card.
      // Gated on !IS_TOUCH only (was also gated on !isMobile, which broke
      // click-through for non-touch desktop windows narrower than 720px).
      svg += '<a href="#/sessions/' + encodeURIComponent(s.sessionId) + '">' + bar + '</a>';
    } else {
      svg += bar;
    }
  });

  svg += '</svg>';
  var barsJson = JSON.stringify(barData).replace(/"/g, '&quot;');
  svg = svg.replace('class="lifetime-waveform"', 'class="lifetime-waveform" data-bars="' + barsJson + '"');
  return svg;
}

function buildCalendarHeatmap(sessions) {
  if (!sessions || !sessions.length) return '';
  var allTs = sessions.map(function(s) { return new Date(s.sessionStart).getTime(); });
  var minTs = Math.min.apply(null, allTs), maxTs = Math.max.apply(null, allTs);

  // Pad data span modestly: little data → short range; lots of data → up to 52 weeks.
  var nowTs = Date.now();
  if (maxTs < nowTs) maxTs = nowTs;
  var dataSpanMs = maxTs - minTs;
  var WEEK_MS = 7 * 86400000;
  // Minimum 8 weeks of scope so the chart has some room to breathe.
  var MIN_SPAN = 8 * WEEK_MS;
  // Cap scope at 52 weeks for heavy users.
  var MAX_SPAN = 52 * WEEK_MS;
  var desiredSpan = Math.max(MIN_SPAN, Math.min(MAX_SPAN, dataSpanMs + 2 * WEEK_MS));
  if (dataSpanMs < desiredSpan) minTs = maxTs - desiredSpan;

  // Snap to week boundaries (Sun → Sat)
  var startD = new Date(minTs);
  startD = new Date(startD.getFullYear(), startD.getMonth(), startD.getDate());
  startD.setDate(startD.getDate() - startD.getDay());
  var endD = new Date(maxTs);
  endD = new Date(endD.getFullYear(), endD.getMonth(), endD.getDate());
  endD.setDate(endD.getDate() + (6 - endD.getDay()));

  var totalWeeks = Math.round((endD - startD) / WEEK_MS) + 1;
  var DOW_LABELS = ['Sun','Mon','Tue','Wed','Thu','Fri','Sat'];
  var MNAMES = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];
  var DOW_W = 28, TOP_H = 18;

  // Auto-size cells: few weeks → larger cells fill width; many weeks → small cells. Mobile capped at 680.
  var isMobileCal = typeof window !== 'undefined' && window.innerWidth < 720;
  var TARGET_W = isMobileCal ? 680 : Math.max(680, Math.min(window.innerWidth || 1400, 1800) - 100);
  var CELL_MAX = isMobileCal ? 20 : 22;
  var GAP = 2;
  var CELL = Math.max(9, Math.min(CELL_MAX, Math.floor((TARGET_W - DOW_W - GAP * (totalWeeks - 1)) / totalWeeks)));
  GAP = Math.max(1, Math.min(3, Math.floor(CELL / 5)));
  var STEP = CELL + GAP;
  var svgW = DOW_W + totalWeeks * STEP;
  var svgH = TOP_H + 7 * STEP;

  // Build day buckets: total integration + best session for click-through
  var dayMap = {};
  var dayImgMap = {};
  var daySessionStart = {};
  var sessionMap = {};
  var latestStart = 0, latestDayKey = null;
  sessions.forEach(function(s) {
    if (!s.sessionStart) return;
    var m = String(s.sessionStart).match(/^(\d{4})-(\d{2})-(\d{2})/);
    if (!m) return;
    var dk = m[1] + '-' + m[2] + '-' + m[3];
    dayMap[dk] = (dayMap[dk] || 0) + (s.totalIntegrationSeconds || 0);
    dayImgMap[dk] = (dayImgMap[dk] || 0) + (s.imageCount || 0);
    var startMs = new Date(s.sessionStart).getTime();
    if (!daySessionStart[dk] || startMs < daySessionStart[dk]) daySessionStart[dk] = startMs;
    if (startMs > latestStart) { latestStart = startMs; latestDayKey = dk; }
    var secs = s.totalIntegrationSeconds || 0, imgs = s.imageCount || 0;
    var cur = sessionMap[dk];
    if (s.sessionId && s.hasReport && (!cur || secs > cur.bestSecs || (secs === cur.bestSecs && imgs > cur.bestImgs))) {
      sessionMap[dk] = { id: s.sessionId, targets: s.targets || [], bestSecs: secs, bestImgs: imgs };
    } else if (!cur) {
      sessionMap[dk] = { id: null, targets: s.targets || [], bestSecs: secs, bestImgs: imgs };
    }
  });

  var maxSecs = 0;
  for (var k in dayMap) { if (dayMap[k] > maxSecs) maxSecs = dayMap[k]; }
  if (!maxSecs) return '';

  function cellColor(secs) {
    if (!secs) return 'rgba(255,255,255,0.05)';
    var norm = Math.pow(secs / maxSecs, 0.55);
    var sat = Math.round(42 + norm * 50);
    var lit = Math.round(16 + norm * 58);
    return 'hsl(215,' + sat + '%,' + lit + '%)';
  }

  var cellData = [];
  var svgBody = '';

  // DOW labels (all 7)
  DOW_LABELS.forEach(function(label, i) {
    svgBody += '<text class="lifetime-heatmap-dow" x="' + (DOW_W - 4) + '" y="' + (TOP_H + i * STEP + Math.floor(CELL * 0.75)) + '" text-anchor="end">' + esc(label) + '</text>';
  });

  // Cells + month labels
  var prevMonth = -1;
  for (var w = 0; w < totalWeeks; w++) {
    for (var dow = 0; dow < 7; dow++) {
      var cellDate = new Date(startD.getTime() + (w * 7 + dow) * 86400000);
      if (cellDate > endD) continue;
      var mo = cellDate.getMonth(), yr = cellDate.getFullYear(), da = cellDate.getDate();
      var dk = yr + '-' + String(mo + 1).padStart(2, '0') + '-' + String(da).padStart(2, '0');
      var cx = DOW_W + w * STEP;
      var cy = TOP_H + dow * STEP;
      var secs = dayMap[dk] || 0;
      var sessInfo = sessionMap[dk];
      var tgtStr = (sessInfo && sessInfo.targets && sessInfo.targets.length) ? sessInfo.targets.join(', ') : '';
      var isLatest = secs > 0 && dk === latestDayKey;
      var imgs = dayImgMap[dk] || 0;
      var tipMeta = secs
        ? fmtDate(dk) + ' \u00b7 ' + fmt(secs) + ' \u00b7 ' + imgs + ' image' + (imgs === 1 ? '' : 's') + (isLatest ? ' \u00b7 latest' : '')
        : fmtDate(dk) + ' \u00b7 no session';
      var clickable = sessInfo && sessInfo.id;
      var fillColor = isLatest ? 'rgb(212,160,106)' : cellColor(secs);
      if (isLatest) {
        svgBody += '<rect x="' + (cx - 4) + '" y="' + (cy - 4) + '" width="' + (CELL + 8) + '" height="' + (CELL + 8) + '" fill="rgb(212,160,106)" opacity="0.12" rx="3"/>';
        svgBody += '<rect x="' + (cx - 2) + '" y="' + (cy - 2) + '" width="' + (CELL + 4) + '" height="' + (CELL + 4) + '" fill="rgb(212,160,106)" opacity="0.28" rx="2.5"/>';
      }
      var rect = '<rect class="lifetime-heatmap-cell lw-bar' + (clickable ? ' is-clickable' : '') + (isLatest ? ' lw-bar-latest' : '') + '" x="' + cx + '" y="' + cy + '" width="' + CELL + '" height="' + CELL + '" rx="2" fill="' + fillColor + '" data-lw-tgt="' + esc(tgtStr) + '" data-lw-meta="' + esc(tipMeta) + '" data-lw-latest="' + (isLatest ? '1' : '0') + '"/>';
      if (clickable && !IS_TOUCH) {
        // In-app session view matches session-card click behavior — was
        // opening the static /report in a new tab, now goes through the SPA.
        svgBody += '<a href="#/sessions/' + encodeURIComponent(sessInfo.id) + '">' + rect + '</a>';
      } else {
        svgBody += rect;
      }
      if (secs > 0) {
        cellData.push({x: cx, y: cy, w: CELL, h: CELL, d: dk, i: secs, n: imgs, t: tgtStr,
          sid: (sessInfo && sessInfo.id) || '', hr: !!(sessInfo && sessInfo.id)});
      }
      if (dow === 0 && mo !== prevMonth) {
        var mlabel = MNAMES[mo] + (mo === 0 ? ' ' + yr : '');
        svgBody += '<text class="lifetime-heatmap-month" x="' + cx + '" y="' + (TOP_H - 5) + '">' + esc(mlabel) + '</text>';
        prevMonth = mo;
      }
    }
  }

  var svg = '<svg class="lifetime-calendar" viewBox="0 0 ' + svgW + ' ' + svgH + '" ' +
    'preserveAspectRatio="xMinYMid meet" ' +
    'width="' + svgW + '" height="' + svgH + '" ' +
    'data-cells=\'' + JSON.stringify(cellData).replace(/'/g, '&#39;') + '\' ' +
    'style="max-width:100%;height:auto">' + svgBody + '</svg>';
  return svg;
}

function toggleLifetimeView(btn, view) {
  var strip = btn.closest('.lifetime-strip');
  strip.querySelectorAll('.lv-toggle-btn').forEach(function(b) { b.classList.remove('lv-toggle-active'); });
  btn.classList.add('lv-toggle-active');
  strip.querySelector('.lifetime-waveform-slot').style.display = view === 'waveform' ? '' : 'none';
  strip.querySelector('.lifetime-calendar-slot').style.display = view === 'calendar' ? '' : 'none';
}

// Cached sessions used to rebuild the activity waveform on window resize.
// The waveform SVG is rendered with an explicit numeric width derived from
// window.innerWidth at build time, so it doesn't reflow on its own.
var __lwCachedSessions = null;
var __lwResizeTimer = null;
window.addEventListener('resize', function() {
  if (__lwResizeTimer) clearTimeout(__lwResizeTimer);
  __lwResizeTimer = setTimeout(function() {
    if (!__lwCachedSessions) return;
    var wrap = document.querySelector('.lifetime-waveform-slot .lw-scroll-wrap');
    if (!wrap) return;
    wrap.innerHTML = buildActivityWaveform(__lwCachedSessions);
    // Reinit the touch scrubber against the freshly rendered SVG (no-op on
    // non-touch). The lw-bar-tip hover handler is delegated on document so
    // it doesn't need rewiring.
    var strip = wrap.closest('.lifetime-strip');
    if (strip && typeof initWaveformScrubber === 'function') initWaveformScrubber(strip);
  }, 200);
});

function renderLifetimeStrip(sessions) {
  if (!sessions || sessions.length === 0) return '';
  __lwCachedSessions = sessions;
  var totalSessions = sessions.length;
  var totalIntegSec = sessions.reduce(function(sum, s) { return sum + (s.totalIntegrationSeconds || 0); }, 0);
  var totalImages = sessions.reduce(function(sum, s) { return sum + (s.imageCount || 0); }, 0);
  var allTargets = {};
  sessions.forEach(function(s) { (s.targets || []).forEach(function(t) { allTargets[t] = true; }); });
  var targetCount = Object.keys(allTargets).length;

  var waveform = buildActivityWaveform(sessions);
  var calendar = buildCalendarHeatmap(sessions);
  var hasChart = !!(waveform || calendar);
  var html = '<div class="lifetime-strip' + (hasChart ? ' lifetime-strip-expandable' : '') + '"'
    + (hasChart ? ' onclick="toggleLifetimeExpand(this)"' : '') + '>';
  html += '<div class="lifetime-stats-row">';
  html += '<div class="lifetime-stats">';
  html += '<div class="card-stat lifetime-card-stat"><div class="card-stat-value">' + totalSessions + '</div><div class="card-stat-label">' + plural(totalSessions, 'Session') + '</div></div>';
  html += '<div class="card-stat lifetime-card-stat"><div class="card-stat-value">' + targetCount + '</div><div class="card-stat-label">' + plural(targetCount, 'Target') + '</div></div>';
  html += '<div class="card-stat lifetime-card-stat"><div class="card-stat-value">' + totalImages + '</div><div class="card-stat-label">' + plural(totalImages, 'Image') + '</div></div>';
  html += '<div class="card-stat lifetime-card-stat"><div class="card-stat-value">' + (totalIntegSec / 3600).toFixed(1) + '<span class="card-stat-unit">h</span></div><div class="card-stat-label">Integration</div></div>';
  html += '</div>';
  html += '<div class="lv-toggle">';
  html += '<button class="lv-toggle-btn lv-toggle-active" onclick="toggleLifetimeView(this,\'waveform\')">Bar</button>';
  html += '<button class="lv-toggle-btn" onclick="toggleLifetimeView(this,\'calendar\')">Calendar</button>';
  html += '</div>';
  html += '</div>';
  if (hasChart) html += '<div class="lifetime-strip-handle"></div>';
  if (waveform) html += '<div class="lifetime-waveform-slot"><div class="lifetime-chart-label">Session Activity \u00b7 ' + esc(fmtActivityRange(sessions)) + '</div><div class="lw-scrubber-info"></div><div class="lw-scroll-wrap">' + waveform + '</div></div>';
  if (calendar) html += '<div class="lifetime-calendar-slot" style="display:none">' + calendar + '</div>';
  html += '</div>';
  return html;
}

function fmtActivityRange(sessions) {
  var MONTHS = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];
  var dates = sessions.map(function(s) { return s.sessionStart ? s.sessionStart.substring(0,10) : ''; }).filter(Boolean).sort();
  if (dates.length < 2) return dates.length ? dates[0].substring(0,7) : '';
  var f = new Date(dates[0] + 'T12:00:00'), l = new Date(dates[dates.length-1] + 'T12:00:00');
  var fm = MONTHS[f.getMonth()], lm = MONTHS[l.getMonth()];
  if (f.getFullYear() !== l.getFullYear())
    return fm + ' ' + f.getFullYear() + ' \u2013 ' + lm + ' ' + l.getFullYear();
  if (f.getMonth() !== l.getMonth())
    return fm + ' ' + f.getDate() + ' \u2013 ' + lm + ' ' + l.getDate() + ', ' + l.getFullYear();
  return fm + ' ' + f.getDate() + ' \u2013 ' + l.getDate() + ', ' + l.getFullYear();
}

function toggleLifetimeExpand(strip) {
  strip.classList.toggle('lifetime-strip--expanded');
  if (strip.classList.contains('lifetime-strip--expanded')) {
    var sw = strip.querySelector('.lw-scroll-wrap');
    if (sw) {
      var snap = function() { sw.scrollLeft = sw.scrollWidth; };
      requestAnimationFrame(function() { requestAnimationFrame(snap); });
      [50, 200].forEach(function(ms) { setTimeout(snap, ms); });
    }
  }
}

function positionPopup(infoEl, anchorX, refEl) {
  var w = infoEl.offsetWidth;
  var rawLeft = anchorX - w / 2;
  infoEl.style.left = Math.max(8, Math.min(rawLeft, window.innerWidth - w - 8)) + 'px';
  var r = refEl.getBoundingClientRect();
  infoEl.style.top = Math.max(0, r.top - infoEl.offsetHeight - 4) + 'px';
}

function initWaveformScrubber(container) {
  // Tie to touch capability rather than viewport size: tablets in landscape
  // and touch-screen laptops still want the long-press scrubber, while a
  // regular mouse-only desktop uses the existing hover crosshair.
  if (!IS_TOUCH) return;
  var slot = container.querySelector('.lifetime-waveform-slot');
  if (!slot) return;
  var svg = slot.querySelector('svg.lifetime-waveform');
  if (!svg) return;
  var bars = [];
  try { bars = JSON.parse(svg.getAttribute('data-bars') || '[]'); } catch (e) {}
  if (!bars.length) return;
  var info = slot.querySelector('.lw-scrubber-info');
  // Move popup to body so iOS scroll-container touch capture can't block it.
  if (info) document.body.appendChild(info);
  var barRects = Array.prototype.slice.call(svg.querySelectorAll('.lw-bar'));
  var currentBar = null;
  var currentBarData = null;
  var pinned = false;
  var MONTHS = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];

  function findNearest(clientX) {
    var rect = svg.getBoundingClientRect();
    var frac = (clientX - rect.left) / rect.width;
    var svgW = parseFloat(svg.getAttribute('width') || '680');
    var touchX = frac * svgW;
    var best = null, bestDist = Infinity;
    bars.forEach(function(b) {
      var d = Math.abs(parseFloat(b.x) - touchX);
      if (d < bestDist) { bestDist = d; best = b; }
    });
    return best;
  }

  function showAt(clientX) {
    if (pinned) return;
    var b = findNearest(clientX);
    if (!b) return;
    currentBarData = b;
    if (currentBar) currentBar.classList.remove('lw-bar-selected');
    currentBar = null;
    barRects.forEach(function(r) { if (r.getAttribute('x') === b.rx) currentBar = r; });
    if (currentBar) currentBar.classList.add('lw-bar-selected');
    if (info && b.d) {
      var dt = new Date(b.d + 'T12:00:00');
      var dateStr = MONTHS[dt.getMonth()] + ' ' + dt.getDate() + ', ' + dt.getFullYear();
      info.innerHTML =
        '<span class="lw-si-date">' + esc(dateStr) + '</span>' +
        '<span class="lw-si-stats">' + fmt(b.i) + ' \u00b7 ' + b.n + ' images</span>' +
        (b.t ? '<span class="lw-si-tgts">' + esc(b.t) + '</span>' : '');
      info.classList.add('lw-scrubber-active');
      var anchorX = currentBar ? currentBar.getBoundingClientRect().left + currentBar.getBoundingClientRect().width / 2 : clientX;
      positionPopup(info, anchorX, slot);
    }
  }

  function pin() {
    if (!currentBarData || !info) return;
    pinned = true;
    var b = currentBarData;
    var dt = new Date(b.d + 'T12:00:00');
    var dateStr = MONTHS[dt.getMonth()] + ' ' + dt.getDate() + ', ' + dt.getFullYear();
    info.innerHTML =
      '<span class="lw-si-date">' + esc(dateStr) + '</span>' +
      '<span class="lw-si-stats">' + fmt(b.i) + ' \u00b7 ' + b.n + ' images</span>' +
      (b.t ? '<span class="lw-si-tgts">' + esc(b.t) + '</span>' : '') +
      '<div class="lw-si-actions">' +
      (b.hr
        ? '<button class="lw-si-report-btn">Open Report \u2192</button>'
        : '<span class="lw-si-no-report">No report</span>') +
      '<button class="lw-si-dismiss">\u00d7</button>' +
      '</div>';
    info.classList.add('lw-scrubber-pinned');
    info.style.pointerEvents = 'auto';
    requestAnimationFrame(function() {
      var anchorX = currentBar ? currentBar.getBoundingClientRect().left + currentBar.getBoundingClientRect().width / 2 : window.innerWidth / 2;
      positionPopup(info, anchorX, slot);
    });
    var dismissBtn = info.querySelector('.lw-si-dismiss');
    if (dismissBtn) {
      dismissBtn.addEventListener('touchend', function(e) { e.stopPropagation(); e.preventDefault(); hide(); }, {passive: false});
      dismissBtn.addEventListener('click', function(e) { e.stopPropagation(); hide(); });
    }
    var reportBtn = info.querySelector('.lw-si-report-btn');
    if (reportBtn) {
      reportBtn.addEventListener('touchend', function(e) {
        e.stopPropagation();
        e.preventDefault();
        hide();
        navigate('#/sessions/' + b.sid);
      }, {passive: false});
      reportBtn.addEventListener('click', function(e) {
        e.stopPropagation();
        e.preventDefault();
        hide();
        navigate('#/sessions/' + b.sid);
      });
    }
    // Close on outside tap. Deferred one tick so the touchend that triggered
    // pin() doesn't fire its own synthetic click and immediately dismiss.
    setTimeout(function() {
      function outsideHandler() {
        if (pinned) hide();
        document.removeEventListener('click', outsideHandler);
      }
      document.addEventListener('click', outsideHandler);
    }, 0);
    // Dismiss on scroll — iOS position:fixed breaks once chart scrolls off-screen
    window.addEventListener('scroll', hide, { passive: true, capture: true, once: true });
  }

  function hide() {
    pinned = false;
    currentBarData = null;
    if (currentBar) { currentBar.classList.remove('lw-bar-selected'); currentBar = null; }
    if (info) {
      info.classList.remove('lw-scrubber-active');
      info.classList.remove('lw-scrubber-pinned');
      info.style.pointerEvents = '';
    }
  }

  if (info) info.addEventListener('click', function(e) { e.stopPropagation(); });

  var touchStartX = 0, touchStartY = 0, lastTouchX = 0;
  var scrubbing = false;
  var dismissing = false;
  var longPressTimer = null;
  var LONG_PRESS_MS = 280;

  function cancelLongPress() {
    if (longPressTimer) { clearTimeout(longPressTimer); longPressTimer = null; }
  }

  svg.addEventListener('touchstart', function(e) {
    dismissing = pinned;  // flag: this touch is dismissing, not selecting
    if (pinned) hide();
    touchStartX = lastTouchX = e.touches[0].clientX;
    touchStartY = e.touches[0].clientY;
    scrubbing = false;
    longPressTimer = setTimeout(function() {
      longPressTimer = null;
      dismissing = false; // committed to new scrub, not just dismissing old pin
      scrubbing = true;
      showAt(lastTouchX);
    }, LONG_PRESS_MS);
  }, {passive: true});

  svg.addEventListener('touchmove', function(e) {
    lastTouchX = e.touches[0].clientX;
    if (scrubbing) {
      e.preventDefault();
      showAt(lastTouchX);
    } else {
      var dx = Math.abs(lastTouchX - touchStartX);
      var dy = Math.abs(e.touches[0].clientY - touchStartY);
      if (dx > 8 || dy > 8) cancelLongPress();
    }
  }, {passive: false});

  svg.addEventListener('touchend', function(e) {
    cancelLongPress();
    if (scrubbing) {
      scrubbing = false;
      if (!dismissing) pin();
    } else if (!dismissing) {
      var dx = Math.abs(e.changedTouches[0].clientX - touchStartX);
      var dy = Math.abs(e.changedTouches[0].clientY - touchStartY);
      if (dx < 10 && dy < 10) { showAt(touchStartX); pin(); }
    }
    dismissing = false;
  });

  svg.addEventListener('touchcancel', function() {
    cancelLongPress();
    scrubbing = false;
    dismissing = false;
    hide();
  });
}

function initCalendarScrubber(container) {
  if (!IS_TOUCH) return;
  var slot = container.querySelector('.lifetime-calendar-slot');
  if (!slot) return;
  var svg = slot.querySelector('svg.lifetime-calendar');
  if (!svg) return;
  var cells = [];
  try { cells = JSON.parse(svg.getAttribute('data-cells') || '[]'); } catch (e) {}
  if (!cells.length) return;

  var latestCell = null;
  cells.forEach(function(c) { if (!latestCell || c.d > latestCell.d) latestCell = c; });

  var info = document.createElement('div');
  info.className = 'lw-scrubber-info';
  document.body.appendChild(info);

  var currentCell = null;
  var currentCellEl = null;
  var pinned = false;
  var MONTHS = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];

  function findNearest(clientX, clientY) {
    var svgRect = svg.getBoundingClientRect();
    var vb = (svg.getAttribute('viewBox') || '0 0 680 200').split(' ');
    var svgW = parseFloat(vb[2]) || 680, svgH = parseFloat(vb[3]) || 200;
    var tx = ((clientX - svgRect.left) / svgRect.width) * svgW;
    var ty = ((clientY - svgRect.top) / svgRect.height) * svgH;
    var best = null, bestDist = Infinity;
    cells.forEach(function(c) {
      var dx = (c.x + c.w / 2) - tx, dy = (c.y + c.h / 2) - ty;
      var d = Math.sqrt(dx * dx + dy * dy);
      if (d < bestDist) { bestDist = d; best = c; }
    });
    return best;
  }

  function getCellEl(c) {
    var rects = svg.querySelectorAll('.lw-bar');
    for (var i = 0; i < rects.length; i++) {
      if (parseFloat(rects[i].getAttribute('x')) === c.x && parseFloat(rects[i].getAttribute('y')) === c.y) return rects[i];
    }
    return null;
  }

  function renderCell(c, anchorX) {
    if (currentCellEl) currentCellEl.classList.remove('lw-bar-selected');
    currentCell = c;
    currentCellEl = getCellEl(c);
    if (currentCellEl) currentCellEl.classList.add('lw-bar-selected');
    var dt = new Date(c.d + 'T12:00:00');
    var dateStr = MONTHS[dt.getMonth()] + ' ' + dt.getDate() + ', ' + dt.getFullYear();
    info.innerHTML =
      '<span class="lw-si-date">' + esc(dateStr) + '</span>' +
      '<span class="lw-si-stats">' + fmt(c.i) + ' · ' + c.n + ' images</span>' +
      (c.t ? '<span class="lw-si-tgts">' + esc(c.t) + '</span>' : '');
    info.classList.add('lw-scrubber-active');
    var ax = anchorX != null ? anchorX : (currentCellEl ? currentCellEl.getBoundingClientRect().left + currentCellEl.getBoundingClientRect().width / 2 : window.innerWidth / 2);
    positionPopup(info, ax, slot);
  }

  function showAt(clientX, clientY) {
    if (pinned) return;
    var c = findNearest(clientX, clientY);
    if (c) renderCell(c, clientX);
  }

  function showCell(c) {
    if (pinned || !c) return;
    renderCell(c, null);
  }

  function pin() {
    if (!currentCell || !info) return;
    pinned = true;
    var c = currentCell;
    var dt = new Date(c.d + 'T12:00:00');
    var dateStr = MONTHS[dt.getMonth()] + ' ' + dt.getDate() + ', ' + dt.getFullYear();
    info.innerHTML =
      '<span class="lw-si-date">' + esc(dateStr) + '</span>' +
      '<span class="lw-si-stats">' + fmt(c.i) + ' · ' + c.n + ' images</span>' +
      (c.t ? '<span class="lw-si-tgts">' + esc(c.t) + '</span>' : '') +
      '<div class="lw-si-actions">' +
      (c.hr ? '<button class="lw-si-report-btn">Open Report →</button>' : '<span class="lw-si-no-report">No report</span>') +
      '<button class="lw-si-dismiss">×</button>' +
      '</div>';
    info.classList.add('lw-scrubber-pinned');
    info.style.pointerEvents = 'auto';
    requestAnimationFrame(function() {
      var ax = currentCellEl ? currentCellEl.getBoundingClientRect().left + currentCellEl.getBoundingClientRect().width / 2 : window.innerWidth / 2;
      positionPopup(info, ax, slot);
    });
    var dismissBtn = info.querySelector('.lw-si-dismiss');
    if (dismissBtn) {
      dismissBtn.addEventListener('touchend', function(e) { e.stopPropagation(); e.preventDefault(); hide(); }, {passive: false});
      dismissBtn.addEventListener('click', function(e) { e.stopPropagation(); hide(); });
    }
    var reportBtn = info.querySelector('.lw-si-report-btn');
    if (reportBtn) {
      reportBtn.addEventListener('touchend', function(e) {
        e.stopPropagation(); e.preventDefault(); hide(); navigate('#/sessions/' + c.sid);
      }, {passive: false});
      reportBtn.addEventListener('click', function(e) {
        e.stopPropagation(); e.preventDefault(); hide(); navigate('#/sessions/' + c.sid);
      });
    }
    setTimeout(function() {
      function outsideHandler() { if (pinned) hide(); document.removeEventListener('click', outsideHandler); }
      document.addEventListener('click', outsideHandler);
    }, 0);
    // Dismiss on scroll — iOS position:fixed breaks once chart scrolls off-screen
    window.addEventListener('scroll', hide, { passive: true, capture: true, once: true });
  }

  function hide() {
    pinned = false;
    currentCell = null;
    if (currentCellEl) { currentCellEl.classList.remove('lw-bar-selected'); currentCellEl = null; }
    info.classList.remove('lw-scrubber-active');
    info.classList.remove('lw-scrubber-pinned');
    info.style.pointerEvents = '';
  }

  info.addEventListener('click', function(e) { e.stopPropagation(); });

  var touchStartX = 0, touchStartY = 0, lastTouchX = 0, lastTouchY = 0;
  var scrubbing = false, dismissing = false, longPressTimer = null;
  var LONG_PRESS_MS = 280;

  function cancelLongPress() {
    if (longPressTimer) { clearTimeout(longPressTimer); longPressTimer = null; }
  }

  svg.addEventListener('touchstart', function(e) {
    dismissing = pinned;
    if (pinned) hide();
    touchStartX = lastTouchX = e.touches[0].clientX;
    touchStartY = lastTouchY = e.touches[0].clientY;
    scrubbing = false;
    longPressTimer = setTimeout(function() {
      longPressTimer = null;
      scrubbing = true;
      showCell(latestCell);
    }, LONG_PRESS_MS);
  }, {passive: true});

  svg.addEventListener('touchmove', function(e) {
    lastTouchX = e.touches[0].clientX;
    lastTouchY = e.touches[0].clientY;
    if (scrubbing) {
      e.preventDefault();
      showAt(lastTouchX, lastTouchY);
    } else {
      var dx = Math.abs(lastTouchX - touchStartX), dy = Math.abs(lastTouchY - touchStartY);
      if (dx > 8 || dy > 8) cancelLongPress();
    }
  }, {passive: false});

  svg.addEventListener('touchend', function(e) {
    cancelLongPress();
    if (scrubbing) {
      scrubbing = false;
      if (!dismissing) pin();
    } else if (!dismissing) {
      var dx = Math.abs(e.changedTouches[0].clientX - touchStartX);
      var dy = Math.abs(e.changedTouches[0].clientY - touchStartY);
      if (dx < 10 && dy < 10) { showAt(touchStartX, touchStartY); pin(); }
    }
    dismissing = false;
  });

  svg.addEventListener('touchcancel', function() {
    cancelLongPress(); scrubbing = false; dismissing = false; hide();
  });
}

function renderHeroSection(session) {
  var s = session;
  var sessionTimes = fmtTime(s.sessionStart) + ' \u2013 ' + fmtTime(s.sessionEnd);
  var targetsHtml = s.targets.length > 0
    ? s.targets.map(function(t, i) { return makeTargetBadge(t, i); }).join('')
    : '<span style="color:var(--text-quaternary);font-size:12px">No targets</span>';
  var badge = s.hasReport ? '' : '<span class="badge badge-red">No report</span>';
  var statsLine = '<span class="stat-val">' + s.imageCount + '</span> imgs' +
    ' &middot; <span class="stat-val">' + fmt(s.totalIntegrationSeconds) + '</span>' +
    ' &middot; HFR <span class="stat-val">' + fmtNum(s.avgHfr) + '</span>px' +
    ' &middot; FWHM <span class="stat-val">' + fmtNum(s.avgFwhm) + '</span>&Prime;' +
    ' &middot; <span class="stat-val">' + fmtNum(s.avgGuiding) + '&Prime;</span> guiding';
  var statBoxes = '<div class="card-stats">' +
    '<div class="card-stat card-stat-expandable stat-images" data-stat-type="images" data-session-id="' + s.sessionId + '"><div class="card-stat-value">' + s.imageCount + '</div><div class="card-stat-label">' + plural(s.imageCount, 'Image') + '</div></div>' +
    '<div class="card-stat card-stat-expandable stat-integration" data-stat-type="integration" data-session-id="' + s.sessionId + '"><div class="card-stat-value">' + fmt(s.totalIntegrationSeconds) + '</div><div class="card-stat-label">Integration</div></div>' +
    '<div class="card-stat stat-hfr"><div class="card-stat-value">' + fmtNum(s.avgHfr) + 'px</div><div class="card-stat-label">HFR</div></div>' +
    '<div class="card-stat stat-fwhm"><div class="card-stat-value">' + fmtNum(s.avgFwhm) + '&Prime;</div><div class="card-stat-label">FWHM</div></div>' +
    '<div class="card-stat stat-guiding"><div class="card-stat-value">' + fmtNum(s.avgGuiding) + '&Prime;</div><div class="card-stat-label">Guiding</div></div>' +
    '<div class="card-stat stat-moon">' + (s.moonPhase ? '<div class="card-stat-value">' + esc(s.moonPhase) + '</div><div class="card-stat-label">Moon</div>' : '') + '</div>' +
    '</div>';

  return '<div class="session-card session-card--latest" onclick="navigate(\'#/sessions/' + s.sessionId + '\')">' +
    '<button class="hide-btn" data-session="' + s.sessionId + '" onclick="event.stopPropagation();hideSession(this.dataset.session)" title="Hide this session">\u2715</button>' +
    '<div class="latest-label">Latest Session</div>' +
    '<div class="card-header">' +
      '<span class="session-date">' + fmtDate(s.sessionStart) + '</span>' +
      '<span class="session-times">' + sessionTimes + '</span>' +
      '<span class="card-targets-line" id="targets-' + s.sessionId + '">' + targetsHtml + '</span>' +
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
}

function renderHeroBreakdown(sessionId, detail) {
  var el = document.getElementById('hero-breakdown-' + sessionId);
  if (!el) return;
  var targets = (detail && detail.targets) ? detail.targets.filter(function(t) { return t.imageCount > 0 || t.integrationSeconds > 0; }) : [];
  if (targets.length < 2) { return; } // single target: breakdown redundant
  var rows = targets.map(function(t) {
    return '<tr><td class="hb-target">' + esc(t.target) + '</td><td class="hb-val">' + t.imageCount + '</td><td class="hb-val">' + fmt(t.integrationSeconds) + '</td></tr>';
  }).join('');
  el.innerHTML = '<table class="hero-breakdown-tbl"><thead><tr><th>Target</th><th>Imgs</th><th>Integration</th></tr></thead><tbody>' + rows + '</tbody></table>';
}

function renderSessionsV2(el, sub, params) {
  var fromVal = params ? (params.get('from') || '') : '';
  var toVal = params ? (params.get('to') || '') : '';
  var sortVal = params ? (params.get('sort') || 'date-desc') : 'date-desc';

  var sessions = sessionsCache;
  if (sessions.length === 0) {
    el.innerHTML = '<div class="empty">No sessions recorded yet.</div>';
    if (sub) sub.textContent = 'Sessions';
    return;
  }

  var byDate = sessions.slice().sort(function(a, b) { return b.sessionStart.localeCompare(a.sessionStart); });
  var hero = byDate[0];
  heroSessionId = hero.sessionId;

  var earlierCount = sessions.length - 1;
  var isOpen = localStorage.getItem('ns-sessions-expander') !== 'closed';

  if (sub) sub.textContent = getSubtitleText();

  var html = renderLifetimeStrip(sessions);
  var heroModeClass = cardViewMode === 'compact' ? ' cards-compact' : '';
  html += '<div class="cards-container' + heroModeClass + '">' + renderHeroSection(hero) + '</div>';
  if (earlierCount > 0) {
    html += '<div class="sessions-expander">' +
      '<button class="sessions-expander-btn" id="sessions-expander-btn">' +
        'Earlier sessions (' + earlierCount + ') ' + (isOpen ? '\u25b2' : '\u25bc') +
      '</button>' +
      '<div id="sessions-history"' + (isOpen ? '' : ' style="display:none"') + '>' +
      '</div>' +
    '</div>';
  }

  el.innerHTML = html;

  // Initial pass to fit any "INTEGRATION"-class long labels into their
  // narrow stat boxes — JS guarantees no overflow even at the tightest
  // viewports where CSS clamp() is just a hair too generous.
  el.querySelectorAll('.session-card').forEach(fitStatLabels);

  var scrollWrap = el.querySelector('.lw-scroll-wrap');
  if (scrollWrap) {
    var snapEnd = function() {
      scrollWrap.scrollLeft = scrollWrap.scrollWidth;
    };
    requestAnimationFrame(function() { requestAnimationFrame(snapEnd); });
    [50, 150, 400, 900].forEach(function(ms) { setTimeout(snapEnd, ms); });
    if (typeof ResizeObserver === 'function') {
      var ro = new ResizeObserver(function() {
        snapEnd();
        if (scrollWrap.scrollWidth > scrollWrap.clientWidth + 4) {
          ro.disconnect();
        }
      });
      try { ro.observe(scrollWrap.firstElementChild || scrollWrap); } catch (e) {}
      setTimeout(function() { try { ro.disconnect(); } catch (e) {} }, 2000);
    }
  }

  initWaveformScrubber(el);
  initCalendarScrubber(el);

  // Load hero card assets
  loadThumbnails([hero]);
  loadLiveStacks([hero]);
  loadAltitudeCharts([hero]);

  // Wire expander
  var expanderBtn = document.getElementById('sessions-expander-btn');
  if (expanderBtn) {
    expanderBtn.addEventListener('click', function() {
      var histEl = document.getElementById('sessions-history');
      var nowOpen = histEl.style.display !== 'none';
      if (nowOpen) {
        histEl.style.display = 'none';
        safeSetItem('ns-sessions-expander', 'closed');
        expanderBtn.textContent = 'Earlier sessions (' + earlierCount + ') \u25bc';
      } else {
        histEl.style.display = '';
        safeSetItem('ns-sessions-expander', 'open');
        expanderBtn.textContent = 'Earlier sessions (' + earlierCount + ') \u25b2';
        if (!histEl.querySelector('.filter-bar')) {
          doRenderList(histEl, null, fromVal, toVal, sortVal);
        }
      }
    });
  }

  // Render history list if expander starts open
  if (isOpen && earlierCount > 0) {
    var histEl = document.getElementById('sessions-history');
    doRenderList(histEl, null, fromVal, toVal, sortVal);
  }
}

function renderSessionList(params) {
  var el = document.getElementById('content');
  var sub = document.getElementById('page-subtitle');

  var fromVal = params ? (params.get('from') || '') : '';
  var toVal = params ? (params.get('to') || '') : '';
  var sortVal = params ? (params.get('sort') || 'date-desc') : 'date-desc';
  var v1 = params && params.get('sessionsV1') === '1';

  sessionsV2Mode = !v1;
  heroSessionId = null;

  function finish() {
    // Strip the prior page's report-view chrome at the paint moment so
    // we don't visibly collapse the iframe + lose header pills before
    // the sessions list is ready.
    exitReportView();
    getAllTargets().forEach(function(t) { selectedTargets[t] = true; });
    if (v1) {
      doRenderList(el, sub, fromVal, toVal, sortVal);
    } else {
      renderSessionsV2(el, sub, params);
    }
  }

  if (sessionsCache && sessionsCache.length) {
    finish();
    return;
  }

  var cancelLoader = deferLoader(el, 'Loading sessions...');
  api('/api/sessions').then(function(data) {
    cancelLoader();
    sessionsCache = data;
    logInfo('Sessions loaded:', data.length);
    finish();
  }).catch(function(err) {
    cancelLoader();
    logError('Failed to load sessions:', err.message);
    el.innerHTML = '<div class="error">Failed to load sessions: ' + esc(err.message) + '</div>';
  });
}

function doRenderList(el, sub, fromFilter, toFilter, sortBy, keepPage) {
  if (!keepPage) visibleSessionCount = SESSION_PAGE_SIZE;
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
        '<input type="text" id="filter-from" class="date-pill" placeholder="From" readonly' + (fromFilter ? ' value="' + esc(fromFilter) + '"' : '') + '>' +
        '<button class="date-clear" data-target="filter-from" title="Clear"' + (fromFilter ? '' : ' style="display:none"') + '>\u00d7</button>' +
      '</div>' +
      '<div class="date-input-wrap">' +
        '<input type="text" id="filter-to" class="date-pill" placeholder="To" readonly' + (toFilter ? ' value="' + esc(toFilter) + '"' : '') + '>' +
        '<button class="date-clear" data-target="filter-to" title="Clear"' + (toFilter ? '' : ' style="display:none"') + '>\u00d7</button>' +
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
    if (sessionsV2Mode && heroSessionId && s.sessionId === heroSessionId) return false;
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

  var visible = filtered.slice(0, visibleSessionCount);

  if (sub) sub.textContent = getSubtitleText();

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

  var cards = visible.map(function(s) {
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
      '<div class="card-stat card-stat-expandable stat-images" data-stat-type="images" data-session-id="' + s.sessionId + '"><div class="card-stat-value">' + s.imageCount + '</div><div class="card-stat-label">' + plural(s.imageCount, 'Image') + '</div></div>' +
      '<div class="card-stat card-stat-expandable stat-integration" data-stat-type="integration" data-session-id="' + s.sessionId + '"><div class="card-stat-value">' + fmt(s.totalIntegrationSeconds) + '</div><div class="card-stat-label">Integration</div></div>' +
      '<div class="card-stat stat-hfr"><div class="card-stat-value">' + fmtNum(s.avgHfr) + 'px</div><div class="card-stat-label">HFR</div></div>' +
      '<div class="card-stat stat-fwhm"><div class="card-stat-value">' + fmtNum(s.avgFwhm) + '&Prime;</div><div class="card-stat-label">FWHM</div></div>' +
      '<div class="card-stat stat-guiding"><div class="card-stat-value">' + fmtNum(s.avgGuiding) + '&Prime;</div><div class="card-stat-label">Guiding</div></div>' +
      '<div class="card-stat stat-moon">' + (s.moonPhase ? '<div class="card-stat-value">' + esc(s.moonPhase) + '</div><div class="card-stat-label">Moon</div>' : '') + '</div>' +
      '</div>';

    return '<div class="session-card" data-date="' + esc(s.sessionStart ? s.sessionStart.substring(0, 10) : '') + '" onclick="navigate(\'#/sessions/' + s.sessionId + '\')">' +
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

  var remaining = filtered.length - visible.length;
  var loadMoreHtml = remaining > 0
    ? '<div class="load-more-wrap">' +
        '<button class="load-more-btn">Load ' + Math.min(SESSION_PAGE_SIZE, remaining) + ' more</button>' +
        '<span class="load-more-label">Showing ' + visible.length + ' of ' + filtered.length + '</span>' +
      '</div>'
    : '';

  el.innerHTML = filterHtml + '<div class="cards-container' + modeClass + '" style="' + fadeStyle + '">' + cards + '<div class="date-filter-empty empty" style="display:none">No sessions match the date filter.</div></div>' + loadMoreHtml;
  bindListEvents();

  loadLiveStacks(visible);

  if (!initialLoadDone && cardViewMode === 'expanded') {
    // First load only: hold opacity:0 until all assets are fetched, then reveal together
    function revealContainer() {
      var container = el.querySelector('.cards-container');
      if (container) container.style.opacity = '1';
    }
    var pending = loadThumbnails(visible).concat(loadAltitudeCharts(visible));
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
    loadThumbnails(visible);
    // Re-render cached charts directly (works even after navigation destroyed the old divs)
    visible.forEach(function(s) {
      if (altitudeChartCache[s.sessionId]) {
        renderAltitudeChart(s, altitudeChartCache[s.sessionId]);
      }
    });
    // IO observer for any uncached charts (lazy-loads as they scroll into view)
    setupAltitudeObserver(visible);
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
  // Live-stack badges are only wired once .card-thumb-wrap elements exist.
  // If the livestack API resolved before thumbnails rendered, retro-wire now.
  if (livestackMap[s.sessionId]) {
    wireLiveStackBadges(s, livestackMap[s.sessionId]);
  }
  setupThumbsScrollMode(el);
  // Fit stat labels for this card (CSS clamp gets close, but truly narrow
  // boxes — iPad Pro 11" landscape ≈ 75px content — still need a JS pass
  // to shrink "INTEGRATION" the last few pixels until it fits its slot).
  var card = el.closest('.session-card');
  if (card) fitStatLabels(card);
}

// Shrink each card-stat-label's font-size until it fits its parent's
// content-box width. CSS clamp() handles the bulk of the scaling; this is
// the last-mile guarantee that nothing overflows. Re-run on resize via the
// global handler below.
function fitStatLabels(card) {
  if (!card) return;
  card.querySelectorAll('.card-stat').forEach(function(stat) {
    var label = stat.querySelector('.card-stat-label');
    if (!label) return;
    label.style.fontSize = ''; // reset so we measure CSS-default first
    var available = stat.clientWidth - 4; // small inner gutter
    var guard = 20;
    while (label.scrollWidth > available && guard-- > 0) {
      var current = parseFloat(getComputedStyle(label).fontSize);
      if (current <= 7) break;
      label.style.fontSize = (current - 0.5) + 'px';
    }
  });
}

// Re-fit all stat labels on window resize (debounced) — viewport changes
// alter the per-stat width via the responsive card layout.
var _fitStatsDebounce = null;
window.addEventListener('resize', function() {
  if (_fitStatsDebounce) clearTimeout(_fitStatsDebounce);
  _fitStatsDebounce = setTimeout(function() {
    document.querySelectorAll('.session-card').forEach(fitStatLabels);
  }, 120);
});

// Toggles .card-thumbs--scroll when the thumbs would wrap to a 2nd row.
// Watches the container with ResizeObserver so window resize re-evaluates.
// In wrap mode scrollWidth always equals clientWidth, so we sum child
// offsetWidths plus gaps to compute the real intrinsic width.
function setupThumbsScrollMode(el) {
  if (!el) return;
  var GAP = 6; // matches .card-thumbs gap
  var updateAtEnd = function() {
    if (!el.classList.contains('card-thumbs--scroll')) {
      el.classList.remove('card-thumbs--at-end');
      return;
    }
    var atEnd = el.scrollLeft + el.clientWidth >= el.scrollWidth - 2;
    el.classList.toggle('card-thumbs--at-end', atEnd);
  };
  var measure = function() {
    var total = 0;
    for (var i = 0; i < el.children.length; i++) {
      total += el.children[i].offsetWidth;
    }
    if (el.children.length > 1) total += GAP * (el.children.length - 1);
    el.classList.toggle('card-thumbs--scroll', total > el.clientWidth + 1);
    updateAtEnd();
    setupScrollHoverClones(el);
  };
  if (typeof ResizeObserver !== 'undefined') {
    if (el._thumbsScrollObs) el._thumbsScrollObs.disconnect();
    var ro = new ResizeObserver(measure);
    ro.observe(el);
    el._thumbsScrollObs = ro;
  }
  if (!el._thumbsScrollListener) {
    el.addEventListener('scroll', updateAtEnd, { passive: true });
    el._thumbsScrollListener = true;
  }
  requestAnimationFrame(measure);
}

// In scroll mode the in-row hover-expand transform would clip against the
// overflow:auto scroll container, so we suppress the transform via CSS and
// pop a position:fixed clone above everything instead. The clone follows the
// hovered thumb's viewport position; its CSS class handles the scale and
// shadow.
function setupScrollHoverClones(el) {
  if (!el || !el.classList.contains('card-thumbs--scroll')) return;
  // Touch devices use setupMobileThumbnailZoom for tap-to-expand, which
  // owns the touch lifecycle (touchstart/move/end) on the same elements.
  // The hover-clone path is mouse-only, otherwise tapping a thumb on an
  // iPad fires the hover clone AND the tap zoom, then the synthetic click
  // bubbles to the card and opens the report.
  if (IS_TOUCH) return;
  if (el._scrollHoverWired) return;
  el._scrollHoverWired = true;

  var activeClone = null;
  var activeWrap = null;

  function showClone(wrap) {
    if (activeClone) hideClone();
    activeWrap = wrap;
    var rect = wrap.getBoundingClientRect();
    var clone = wrap.cloneNode(true);
    clone.classList.add('card-thumb-wrap--clone');
    // Match the in-row hover animation exactly: 450ms cubic-bezier with a
    // 150ms delay (--t-medium 150ms). Pin at scale(1) on append, ramp to
    // 1.67x on next frame so the CSS transition fires.
    var TIMING = '450ms cubic-bezier(0.22, 1, 0.36, 1) 150ms';
    clone.style.cssText =
      'position:fixed !important;' +
      'left:' + rect.left + 'px !important;' +
      'top:' + rect.top + 'px !important;' +
      'width:' + rect.width + 'px !important;' +
      'height:' + rect.height + 'px !important;' +
      'transform:scale(1) !important;' +
      'transform-origin:center center !important;' +
      'transition:transform ' + TIMING + ' !important;' +
      'z-index:2000 !important;' +
      'pointer-events:none !important;' +
      'margin:0 !important;';
    // Original :hover rules don't reach a pointer-events:none clone, so
    // mirror their effects here: label fades in, livestack badge fades out
    // (matching .card-thumb-wrap:hover .thumb-label / .livestack-badge).
    var label = clone.querySelector('.thumb-label');
    if (label) {
      label.style.setProperty('opacity', '1', 'important');
      label.style.setProperty('transition', 'opacity ' + TIMING + ' !important', 'important');
    }
    var badges = clone.querySelectorAll('.livestack-badge');
    for (var i = 0; i < badges.length; i++) {
      badges[i].style.setProperty('opacity', '0', 'important');
    }
    document.body.appendChild(clone);
    activeClone = clone;
    // Trigger scale animation on next frame.
    requestAnimationFrame(function() {
      if (clone === activeClone) {
        clone.style.setProperty('transform', 'scale(1.67)', 'important');
      }
    });
  }

  function hideClone() {
    if (activeClone && activeClone.parentNode) {
      activeClone.parentNode.removeChild(activeClone);
    }
    activeClone = null;
    activeWrap = null;
  }

  // Delegated mouseover/out so newly-cloned/added thumbs are picked up.
  el.addEventListener('mouseover', function(e) {
    if (!el.classList.contains('card-thumbs--scroll')) return;
    var wrap = e.target.closest('.card-thumb-wrap');
    if (!wrap || !el.contains(wrap) || wrap === activeWrap) return;
    showClone(wrap);
  });
  el.addEventListener('mouseout', function(e) {
    var wrap = e.target.closest('.card-thumb-wrap');
    if (!wrap) return;
    if (e.relatedTarget && wrap.contains(e.relatedTarget)) return;
    hideClone();
  });
  // If the row scrolls while a clone is showing, the clone would drift away
  // from the (moving) thumb. Cheapest fix: hide on scroll.
  el.addEventListener('scroll', hideClone, { passive: true });
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

// Livestack thumbs are hover-driven via setupLiveStackHover. On touch
// devices the sticky :hover that fires on tap conflicts with the tap-zoom
// preview, producing inconsistent shelves that fail to dismiss. Gate the
// whole feature on viewport size — at <=1100 the CSS hides the badge and
// shelf and the JS skips wiring entirely; at >1100 the desktop hover
// model takes over and tap-zoom (setupMobileThumbnailZoom) is suppressed.
function wireLiveStackBadges(s, data) {
  if (window.innerWidth < CARD_DESKTOP_MIN_WIDTH) return;
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

// One shelf at a time: holds the hideShelf fn of whichever shelf is currently
// open so a new hover can close it before opening its own.
var _activeShelfHide = null;

function setupLiveStackHover(thumbWrap, sessionId, targetName) {
  var hoverTimer = null;
  var shelf = null;
  var shelfLeaveTimer = null;
  var _isAbove = false;
  var _scrollHandler = null;
  // True while the fullscreen zoom overlay is open — suppresses shelf dismiss
  // on mouseleave (the overlay appearing under the cursor fires mouseleave on
  // the shelf even though the user didn't intentionally leave it).
  var _zoomOpen = false;
  // Document-coordinate anchors set in showShelf — stable through scroll and
  // immune to mid-animation getBoundingClientRect jitter on the thumb.
  var _docAnchorBottom = 0; // doc-Y of scaled-thumb bottom edge
  var _docAnchorTop = 0;    // doc-Y of scaled-thumb top edge
  var _cx = 0;              // viewport-X center of thumb (unchanged by scroll)

  // Reposition shelf using stored doc-coordinate anchors. No live thumb
  // BoundingClientRect so scroll fires can't observe mid-animation values.
  function placeShelf() {
    if (!shelf) return;
    if (_isAbove) {
      var h = shelf.getBoundingClientRect().height;
      shelf.style.top = (_docAnchorTop - h - 12) + 'px';
    } else {
      shelf.style.top = (_docAnchorBottom + 12) + 'px';
    }
    shelf.style.left = _cx + 'px';
    var sr = shelf.getBoundingClientRect();
    if (sr.left < 12) {
      shelf.style.left = (_cx + (12 - sr.left)) + 'px';
    } else if (sr.right > window.innerWidth - 12) {
      shelf.style.left = (_cx - (sr.right - (window.innerWidth - 12))) + 'px';
    }
  }

  function showShelf() {
    if (shelf) return;
    // Close any other open shelf before opening this one.
    if (_activeShelfHide && _activeShelfHide !== hideShelf) _activeShelfHide();
    _activeShelfHide = hideShelf;
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
      imgEl.loading = 'eager'; // load immediately — shelf is about to be shown
      imgEl.style.cursor = 'pointer';
      imgEl.addEventListener('click', function(e) {
        e.stopPropagation();
        _zoomOpen = true;
        var overlay = document.createElement('div');
        overlay.className = 'livestack-zoom-overlay';
        var zoomImg = document.createElement('img');
        zoomImg.src = img.url;
        zoomImg.alt = img.label;
        overlay.appendChild(zoomImg);
        function closeOverlay() {
          _zoomOpen = false;
          overlay.remove();
        }
        // Close on tap. touchend with preventDefault stops the synthetic
        // click from bubbling further to body listeners that would dismiss
        // the underlying shelf — closing the hero view should return to
        // the shelf, not all the way back to the card.
        overlay.addEventListener('touchend', function(ev) {
          ev.preventDefault();
          ev.stopPropagation();
          closeOverlay();
        }, { passive: false });
        overlay.addEventListener('click', function(ev) {
          ev.stopPropagation();
          closeOverlay();
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

    // Append to <body> with position:fixed so the shelf escapes any overflow
    // clipping on ancestor containers (e.g. when .card-thumbs goes into
    // overflow-x:auto scroll mode for cards with many thumbnails).
    var thumbsContainer = thumbWrap.closest('.card-thumbs');
    if (!thumbsContainer) { shelf = null; return; }
    // Snapshot the thumb's natural bounds BEFORE applying .shelf-active —
    // that class triggers a scale(1.67) transform, and getBoundingClientRect
    // during the transition can return either pre- or post-transform values
    // depending on browser timing. We compute the overhang manually.
    var wrapRect = thumbWrap.getBoundingClientRect();
    thumbWrap.classList.add('shelf-active');
    shelf.style.position = 'absolute';
    document.body.appendChild(shelf);

    // Compute explicit shelf width based on image count so the inner flex
    // grid can wrap to multiple columns. Without this the shelf is sized
    // to its intrinsic content (single column of 200px items) and never
    // expands. Cap at 4 columns for a balanced grid; cap shelf width at
    // viewport - 24 so it doesn't run off-screen on iPad.
    var ITEM_W = 180;
    var GAP = 8;
    var SHELF_PAD = 20; // 10px each side
    var MAX_COLS = 4;
    var maxShelfW = window.innerWidth - 24;
    var cols = Math.min(images.length, MAX_COLS);
    var desiredW = cols * ITEM_W + (cols - 1) * GAP + SHELF_PAD;
    var shelfW = Math.min(desiredW, maxShelfW);
    shelf.style.width = shelfW + 'px';

    // In scroll mode (.card-thumbs--scroll) the CSS overrides shelf-active to
    // transform:none — the thumb doesn't scale, so overhang is zero. Using the
    // non-zero value in scroll mode causes the shelf to start too low by ~40px
    // and then jump up on first scroll when placeShelf() measures the unscaled
    // thumb.
    var isScrollMode = !!thumbWrap.closest('.card-thumbs--scroll');
    var SCALE_OVERHANG = isScrollMode ? 0 : wrapRect.height * 0.34; // (scale-1)/2 for scale(1.67)
    _cx = wrapRect.left + wrapRect.width / 2;
    // shelf padding (20px) + border (2px) + arrow pseudo-element (~6px)
    var CHROME = 28;
    // Choose above/below by which side has more available space. This is more
    // robust than a fixed 60%-of-viewport threshold, which can flip on minor
    // differences in browser chrome height between machines.
    var snapSpaceBelow = window.innerHeight - (wrapRect.bottom + SCALE_OVERHANG) - 12 - CHROME;
    var snapSpaceAbove = (wrapRect.top    - SCALE_OVERHANG) - 12 - CHROME;
    _isAbove = snapSpaceAbove > snapSpaceBelow;
    if (_isAbove) shelf.classList.add('livestack-shelf--above');
    // Store doc-coordinate anchors now — immutable through scroll and immune
    // to mid-animation BoundingClientRect drift on the thumb.
    _docAnchorBottom = wrapRect.bottom + SCALE_OVERHANG + window.scrollY;
    _docAnchorTop    = wrapRect.top    - SCALE_OVERHANG + window.scrollY;

    // Initial hidden placement — rAF corrects final position after layout.
    shelf.style.visibility = 'hidden';
    shelf.style.left = _cx + 'px';
    shelf.style.top = '0px';

    // rAF: DOM has laid out — clamp images to available space, then place.
    requestAnimationFrame(function() {
      if (!shelf) return;
      var imagesEl = shelf.querySelector('.livestack-shelf-images');
      var vpBottom = _docAnchorBottom - window.scrollY;
      var vpTop    = _docAnchorTop    - window.scrollY;
      if (_isAbove) {
        var spaceAbove = vpTop - 12 - CHROME;
        if (imagesEl) imagesEl.style.maxHeight = Math.max(80, spaceAbove) + 'px';
        // Above-shelf position depends on shelf height (vpTop - h - 12). If we
        // reveal before images load, h is ~0 so the shelf appears too close to
        // the thumb and snaps upward when images arrive. Instead, wait for all
        // images to load (or error) before placing and revealing. The
        // shelf-reveal animation keeps running hidden; when we remove
        // visibility:hidden the shelf fades in at its current animation frame —
        // no extra delay, no snap.
        var imgs = shelf.querySelectorAll('.livestack-shelf-img');
        var remaining = imgs.length;
        var revealTimer = null;
        function revealAbove() {
          if (!shelf) return;
          clearTimeout(revealTimer);
          placeShelf();
          shelf.style.visibility = '';
        }
        if (remaining === 0) {
          revealAbove();
        } else {
          revealTimer = setTimeout(revealAbove, 800); // fallback for slow/failed loads
          imgs.forEach(function(img) {
            function onDone() { if (--remaining <= 0) revealAbove(); }
            if (img.complete) { onDone(); }
            else {
              img.addEventListener('load',  onDone, { once: true });
              img.addEventListener('error', onDone, { once: true });
            }
          });
        }
      } else {
        // Below-shelf top is fixed (vpBottom + 12) regardless of height — safe
        // to reveal immediately; images fill in downward within the maxHeight
        // scroll container without shifting the shelf position.
        var spaceBelow = window.innerHeight - vpBottom - 12 - CHROME;
        if (imagesEl) imagesEl.style.maxHeight = Math.max(80, spaceBelow) + 'px';
        placeShelf();
        shelf.style.visibility = '';
      }
    });

    // position:absolute on body means the shelf lives in document coordinates —
    // scroll moves it with the page naturally, no tracking needed.

    // Shelf hover: keep alive when mouse enters shelf
    shelf.addEventListener('mouseenter', function() {
      clearTimeout(shelfLeaveTimer);
    });
    shelf.addEventListener('mouseleave', function(e) {
      // Hide unless mouse went back to the thumb
      if (thumbWrap.contains(e.relatedTarget)) return;
      // Overlay appearing under the cursor fires mouseleave on the shelf even
      // though the user didn't intentionally leave — keep shelf alive.
      if (_zoomOpen) return;
      hideShelf();
    });
  }

  function hideShelf() {
    clearTimeout(shelfLeaveTimer);
    if (_activeShelfHide === hideShelf) _activeShelfHide = null;
    thumbWrap.classList.remove('shelf-active');
    if (_scrollHandler) {
      window.removeEventListener('scroll', _scrollHandler);
      _scrollHandler = null;
    }
    if (shelf) {
      shelf.classList.add('shelf-hiding');
      var s = shelf;
      setTimeout(function() { if (s.parentNode) s.parentNode.removeChild(s); }, 150);
      shelf = null;
    }
  }

  if (IS_TOUCH) {
    // Touch model: tap toggles the shelf and preventDefault stops the
    // synthetic click from bubbling to the card (which would open the
    // report). Don't wire mouseenter — on iOS Safari sticky :hover from a
    // tap fires mouseenter alongside touchend, opening the shelf AND
    // letting the click through.
    thumbWrap.addEventListener('touchend', function(e) {
      e.preventDefault();
      if (shelf) hideShelf();
      else showShelf();
    }, { passive: false });
    // Document-level dismiss: any tap outside the shelf and the originating
    // thumb closes the shelf and suppresses the synthetic click. Without
    // this, tapping the card background would open the report.
    document.addEventListener('touchend', function(e) {
      if (!shelf) return;
      if (shelf.contains(e.target)) return;
      if (thumbWrap.contains(e.target)) return;
      // Don't dismiss the shelf when tapping the fullscreen zoom overlay —
      // its own click handler removes the overlay only. Without this guard
      // the body touchend would preventDefault the synthetic click, so the
      // first tap silently dismisses the shelf and the second tap is
      // needed to actually close the overlay.
      if (e.target.closest('.livestack-zoom-overlay')) return;
      e.preventDefault();
      hideShelf();
    }, { passive: false });
  } else {
    thumbWrap.addEventListener('mouseenter', function() {
      clearTimeout(shelfLeaveTimer);
      hoverTimer = setTimeout(showShelf, 200);
    });
    thumbWrap.addEventListener('mouseleave', function(e) {
      clearTimeout(hoverTimer);
      // Don't hide immediately if mouse moved into the shelf — grace period
      if (shelf && shelf.contains(e.relatedTarget)) return;
      shelfLeaveTimer = setTimeout(hideShelf, 100);
    });
  }
}

function hideSession(sessionId) {
  hiddenSessions[sessionId] = true;
  safeSetItem('ns-hidden-sessions', JSON.stringify(hiddenSessions));

  var btn = document.querySelector('.hide-btn[data-session="' + sessionId + '"]');
  var card = btn ? btn.closest('.session-card') : null;

  function afterRemove() {
    // Update subtitle
    var sub = document.getElementById('page-subtitle');
    if (sub) {
      sub.textContent = getSubtitleText();
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
        safeSetItem('ns-hidden-sessions', '{}');
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

// ── Waveform bar hover tooltip ───────────────────────────────────────────
(function() {
  var tip = document.createElement('div');
  tip.className = 'lw-bar-tip';
  document.body.appendChild(tip);

  function show(bar) {
    var tgt = bar.getAttribute('data-lw-tgt') || '';
    var meta = bar.getAttribute('data-lw-meta') || '';
    var latest = bar.getAttribute('data-lw-latest') === '1';
    var html = '';
    if (tgt) html += '<div class="lw-tip-tgt">' + tgt + '</div>';
    html += '<div class="lw-tip-meta">' + meta + '</div>';
    tip.innerHTML = html;
    tip.classList.toggle('lw-bar-tip--latest', latest);
    tip.style.top = '-9999px';
    tip.style.left = '-9999px';
    tip.classList.add('visible');
    var r = bar.getBoundingClientRect();
    var tw = tip.offsetWidth;
    var th = tip.offsetHeight;
    var left = r.left + r.width / 2 - tw / 2;
    left = Math.max(8, Math.min(left, window.innerWidth - tw - 8));
    var top = r.top - th - 8;
    if (top < 8) top = r.bottom + 8;
    tip.style.left = left + 'px';
    tip.style.top = top + 'px';
  }
  function hide() { tip.classList.remove('visible'); }

  document.addEventListener('mouseover', function(e) {
    if (isTouchDevice) return;
    var bar = e.target.closest('.lw-bar');
    if (bar) show(bar);
  });
  document.addEventListener('mouseout', function(e) {
    if (isTouchDevice) return;
    var bar = e.target.closest('.lw-bar');
    if (bar && !bar.contains(e.relatedTarget)) hide();
  });
  // Hide on bar click — desktop click navigates to #/sessions/{id}, removing
  // the .lw-bar from DOM before mouseout can fire. Without this the tip
  // stays pinned at its last position on the new page.
  document.addEventListener('click', function(e) {
    if (e.target.closest('.lw-bar')) hide();
  });
  // Also hide on any hash change — defensive for any other navigation path
  // (keyboard nav, programmatic route change, back/forward, etc.).
  window.addEventListener('hashchange', hide);
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
      // Position label just above the dot; counter-transform text
      var labelGap = isMobile ? 10 : 5;
      var labelSpacing = isMobile ? 20 : 10;
      var lx = sx + labelGap, ly2 = y - 4 - labelSpacing;
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
  // Tap-to-zoom on every touch viewport. At narrow viewports (<1100)
  // livestack is hidden so this is the only thumb interaction. At wider
  // viewports livestack thumbs are owned by setupLiveStackHover; the
  // touchend handler below skips them by checking for the .livestack-badge.
  if (!thumbsContainer || !IS_TOUCH) return;

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
    // Livestack thumbs are owned by setupLiveStackHover's touch handler —
    // it shows the multi-frame shelf instead of the single-image preview.
    if (thumbWrap.querySelector('.livestack-badge')) return;
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

  // Dismiss when tapping outside the thumbs row. preventDefault stops the
  // synthetic click so it can't bubble to the card and open the report —
  // a tap-anywhere-while-preview-open is treated as a dismiss gesture only.
  document.addEventListener('touchend', function(e) {
    if (!activeThumb) return;
    if (!thumbsContainer.contains(e.target) && !(preview && preview.contains(e.target))) {
      e.preventDefault();
      hidePreview();
    }
  }, { passive: false });
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
    // Full counter-scale — Y labels overflow the SVG's left edge into the
    // 30px gutter on .chart-svg-wrap (overflow:visible on the SVG, padding
    // and overflow:hidden on the wrap). X label collisions are handled by
    // dedupChartXLabels() below, not by capping the ratio here.
    svg.querySelectorAll('text').forEach(function(t) {
      var x = parseFloat(t.getAttribute('x') || '0');
      var y = parseFloat(t.getAttribute('y') || '0');
      t.setAttribute('transform',
        'translate(' + x + ',' + y + ') scale(' + ratio.toFixed(4) + ',1) translate(' + (-x) + ',' + (-y) + ')');
    });
    dedupChartXLabels(svg);
  });
}

// At narrow viewports, the X-axis has fixed-spacing hourly ticks plus a
// chart-end label that may sit within an hour of the last tick. With full
// counter-scale, those labels overlap. Greedy left-to-right pass: walk by
// screen position and hide any label whose left edge crosses the previous
// kept label's right edge (with a small gap). Y labels are filtered out by
// looking at y position — X labels live near the bottom of the viewBox.
function dedupChartXLabels(svg) {
  var vb = (svg.getAttribute('viewBox') || '').split(/\s+/);
  var vbY = parseFloat(vb[1] || '0');
  var vbH = parseFloat(vb[3] || '0');
  if (!vbH) return;
  // 0.95 — Y "0°" label sits at y=224 (just below plot bottom 220), X-axis
  // ticks sit at y=238 (further below). Threshold of 0.95 (vbY + 0.95*vbH =
  // 14 + 220 = 234) excludes the "0°" label and includes the X ticks.
  var bottomThreshold = vbY + vbH * 0.95;
  var labels = [];
  svg.querySelectorAll('text').forEach(function(t) {
    if (t.style.display === 'none') t.style.display = ''; // reset prior pass
    var y = parseFloat(t.getAttribute('y') || '0');
    if (y < bottomThreshold) return;
    var rect = t.getBoundingClientRect();
    if (!rect.width) return;
    labels.push({ el: t, left: rect.left, right: rect.right });
  });
  labels.sort(function(a, b) { return a.left - b.left; });
  var GAP = 4; // minimum screen px between adjacent kept labels
  var prevRight = -Infinity;
  for (var i = 0; i < labels.length; i++) {
    var lbl = labels[i];
    if (lbl.left < prevRight + GAP) {
      lbl.el.style.display = 'none';
    } else {
      prevRight = lbl.right;
    }
  }
}

// Tablet/laptop breakpoint matches the @media (max-width: 1100px) in dashboard.css
// where .card-stats becomes a 3-col grid and the card-content slot grows tall
// enough that the chart no longer needs the pull-up trick. Keep both in sync.
var CARD_DESKTOP_MIN_WIDTH = 1101;

// The chart SVG uses preserveAspectRatio="none" so it stretches to fit. The
// pull-up was designed for desktop layout where card-content was a single
// short row of 6 stats; pulling the chart up into the header area claimed
// the otherwise-blank space above it. With the new 3-col grid (<=1100px),
// card-content is two rows tall, so card-altitude is naturally as tall as
// the card-body — pulling it further up overflows above the card and
// collides with the target pills.
function applyChartPullUp(el) {
  var card = el.closest('.session-card');
  var header = card ? card.querySelector('.card-header') : null;
  if (!header) return;
  if (window.innerWidth < CARD_DESKTOP_MIN_WIDTH) {
    el.style.marginTop = '0px';
    return;
  }
  var headerH = header.offsetHeight;
  var headerMargin = 4;
  var cardPadTop = 8;
  var lastChild = header.lastElementChild;
  var textRight = lastChild ? lastChild.getBoundingClientRect().right : 0;
  var svgWrap = el.querySelector('.chart-svg-wrap');
  var chartLeft = svgWrap ? svgWrap.getBoundingClientRect().left : el.getBoundingClientRect().left;
  var singleRowH = 32;
  var multiRow = headerH > singleRowH + 4;
  var overlapsChart = textRight > chartLeft - 15;
  var clearance = multiRow ? 48 : (overlapsChart ? 32 : 0);
  var latestLabel = card.querySelector('.latest-label');
  var extraPullUp = latestLabel
    ? latestLabel.offsetHeight + parseFloat(getComputedStyle(latestLabel).marginBottom || 0)
    : 0;
  var pullUp = Math.max(0, headerH + headerMargin - cardPadTop - clearance + extraPullUp);
  el.style.marginTop = '-' + pullUp + 'px';
}

// Track ResizeObservers per chart element so we can disconnect on re-render.
var _chartResizeObservers = new WeakMap();

function renderAltitudeChart(s, data) {
  var el = document.getElementById('altitude-' + s.sessionId);
  if (!el) return;
  // Legend intentionally omitted — color-matched target pills in the card
  // header serve as the legend, freeing the chart to fill the full width.
  el.innerHTML = '<div class="chart-svg-wrap">' + data.svg + '</div>';
  setupCurveAnimation(el);
  setupChartCrosshair(el);
  fixChartTextDistortion(el);
  applyChartPullUp(el);
  var body = el.parentElement;
  if (body) body.classList.add('has-chart');

  // ResizeObserver re-runs the text-distortion counter-scale and the pull-up
  // when the chart container changes size (window resize, layout reflow,
  // breakpoint switch). Without this, both go stale and the chart stretches.
  if (typeof ResizeObserver === 'function') {
    var prev = _chartResizeObservers.get(el);
    if (prev) prev.disconnect();
    var svgWrap = el.querySelector('.chart-svg-wrap');
    if (svgWrap) {
      var first = true;
      var ro = new ResizeObserver(function() {
        if (first) { first = false; return; } // skip initial fire
        fixChartTextDistortion(el);
        applyChartPullUp(el);
      });
      ro.observe(svgWrap);
      _chartResizeObservers.set(el, ro);
    }
  }
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

// ── Popup shield overlay ──────────────────────────────────────────────────────
// A fixed full-screen div (z-index 50) that sits above session cards but below
// the sticky header (z-index 100) and Flatpickr calendar (z-index 99999).
// When any filter popup is open, the overlay intercepts taps/clicks on cards:
//   • Mobile: touchstart + preventDefault() cancels the synthetic click chain.
//   • Desktop: overlay physically captures the click (higher z-index than cards).
// Flatpickr on desktop closes via its own mousedown listener before our click
// fires, so onClose uses setTimeout(0) to keep the overlay visible through click.

var _popupOverlay = null;
var _popupCloseFn = null;

function _ensureOverlay() {
  if (_popupOverlay) return;
  _popupOverlay = document.createElement('div');
  _popupOverlay.style.cssText = 'position:fixed;inset:0;z-index:50;display:none;';
  _popupOverlay.addEventListener('touchstart', function(e) {
    e.preventDefault();
    e.stopPropagation();
    _dismissOverlay();
  }, { passive: false });
  _popupOverlay.addEventListener('click', function(e) {
    e.stopPropagation();
    _dismissOverlay();
  });
  document.body.appendChild(_popupOverlay);
}

function openPopupOverlay(closeFn) {
  _ensureOverlay();
  _popupCloseFn = closeFn;
  _popupOverlay.style.display = '';
}

function _dismissOverlay() {
  if (!_popupOverlay) return;
  _popupOverlay.style.display = 'none';
  var fn = _popupCloseFn;
  _popupCloseFn = null;
  if (fn) fn();
}

function closePopupOverlay() {
  if (_popupOverlay) _popupOverlay.style.display = 'none';
  _popupCloseFn = null;
}

// Show/hide session cards by date without re-rendering the DOM.
// Called by Flatpickr onChange and the × clear buttons so thumbnails never blink.
function applyDateVisibility(from, to) {
  var anyVisible = false;
  document.querySelectorAll('.session-card[data-date]').forEach(function(card) {
    var d = card.getAttribute('data-date');
    var hide = (from && d < from) || (to && d > to);
    card.style.display = hide ? 'none' : '';
    if (!hide) anyVisible = true;
  });
  var fromClear = document.querySelector('.date-clear[data-target="filter-from"]');
  var toClear   = document.querySelector('.date-clear[data-target="filter-to"]');
  if (fromClear) fromClear.style.display = from ? '' : 'none';
  if (toClear)   toClear.style.display   = to   ? '' : 'none';
  var emptyMsg = document.querySelector('.date-filter-empty');
  if (emptyMsg) emptyMsg.style.display = (!anyVisible && (from || to)) ? '' : 'none';
}

function bindListEvents() {
  // Reset overlay on every re-render; re-open below if a popup was already open.
  closePopupOverlay();

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
    var sub = document.getElementById('page-subtitle');
    var el = sessionsV2Mode
      ? (document.getElementById('sessions-history') || document.getElementById('content'))
      : document.getElementById('content');
    doRenderList(el, sub, f.from, f.to, f.sort);
  }

  // Target dropdown toggle
  var dropBtn = document.getElementById('target-dropdown-btn');
  var dropMenu = document.getElementById('target-dropdown-menu');
  function openTargetOverlay() {
    openPopupOverlay(function() {
      dropdownOpen = false;
      targetSearch = '';
      var m = document.getElementById('target-dropdown-menu');
      if (m) m.classList.remove('open');
    });
  }
  if (dropBtn && dropMenu) {
    // Restore open state and search after re-render
    if (dropdownOpen) {
      dropMenu.classList.add('open');
      if (targetSearch) applyTargetSearch(targetSearch);
      openTargetOverlay();
    }
    dropBtn.addEventListener('click', function(e) {
      e.stopPropagation();
      dropdownOpen = !dropdownOpen;
      dropMenu.classList.toggle('open');
      if (dropdownOpen) openTargetOverlay(); else closePopupOverlay();
    });
    // Prevent menu clicks from closing (overlay handles outside clicks)
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

  // Date pickers — Flatpickr handles all platforms (iOS, Android, desktop) uniformly.
  // disableMobile:true prevents Flatpickr from falling back to native pickers on touch devices.
  if (fpFrom) { try { fpFrom.destroy(); } catch(e) {} fpFrom = null; }
  if (fpTo)   { try { fpTo.destroy();   } catch(e) {} fpTo   = null; }
  var fpConfig = {
    dateFormat: 'Y-m-d',
    altInput: true,
    altFormat: 'n/j',
    altInputClass: 'date-pill',
    disableMobile: true,
    allowInput: false,
    onOpen: function(dates, dateStr, instance) {
      openPopupOverlay(function() { instance.close(); });
    },
    // Desktop: Flatpickr closes on mousedown (before click fires). Use setTimeout so
    // the overlay stays visible through the subsequent click, preventing it from
    // reaching a session card before the overlay can intercept it.
    onClose: function() { setTimeout(closePopupOverlay, 0); }
  };
  function onDateChange() {
    applyDateVisibility(fromEl ? fromEl.value || '' : '', toEl ? toEl.value || '' : '');
  }
  if (fromEl) {
    fpFrom = flatpickr(fromEl, Object.assign({}, fpConfig, {
      defaultDate: fromEl.value || null,
      onChange: onDateChange
    }));
  }
  if (toEl) {
    fpTo = flatpickr(toEl, Object.assign({}, fpConfig, {
      defaultDate: toEl.value || null,
      onChange: onDateChange
    }));
  }
  // Restore correct visibility after a full re-render triggered by a non-date filter
  applyDateVisibility(fromEl ? fromEl.value || '' : '', toEl ? toEl.value || '' : '');
  // Sort dropdown
  var sortBtn = document.getElementById('sort-dropdown-btn');
  var sortMenu = document.getElementById('sort-dropdown-menu');
  function openSortOverlay() {
    openPopupOverlay(function() {
      sortDropdownOpen = false;
      var m = document.getElementById('sort-dropdown-menu');
      if (m) m.classList.remove('open');
    });
  }
  if (sortBtn && sortMenu) {
    if (sortDropdownOpen) { sortMenu.classList.add('open'); openSortOverlay(); }
    sortBtn.addEventListener('click', function(e) {
      e.stopPropagation();
      sortDropdownOpen = !sortDropdownOpen;
      sortMenu.classList.toggle('open');
      if (sortDropdownOpen) openSortOverlay(); else closePopupOverlay();
    });
    sortMenu.addEventListener('click', function(e) { e.stopPropagation(); });
    sortMenu.querySelectorAll('.sort-option').forEach(function(btn) {
      btn.addEventListener('click', function() {
        currentSort = this.dataset.sort;
        safeSetItem('ns-sort', currentSort);
        sortDropdownOpen = false;
        closePopupOverlay();
        refresh();
      });
    });
  }

  // Clear (×) buttons on date inputs — fp.clear() fires onChange → applyDateVisibility
  document.querySelectorAll('.date-clear').forEach(function(btn) {
    btn.addEventListener('click', function() {
      var fp = btn.dataset.target === 'filter-from' ? fpFrom : fpTo;
      if (fp) fp.clear();
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
      safeSetItem('ns-show-fov', showFovOverlay ? 'true' : 'false');
      document.querySelectorAll('.card-thumb-wrap svg, .target-card-thumb svg, .pdp-multi-thumb-cell svg, #pdp-thumb-wrap svg, #tdp-hero-wrap svg').forEach(function(svg) {
        svg.style.display = showFovOverlay ? '' : 'none';
      });
      document.querySelectorAll('.mosaic-fov-svg').forEach(function(svg) {
        svg.style.display = showFovOverlay ? '' : 'none';
      });
    });
  }

  // Show altitude chart checkbox — toggle visibility without re-render
  var altEl = document.getElementById('filter-altitude');
  if (altEl) {
    altEl.addEventListener('change', function() {
      showAltitude = this.checked;
      safeSetItem('ns-show-altitude', showAltitude ? 'true' : 'false');
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
      safeSetItem('ns-hidden-sessions', '{}');
      refresh();
    });
  }

  if (clearEl) {
    clearEl.addEventListener('click', function() {
      getAllTargets().forEach(function(t) { selectedTargets[t] = true; });
      showEmptySessions = false;
      showHidden = false;
      currentSort = 'date-desc';
      var el = sessionsV2Mode
        ? (document.getElementById('sessions-history') || document.getElementById('content'))
        : document.getElementById('content');
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
      safeSetItem('ns-card-view', cardViewMode);
      var toggle = this.closest('.view-toggle');
      toggle.classList.toggle('is-compact', cardViewMode === 'compact');
      toggle.classList.toggle('is-expanded', cardViewMode === 'expanded');
      toggle.querySelectorAll('.view-toggle-btn').forEach(function(b) {
        b.classList.toggle('active', b.dataset.view === cardViewMode);
      });
      // Hero card lives outside #sessions-history — toggle its container class directly
      var heroCard = document.querySelector('.session-card--latest');
      if (heroCard && heroCard.parentElement && heroCard.parentElement.classList.contains('cards-container')) {
        heroCard.parentElement.classList.toggle('cards-compact', cardViewMode === 'compact');
      }
      setTimeout(refresh, 230);
    });
  });

  // Load more button
  var loadMoreBtn = document.querySelector('.load-more-btn');
  if (loadMoreBtn) {
    loadMoreBtn.addEventListener('click', function() {
      visibleSessionCount += SESSION_PAGE_SIZE;
      var f = getFilters();
      var sub = document.getElementById('page-subtitle');
      var listEl = sessionsV2Mode
        ? (document.getElementById('sessions-history') || document.getElementById('content'))
        : document.getElementById('content');
      doRenderList(listEl, sub, f.from, f.to, f.sort, true);
    });
  }

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

// X-axis options: Time(0), Frame Index(1), then primary metrics offset by 2
// Indices must match ChartGenerator.XAxisTime/XAxisFrameIndex/XAxisMetricOffset constants
var XAXIS_OPTIONS = [
  'Time', 'Frame Index',
  'HFR', 'FWHM', 'Guiding RMS', 'Eccentricity', 'Star Count',
  'Focuser Temp', 'Ambient Temp', 'Camera Temp', 'Cooler Setpoint',
  'Altitude', 'Azimuth', 'Airmass', 'Position Angle', 'Rotator Position', 'Focuser Position',
  'Seeing FWHM', 'Sky Quality', 'Sky Brightness', 'Cloud Cover', 'Sky Temp',
  'Humidity', 'Dew Point', 'Wind Speed', 'Wind Gust', 'Wind Direction', 'Pressure',
  'Exposure', 'Gain', 'Offset',
  'Median ADU', 'Mean ADU', 'Std Deviation', 'MAD', 'Min ADU', 'Max ADU'
];

// Primary/secondary options: metrics only, no Time/Frame Index
// Index must match ChartGenerator.PrimaryXxx constants (0=HFR, 1=FWHM, ...)
// Secondary uses these same indices but offset by 1 (index 0 = None) via includeNone=true
var PRIMARY_OPTIONS = [
  'HFR', 'FWHM', 'Guiding RMS', 'Eccentricity', 'Star Count',
  'Focuser Temp', 'Ambient Temp', 'Camera Temp', 'Cooler Setpoint',
  'Altitude', 'Azimuth', 'Airmass', 'Position Angle', 'Rotator Position', 'Focuser Position',
  'Seeing FWHM', 'Sky Quality', 'Sky Brightness', 'Cloud Cover', 'Sky Temp',
  'Humidity', 'Dew Point', 'Wind Speed', 'Wind Gust', 'Wind Direction', 'Pressure',
  'Exposure', 'Gain', 'Offset',
  'Median ADU', 'Mean ADU', 'Std Deviation', 'MAD', 'Min ADU', 'Max ADU'
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
  // tsAvailable defaults to true when the field is missing (older cached responses);
  // only hides TS-specific UI when the server explicitly reports false.
  var tsAvailable = s.tsAvailable !== false;

  var html = '<div id="settings-panel" class="settings-panel' + (tsAvailable ? '' : ' no-ts') + '" style="display:none">';

  if (!tsAvailable) {
    html += '<div class="settings-ts-banner">' +
      '<strong>Target Scheduler not detected.</strong> ' +
      'TS Progress Bars, Min Altitude line, and Tonight\'s Preview will have no effect until Target Scheduler is installed and its API is enabled.' +
      '</div>';
  }

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
      settingsCheckbox('s-roofMarkers', 'Safety Markers', s.showChartRoofMarkers) +
      settingsCheckbox('s-perTargetIQ', 'Per-Target IQ', s.showPerTargetIQ) +
      settingsCheckbox('s-equipment', 'Equipment Profile', s.showEquipmentProfile) +
      settingsCheckbox('s-timelineAlt', 'Timeline Altitude View', s.timelineAltitudeDefault) +
      settingsCheckbox('s-expand', 'Expand Sections', s.expandSectionsDefault) +
    '</div></div></div>';

  // Row 3: Charts (collapsible)
  html += '<details class="settings-expander"><summary class="settings-expander-summary">Charts</summary>' +
    '<div class="settings-expander-body">' +
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
    '</div></details>';

  // Row 5: Filter classifications + types (collapsible)
  if (filters && filters.length > 0) {
    var TYPE_OPTIONS = ['Auto', 'L', 'R', 'G', 'B', 'H', 'S', 'O'];
    var TYPE_CODES   = ['A',    'L', 'R', 'G', 'B', 'H', 'S', 'O'];
    html += '<details class="settings-expander"><summary class="settings-expander-summary">Filter Classifications &amp; Types</summary>' +
      '<div class="settings-expander-body">' +
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
    html += '</div></div></details>';
  }

  // Row 6: Equipment (collapsible)
  html += '<details class="settings-expander"><summary class="settings-expander-summary">Equipment</summary>' +
    '<div class="settings-expander-body">' +
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
  html += '</div></div></details>';

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
    timelineAltitudeDefault: document.getElementById('s-timelineAlt').checked,
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

  var isLight = document.documentElement.classList.contains('light');
  fetch('/api/sessions/' + sessionId + '/report?theme=' + (isLight ? 'light' : 'dark'))
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

      // Inject the same SVG color overrides used by syncReportTheme for iframes
      var themeStyle = document.createElement('style');
      themeStyle.id = 'ns-theme-override';
      themeStyle.textContent = isLight ? REPORT_THEME_LIGHT : REPORT_THEME_DARK;
      shadow.appendChild(themeStyle);

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

function renderSessionDetail(sessionId, params) {
  var el = document.getElementById('content');
  var sub = document.getElementById('page-subtitle');

  var cancelLoader = deferLoader(el, 'Loading report...');

  // Context-aware back-button: TDP/PDP origin returns to that modal.
  var from   = params && params.get ? params.get('from')   : null;
  var fromTarget = params && params.get ? params.get('target') : null;
  var fromPid    = params && params.get ? params.get('pid')    : null;
  var fromPname  = params && params.get ? params.get('pname')  : null;
  var backHref = '#/sessions';
  var backLabel = 'Sessions';
  if (from === 'tdp' && fromTarget) {
    backHref = '#/stats?openTdp=' + encodeURIComponent(fromTarget);
    backLabel = fromTarget;
  } else if (from === 'pdp' && fromPid) {
    backHref = '#/stats?openPdp=' + encodeURIComponent(fromPid) +
      (fromPname ? '&pname=' + encodeURIComponent(fromPname) : '');
    backLabel = fromPname || 'Project';
  }

  Promise.all([
    api('/api/sessions/' + sessionId),
    api('/api/sessions/' + sessionId + '/settings'),
    cachedFilters ? Promise.resolve({ filters: cachedFilters }) : api('/api/filters')
  ]).then(function(results) {
    cancelLoader();
    var detail = results[0];
    currentSettings = results[1];
    cachedFilters = results[2].filters || [];
    logInfo('Session detail loaded:', sessionId);
    logDebug('Settings received:', JSON.stringify(currentSettings, null, 2));

    var targets = detail.targets.map(function(t) { return t.target; }).join(', ') || 'Unknown';
    if (sub) sub.textContent = getSubtitleText();

    // Forward TDP/PDP origin context to Frames so its in-page back link
     // can return through the report (and ultimately to the TDP/PDP) rather
     // than to the bare Sessions list.
    var framesQs = '';
    if (from === 'tdp' && fromTarget) {
      framesQs = '?from=tdp&target=' + encodeURIComponent(fromTarget);
    } else if (from === 'pdp' && fromPid) {
      framesQs = '?from=pdp&pid=' + encodeURIComponent(fromPid) +
        (fromPname ? '&pname=' + encodeURIComponent(fromPname) : '');
    }

    var navHtml = '<div class="report-nav" id="header-report-nav">' +
      '<a class="back-btn" href="' + backHref + '">\u2190 ' + esc(backLabel) + '</a>' +
      '<div class="report-nav-actions">' +
        '<a class="report-btn" href="#/sessions/' + encodeURIComponent(sessionId) + '/frames' + framesQs + '">\ud83d\uddbc Frames</a>' +
        '<button class="report-btn" id="btn-settings">\u2699 Settings</button>';

    if (detail.hasReport) {
      // Mobile gets a short label so all four toolbar buttons (back, Frames,
      // Settings, this one) fit on a single row at equal width.
      var newTabLabel = window.matchMedia('(max-width: 700px)').matches
        ? '\u2197 New Tab' : 'Open in New Tab \u2192';
      navHtml += '<a href="/api/sessions/' + sessionId + '/report" target="_blank" class="report-btn">' + newTabLabel + '</a>';
    }

    navHtml += '</div></div>';

    var existingNav = document.getElementById('header-report-nav');
    if (existingNav) existingNav.remove();
    var hdr = document.querySelector('header');
    if (hdr) {
      hdr.insertAdjacentHTML('beforeend', navHtml);
      hdr.getBoundingClientRect(); // force layout so offsetHeight is accurate
      document.documentElement.style.setProperty('--header-h', hdr.offsetHeight + 'px');
    }

    var html = buildSettingsPanel(currentSettings, cachedFilters);

    var isMobile = window.innerWidth <= 700;

    if (detail.hasReport) {
      if (isMobile) {
        // Mobile: use shadow DOM to render report inline — iframes don't
        // respect viewport constraints on mobile, causing squished content
        html += '<div class="report-viewer"><div id="report-shadow-host" class="report-shadow-host"></div></div>';
      } else {
        html += '<div class="report-viewer">' +
          '<iframe id="report-iframe" class="report-iframe" src="/api/sessions/' + sessionId + '/report?theme=' + (document.documentElement.classList.contains('light') ? 'light' : 'dark') + '" sandbox="allow-same-origin"></iframe>' +
        '</div>';
      }
    } else {
      html += '<div class="report-viewer">' +
        '<div class="empty">No report generated for this session. Click "Regenerate Report" to generate one.</div>' +
      '</div>';
    }

    el.innerHTML = html;

    // Dismiss any open TDP/PDP modal that was left visible during navigation
    // so the underlying page wouldn't flash. Modal's own fade-out animation
    // runs over the now-painted report — smooth handoff.
    if (document.getElementById('tdp-backdrop')) closeTargetDetail();
    if (typeof closeProjectDetail === 'function' &&
        document.getElementById('pdp-backdrop')) closeProjectDetail();

    if (detail.hasReport && isMobile) {
      loadReportIntoShadow(sessionId);
    }

    var iframeEl = document.getElementById('report-iframe');
    if (iframeEl) iframeEl.addEventListener('load', function() {
      iframeEl.classList.add('is-loaded');
      syncReportTheme();
    });

    bindDetailEvents(sessionId);
  }).catch(function(err) {
    cancelLoader();
    logError('Failed to load session detail:', sessionId, err.message);
    el.innerHTML = '<a class="back-btn" href="' + backHref + '">\u2190 ' + esc(backLabel) + '</a>' +
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
            iframe.src = '/api/sessions/' + sessionId + '/report?theme=' + (document.documentElement.classList.contains('light') ? 'light' : 'dark') + '&t=' + Date.now();
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
var statsTsStatus   = localStorage.getItem('ns-ts-status') || null;   // "available" | "not_installed" | "error" | null
var statsTsError    = null;   // string or null
var statsTsProjects = null;   // array of { guid, name, state, isMosaic, isCustom, targetCount, targets: [{guid,name}] }
var statsProjectAssignments = null; // { "target name (lowercase)": ["project-guid", ...] }
var statsTargetExclusions  = null; // { "project-guid": ["target name (lowercase)", ...] }

// Normalize projectAssignments: old string values → arrays for backward compat
function normalizeAssignments(obj) {
  if (!obj) return {};
  var result = {};
  Object.keys(obj).forEach(function(k) {
    var v = obj[k];
    if (typeof v === 'string') result[k] = v ? [v] : [];
    else if (Array.isArray(v)) result[k] = v;
    else result[k] = [];
  });
  return result;
}

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
// UTC offset (minutes) of the NINA/observatory machine; null = fall back to browser TZ.
// Set from /api/tonight/preview response so times render in observatory time, not viewer's.
var tonightTzOffsetMin = null;

function fmtDuration(totalSec) {
  var s = Math.round(totalSec);
  var h = Math.floor(s / 3600);
  var m = Math.floor((s % 3600) / 60);
  if (h > 0) return h + 'h ' + m + 'm';
  if (m > 0) return m + 'm';
  return s + 's';
}

function fmtTimeHHMM(d) {
  if (tonightTzOffsetMin == null) {
    var hL = d.getHours().toString().padStart(2, '0');
    var mL = d.getMinutes().toString().padStart(2, '0');
    return hL + ':' + mL;
  }
  var shifted = new Date(d.getTime() + tonightTzOffsetMin * 60000);
  var h = shifted.getUTCHours().toString().padStart(2, '0');
  var m = shifted.getUTCMinutes().toString().padStart(2, '0');
  return h + ':' + m;
}

// Returns Date of next tick-aligned boundary strictly after `after`,
// aligned to observatory hour boundaries when tonightTzOffsetMin is set.
function tonightNextTick(after, intervalMin) {
  if (tonightTzOffsetMin == null) {
    var t = new Date(after);
    t.setMinutes(0, 0, 0);
    t = new Date(t.getTime() + intervalMin * 60000);
    while (t <= after) t = new Date(t.getTime() + intervalMin * 60000);
    return t;
  }
  var offsetMs = tonightTzOffsetMin * 60000;
  var floored  = Math.floor((after.getTime() + offsetMs) / 3600000) * 3600000;
  var firstUtc = floored - offsetMs + intervalMin * 60000;
  while (firstUtc <= after.getTime()) firstUtc += intervalMin * 60000;
  return new Date(firstUtc);
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
  tonightTzOffsetMin = (typeof data.tzOffsetMinutes === 'number') ? data.tzOffsetMinutes : null;
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

  // Date header (in observatory TZ when offset is known)
  var pd = new Date(targets[0].startTime);
  var dateStr;
  if (tonightTzOffsetMin != null) {
    var pdShifted = new Date(pd.getTime() + tonightTzOffsetMin * 60000);
    var monthNames = ['January','February','March','April','May','June','July','August','September','October','November','December'];
    dateStr = monthNames[pdShifted.getUTCMonth()] + ' ' + pdShifted.getUTCDate() + ', ' + pdShifted.getUTCFullYear();
  } else {
    dateStr = pd.toLocaleDateString(undefined, {month: 'long', day: 'numeric', year: 'numeric'});
  }
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

  // Cached-payload pill — surfaces when the server fell back to disk cache
  // (companion w/ NINA off, or transient TS API hiccup). Mount after the main
  // render so the inner innerHTML assignment above doesn't blow it away.
  if (data.cached && data.cachedAtUtc) {
    var when = new Date(data.cachedAtUtc);
    var rel = (function(d){
      var s = Math.floor((Date.now() - d.getTime())/1000);
      if (s < 60)   return s + 's ago';
      if (s < 3600) return Math.floor(s/60) + 'm ago';
      if (s < 86400)return Math.floor(s/3600) + 'h ago';
      return Math.floor(s/86400) + 'd ago';
    })(when);
    container.insertAdjacentHTML('afterbegin',
      '<div class="tonight-cached-pill" title="Live Target Scheduler API not reachable — showing last successful preview from ' + esc(when.toLocaleString()) + '">' +
        '<span class="tonight-cached-dot"></span>Cached ' + esc(rel) +
      '</div>');
  }

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

  var tickT = tonightNextTick(timelineStart, tickIntervalMin);
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
  var tickMs = tonightNextTick(timelineStart, tickIntervalMin);

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
  var nativeTsProjects = statsTsProjects ? statsTsProjects.filter(function(p) { return !p.isCustom; }) : [];
  if (statsTsStatus === 'available' && nativeTsProjects.length === 0) {
    return '<div class="ts-status-banner info">No Target Scheduler projects found — TS features (completion tracking, exposure plans) unavailable. Create a project in Target Scheduler, or use Manage Projects to add a manual project.</div>';
  }
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

var _manageProjectsDirty = false;
function closeManageProjectsModal() {
  var bd = document.getElementById('manage-projects-backdrop');
  if (bd && bd.parentNode) bd.parentNode.removeChild(bd);
  document.removeEventListener('keydown', _manageProjectsKeyHandler);
  document.body.style.overflow = '';
  if (_manageProjectsDirty) {
    _manageProjectsDirty = false;
    renderStats();
  }
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
  // Also include projects that have assignments
  Object.keys(statsProjectAssignments || {}).forEach(function(k) {
    (statsProjectAssignments[k] || []).forEach(function(g) { matched[g] = true; });
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
      if ((statsProjectAssignments[k] || []).indexOf(p.guid) >= 0) {
        var alreadyListed = targets.some(function(t) { return t.name.toLowerCase() === k; });
        if (!alreadyListed) targets.push({ name: k, source: 'assigned' });
      }
    });
    return targets;
  }

  function renderProjectRow(p) {
    var targets = projectTargetList(p);
    var excludedCount = (!p.isCustom && statsTargetExclusions) ? (statsTargetExclusions[p.guid] || []).length : 0;
    var subtitle;
    if (p.isCustom) {
      subtitle = targets.length > 0
        ? targets.length + ' assigned target' + (targets.length > 1 ? 's' : '')
        : 'No targets assigned';
    } else {
      var visible = p.targetCount - excludedCount;
      subtitle = visible + ' TS target' + (visible !== 1 ? 's' : '');
      if (excludedCount > 0) subtitle += ' \u00b7 ' + excludedCount + ' hidden';
    }
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
        if ((statsProjectAssignments[k] || []).indexOf(projectGuid) >= 0) assignedCount++;
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
    _manageProjectsDirty = true;
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
      if (statsProjectAssignments) {
        var tKey = targetName.toLowerCase();
        var arr = statsProjectAssignments[tKey] || [];
        var idx = arr.indexOf(projectGuid);
        if (idx >= 0) arr.splice(idx, 1);
        if (arr.length === 0) delete statsProjectAssignments[tKey];
        else statsProjectAssignments[tKey] = arr;
      }
      url  = '/api/stats/ts/assign';
      body = { targetName: targetName, projectGuid: projectGuid };
    }
    var row = btn.closest('.manage-project-target');
    if (row) row.remove();
    updateProjectRowMeta(projectGuid);
    fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body) });
  }

  // Restore project to raw TS state: re-add excluded targets and drop any
  // cross-assigned targets that point to this project.
  function handleProjectReset(projectGuid) {
    _manageProjectsDirty = true;
    var proj = (statsTsProjects || []).find(function(p) { return p.guid === projectGuid; });
    if (!proj) return;
    var excluded = ((statsTargetExclusions || {})[projectGuid] || []).slice();
    var toRestore = (proj.targets || []).filter(function(t) {
      return excluded.indexOf((t.name || '').toLowerCase()) >= 0;
    });
    var nativeNames = (proj.targets || []).reduce(function(acc, t) {
      acc[(t.name || '').toLowerCase()] = true; return acc;
    }, {});
    var targetList = backdrop.querySelector('.manage-project-targets[data-guid="' + projectGuid + '"]');

    // Remove cross-assigned rows (anything not in the project's native targets)
    if (targetList) {
      targetList.querySelectorAll('.manage-project-target').forEach(function(row) {
        var nameEl = row.querySelector('.manage-project-target-name');
        var name = nameEl ? (nameEl.textContent || '').toLowerCase() : '';
        if (name && !nativeNames[name]) row.remove();
      });
    }

    // Re-add previously excluded TS targets
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
    // Drop assignments that point to this project
    if (statsProjectAssignments) {
      Object.keys(statsProjectAssignments).forEach(function(k) {
        var arr = statsProjectAssignments[k] || [];
        var idx = arr.indexOf(projectGuid);
        if (idx >= 0) arr.splice(idx, 1);
        if (arr.length === 0) delete statsProjectAssignments[k];
      });
    }
    updateProjectRowMeta(projectGuid);
    fetch('/api/stats/projects/' + encodeURIComponent(projectGuid) + '/reset', {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({})
    });
  }

  // Rebuild the project list in-place (for structural changes: create, delete, global reset).
  function rebuildList() {
    _manageProjectsDirty = true;
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
            var arr = statsProjectAssignments[k] || [];
            var idx = arr.indexOf(guid);
            if (idx >= 0) arr.splice(idx, 1);
            if (arr.length === 0) delete statsProjectAssignments[k];
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

var _projectAssignDirty = false;
function closeProjectAssignPicker() {
  var bd = document.getElementById('project-assign-backdrop');
  if (bd && bd.parentNode) bd.parentNode.removeChild(bd);
  var dd = document.getElementById('project-assign-dropdown');
  if (dd && dd.parentNode) dd.parentNode.removeChild(dd);
  document.removeEventListener('keydown', _projectAssignKeyHandler);
  document.body.style.overflow = '';
  if (_projectAssignDirty) {
    _projectAssignDirty = false;
    renderStats();
  }
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
  var currentGuids = (statsProjectAssignments || {})[targetName.toLowerCase()] || [];
  // Also check if target is auto-matched to a TS project
  var targetRow = (statsTargetData || []).filter(function(t) { return t.target === targetName; })[0];
  var autoProjectGuid = (targetRow && targetRow.ts && targetRow.ts.project) ? targetRow.ts.project.guid : null;
  // All checked GUIDs: manual assignments + auto-match
  var checkedGuids = currentGuids.slice();
  if (autoProjectGuid && checkedGuids.indexOf(autoProjectGuid) < 0) checkedGuids.push(autoProjectGuid);

  function renderOption(p) {
    var isChecked = checkedGuids.indexOf(p.guid) >= 0;
    var isAuto = p.guid === autoProjectGuid && currentGuids.indexOf(p.guid) < 0;
    var cls = 'project-assign-option' + (isChecked ? ' selected' : '');
    var tag = p.isCustom ? 'Custom' : (p.isMosaic ? 'Mosaic' : 'TS');
    var checkmark = isChecked ? '\u2611' : '\u2610';
    var autoLabel = isAuto ? ' <span class="project-assign-auto">(auto)</span>' : '';
    return '<div class="' + cls + '" data-guid="' + esc(p.guid) + '">' +
      '<span class="project-assign-check">' + checkmark + '</span>' +
      '<span class="project-assign-name">' + esc(p.name) + autoLabel + '</span>' +
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
    '<div class="project-assign-header">Assign to projects</div>' +
    '<div class="project-assign-list">' + options + '</div>' +
    '<div class="project-assign-footer">' +
      '<div class="project-assign-reset" data-action="clear">Remove all</div>' +
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

  // Click handlers — toggle checkbox, don't close picker
  dropdown.querySelectorAll('.project-assign-option').forEach(function(opt) {
    opt.addEventListener('click', function() {
      var guid = opt.getAttribute('data-guid');
      var key = targetName.toLowerCase();
      // Toggle in local state
      _projectAssignDirty = true;
      if (!statsProjectAssignments) statsProjectAssignments = {};
      var arr = statsProjectAssignments[key] || [];
      var idx = arr.indexOf(guid);
      if (idx >= 0) { arr.splice(idx, 1); } else { arr.push(guid); }
      if (arr.length === 0) delete statsProjectAssignments[key];
      else statsProjectAssignments[key] = arr;
      // Update checkbox visual
      var isNowChecked = idx < 0; // was not checked, now is
      opt.classList.toggle('selected', isNowChecked);
      var checkEl = opt.querySelector('.project-assign-check');
      if (checkEl) checkEl.textContent = isNowChecked ? '\u2611' : '\u2610';
      // Persist to server (toggle endpoint)
      fetch('/api/stats/ts/assign', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ targetName: targetName, projectGuid: guid })
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

  // Bucket sessions by local YYYY-MM-DD. Each bucket tracks total
  // integration seconds and the "best" session's id for click-through
  // (most integration time, breaking ties on image count). This avoids
  // linking to false-start sessions (e.g. 15-second dry runs with 0
  // images) when a real multi-hour session exists on the same date.
  var byDay = {};
  sessions.forEach(function(s) {
    if (!s.sessionStart) return;
    var m = String(s.sessionStart).match(/^(\d{4})-(\d{2})-(\d{2})/);
    if (!m) return;
    var key = m[1] + '-' + m[2] + '-' + m[3];
    var bucket = byDay[key] || { seconds: 0, sessionId: null, bestSecs: -1, bestImgs: -1 };
    var secs = s.totalIntegrationSeconds || 0;
    var imgs = s.imageCount || 0;
    bucket.seconds += secs;
    if (s.sessionId && s.hasReport && (secs > bucket.bestSecs || (secs === bucket.bestSecs && imgs > bucket.bestImgs))) {
      bucket.sessionId = s.sessionId;
      bucket.bestSecs = secs;
      bucket.bestImgs = imgs;
    }
    byDay[key] = bucket;
  });

  // Grid spans firstSession -> today, capped at rolling 365 days so the
  // heatmap builds out organically rather than showing a pre-seeded year.
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
    var bucket = byDay[key] || null;
    var secs = bucket ? bucket.seconds : 0;
    var hrs = secs / 3600;
    var preHistory = d < firstDate;
    cells.push({
      date: new Date(d), key: key, hours: hrs, intensity: bucketFor(hrs), pre: preHistory,
      sessionId: bucket ? bucket.sessionId : null
    });
    d.setDate(d.getDate() + 1);
  }

  // Cell size scales with data span: sparse histories get bigger cells so
  // the grid doesn't render as a tiny clump in the corner. A full year
  // collapses to ~14px cells (GitHub-standard); two months gets ~28px.
  var totalCols = Math.ceil(cells.length / 7);
  var gap = 3;
  var TARGET_WIDTH = 520;
  var cellSize = Math.max(14, Math.min(28, Math.floor((TARGET_WIDTH - (totalCols - 1) * gap) / totalCols)));
  var step = cellSize + gap;
  var gridWidth = totalCols * step - gap;
  var monthLabelH = 14;
  var legendH = 16;
  // Day-of-week label gutter (left). Shows Mon/Wed/Fri — the column-per-week
  // layout reads weeks left->right, days top->bottom, and the gutter
  // disambiguates that vs. a calendar grid.
  var dowLabelW = 26;
  var height = monthLabelH + 7 * step - gap + legendH;
  var width = dowLabelW + gridWidth;
  // Minimum width to fit the legend; expand viewBox if grid is narrower
  var MIN_WIDTH_FOR_LEGEND = 24 + 5 + (5 * (12 + 2) - 2) + 5 + 28; // ~130
  var svgWidth = Math.max(width, dowLabelW + MIN_WIDTH_FOR_LEGEND);

  var MONTHS = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];
  var DOW_LABELS = { 0: 'Sun', 1: 'Mon', 2: 'Tue', 3: 'Wed', 4: 'Thu', 5: 'Fri', 6: 'Sat' };

  var svg = '<svg class="lifetime-heatmap" viewBox="0 0 ' + svgWidth + ' ' + height + '" ';
  svg += 'preserveAspectRatio="xMidYMid meet" ';
  svg += 'width="' + svgWidth + '" height="' + height + '" ';
  svg += 'style="max-width:100%;height:auto">';

  // Day-of-week labels (left gutter) — only Mon/Wed/Fri to avoid clutter
  for (var dr = 0; dr < 7; dr++) {
    if (!DOW_LABELS[dr]) continue;
    var dy = monthLabelH + dr * step + Math.floor(cellSize * 0.7);
    svg += '<text class="lifetime-heatmap-dow" x="0" y="' + dy + '">' + DOW_LABELS[dr] + '</text>';
  }

  // Month labels (top) — once per column where the month first appears
  var lastMonth = -1;
  for (var col = 0; col < totalCols; col++) {
    var firstCellOfCol = cells[col * 7];
    if (!firstCellOfCol) break;
    var m = firstCellOfCol.date.getMonth();
    if (m !== lastMonth) {
      var lx = dowLabelW + col * step;
      svg += '<text class="lifetime-heatmap-month" x="' + lx + '" y="10">' + MONTHS[m] + '</text>';
      lastMonth = m;
    }
  }

  var todayTime = today.getTime();

  // Cells — skip anything past today
  cells.forEach(function(c, i) {
    if (c.date > today) return;
    var col = Math.floor(i / 7);
    var row = i % 7;
    var x = dowLabelW + col * step;
    var y = monthLabelH + row * step;
    var cls = 'lifetime-heatmap-cell intensity-' + c.intensity;
    if (c.pre) cls += ' pre-history';
    var isToday = c.date.getTime() === todayTime;
    if (isToday) cls += ' is-today';
    if (c.sessionId) cls += ' is-clickable';
    var tooltipSuffix = c.pre
      ? ' \u00b7 before first session'
      : ' \u00b7 ' + (c.hours > 0 ? c.hours.toFixed(1) + 'h' : 'no session');
    var tooltip = c.key + (isToday ? ' (today)' : '') + tooltipSuffix;
    var rect = '<rect class="' + cls + '" x="' + x + '" y="' + y + '" ' +
               'width="' + cellSize + '" height="' + cellSize + '" rx="2"><title>' +
               esc(tooltip) + '</title></rect>';
    if (c.sessionId && !IS_TOUCH) {
      // In-app session view, same pattern as session-card click.
      svg += '<a href="#/sessions/' + encodeURIComponent(c.sessionId) + '">' + rect + '</a>';
    } else {
      svg += rect;
    }
  });

  // Legend — right-aligned: "Less [0][1][2][3][4] More"
  var legendY = monthLabelH + 7 * step - gap + 4;
  var legendCells = 5;
  var legendCellSize = Math.max(10, cellSize - 2);
  var legendGap = 2;
  var legendTextPad = 5;
  var lessW = 24;
  var moreW = 28;
  var swatchRowW = legendCells * (legendCellSize + legendGap) - legendGap;
  var legendTotalW = lessW + legendTextPad + swatchRowW + legendTextPad + moreW;
  var legendStartX = Math.max(0, svgWidth - legendTotalW);
  var legendTextY = legendY + legendCellSize - 2;
  svg += '<text class="lifetime-heatmap-legend" x="' + legendStartX + '" y="' + legendTextY + '">Less</text>';
  var legendSwatchStart = legendStartX + lessW + legendTextPad;
  for (var li = 0; li < legendCells; li++) {
    var lx2 = legendSwatchStart + li * (legendCellSize + legendGap);
    svg += '<rect class="lifetime-heatmap-cell intensity-' + li + '" x="' + lx2 + '" y="' + legendY + '" ';
    svg += 'width="' + legendCellSize + '" height="' + legendCellSize + '" rx="2"/>';
  }
  var legendMoreX = legendSwatchStart + swatchRowW + legendTextPad;
  svg += '<text class="lifetime-heatmap-legend" x="' + legendMoreX + '" y="' + legendTextY + '">More</text>';

  svg += '</svg>';
  return svg;
}

function renderStats(params) {
  var el = document.getElementById('content');
  var sub = document.getElementById('page-subtitle');
  if (sub) sub.textContent = getSubtitleText();

  // Pull deep-link params for auto-opening TDP/PDP after stats render.
  // Used when returning from a session detail view that was launched from
  // a TDP/PDP "View" button — preserves the user's prior context.
  var openTdp = params && params.get ? params.get('openTdp') : null;
  var openPdp = params && params.get ? params.get('openPdp') : null;
  var openPname = params && params.get ? params.get('pname')   : null;

  var cancelLoader = deferLoader(el, 'Loading stats...');

  // Pre-fetch TDP sessions in parallel with the stats load when returning
  // from a session-detail view. Included in the main Promise.all so the
  // modal opens in the *same* paint cycle as the stats page — no 1-frame
  // gap where the user sees Stats without its overlaid modal.
  var tdpPrefetch = openTdp
    ? api('/api/stats/targets/' + encodeURIComponent(openTdp) + '/sessions').catch(function() { return null; })
    : Promise.resolve(null);

  Promise.all([
    api('/api/stats/targets'),
    api('/api/stats/summary'),
    api('/api/settings'),
    api('/api/sessions'),
    tdpPrefetch
  ]).then(function(results) {
    cancelLoader();
    var targetData = results[0];
    var summary    = results[1];
    var settings   = results[2];
    var sessions   = results[3] || [];
    var tdpData    = results[4];
    if (sessions.length > 0) sessionsCache = sessions;
    var targets = targetData.targets || [];
    statsTargetData = targets;
    statsTsStatus   = targetData.tsStatus   || null;
    if (statsTsStatus) safeSetItem('ns-ts-status', statsTsStatus);
    statsTsError    = targetData.tsError    || null;
    statsTsProjects = targetData.tsProjects || null;
    statsProjectAssignments = normalizeAssignments(targetData.projectAssignments || {});
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

    if (sub) sub.textContent = getSubtitleText();

    // Strip prior report-view chrome in the same sync tick as the stats
    // innerHTML write below — keeps the iframe + header pills intact while
    // the stats data was loading.
    exitReportView();

    var html = '';

    // Tab bar + content
    var tsOn = statsTsStatus === 'available';
    updateStatsNavLabel();

    if (tsOn) {
      var tsNoProjects = !statsTsProjects || statsTsProjects.length === 0;
      var tabs = [{id: 'targets', label: 'Targets'}, {id: 'tonight', label: 'Tonight', disabled: tsNoProjects}];
      var activeTab = localStorage.getItem('ns-stats-tab') || 'targets';
      if (!tabs.some(function(t) { return t.id === activeTab; })) activeTab = 'targets';
      html += '<div class="stats-tab-row">';
      html += renderTabBar(tabs, activeTab);
      html += '<button type="button" class="targets-manage-projects-btn" data-action="manage-projects">Manage Projects</button>';
      html += '</div>';
      html += '<div id="stats-tab-content"></div>';
      el.innerHTML = html;
      initTabBar(renderStatsTabContent);
      var manageBtn = el.querySelector('.targets-manage-projects-btn');
      if (manageBtn) manageBtn.addEventListener('click', function() { openManageProjectsModal(); });
      renderStatsTabContent(activeTab);
    } else {
      html += '<div id="stats-tab-content"></div>';
      el.innerHTML = html;
      renderStatsTabContent('targets');
    }

    // Auto-open TDP/PDP after stats paints — used by deep-links from
    // session-detail "back" buttons that originated in a TDP/PDP modal.
    // TDP modal opens synchronously here so the browser paints stats +
    // overlaid modal in one cycle (no flash of bare Stats).
    if (openTdp) {
      // Find the latest session for this target so the TDP hero thumb populates.
      var ttarget = (targets || []).find(function(t) { return t.target === openTdp; });
      var latestSid = ttarget ? ttarget.latestSessionId : null;
      openTargetDetail(openTdp, latestSid, tdpData || undefined);
    } else if (openPdp) {
      requestAnimationFrame(function() { openProjectDetail(openPdp, openPname); });
    }
  }).catch(function(err) {
    cancelLoader();
    logError('Failed to load stats:', err.message);
    el.innerHTML = '<div class="error">Failed to load stats: ' + esc(err.message) + '</div>';
  });
}

// ── Raw Image Frames Gallery (RAW_THUMBNAILS_DESIGN.md) ──────────────────
// Three view modes share one renderer: per-session, per-target (cross-session),
// per-project (TS-mediated). Each fetches a different endpoint but produces the
// same {id, sessionId, timestamp, filter, accepted, gradingStatus,
//        thumbnailVersion, [targetName for target/project views]} entries.

function renderFramesGallery(view) {
  var el = document.getElementById('content');
  var cancelLoader = deferLoader(el, 'Loading frames...');

  // Preserve TDP/PDP origin so the back-link returns through the
   // report carrying the same from= context (avoids dead-ending on
   // the bare Sessions list — bug from feature/frames-back-nav).
  var p = view.params;
  var vFrom    = p && p.get ? p.get('from')   : null;
  var vTarget  = p && p.get ? p.get('target') : null;
  var vPid     = p && p.get ? p.get('pid')    : null;
  var vPname   = p && p.get ? p.get('pname')  : null;
  var sessionBackQs = '';
  if (vFrom === 'tdp' && vTarget) {
    sessionBackQs = '?from=tdp&target=' + encodeURIComponent(vTarget);
  } else if (vFrom === 'pdp' && vPid) {
    sessionBackQs = '?from=pdp&pid=' + encodeURIComponent(vPid) +
      (vPname ? '&pname=' + encodeURIComponent(vPname) : '');
  }

  var url, title, backHref;
  if (view.kind === 'session') {
    url = '/api/sessions/' + encodeURIComponent(view.id) + '/images';
    title = 'Frames';
    backHref = '#/sessions/' + encodeURIComponent(view.id) + sessionBackQs;
  } else if (view.kind === 'target') {
    url = '/api/targets/' + encodeURIComponent(view.id) + '/frames';
    title = 'Frames — ' + view.id;
    backHref = '#/stats';
  } else {
    url = '/api/projects/' + encodeURIComponent(view.id) + '/frames';
    title = 'Frames — Project';
    backHref = '#/stats';
  }

  api(url).then(function(rows) {
    cancelLoader();
    // For session-view we got the full /images dump including darks/flats.
    // Filter to LIGHT frames that have a thumb.
    var frames = (rows || []).filter(function(r) {
      if (view.kind === 'session') {
        if (r.imageType && r.imageType.toUpperCase() !== 'LIGHT') return false;
      }
      return (r.thumbnailVersion || 0) > 0;
    });

    if (frames.length === 0) {
      exitReportView();
      el.innerHTML =
        '<a class="back-btn" href="' + backHref + '">← Back</a>' +
        '<h2>' + esc(title) + '</h2>' +
        '<div class="empty">No thumbnails available. Enable "Capture Thumbnails" in Options to start collecting them, or click "Import from Target Scheduler" to backfill from existing TS data.</div>';
      return;
    }

    // Sort: target (chronological by first-imaged timestamp) → filter (stack
    // order L,R,G,B,H,S,O,N) → exposure → timestamp. Lightbox prev/next walks
    // this same order so navigation flows naturally across groups.
    // Pre-compute first-imaged timestamp per target so the comparator stays O(n log n).
    var targetFirstTs = {};
    frames.forEach(function(f) {
      var name = f.targetName || '(unknown target)';
      var ts = new Date(f.timestamp || 0).getTime();
      if (ts > 0 && (targetFirstTs[name] == null || ts < targetFirstTs[name])) {
        targetFirstTs[name] = ts;
      }
    });
    frames.sort(function(a, b) {
      var an = a.targetName || '(unknown target)';
      var bn = b.targetName || '(unknown target)';
      var t = (targetFirstTs[an] || 0) - (targetFirstTs[bn] || 0);
      if (t) return t;
      var f = compareFilterStackOrder(a.filter, b.filter);
      if (f) return f;
      var e = (a.exposureDuration || 0) - (b.exposureDuration || 0);
      if (e) return e;
      return new Date(a.timestamp || 0) - new Date(b.timestamp || 0);
    });

    // Group: top by target (skipped on target view since they're all the same),
    // then sub-group by filter+exposure within each.
    var groupByTarget = view.kind !== 'target';
    var groups = [];        // [{ target, subgroups: [{ key, label, frames }] }]
    var groupMap = {};      // target -> group ref
    function expoLabel(d) { return d ? Math.round(d) + 's' : '?'; }
    for (var gi = 0; gi < frames.length; gi++) {
      var ff = frames[gi];
      var tname = groupByTarget ? (ff.targetName || '(unknown target)') : '';
      var g = groupMap[tname];
      if (!g) {
        g = { target: tname, subgroups: [], subMap: {} };
        groupMap[tname] = g;
        groups.push(g);
      }
      var subKey = (ff.filter || '?') + '|' + expoLabel(ff.exposureDuration);
      var sub = g.subMap[subKey];
      if (!sub) {
        sub = { key: subKey, filter: ff.filter || '?', exposure: expoLabel(ff.exposureDuration), frames: [] };
        g.subMap[subKey] = sub;
        g.subgroups.push(sub);
      }
      sub.frames.push(ff);
    }

    function renderThumb(ff, viewKind) {
      var sid2 = ff.sessionId || view.id;
      var src = '/api/frames/' + ff.id + '/thumb?size=sm';
      var rejected = (ff.gradingStatus === 2) || (ff.accepted === false);
      // Tile caption: filter is already shown in the subgroup header above,
      // and target is shown in the group header — only the project view
      // mixes multiple targets per subgroup, so only there is a per-tile
      // target label useful. Other views render no meta strip.
      var meta = (viewKind === 'project' && ff.targetName) ? ff.targetName : '';
      // data-caption still includes filter + timestamp so lightbox prev/next
      // can label the slide meaningfully (independent of the visible tile).
      var dataCaption = (ff.filter || '');
      if (ff.targetName && viewKind !== 'session') dataCaption = ff.targetName + ' • ' + dataCaption;
      var tsLabel = ff.timestamp ? fmtDate(ff.timestamp) : '';
      return (
        '<div class="frames-thumb' + (rejected ? ' rejected' : '') + '"' +
             ' data-id="' + ff.id + '" data-sid="' + esc(sid2) + '"' +
             ' data-caption="' + esc(dataCaption + (tsLabel ? ' • ' + tsLabel : '')) + '">' +
          '<img loading="lazy" src="' + src + '" alt="" />' +
          (meta ? '<div class="frames-thumb-meta">' + esc(meta) + '</div>' : '') +
        '</div>'
      );
    }

    var html =
      '<a class="back-btn" href="' + backHref + '">← Back</a>' +
      '<h2>' + esc(title) + ' <span class="frames-count">' + frames.length + '</span></h2>' +
      '<div class="frames-groups">';

    for (var gj = 0; gj < groups.length; gj++) {
      var grp = groups[gj];
      var grpTotal = grp.subgroups.reduce(function(s, x) { return s + x.frames.length; }, 0);
      html += '<section class="frames-target-group">';
      if (groupByTarget) {
        html += '<h3 class="frames-target-h">' + esc(grp.target) +
                ' <span class="frames-target-count">' + grpTotal + '</span></h3>';
      }
      for (var sj = 0; sj < grp.subgroups.length; sj++) {
        var sg = grp.subgroups[sj];
        html += '<div class="frames-subgroup">' +
                  '<div class="frames-subgroup-h">' +
                    '<span class="sg-filter">' + esc(sg.filter) + '</span>' +
                    '<span class="sg-sep">·</span>' +
                    '<span class="sg-exposure">' + esc(sg.exposure) + '</span>' +
                    '<span class="sg-count">' + sg.frames.length +
                      ' frame' + (sg.frames.length === 1 ? '' : 's') + '</span>' +
                  '</div>' +
                  '<div class="frames-gallery">';
        for (var fk = 0; fk < sg.frames.length; fk++) {
          html += renderThumb(sg.frames[fk], view.kind);
        }
        html += '</div></div>';
      }
      html += '</section>';
    }
    html += '</div>';
    // Lightbox structure: sticky header (badge / counter / close) above a
    // scrolling stage (image + metrics panel). Header lives inside the stage
    // so it travels with the slide animation; position:sticky keeps it pinned
    // to the visible top when the user scrolls the metrics panel.
    html += '<div class="frames-lightbox" id="frames-lightbox" style="display:none">' +
              '<button class="frames-lightbox-prev"  aria-label="Previous">‹</button>' +
              '<button class="frames-lightbox-next"  aria-label="Next">›</button>' +
              '<div class="frames-lightbox-stage">' +
                '<div class="lb-header">' +
                  '<div id="frames-lightbox-badge" class="lb-badge" style="display:none">TS Import</div>' +
                  '<div id="frames-lightbox-counter" class="lb-counter"></div>' +
                  '<button class="frames-lightbox-close" aria-label="Close">×</button>' +
                '</div>' +
                '<div class="frames-lightbox-imgwrap">' +
                  '<img id="frames-lightbox-img" alt="" />' +
                '</div>' +
                '<div id="frames-lightbox-panel" class="frames-lightbox-panel"></div>' +
              '</div>' +
            '</div>';

    exitReportView();
    el.innerHTML = html;
    bindFramesGallery(frames);
  }).catch(function(err) {
    cancelLoader();
    logError('Failed to load frames:', err.message);
    el.innerHTML =
      '<a class="back-btn" href="' + backHref + '">← Back</a>' +
      '<div class="error">Failed to load frames: ' + esc(err.message) + '</div>';
  });
}

function bindFramesGallery(frames) {
  var lb        = document.getElementById('frames-lightbox');
  var lbImg     = document.getElementById('frames-lightbox-img');
  var lbPanel   = document.getElementById('frames-lightbox-panel');
  var lbBadge   = document.getElementById('frames-lightbox-badge');
  var lbCounter = document.getElementById('frames-lightbox-counter');
  var idx       = 0;

  // After the image decodes, fade it in (JS removes lb-loading) and decide whether
  // it was the medium (≥400px tall = native, sharp) or the small fallback
  // (force-upscale + show "Original res" badge so the user knows why it's blurry).
  //
  // When upscaling, set explicit width/height that preserves the natural aspect
  // ratio. CSS-only sizing (width:1100px + max-height:58vh) created a frame on
  // wider viewports: the IMG element became wider than the actual image content
  // could fill (object-fit:contain letterboxed inside), and the box-shadow's
  // outline drew around that wider element — giving visible side-frames where
  // there was no image content. Computing dimensions from naturalWidth/Height
  // makes the IMG box hug actual content edges at every viewport size.
  function applyUpscaledSize() {
    var nw = lbImg.naturalWidth, nh = lbImg.naturalHeight;
    if (!nw || !nh) return;
    var maxW = Math.min(window.innerWidth * 0.92, 1100);
    var maxH = window.innerHeight * 0.58;
    var aspect = nw / nh;
    var w, h;
    if (maxW / aspect <= maxH) { w = maxW; h = maxW / aspect; }
    else                       { h = maxH; w = maxH * aspect; }
    lbImg.style.width  = w + 'px';
    lbImg.style.height = h + 'px';
  }
  // Match the header bar width to the image so the TS Import badge and close
  // button align with the image's left/right edges (regardless of upscale
  // state or viewport size). Without this, on wide desktop viewports the
  // header spans the full 1100px stage width but the image can be narrower
  // (e.g. a portrait-aspect frame, or any upscaled thumb), pushing the badge
  // and close way off the image corners. Read the actual rendered width.
  function syncHeaderToImage() {
    var header = lb.querySelector('.lb-header');
    if (!header) return;
    var imgW = lbImg.getBoundingClientRect().width;
    if (imgW > 0) header.style.width = imgW + 'px';
  }
  lbImg.addEventListener('load', function() {
    var nat = lbImg.naturalHeight || 0;
    var isUpscaled = nat > 0 && nat < 400;
    // Class still drives the "Original res" badge visibility; sizing path is
    // unified below so medium thumbs and upscaled small thumbs both get explicit
    // inline width/height set in the same tick — the header sync that follows
    // reads a stable, already-laid-out bounding rect on both paths.
    lbImg.classList.toggle('lb-upscaled', isUpscaled);
    if (lbBadge) lbBadge.style.display = isUpscaled ? 'block' : 'none';
    applyUpscaledSize();
    syncHeaderToImage();
    lbImg.classList.remove('lb-loading');
  });
  // Re-size on window resize: keep the upscaled image aspect-correct, and
  // keep the header pinned to the image edges. Coalesce via requestAnimationFrame
  // so it updates every frame during drag (no visible lag) but never more than
  // once per frame. Cheap operations (just style writes), so no extra debounce.
  var __lbResizeRaf = null;
  window.addEventListener('resize', function() {
    if (lb.style.display !== 'flex') return;
    if (__lbResizeRaf) return;
    __lbResizeRaf = requestAnimationFrame(function() {
      __lbResizeRaf = null;
      if (lbImg.classList.contains('lb-upscaled')) applyUpscaledSize();
      syncHeaderToImage();
    });
  });

  // Cheap-and-effective preload: keep the next/prev frames warm in the browser
  // cache so navigation feels instant even on the small (192px) variant.
  function preloadNeighbors(i) {
    [(i - 1 + frames.length) % frames.length, (i + 1) % frames.length].forEach(function(k) {
      var f = frames[k]; if (!f || f._preloaded) return;
      var img = new Image();
      // medium first; server falls back to small if md missing.
      img.src = '/api/frames/' + f.id + '/thumb?size=md';
      f._preloaded = true;
    });
  }

  // Renders a key/value chip if the value is non-null and non-empty. Numeric
  // formatting is opt-in via the formatter; default is identity (avoids
  // accidentally rounding ints).
  function chip(label, val, fmt) {
    if (val == null || val === '' || (typeof val === 'number' && !isFinite(val))) return '';
    var v = fmt ? fmt(val) : String(val);
    return '<div class="m-row"><span class="m-k">' + esc(label) + '</span><span class="m-v">' + esc(v) + '</span></div>';
  }
  function fix(n)    { return Number(n).toFixed(2); }
  function fix1(n)   { return Number(n).toFixed(1); }
  function int(n)    { return String(Math.round(Number(n))); }
  function arcsec(n) { return Number(n).toFixed(2) + '"'; }
  function px(n)     { return Number(n).toFixed(2); }

  // Empty placeholder rendered on first open so the panel has its full
  // dimensions immediately — prevents the "small loader → full content"
  // size pop while metrics are fetching.
  function renderSkeleton() {
    return (
      '<div class="m-strip">&nbsp;</div>' +
      '<div class="m-grid">' +
        '<div class="m-group"><div class="m-group-h">Capture</div></div>' +
        '<div class="m-group"><div class="m-group-h">ADU</div></div>' +
        '<div class="m-group"><div class="m-group-h">Guiding</div></div>' +
        '<div class="m-group"><div class="m-group-h">Environment</div></div>' +
      '</div>'
    );
  }

  function renderPanel(m) {
    if (!m) { lbPanel.innerHTML = ''; return; }

    // Status pill — color-keyed by accept/reject, prefixed with the source:
    //   "TS"     → from Target Scheduler grading (gradingStatus 1/2)
    //   "Manual" → from NINA's side (accepted=false, no TS row)
    // -1/null with accepted=true = no grading data anywhere → "Not graded"
    // when TS is available, but suppressed entirely for non-TS users since
    // an ungraded label is meaningless without grading as a concept.
    var status = '';
    if (m.gradingStatus === 2) {
      status = '<span class="m-status m-status-rejected">TS Rejected</span>';
      if (m.rejectReason) status += '<span class="m-reject">' + esc(m.rejectReason) + '</span>';
    } else if (m.accepted === false) {
      status = '<span class="m-status m-status-rejected">Manual Rejected</span>';
      if (m.rejectReason) status += '<span class="m-reject">' + esc(m.rejectReason) + '</span>';
    } else if (m.gradingStatus === 1) {
      status = '<span class="m-status m-status-accepted">TS Accepted</span>';
    } else if (m.gradingStatus === 0) {
      status = '<span class="m-status m-status-pending">TS Pending</span>';
    } else if (m.tsAvailable !== false) {
      status = '<span class="m-status m-status-ungraded">Not graded</span>';
    }

    // Header strip — status pill first, then quick-glance key stats inline.
    var header =
      '<div class="m-strip">' +
        status +
        chip('Date',   m.timestamp, fmtDate) +
        chip('Target', m.targetName) +
        chip('Filter', m.filter) +
        chip('Stars',  m.starCount, int) +
        chip('HFR',    m.hfr,       fix) +
        chip('FWHM',   m.fwhm,      fix) +
        chip('Eccen.', m.eccentricity, fix) +
      '</div>';

    // Capture column
    var capture =
      '<div class="m-group"><div class="m-group-h">Capture</div>' +
        chip('Exposure', m.exposureDuration, function(v) { return v + 's'; }) +
        chip('Gain',     m.gain >= 0 ? m.gain : null) +
        chip('Offset',   m.offset >= 0 ? m.offset : null) +
        chip('Binning',  m.binning > 0 ? m.binning + 'x' + m.binning : null) +
        chip('Readout',  m.readoutMode) +
        chip('Profile',  m.profileName) +
        chip('Project',          m.project) +
        chip('Exposure Profile', m.exposureTemplate) +
        (m.filePath
          ? (function() {
              var fname = m.filePath.split(/[\\/]/).pop();
              return '<div class="m-row"><span class="m-k">File</span><span class="m-v m-mono" title="' + esc(fname) + '">' + esc(fname) + '</span></div>';
            })()
          : '') +
      '</div>';

    // Quality dropped — HFR/FWHM/Eccentricity/Stars already in the m-strip
    // header above. HFR StDev was the only unique field (TS-only augment)
    // and isn't worth a whole group; can resurface elsewhere if needed.

    // ADU column (NS v2.10+ StatX columns)
    var adu =
      '<div class="m-group"><div class="m-group-h">ADU</div>' +
        chip('Min',    m.aduMin,    int) +
        chip('Max',    m.aduMax,    int) +
        chip('Mean',   m.aduMean,   fix1) +
        chip('Median', m.aduMedian, int) +
        chip('Std Dev',m.aduStDev,  fix1) +
      '</div>';

    // Guiding column — total from NS, RA/Dec from TS when present
    var guiding =
      '<div class="m-group"><div class="m-group-h">Guiding</div>' +
        chip('RMS px',    m.guidingRmsTotal     > 0 ? m.guidingRmsTotal : null,     px) +
        chip('RMS arcsec', m.guidingArcsec      > 0 ? m.guidingArcsec : null,       arcsec) +
        chip('RA px',     m.guidingRmsRa        != null ? m.guidingRmsRa : null,    px) +
        chip('RA arcsec', m.guidingRmsRaArcsec  != null ? m.guidingRmsRaArcsec : null, arcsec) +
        chip('Dec px',    m.guidingRmsDec       != null ? m.guidingRmsDec : null,   px) +
        chip('Dec arcsec',m.guidingRmsDecArcsec != null ? m.guidingRmsDecArcsec : null, arcsec) +
      '</div>';

    // Environment column
    var env =
      '<div class="m-group"><div class="m-group-h">Environment</div>' +
        chip('Airmass',     m.airmass,         fix) +
        chip('Altitude',    m.altitude,        function(v) { return fix1(v) + '°'; }) +
        chip('Azimuth',     m.azimuth,         function(v) { return fix1(v) + '°'; }) +
        chip('Camera Temp', m.cameraTemp,      function(v) { return fix1(v) + '°C'; }) +
        chip('Focuser Temp',m.focuserTemp,     function(v) { return fix1(v) + '°C'; }) +
        chip('Focuser Pos', m.focuserPosition) +
        chip('Ambient',     m.ambientTemp,     function(v) { return fix1(v) + '°C'; }) +
        chip('Humidity',    m.humidity,        function(v) { return fix1(v) + '%'; }) +
        chip('Pressure',    m.pressure,        function(v) { return fix1(v) + ' hPa'; }) +
      '</div>';

    lbPanel.innerHTML =
      header +
      '<div class="m-grid">' + capture + adu + guiding + env + '</div>';
  }

  function open(i) {
    idx = (i + frames.length) % frames.length;
    var f = frames[idx];
    // Reset upscale state until the new image's naturalHeight is known.
    // Also clear any inline width/height left over from a previous upscaled
    // frame so the next image starts from a clean CSS-driven size.
    lbImg.classList.remove('lb-upscaled');
    lbImg.style.width = '';
    lbImg.style.height = '';
    if (lbBadge) lbBadge.style.display = 'none';
    // Position counter at top of frame, e.g. "12 / 47". On mobile the
    // data-multi attr triggers '‹ 12 / 47 ›' chevrons via CSS pseudo-elements
    // — visual affordance for swipe-to-navigate (which replaces prev/next
    // buttons on small screens).
    if (lbCounter) {
      lbCounter.textContent = (idx + 1) + ' / ' + frames.length;
      if (frames.length > 1) lbCounter.setAttribute('data-multi', '');
      else                   lbCounter.removeAttribute('data-multi');
    }

    // Detect first-open (lightbox hidden) so we can synchronize reveal of image
    // and panel — both stay invisible until both finish loading, then fade in
    // together. Avoids the "skeleton then real content" two-step that looked
    // jagged. Prev/next navigation keeps prior content visible during fetch.
    var firstOpen = lb.style.display === 'none' || lb.style.display === '';
    lb.style.display = 'flex';
    document.body.style.overflow = 'hidden';

    if (firstOpen) {
      // Skeleton sets layout dimensions; .lb-pending hides image+panel content
      // (opacity 0) until everything is ready.
      lbPanel.innerHTML = renderSkeleton();
      lb.classList.add('lb-pending');
      if (lb.animate) {
        lb.animate(
          [{ opacity: 0 }, { opacity: 1 }],
          { duration: 180, easing: 'ease-out', fill: 'forwards' }
        );
      }
    } else if (!lbPanel.innerHTML.trim()) {
      lbPanel.innerHTML = renderSkeleton();
    }

    // Track which frame this fetch is for so a slow response from a previous
    // frame doesn't overwrite a newer frame's panel after the user clicked next.
    var fetchFrameId = f.id;

    // First-open: capture both image-load and metrics-fetch promises so we can
    // reveal them together. Subsequent navigates: render metrics as soon as
    // they arrive (image animation handled by navigate() slide).
    var imgReady = new Promise(function(resolve) {
      var done = function() { lbImg.removeEventListener('load', done); lbImg.removeEventListener('error', done); resolve(); };
      lbImg.addEventListener('load', done);
      lbImg.addEventListener('error', done);
      // Try medium first; server falls back to small if md missing.
      lbImg.src = '/api/frames/' + f.id + '/thumb?size=md';
    });

    var metricsReady = api('/api/frames/' + f.id + '/metrics')
      .then(function(m) {
        if (frames[idx].id !== fetchFrameId) return null;
        return m;
      })
      .catch(function(err) {
        logError('Failed to load frame metrics:', err && err.message);
        return null;
      });

    if (firstOpen) {
      Promise.all([imgReady, metricsReady]).then(function(results) {
        if (frames[idx].id !== fetchFrameId) return;
        var m = results[1];
        if (m) renderPanel(m);
        else   lbPanel.innerHTML = '<div class="m-loading">Metrics unavailable</div>';
        // Synchronized reveal — CSS transition does the fade.
        lb.classList.remove('lb-pending');
      });
    } else {
      metricsReady.then(function(m) {
        if (frames[idx].id !== fetchFrameId) return;
        if (m) renderPanel(m);
        else   lbPanel.innerHTML = '<div class="m-loading">Metrics unavailable</div>';
      });
    }

    // Warm the cache for the neighbors so the next click is instant.
    preloadNeighbors(idx);
  }

  // Slide the whole stage (image + panel) as one unit when navigating prev/next.
  // The user sees the current frame leave the viewport in the direction of travel,
  // then the new frame enters from the opposite side. Web Animations API instead
  // of CSS classes so we can chain the swap mid-animation cleanly.
  var navigating = false;
  function navigate(delta) {
    if (navigating) return;            // ignore mash-clicks during a transition
    if (frames.length < 2) return;
    var stage = lb.querySelector('.frames-lightbox-stage');
    if (!stage || !stage.animate) {     // no Web Animations support → fallback
      open(idx + delta);
      return;
    }
    navigating = true;
    var outX = delta > 0 ? '-12%' : '12%';
    var inX  = delta > 0 ? '12%'  : '-12%';
    stage.animate(
      [
        { transform: 'translateX(0)', opacity: 1 },
        { transform: 'translateX(' + outX + ')', opacity: 0 }
      ],
      { duration: 160, easing: 'ease-in', fill: 'forwards' }
    ).onfinish = function() {
      open(idx + delta);
      stage.animate(
        [
          { transform: 'translateX(' + inX + ')', opacity: 0 },
          { transform: 'translateX(0)', opacity: 1 }
        ],
        { duration: 180, easing: 'ease-out', fill: 'forwards' }
      ).onfinish = function() { navigating = false; };
    };
  }
  function close() {
    lb.style.display = 'none';
    lb.classList.remove('lb-pending');
    lbImg.src = '';
    lbImg.classList.remove('lb-loading', 'lb-upscaled');
    lbImg.style.width = '';
    lbImg.style.height = '';
    var header = lb.querySelector('.lb-header');
    if (header) header.style.width = '';
    if (lbBadge) lbBadge.style.display = 'none';
    lbPanel.innerHTML = '';
    document.body.style.overflow = '';
  }

  // Click delegation across all (now multiple) gallery grids — the wrapping
  // .frames-groups div catches every thumb regardless of which subgroup it lives in.
  var groupsRoot = document.querySelector('.frames-groups');
  if (groupsRoot) {
    groupsRoot.addEventListener('click', function(e) {
      var thumb = e.target.closest('.frames-thumb');
      if (!thumb) return;
      var id = +thumb.getAttribute('data-id');
      var i = frames.findIndex(function(f) { return f.id === id; });
      if (i >= 0) open(i);
    });
  }
  lb.querySelector('.frames-lightbox-close').addEventListener('click', close);
  lb.querySelector('.frames-lightbox-prev').addEventListener('click', function() { navigate(-1); });
  lb.querySelector('.frames-lightbox-next').addEventListener('click', function() { navigate(+1); });
  // Close when click lands on the dark backdrop OR on the stage's empty
  // areas (gap above/below image and panel). Stage on small viewports is
  // nearly fullscreen, so requiring the click to land on the dark border
  // makes outside-click unusable on mobile.
  lb.addEventListener('click', function(e) {
    if (e.target === lb || e.target.classList.contains('frames-lightbox-stage')) close();
  });

  // Touch swipe — replaces prev/next buttons on mobile (buttons display:none'd
  // via CSS @media). 50px horizontal threshold; vertical-dominant gestures are
  // ignored so the stage's overflow-y scroll continues to work. Left swipe
  // (dx < 0) advances to next, right swipe goes back — matches Photos.app and
  // every other mobile gallery.
  var touchX0 = 0, touchY0 = 0, touchActive = false;
  lb.addEventListener('touchstart', function(e) {
    if (e.touches.length !== 1) { touchActive = false; return; }
    touchX0 = e.touches[0].clientX;
    touchY0 = e.touches[0].clientY;
    touchActive = true;
  }, { passive: true });
  lb.addEventListener('touchend', function(e) {
    if (!touchActive || !e.changedTouches.length) return;
    touchActive = false;
    var dx = e.changedTouches[0].clientX - touchX0;
    var dy = e.changedTouches[0].clientY - touchY0;
    if (Math.abs(dx) < 50) return;                  // too short — treat as tap
    if (Math.abs(dy) > Math.abs(dx)) return;        // vertical-dominant — let stage scroll
    if (frames.length < 2) return;
    navigate(dx < 0 ? +1 : -1);
  }, { passive: true });
  document.addEventListener('keydown', function lbKey(e) {
    if (lb.style.display === 'none') { document.removeEventListener('keydown', lbKey); return; }
    if (e.key === 'Escape')          close();
    else if (e.key === 'ArrowLeft')  navigate(-1);
    else if (e.key === 'ArrowRight') navigate(+1);
  });
}

// ── Init ───────────────────────────────────────────────────────────────────

if ('scrollRestoration' in history) history.scrollRestoration = 'manual';
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
initCompanionBanner();
logInfo('Dashboard ready');

// ── Companion sync banner ────────────────────────────────────────────────
// Hidden in primary mode; in companion mode shows last-sync time + a Sync Now
// button. Polls every 30 s so the banner reflects scheduler runs without a
// page reload. Status reads come from /api/companion/status; the button POSTs
// /api/companion/sync (server coalesces concurrent triggers).
var COMPANION_MODE = false;

function initCompanionBanner() {
  fetch('/api/mode').then(function(r){ return r.json(); }).then(function(j){
    if (!j || j.mode !== 'companion') return;
    COMPANION_MODE = true;
    var banner = document.getElementById('companion-banner');
    if (banner) banner.hidden = false;
    var settingsLink = document.querySelector('.nav-link.companion-only[data-page="settings"]');
    if (settingsLink) settingsLink.hidden = false;
    var btn = document.getElementById('companion-sync-btn');
    // Use .onclick (not addEventListener) so renderCompanionStatus can swap
    // the handler when the button morphs into "Open Settings" during setup.
    if (btn) btn.onclick = companionSyncNow;
    refreshCompanionStatus();
    setInterval(refreshCompanionStatus, 10000);
    // If config is incomplete on first paint, redirect to setup so the user
    // doesn't land on an empty Sessions tab and wonder what to do.
    fetch('/api/companion/config').then(function(r){ return r.json(); }).then(function(c){
      if (c && c.isComplete === false && location.hash !== '#/settings') {
        navigate('#/settings');
      }
    }).catch(function(){});
  }).catch(function(){ /* ignore — primary mode or transient failure */ });
}

function refreshCompanionStatus() {
  fetch('/api/companion/status').then(function(r){
    if (!r.ok) throw new Error('status ' + r.status);
    return r.json();
  }).then(renderCompanionStatus).catch(function(){
    var el = document.getElementById('companion-banner-status');
    if (el) el.textContent = 'Status unavailable';
  });
}

function companionSyncNow() {
  var btn = document.getElementById('companion-sync-btn');
  var banner = document.getElementById('companion-banner');
  var statusEl = document.getElementById('companion-banner-status');
  if (btn) btn.disabled = true;
  if (banner) banner.classList.add('is-syncing');
  if (statusEl) statusEl.textContent = 'Syncing…';
  fetch('/api/companion/sync', { method: 'POST' }).then(function(r){
    if (!r.ok) throw new Error('sync ' + r.status);
    return r.json();
  }).then(function(s){
    renderCompanionStatus(s);
  }).catch(function(err){
    if (statusEl) statusEl.textContent = 'Sync failed: ' + (err && err.message || 'unknown');
    if (banner) banner.classList.add('is-error');
  }).finally(function(){
    if (btn) btn.disabled = false;
    if (banner) banner.classList.remove('is-syncing');
  });
}

function renderCompanionStatus(s) {
  var statusEl = document.getElementById('companion-banner-status');
  var banner = document.getElementById('companion-banner');
  var btn = document.getElementById('companion-sync-btn');
  if (!statusEl || !banner) return;
  banner.classList.remove('is-stale', 'is-error', 'is-setup');

  // Setup-required path takes precedence — no point talking about syncs or
  // reachability when there's no host to reach.
  if (s.isComplete === false) {
    banner.classList.add('is-setup');
    statusEl.textContent = 'Setup required — ' + (s.incompleteReason || 'finish configuration to start syncing');
    if (btn) {
      btn.disabled = false;
      btn.textContent = 'Open Settings';
      btn.onclick = function() { navigate('#/settings'); };
    }
    return;
  }
  // Restore Sync Now wiring if we previously hijacked the button for setup.
  if (btn && btn.textContent !== 'Sync Now') {
    btn.textContent = 'Sync Now';
    btn.onclick = companionSyncNow;
  }

  // Reachability prefix — only when we have a definitive answer. Disable Sync Now
  // when offline so the user gets immediate feedback rather than a slow timeout.
  var reachPrefix = '';
  if (s.primaryReachable === false) {
    reachPrefix = 'Primary offline · ';
    banner.classList.add('is-error');
    if (btn) btn.disabled = true;
  } else if (s.primaryReachable === true) {
    reachPrefix = 'Primary online · ';
    if (btn && !s.isRunning) btn.disabled = false;
  } else {
    if (btn && !s.isRunning) btn.disabled = false;
  }

  if (s.isRunning) {
    statusEl.textContent = reachPrefix + 'Sync in progress…';
    return;
  }
  if (s.lastError) {
    banner.classList.add('is-error');
    statusEl.textContent = reachPrefix + 'Last sync failed: ' + s.lastError;
    return;
  }
  if (!s.lastSuccessUtc) {
    statusEl.textContent = reachPrefix + 'No sync yet — click Sync Now to pull from the primary.';
    return;
  }
  var when = new Date(s.lastSuccessUtc);
  var ageMin = (Date.now() - when.getTime()) / 60000;
  if (ageMin > 60 * 24) banner.classList.add('is-stale');
  statusEl.textContent = reachPrefix + 'Last synced ' + relativeTime(ageMin) +
    ' (primary v' + (s.primaryVersion || '?') + ')';
}

function relativeTime(ageMin) {
  if (ageMin < 1)   return 'just now';
  if (ageMin < 60)  return Math.round(ageMin) + ' min ago';
  var ageH = ageMin / 60;
  if (ageH < 24)    return ageH.toFixed(1) + ' h ago';
  return (ageH / 24).toFixed(1) + ' d ago';
}

// ── Companion Settings tab ────────────────────────────────────────────────
// Edits companion.json via /api/companion/config. The api key is masked when
// loaded; the input shows a placeholder and only sends a value on save when
// the user actually types one (else the server keeps the existing key).
function renderSettingsPage() {
  document.getElementById('page-subtitle').textContent = 'Companion settings';
  var content = document.getElementById('content');
  if (!COMPANION_MODE) {
    content.innerHTML = '<div class="settings-shell"><div class="settings-card"><p>Settings are only available when running in companion mode.</p></div></div>';
    return;
  }
  content.innerHTML = '<div class="settings-shell"><div class="settings-card"><p>Loading…</p></div></div>';
  fetch('/api/companion/config').then(function(r){
    if (!r.ok) throw new Error('config ' + r.status);
    return r.json();
  }).then(function(c){
    content.innerHTML = settingsHtml(c);
    bindSettingsForm(c);
  }).catch(function(err){
    content.innerHTML = '<div class="settings-shell"><div class="settings-card is-error"><p>Failed to load config: ' + esc(err.message || 'unknown') + '</p></div></div>';
  });
}

function settingsHtml(c) {
  var setupBanner = c.isComplete ? '' :
    '<div class="settings-card is-setup"><strong>Setup required</strong><p>' +
    esc(c.incompleteReason || 'Fill in the fields below to start syncing from your NINA machine.') +
    '</p></div>';
  return '' +
    '<div class="settings-shell">' +
      setupBanner +
      '<form class="settings-card" id="settings-form" autocomplete="off">' +
        '<h2>Primary NINA</h2>' +
        '<label class="settings-row">' +
          '<span class="settings-label">Host <span class="settings-hint">IP, hostname, or Tailnet name</span></span>' +
          '<input type="text" id="cfg-host" value="' + esc(c.host || '') + '" placeholder="100.x.y.z or nina-rig" required>' +
        '</label>' +
        '<label class="settings-row">' +
          '<span class="settings-label">Port <span class="settings-hint">NINA Night Summary plugin port (default 8181)</span></span>' +
          '<input type="number" id="cfg-port" value="' + (c.port || 8181) + '" min="1" max="65535" required>' +
        '</label>' +
        '<label class="settings-row">' +
          '<span class="settings-label">API key <span class="settings-hint">Copy from the NS plugin settings on the primary</span></span>' +
          '<div class="settings-key-row">' +
            '<input type="password" id="cfg-apikey" placeholder="' + (c.apiKeySet ? esc(c.apiKeyMasked) + ' (leave blank to keep)' : 'paste api key') + '">' +
            '<button type="button" class="settings-key-toggle" id="cfg-apikey-show" title="Show/hide">show</button>' +
          '</div>' +
        '</label>' +
        '<h2>Sync schedule</h2>' +
        '<label class="settings-row settings-row-inline">' +
          '<input type="checkbox" id="cfg-onboot"' + (c.onBoot ? ' checked' : '') + '>' +
          '<span>Sync once when the companion server starts</span>' +
        '</label>' +
        '<label class="settings-row">' +
          '<span class="settings-label">Interval after success <span class="settings-hint">Hours between syncs while the primary is reachable</span></span>' +
          '<input type="number" id="cfg-success" value="' + (c.pollingIntervalHoursOnSuccess || 4) + '" min="1" max="168">' +
        '</label>' +
        '<label class="settings-row">' +
          '<span class="settings-label">Interval after failure <span class="settings-hint">Minutes between retries while the primary is offline</span></span>' +
          '<input type="number" id="cfg-failure" value="' + (c.pollingIntervalMinutesOnFailure || 30) + '" min="1" max="1440">' +
        '</label>' +
        '<h2>Storage</h2>' +
        '<div class="settings-row">' +
          '<span class="settings-label">Data directory <span class="settings-hint">Read-only; edit companion.json directly to relocate (will orphan existing data)</span></span>' +
          '<input type="text" value="' + esc(c.dataDir || '') + '" readonly>' +
        '</div>' +
        '<div class="settings-row">' +
          '<span class="settings-label">Dashboard port <span class="settings-hint">Edit companion.json directly; restart required</span></span>' +
          '<input type="text" value="' + (c.dashboardPort || '') + '" readonly>' +
        '</div>' +
        '<div class="settings-actions">' +
          '<div class="settings-status" id="cfg-status"></div>' +
          '<button type="button" class="settings-btn" id="cfg-test">Test connection</button>' +
          '<button type="submit" class="settings-btn settings-btn-primary" id="cfg-save">Save</button>' +
        '</div>' +
      '</form>' +
    '</div>';
}

function bindSettingsForm(initial) {
  var form    = document.getElementById('settings-form');
  var hostEl  = document.getElementById('cfg-host');
  var portEl  = document.getElementById('cfg-port');
  var keyEl   = document.getElementById('cfg-apikey');
  var keyToggle = document.getElementById('cfg-apikey-show');
  var bootEl  = document.getElementById('cfg-onboot');
  var sucEl   = document.getElementById('cfg-success');
  var failEl  = document.getElementById('cfg-failure');
  var status  = document.getElementById('cfg-status');
  var testBtn = document.getElementById('cfg-test');
  var saveBtn = document.getElementById('cfg-save');

  keyToggle.addEventListener('click', function() {
    var showing = keyEl.type === 'text';
    keyEl.type = showing ? 'password' : 'text';
    keyToggle.textContent = showing ? 'show' : 'hide';
  });

  function readEdit() {
    return {
      host: hostEl.value.trim(),
      port: parseInt(portEl.value, 10) || 0,
      // Empty string from the form means "leave the saved key alone".
      apiKey: keyEl.value === '' ? null : keyEl.value,
      onBoot: !!bootEl.checked,
      pollingIntervalHoursOnSuccess:   parseInt(sucEl.value, 10) || 0,
      pollingIntervalMinutesOnFailure: parseInt(failEl.value, 10) || 0,
    };
  }

  function setStatus(text, cls) {
    status.textContent = text || '';
    status.className = 'settings-status' + (cls ? ' ' + cls : '');
  }

  testBtn.addEventListener('click', function() {
    var edit = readEdit();
    setStatus('Testing…', '');
    testBtn.disabled = true;
    fetch('/api/companion/test-connection', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ host: edit.host, port: edit.port, apiKey: edit.apiKey || '' }),
    }).then(function(r){ return r.json(); }).then(function(j){
      if (j.ok) {
        var info = j.version ? ' · primary v' + j.version : '';
        setStatus('Connected' + info, 'is-ok');
      } else {
        setStatus('Failed: ' + (j.error || 'unknown'), 'is-error');
      }
    }).catch(function(err){
      setStatus('Failed: ' + (err.message || 'network error'), 'is-error');
    }).finally(function(){ testBtn.disabled = false; });
  });

  form.addEventListener('submit', function(e) {
    e.preventDefault();
    var edit = readEdit();
    setStatus('Saving…', '');
    saveBtn.disabled = true;
    fetch('/api/companion/config', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(edit),
    }).then(function(r){ return r.json().then(function(j){ return { status: r.status, body: j }; }); }).then(function(o){
      if (o.body && o.body.ok) {
        setStatus('Saved. ' + (o.body.config && o.body.config.isComplete ? 'Initial sync starting.' : 'Setup still incomplete.'), 'is-ok');
        // Re-render so the masked key reflects whatever was saved and to flip
        // any "setup required" banner off.
        renderSettingsPage();
        // Refresh the top banner immediately too — config changes affect reachability.
        if (typeof refreshCompanionStatus === 'function') refreshCompanionStatus();
      } else {
        setStatus('Save failed: ' + (o.body && o.body.error || ('http ' + o.status)), 'is-error');
      }
    }).catch(function(err){
      setStatus('Save failed: ' + (err.message || 'network error'), 'is-error');
    }).finally(function(){ saveBtn.disabled = false; });
  });
}
