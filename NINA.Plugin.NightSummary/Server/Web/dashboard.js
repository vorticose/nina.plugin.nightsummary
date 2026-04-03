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

function fmtDuration(startIso, endIso) {
  var ms = new Date(endIso) - new Date(startIso);
  var h = Math.floor(ms / 3600000);
  var m = Math.floor((ms % 3600000) / 60000);
  return h > 0 ? h + 'h ' + m + 'm' : m + 'm';
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

  // Update nav active state
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

function detailItem(label, value) {
  return '<div class="detail-item">' +
    '<div class="label">' + esc(label) + '</div>' +
    '<div class="value">' + esc(String(value != null ? value : '--')) + '</div>' +
    '</div>';
}

function metaSpan(label, value) {
  return '<span><span class="meta-label">' + esc(label) + ' </span>' +
    '<span class="meta-value">' + esc(String(value)) + '</span></span>';
}

// ── Sessions List Page ─────────────────────────────────────────────────────

var sessionsCache = [];

function renderSessionList(params) {
  var el = document.getElementById('content');
  var sub = document.getElementById('page-subtitle');

  // Build filter bar
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
      var match = s.targets.some(function(t) {
        return t.toLowerCase().indexOf(targetFilter.toLowerCase()) >= 0;
      });
      if (!match) return false;
    }
    if (fromFilter) {
      var sessionDate = s.sessionStart.substring(0, 10);
      if (sessionDate < fromFilter) return false;
    }
    if (toFilter) {
      var sessionDate2 = s.sessionStart.substring(0, 10);
      if (sessionDate2 > toFilter) return false;
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
    var duration = fmtDuration(s.sessionStart, s.sessionEnd);
    var targets = s.targets.length > 0 ? s.targets.join(', ') : 'No targets';
    var badge = s.hasReport
      ? '<span class="badge badge-green">Report</span>'
      : '<span class="badge badge-red">No report</span>';

    return '<div class="session-card" onclick="navigate(\'#/sessions/' + s.sessionId + '\')">' +
      '<div class="session-header">' +
        '<span class="session-date">' + fmtDateTime(s.sessionStart) + '</span>' +
        badge +
      '</div>' +
      '<div class="session-meta">' +
        metaSpan('Profile', s.profileName || 'Unknown') +
        metaSpan('Images', s.imageCount) +
        metaSpan('Duration', duration) +
        metaSpan('Integration', fmt(s.totalIntegrationSeconds)) +
        metaSpan('HFR', fmtNum(s.avgHfr)) +
        metaSpan('Guiding', fmtNum(s.avgGuiding) + '"') +
      '</div>' +
      '<div class="targets-row">Targets: ' + esc(targets) + '</div>' +
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
    // Update without triggering full re-render
    history.replaceState(null, '', '#' + hash);
    var params = new URLSearchParams(parts.join('&'));
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

// ── Session Detail Page ────────────────────────────────────────────────────

function renderSessionDetail(sessionId) {
  var el = document.getElementById('content');
  var sub = document.getElementById('page-subtitle');

  el.innerHTML = '<a class="back-btn" href="#/sessions">\u2190 All Sessions</a>' +
    '<div class="loading">Loading session...</div>';

  Promise.all([
    api('/api/sessions/' + sessionId),
    api('/api/sessions/' + sessionId + '/images'),
    api('/api/sessions/' + sessionId + '/events')
  ]).then(function(results) {
    var detail = results[0];
    var images = results[1];
    var events = results[2];

    sub.textContent = fmtDate(detail.sessionStart) + ' \u2014 ' + (detail.profileName || 'Unknown');

    var html = '<a class="back-btn" href="#/sessions">\u2190 All Sessions</a>';

    // Summary stat grid
    html += '<div class="detail-section"><h2>Summary</h2><div class="stat-grid">';
    html += statBox(fmtDuration(detail.sessionStart, detail.sessionEnd), 'Duration');
    html += statBox(detail.summary.totalImages + ' (' + detail.summary.accepted + ' ok)', 'Images');
    html += statBox(fmt(detail.summary.totalIntegrationSeconds), 'Integration');
    html += statBox(fmtNum(detail.summary.avgHfr), 'Avg HFR');
    html += statBox(fmtNum(detail.summary.avgFwhm), 'Avg FWHM');
    html += statBox(fmtNum(detail.summary.avgGuiding) + '"', 'Avg Guiding');
    html += statBox(fmtNum(detail.summary.avgStarCount, 0), 'Avg Stars');
    html += statBox(detail.summary.autoFocusRuns, 'AF Runs');
    if (detail.summary.meridianFlips > 0) html += statBox(detail.summary.meridianFlips, 'Flips');
    if (detail.skippedExposures > 0) html += statBox(detail.skippedExposures, 'Skipped');
    html += '</div></div>';

    // Equipment
    var eq = detail.equipment;
    var eqEntries = Object.entries(eq).filter(function(e) { return e[1]; });
    if (eqEntries.length > 0) {
      html += '<div class="detail-section"><details open><summary>Equipment</summary>' +
        '<div class="detail-grid" style="margin-top:10px">';
      eqEntries.forEach(function(e) {
        var label = e[0].replace(/([A-Z])/g, ' $1').replace(/^./, function(c) { return c.toUpperCase(); }).trim();
        html += detailItem(label, e[1]);
      });
      html += '</div></details></div>';
    }

    // Targets
    if (detail.targets.length > 0) {
      html += '<div class="detail-section"><h2>Targets</h2>';
      detail.targets.forEach(function(t) {
        html += '<div class="target-card"><h3>' + esc(t.target) + '</h3>';
        html += '<div class="target-stats">' +
          metaSpan('Images', t.imageCount) +
          metaSpan('Accepted', t.accepted) +
          metaSpan('Integration', fmt(t.integrationSeconds)) +
          metaSpan('HFR', fmtNum(t.avgHfr)) +
          metaSpan('FWHM', fmtNum(t.avgFwhm)) +
          metaSpan('Guiding', fmtNum(t.avgGuiding) + '"') +
          metaSpan('Stars', fmtNum(t.avgStarCount, 0)) +
          '</div>';
        if (t.filters && t.filters.length > 0) {
          html += '<div class="filter-pills">';
          t.filters.forEach(function(f) {
            html += '<span class="filter-pill">' + esc(f.filter) + ': ' +
              f.accepted + '/' + f.count + ' (' + fmt(f.integrationSeconds) + ')</span>';
          });
          html += '</div>';
        }
        html += '</div>';
      });
      html += '</div>';
    }

    // Events
    if (events.length > 0) {
      html += '<div class="detail-section"><details><summary>Events (' + events.length + ')</summary>';
      html += '<div class="table-scroll" style="margin-top:10px"><table class="data-table">';
      html += '<thead><tr><th>Time</th><th>Type</th><th>Details</th></tr></thead><tbody>';
      events.forEach(function(e) {
        var desc = e.description || '';
        if (e.eventType === 'AutoFocus') {
          desc = (e.afSucceeded ? 'Success' : 'Failed') +
            (e.afHfr > 0 ? ' \u2014 HFR: ' + fmtNum(e.afHfr) : '');
        }
        html += '<tr><td>' + fmtTime(e.timestamp) + '</td>' +
          '<td>' + esc(e.eventType) + '</td>' +
          '<td>' + esc(desc) + '</td></tr>';
      });
      html += '</tbody></table></div></details></div>';
    }

    // Images table
    if (images.length > 0) {
      html += '<div class="detail-section"><details><summary>Images (' + images.length + ')</summary>';
      html += '<div class="table-scroll" style="margin-top:10px"><table class="data-table">';
      html += '<thead><tr><th>Time</th><th>Target</th><th>Filter</th><th>Exp</th>' +
        '<th>HFR</th><th>FWHM</th><th>Stars</th><th>Guiding</th><th>Alt</th><th>Status</th></tr></thead><tbody>';
      images.forEach(function(i) {
        var status = i.accepted
          ? '<span style="color:var(--green)">OK</span>'
          : '<span style="color:var(--red)">Rejected</span>';
        html += '<tr>' +
          '<td>' + fmtTime(i.timestamp) + '</td>' +
          '<td>' + esc(i.targetName || '--') + '</td>' +
          '<td>' + esc(i.filter || '--') + '</td>' +
          '<td>' + (i.exposureDuration || '--') + 's</td>' +
          '<td>' + fmtNum(i.hfr) + '</td>' +
          '<td>' + fmtNum(i.fwhm) + '</td>' +
          '<td>' + (i.starCount > 0 ? i.starCount : '--') + '</td>' +
          '<td>' + fmtNum(i.guidingRmsTotal) + '</td>' +
          '<td>' + fmtNum(i.altitude, 1) + '\u00b0</td>' +
          '<td>' + status + '</td>' +
          '</tr>';
      });
      html += '</tbody></table></div></details></div>';
    }

    // Embedded report viewer
    if (detail.hasReport) {
      html += '<div class="detail-section report-section">' +
        '<div class="report-header">' +
          '<h2>Full Report</h2>' +
          '<div class="report-actions">' +
            '<button class="report-btn" id="toggle-report">\u25BC Show Report</button>' +
            '<a href="/api/sessions/' + sessionId + '/report" target="_blank" class="report-btn">Open in New Tab \u2192</a>' +
          '</div>' +
        '</div>' +
        '<div id="report-container" class="report-container" style="display:none">' +
          '<iframe id="report-iframe" class="report-iframe" sandbox="allow-same-origin"></iframe>' +
        '</div>' +
      '</div>';
    }

    el.innerHTML = html;
    bindReportToggle(sessionId);
  }).catch(function(err) {
    el.innerHTML = '<a class="back-btn" href="#/sessions">\u2190 All Sessions</a>' +
      '<div class="error">Failed to load session: ' + esc(err.message) + '</div>';
  });
}

function bindReportToggle(sessionId) {
  var btn = document.getElementById('toggle-report');
  var container = document.getElementById('report-container');
  var iframe = document.getElementById('report-iframe');
  if (!btn || !container || !iframe) return;

  var loaded = false;
  btn.addEventListener('click', function() {
    var visible = container.style.display !== 'none';
    if (visible) {
      container.style.display = 'none';
      btn.textContent = '\u25BC Show Report';
    } else {
      container.style.display = 'block';
      if (!loaded) {
        iframe.src = '/api/sessions/' + sessionId + '/report';
        loaded = true;
      }
      btn.textContent = '\u25B2 Hide Report';
    }
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

    // Summary stat boxes
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

    // Target integration table
    if (targets.length > 0) {
      var maxHours = targets.length > 0 ? targets[0].totalIntegrationHours : 1;

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
