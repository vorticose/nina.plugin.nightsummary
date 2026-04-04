// ── Night Summary Dashboard ──

// ── Utilities ──────────────────────────────────────────────────────────────

function fmt(seconds) {
  if (!seconds || seconds <= 0) return '--';
  return (seconds / 3600).toFixed(1) + 'h';
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

  if (path === '/sessions') {
    renderSessionList(params);
  } else if (path.match(/^\/sessions\/[^/]+$/)) {
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

function renderSessionList(params) {
  var el = document.getElementById('content');
  var sub = document.getElementById('page-subtitle');

  var targetVal = params ? (params.get('target') || '') : '';
  var fromVal = params ? (params.get('from') || '') : '';
  var toVal = params ? (params.get('to') || '') : '';

  var filterHtml = '<div class="filter-bar">' +
    '<input type="text" id="filter-target" placeholder="Search targets..." value="' + esc(targetVal) + '">' +
    '<input type="date" id="filter-from" value="' + esc(fromVal) + '">' +
    '<input type="date" id="filter-to" value="' + esc(toVal) + '">' +
    '<button id="filter-clear">Clear</button>' +
    '</div>';

  if (sessionsCache.length === 0) {
    el.innerHTML = filterHtml + '<div class="loading">Loading sessions...</div>';
    api('/api/sessions').then(function(data) {
      sessionsCache = data;
      renderSessionCards(el, filterHtml, targetVal, fromVal, toVal);
      sub.textContent = sessionsCache.length + ' sessions';
      bindFilterEvents();
    }).catch(function(err) {
      el.innerHTML = filterHtml + '<div class="error">Failed to load sessions: ' + esc(err.message) + '</div>';
    });
  } else {
    renderSessionCards(el, filterHtml, targetVal, fromVal, toVal);
    sub.textContent = sessionsCache.length + ' sessions';
    bindFilterEvents();
  }
}

function renderSessionCards(el, filterHtml, targetFilter, fromFilter, toFilter) {
  var filtered = sessionsCache.filter(function(s) {
    if (targetFilter) {
      var q = targetFilter.toLowerCase();
      var match = s.targets.some(function(t) {
        return t.toLowerCase().indexOf(q) >= 0;
      });
      if (!match) return false;
    }
    if (fromFilter) {
      if (s.sessionStart.substring(0, 10) < fromFilter) return false;
    }
    if (toFilter) {
      if (s.sessionStart.substring(0, 10) > toFilter) return false;
    }
    return true;
  });

  if (filtered.length === 0 && sessionsCache.length === 0) {
    el.innerHTML = filterHtml + '<div class="empty">No sessions recorded yet.</div>';
    return;
  }

  if (filtered.length === 0) {
    el.innerHTML = filterHtml + '<div class="empty">No sessions match the current filters.</div>';
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
      '<div class="session-stats">' +
        '<span>' + fmt(s.totalIntegrationSeconds) + ' int</span>' +
        '<span>HFR ' + fmtNum(s.avgHfr) + '</span>' +
        '<span>Guiding ' + fmtNum(s.avgGuiding) + '"</span>' +
        '<span>' + s.imageCount + ' images</span>' +
      '</div>' +
    '</div>';
  }).join('');

  el.innerHTML = filterHtml + cards;
}

function bindFilterEvents() {
  var targetEl = document.getElementById('filter-target');
  var fromEl = document.getElementById('filter-from');
  var toEl = document.getElementById('filter-to');
  var clearEl = document.getElementById('filter-clear');

  if (!targetEl) return;

  function applyFilters() {
    var parts = [];
    if (targetEl.value) parts.push('target=' + encodeURIComponent(targetEl.value));
    if (fromEl.value) parts.push('from=' + fromEl.value);
    if (toEl.value) parts.push('to=' + toEl.value);
    var hash = '/sessions' + (parts.length > 0 ? '?' + parts.join('&') : '');
    history.replaceState(null, '', '#' + hash);
    var el = document.getElementById('content');
    var filterHtml = el.querySelector('.filter-bar').outerHTML;
    renderSessionCards(el, filterHtml, targetEl.value, fromEl.value, toEl.value);
    bindFilterEvents();
  }

  targetEl.addEventListener('input', applyFilters);
  fromEl.addEventListener('change', applyFilters);
  toEl.addEventListener('change', applyFilters);

  clearEl.addEventListener('click', function() {
    targetEl.value = '';
    fromEl.value = '';
    toEl.value = '';
    history.replaceState(null, '', '#/sessions');
    applyFilters();
  });
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
