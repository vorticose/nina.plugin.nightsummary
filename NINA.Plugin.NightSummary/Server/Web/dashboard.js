// ── Night Summary Dashboard ──

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
  return new Date(iso).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' });
}

function fmtTime(iso) {
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

// ── API ────────────────────────────────────────────────────────────────────

function api(path) {
  return fetch(path).then(function(r) {
    if (!r.ok) throw new Error('HTTP ' + r.status);
    return r.json();
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
}

function navigate(hash) {
  location.hash = hash;
}

// ── Components ─────────────────────────────────────────────────────────────

function statBox(value, label) {
  return '<div class="stat-box">' +
    '<div class="stat-value">' + esc(String(value != null ? value : '--')) + '</div>' +
    '<div class="stat-label">' + esc(label) + '</div>' +
    '</div>';
}

// ── Sessions List Page ─────────────────────────────────────────────────────

var sessionsCache = [];
var selectedTargets = {}; // target name -> boolean (true = selected)

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
      // Initialize: all targets selected
      getAllTargets().forEach(function(t) { selectedTargets[t] = true; });
      doRenderList(el, sub, fromVal, toVal, sortVal);
    }).catch(function(err) {
      el.innerHTML = '<div class="error">Failed to load sessions: ' + esc(err.message) + '</div>';
    });
  } else {
    doRenderList(el, sub, fromVal, toVal, sortVal);
  }
}

function doRenderList(el, sub, fromFilter, toFilter, sortBy) {
  // Build target filter checkboxes
  var allTargets = getAllTargets();
  var targetFilterHtml = '';
  if (allTargets.length > 0) {
    targetFilterHtml = '<div class="target-filter">' +
      '<div class="target-filter-label">Targets</div>' +
      '<div class="target-filter-list">';
    allTargets.forEach(function(t) {
      var checked = selectedTargets[t] !== false ? 'checked' : '';
      targetFilterHtml += '<label class="target-check">' +
        '<input type="checkbox" data-target="' + esc(t) + '" ' + checked + '>' +
        '<span>' + esc(t) + '</span></label>';
    });
    targetFilterHtml += '</div>' +
      '<div class="target-filter-actions">' +
        '<button id="targets-all" class="filter-link">All</button>' +
        '<button id="targets-none" class="filter-link">None</button>' +
      '</div></div>';
  }

  var filterHtml = '<div class="filter-bar">' +
    '<div class="filter-dates">' +
      '<input type="date" id="filter-from" value="' + esc(fromFilter) + '" title="From date">' +
      '<input type="date" id="filter-to" value="' + esc(toFilter) + '" title="To date">' +
    '</div>' +
    '<div class="filter-sort">' +
      '<select id="filter-sort">' +
        '<option value="date-desc"' + (sortBy === 'date-desc' ? ' selected' : '') + '>Newest first</option>' +
        '<option value="date-asc"' + (sortBy === 'date-asc' ? ' selected' : '') + '>Oldest first</option>' +
        '<option value="integration"' + (sortBy === 'integration' ? ' selected' : '') + '>Most integration</option>' +
        '<option value="images"' + (sortBy === 'images' ? ' selected' : '') + '>Most images</option>' +
      '</select>' +
    '</div>' +
    '<button id="filter-clear" class="filter-link">Clear filters</button>' +
    '</div>';

  // Filter sessions
  var activeTargets = {};
  allTargets.forEach(function(t) {
    if (selectedTargets[t] !== false) activeTargets[t] = true;
  });
  var allSelected = Object.keys(activeTargets).length === allTargets.length;

  var filtered = sessionsCache.filter(function(s) {
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
    return b.sessionStart.localeCompare(a.sessionStart); // date-desc default
  });

  sub.textContent = filtered.length + ' of ' + sessionsCache.length + ' sessions';

  if (sessionsCache.length === 0) {
    el.innerHTML = filterHtml + '<div class="empty">No sessions recorded yet.</div>';
    bindListEvents();
    return;
  }

  if (filtered.length === 0) {
    el.innerHTML = targetFilterHtml + filterHtml + '<div class="empty">No sessions match the current filters.</div>';
    bindListEvents();
    return;
  }

  var cards = filtered.map(function(s) {
    var targetPills = s.targets.length > 0
      ? s.targets.map(function(t) { return '<span class="target-pill">' + esc(t) + '</span>'; }).join('')
      : '<span class="target-pill target-pill-none">No targets</span>';

    var badge = s.hasReport
      ? '<span class="badge badge-green">Report</span>'
      : '<span class="badge badge-red">No report</span>';

    return '<div class="session-card" onclick="navigate(\'#/sessions/' + s.sessionId + '\')">' +
      '<div class="session-header">' +
        '<span class="session-date">' + fmtDate(s.sessionStart) + '</span>' +
        badge +
      '</div>' +
      '<div class="session-targets">' + targetPills + '</div>' +
      '<div class="card-stats">' +
        '<div class="card-stat"><div class="card-stat-value">' + s.imageCount + '</div><div class="card-stat-label">Images</div></div>' +
        '<div class="card-stat"><div class="card-stat-value">' + fmt(s.totalIntegrationSeconds) + '</div><div class="card-stat-label">Integration</div></div>' +
        '<div class="card-stat"><div class="card-stat-value">' + fmtNum(s.avgHfr) + '</div><div class="card-stat-label">HFR</div></div>' +
        '<div class="card-stat"><div class="card-stat-value">' + fmtNum(s.avgGuiding) + '"</div><div class="card-stat-label">Guiding</div></div>' +
      '</div>' +
    '</div>';
  }).join('');

  el.innerHTML = targetFilterHtml + filterHtml + cards;
  bindListEvents();
}

function bindListEvents() {
  var fromEl = document.getElementById('filter-from');
  var toEl = document.getElementById('filter-to');
  var sortEl = document.getElementById('filter-sort');
  var clearEl = document.getElementById('filter-clear');
  var allBtn = document.getElementById('targets-all');
  var noneBtn = document.getElementById('targets-none');

  function getFilters() {
    return {
      from: fromEl ? fromEl.value : '',
      to: toEl ? toEl.value : '',
      sort: sortEl ? sortEl.value : 'date-desc'
    };
  }

  function refresh() {
    var f = getFilters();
    var el = document.getElementById('content');
    var sub = document.getElementById('page-subtitle');
    doRenderList(el, sub, f.from, f.to, f.sort);
  }

  if (fromEl) fromEl.addEventListener('change', refresh);
  if (toEl) toEl.addEventListener('change', refresh);
  if (sortEl) sortEl.addEventListener('change', refresh);

  if (clearEl) {
    clearEl.addEventListener('click', function() {
      getAllTargets().forEach(function(t) { selectedTargets[t] = true; });
      var el = document.getElementById('content');
      var sub = document.getElementById('page-subtitle');
      doRenderList(el, sub, '', '', 'date-desc');
    });
  }

  // Target checkboxes
  document.querySelectorAll('.target-check input').forEach(function(cb) {
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
}

// ── Session Detail Page (Report-First) ────────────────────────────────────

function renderSessionDetail(sessionId) {
  var el = document.getElementById('content');
  var sub = document.getElementById('page-subtitle');

  el.innerHTML = '<div class="loading">Loading report...</div>';

  api('/api/sessions/' + sessionId).then(function(detail) {
    var targets = detail.targets.map(function(t) { return t.target; }).join(', ') || 'Unknown';
    sub.textContent = fmtDate(detail.sessionStart) + ' \u2014 ' + targets;

    var html = '<div class="report-nav">' +
      '<a class="back-btn" href="#/sessions">\u2190 Sessions</a>' +
      '<div class="report-nav-info">' +
        '<span class="report-nav-date">' + fmtDate(detail.sessionStart) + '</span>' +
        '<span class="report-nav-targets">' + esc(targets) + '</span>' +
      '</div>' +
      '<div class="report-nav-actions">';

    if (detail.hasReport) {
      html += '<a href="/api/sessions/' + sessionId + '/report" target="_blank" class="report-btn">Open in New Tab \u2192</a>';
    }

    html += '</div></div>';

    if (detail.hasReport) {
      html += '<div class="report-viewer">' +
        '<iframe class="report-iframe" src="/api/sessions/' + sessionId + '/report" sandbox="allow-same-origin"></iframe>' +
      '</div>';
    } else {
      html += '<div class="empty">No report generated for this session.</div>';
    }

    el.innerHTML = html;
  }).catch(function(err) {
    el.innerHTML = '<a class="back-btn" href="#/sessions">\u2190 Sessions</a>' +
      '<div class="error">Failed to load session: ' + esc(err.message) + '</div>';
  });
}

// ── Stats Page ─────────────────────────────────────────────────────────────

function renderStats() {
  var el = document.getElementById('content');
  var sub = document.getElementById('page-subtitle');
  sub.textContent = 'Lifetime Statistics';

  el.innerHTML = '<div class="loading">Loading stats...</div>';

  Promise.all([
    api('/api/stats/targets'),
    api('/api/stats/summary')
  ]).then(function(results) {
    var targetData = results[0];
    var summary = results[1];
    var targets = targetData.targets || [];

    var html = '';

    html += '<div class="detail-section"><h2>All-Time Summary</h2><div class="stat-grid">';
    html += statBox(summary.totalSessions, 'Sessions');
    html += statBox(summary.totalIntegrationHours.toFixed(1) + 'h', 'Integration');
    html += statBox(summary.targetCount, 'Targets');
    if (summary.firstSession) {
      html += statBox(fmtDate(summary.firstSession), 'First Session');
    }
    if (summary.lastSession) {
      html += statBox(fmtDate(summary.lastSession), 'Last Session');
    }
    html += '</div></div>';

    if (targets.length > 0) {
      var maxHours = targets[0].totalIntegrationHours || 1;

      html += '<div class="detail-section"><h2>Integration by Target</h2>';
      html += '<table class="stats-table"><thead><tr>' +
        '<th>Target</th><th>Integration</th><th></th></tr></thead><tbody>';
      targets.forEach(function(t) {
        var pct = maxHours > 0 ? (t.totalIntegrationHours / maxHours * 100) : 0;
        html += '<tr><td>' + esc(t.target) + '</td>' +
          '<td>' + t.totalIntegrationHours.toFixed(1) + 'h</td>' +
          '<td style="width:50%"><div class="integration-bar" style="width:' + pct.toFixed(1) + '%"></div></td>' +
          '</tr>';
      });
      html += '</tbody></table></div>';
    } else {
      html += '<div class="empty">No target data available yet.</div>';
    }

    el.innerHTML = html;
  }).catch(function(err) {
    el.innerHTML = '<div class="error">Failed to load stats: ' + esc(err.message) + '</div>';
  });
}

// ── Init ───────────────────────────────────────────────────────────────────

initTheme();
document.getElementById('theme-toggle').addEventListener('click', toggleTheme);
window.addEventListener('hashchange', route);
route();
