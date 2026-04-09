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

function fmtDateTime(iso) {
  return fmtDate(iso) + '  ' + fmtTime(iso);
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
  'L': '#90A4AE', // Luminance  — full spectrum blue-silver
  'R': '#FF7043', // Red BB     — warm orange-red (broadband = less spectrally pure)
  'G': '#66BB6A', // Green BB
  'B': '#42A5F5', // Blue BB
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
  var color = TARGET_COLORS[index % TARGET_COLORS.length];
  var initial = t.target ? t.target.charAt(0).toUpperCase() : '?';

  var html = '<div class="target-card">';

  // Thumbnail
  html += '<div class="target-card-thumb" data-session-id="' + esc(t.latestSessionId || '') + '" data-target="' + esc(t.target) + '">';
  html += '<span class="thumb-placeholder">' + esc(initial) + '</span>';
  html += '</div>';

  // Body
  html += '<div class="target-card-body">';

  // Name with accent border
  html += '<div class="target-card-name" style="border-left:3px solid ' + color + '">' + esc(t.target) + '</div>';

  // Meta line
  var meta = [];
  if (t.sessionCount) meta.push(t.sessionCount + ' session' + (t.sessionCount !== 1 ? 's' : ''));
  if (t.lastImaged) meta.push('Last: ' + fmtDate(t.lastImaged));
  if (meta.length) html += '<div class="target-card-meta">' + esc(meta.join(' \u00b7 ')) + '</div>';

  // Stat boxes — Hours and Frames are expandable (hover/tap shows per-filter breakdown)
  html += '<div class="target-card-stats">';
  var hours = t.totalIntegrationHours != null ? t.totalIntegrationHours.toFixed(1) : '--';
  var frames = t.acceptedFrames != null ? t.acceptedFrames : '--';
  html += '<div class="target-card-stat target-stat-expandable" data-stat-type="integration" data-target-idx="' + index + '">' +
    '<div class="target-card-stat-value">' + esc(String(hours)) + '<span class="target-card-stat-unit">h</span></div>' +
    '<div class="target-card-stat-label">Hours</div></div>';
  html += '<div class="target-card-stat target-stat-expandable" data-stat-type="frames" data-target-idx="' + index + '">' +
    '<div class="target-card-stat-value">' + esc(String(frames)) + '</div>' +
    '<div class="target-card-stat-label">Frames</div></div>';
  html += targetStatBox(t.avgHFR ? t.avgHFR.toFixed(2) : '--', 'HFR', 'px');
  html += targetStatBox(t.avgGuidingRMS ? t.avgGuidingRMS.toFixed(2) + '"' : '--', 'Guide');
  html += '</div>';

  html += '</div></div>';
  return html;
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

  Object.keys(sessionMap).forEach(function(sid) {
    api('/api/sessions/' + sid + '/thumbnails').then(function(thumbs) {
      if (!Array.isArray(thumbs)) return;
      sessionMap[sid].forEach(function(el) {
        var target = el.getAttribute('data-target');
        var match = null;
        for (var i = 0; i < thumbs.length; i++) {
          if (thumbs[i].target === target) { match = thumbs[i]; break; }
        }
        if (match && match.dataUri) {
          el.innerHTML = '<img src="' + match.dataUri + '" alt="' + esc(target) + '">';
          el.classList.add('has-image');
          (function(thumbEl) {
            thumbEl.addEventListener('click', function(e) {
              e.stopPropagation();
              var img = thumbEl.querySelector('img');
              if (!img) return;
              var overlay = document.createElement('div');
              overlay.className = 'livestack-zoom-overlay';
              var zoomImg = document.createElement('img');
              zoomImg.src = img.src;
              zoomImg.alt = img.alt;
              overlay.appendChild(zoomImg);
              overlay.addEventListener('click', function() { overlay.remove(); });
              document.body.appendChild(overlay);
            });
          })(el);
        }
      });
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
      '<div class="card-stat stat-hfr"><div class="card-stat-value">' + fmtNum(s.avgHfr) + '<span class="card-stat-unit">px</span></div><div class="card-stat-label">HFR</div></div>' +
      '<div class="card-stat stat-fwhm"><div class="card-stat-value">' + fmtNum(s.avgFwhm) + '<span class="card-stat-unit">&Prime;</span></div><div class="card-stat-label">FWHM</div></div>' +
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

  // Detach rendered altitude divs before rebuild — the IO observer watches the element
  // itself, so we must preserve the exact node (not just its children)
  var savedAltNodes = {};
  if (initialLoadDone) {
    filtered.forEach(function(s) {
      if (!altitudeChartCache[s.sessionId]) return;
      var altEl = document.getElementById('altitude-' + s.sessionId);
      if (!altEl || !altEl.hasChildNodes()) return;
      altEl.parentNode.removeChild(altEl); // detach; reference keeps it alive
      savedAltNodes[s.sessionId] = altEl;
    });
  }

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
    // Slot saved altitude divs back in — same DOM node, IO observer intact, no replay
    Object.keys(savedAltNodes).forEach(function(id) {
      var freshEl = document.getElementById('altitude-' + id);
      if (freshEl && freshEl.parentNode) {
        freshEl.parentNode.replaceChild(savedAltNodes[id], freshEl);
      }
    });
    // Lazy-load any newly visible uncached charts via IntersectionObserver
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

function showTargetStatExpand(el, targetIdx, type) {
  var t = statsTargetData && statsTargetData[targetIdx];
  if (!t || !t.filters || t.filters.length === 0) return;

  var SORT_ORDER = ['L', 'R', 'G', 'B', 'H', 'S', 'O', 'N'];
  var sorted = t.filters.slice().filter(function(f) {
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
    var fc = getFilterColor(f.filter);
    var typeLetter = resolveFilterType(f.filter) || f.filter.charAt(0).toUpperCase();
    var dot = '<span class="filter-type-dot" style="background:' + (fc || 'var(--dim)') + '">' + esc(typeLetter) + '</span>';
    return '<div class="stat-expand-row">' +
      '<span class="stat-expand-filter">' + dot + '<span style="color:var(--text-secondary)">' + esc(f.filter) + '</span></span>' +
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
    var targetIdx = parseInt(tel.dataset.targetIdx, 10);
    var ttype = tel.dataset.statType;
    if (statExpandActiveEl === tel) {
      hideStatExpand();
    } else {
      statExpandActiveEl = tel;
      showTargetStatExpand(tel, targetIdx, ttype);
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

// Event delegation for target card stat box hover expansion (desktop only)
document.addEventListener('mouseenter', function(e) {
  if (isTouchDevice) return;
  var el = e.target.closest('.target-stat-expandable');
  if (!el) return;
  var targetIdx = parseInt(el.dataset.targetIdx, 10);
  var type = el.dataset.statType;
  if (isNaN(targetIdx) || !type) return;
  clearTimeout(statExpandTimer);
  statExpandActiveEl = el;
  statExpandTimer = setTimeout(function() {
    showTargetStatExpand(el, targetIdx, type);
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
      document.querySelectorAll('.card-thumb-wrap svg').forEach(function(svg) {
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

function renderStatsTabContent(tabId) {
  var container = document.getElementById('stats-tab-content');
  if (!container) return;

  if (tabId === 'targets') {
    var targets = statsTargetData || [];
    if (targets.length === 0) {
      container.innerHTML = '<div class="empty">No target data available yet.</div>';
      return;
    }
    var html = '<div class="target-grid">';
    targets.forEach(function(t, i) {
      html += renderTargetCard(t, i);
    });
    html += '</div>';
    container.innerHTML = html;
    loadTargetThumbnails();
  }
}

function renderStats() {
  var el = document.getElementById('content');
  var sub = document.getElementById('page-subtitle');
  sub.textContent = 'Lifetime Statistics';

  el.innerHTML = '<div class="loading">Loading stats...</div>';

  Promise.all([
    api('/api/stats/targets'),
    api('/api/stats/summary'),
    api('/api/settings')
  ]).then(function(results) {
    var targetData = results[0];
    var summary    = results[1];
    var settings   = results[2];
    var targets = targetData.targets || [];
    statsTargetData = targets;

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

    // All-Time Summary
    html += '<div class="detail-section"><h2>All-Time Summary</h2><div class="stat-grid">';
    html += statBox(summary.totalSessions, 'Sessions');
    html += statBox(summary.targetCount, 'Targets');
    html += statBox(summary.totalIntegrationHours.toFixed(1) + 'h', 'Integration');
    html += statBox(summary.totalImages != null ? summary.totalImages : '--', 'Images');
    if (summary.firstSession) {
      html += statBox(fmtDate(summary.firstSession), 'First Session', 'stat-date');
    }
    if (summary.lastSession) {
      html += statBox(fmtDate(summary.lastSession), 'Last Session', 'stat-date');
    }
    html += '</div></div>';

    // Tab bar + content
    var tabs = [{id: 'targets', label: 'Targets'}];
    var activeTab = localStorage.getItem('ns-stats-tab') || 'targets';
    if (!tabs.some(function(t) { return t.id === activeTab; })) activeTab = 'targets';

    html += renderTabBar(tabs, activeTab);
    html += '<div id="stats-tab-content"></div>';

    el.innerHTML = html;

    initTabBar(renderStatsTabContent);
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
