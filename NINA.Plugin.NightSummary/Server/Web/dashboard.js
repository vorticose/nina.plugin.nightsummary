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
        '<div class="target-dropdown-actions">' +
          '<button id="targets-all" class="filter-link">All</button>' +
          '<button id="targets-none" class="filter-link">None</button>' +
        '</div>';
    allTargets.forEach(function(t) {
      var checked = selectedTargets[t] !== false ? 'checked' : '';
      targetDropHtml += '<label class="target-check">' +
        '<input type="checkbox" data-target="' + esc(t) + '" ' + checked + '>' +
        '<span>' + esc(t) + '</span></label>';
    });
    targetDropHtml += '</div></div>';
  }

  var filterHtml = '<div class="filter-bar">' +
    targetDropHtml +
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
    el.innerHTML = filterHtml + '<div class="empty">No sessions match the current filters.</div>';
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

  el.innerHTML = filterHtml + cards;
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

  // Target dropdown toggle
  var dropBtn = document.getElementById('target-dropdown-btn');
  var dropMenu = document.getElementById('target-dropdown-menu');
  if (dropBtn && dropMenu) {
    dropBtn.addEventListener('click', function(e) {
      e.stopPropagation();
      dropMenu.classList.toggle('open');
    });
    // Close on click outside
    document.addEventListener('click', function closeDropdown(e) {
      var dropdown = document.getElementById('target-dropdown');
      if (dropdown && !dropdown.contains(e.target)) {
        dropMenu.classList.remove('open');
      }
    });
    // Prevent menu clicks from closing
    dropMenu.addEventListener('click', function(e) { e.stopPropagation(); });
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

var currentSettings = null;

var METRIC_OPTIONS = [
  'Time', 'Frame Index', 'HFR', 'FWHM', 'Guiding RMS', 'Focuser Temp',
  'Ambient Temp', 'Eccentricity', 'Altitude', 'Airmass', 'Humidity',
  'Focuser Position', 'Sky Quality', 'Cloud Cover', 'Camera Temp',
  'Dew Point', 'Wind Speed', 'Pressure', 'Star Count', 'Azimuth', 'Seeing FWHM'
];

var EQUIPMENT_FIELDS = [
  'Camera', 'Telescope', 'Mount', 'Filter Wheel', 'Focuser', 'Rotator',
  'Guider', 'Dome', 'Flat Device', 'Safety Monitor', 'Weather', 'Switch'
];

function metricSelect(id, value, includeNone) {
  var html = '<select id="' + id + '" class="settings-select">';
  if (includeNone) html += '<option value="0"' + (value === 0 ? ' selected' : '') + '>None</option>';
  var offset = includeNone ? 1 : 0;
  var startIdx = includeNone ? 0 : 0;
  METRIC_OPTIONS.forEach(function(m, i) {
    var val = includeNone ? i + 1 : i;
    html += '<option value="' + val + '"' + (value === val ? ' selected' : '') + '>' + esc(m) + '</option>';
  });
  html += '</select>';
  return html;
}

function settingsCheckbox(id, label, checked) {
  return '<label class="settings-check"><input type="checkbox" id="' + id + '"' +
    (checked ? ' checked' : '') + '><span>' + esc(label) + '</span></label>';
}

function buildSettingsPanel(settings) {
  var s = settings;
  var visibleFields = (s.equipmentVisibleFields || '').split(',').map(function(f) { return f.trim(); });

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
      settingsCheckbox('s-perTargetIQ', 'Per-Target IQ', s.showPerTargetIQ) +
      settingsCheckbox('s-equipment', 'Equipment Profile', s.showEquipmentProfile) +
      settingsCheckbox('s-expand', 'Expand Sections', s.expandSectionsDefault) +
    '</div></div></div>';

  // Row 3: Metric chart config
  html += '<div class="settings-row">' +
    '<div class="settings-group"><label class="settings-label">X-Axis</label>' +
      metricSelect('s-xAxis', s.chartXAxisMetric, false) + '</div>' +
    '<div class="settings-group"><label class="settings-label">Primary Metric</label>' +
      metricSelect('s-primary', s.chartPrimaryMetric, false) + '</div>' +
    '<div class="settings-group"><label class="settings-label">Secondary Metric</label>' +
      metricSelect('s-secondary', s.chartSecondaryMetric, true) + '</div>' +
  '</div>';

  // Row 4: Equipment visible fields
  html += '<div class="settings-row"><div class="settings-group"><label class="settings-label">Equipment Fields</label>' +
    '<div class="settings-checks">';
  EQUIPMENT_FIELDS.forEach(function(f) {
    var checked = visibleFields.indexOf(f) >= 0;
    html += settingsCheckbox('s-eq-' + f.replace(/\s/g, ''), f, checked);
  });
  html += '</div></div></div>';

  // Row 5: Advanced text inputs
  html += '<div class="settings-row">' +
    '<div class="settings-group"><label class="settings-label">Filter Classifications</label>' +
      '<input type="text" id="s-filterClass" class="settings-input" value="' + esc(s.filterClassifications || '') + '" placeholder="e.g. Ha:NB,OIII:NB,Lum:BB"></div>' +
    '<div class="settings-group"><label class="settings-label">Equipment Overrides</label>' +
      '<input type="text" id="s-eqOverrides" class="settings-input" value="' + esc(s.equipmentOverrides || '') + '" placeholder="e.g. Camera:ASI2600,Telescope:Esprit 100"></div>' +
  '</div>';

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
    showPerTargetIQ:       document.getElementById('s-perTargetIQ').checked,
    showEquipmentProfile:  document.getElementById('s-equipment').checked,
    chartXAxisMetric:      parseInt(document.getElementById('s-xAxis').value),
    chartPrimaryMetric:    parseInt(document.getElementById('s-primary').value),
    chartSecondaryMetric:  parseInt(document.getElementById('s-secondary').value),
    equipmentVisibleFields: visibleFields.join(','),
    filterClassifications: document.getElementById('s-filterClass').value,
    equipmentOverrides:    document.getElementById('s-eqOverrides').value
  };
}

function renderSessionDetail(sessionId) {
  var el = document.getElementById('content');
  var sub = document.getElementById('page-subtitle');

  el.innerHTML = '<div class="loading">Loading report...</div>';

  Promise.all([
    api('/api/sessions/' + sessionId),
    currentSettings ? Promise.resolve(currentSettings) : api('/api/settings')
  ]).then(function(results) {
    var detail = results[0];
    currentSettings = results[1];

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

    html += buildSettingsPanel(currentSettings);

    if (detail.hasReport) {
      html += '<div class="report-viewer">' +
        '<iframe id="report-iframe" class="report-iframe" src="/api/sessions/' + sessionId + '/report" sandbox="allow-same-origin"></iframe>' +
      '</div>';
    } else {
      html += '<div class="report-viewer">' +
        '<div class="empty">No report generated for this session. Click "Regenerate Report" to generate one.</div>' +
      '</div>';
    }

    el.innerHTML = html;
    bindDetailEvents(sessionId);
  }).catch(function(err) {
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

  if (regenBtn) {
    regenBtn.addEventListener('click', function() {
      var settings = collectSettings();
      status.textContent = 'Generating...';
      status.className = 'regen-status';
      regenBtn.disabled = true;

      fetch('/api/sessions/' + sessionId + '/regenerate', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(settings)
      }).then(function(r) { return r.json(); }).then(function(data) {
        if (data.status === 'ok') {
          status.textContent = 'Done';
          status.className = 'regen-status regen-ok';
          // Reload iframe
          var iframe = document.getElementById('report-iframe');
          if (iframe) {
            iframe.src = '/api/sessions/' + sessionId + '/report?t=' + Date.now();
          } else {
            // Report didn't exist before — re-render the whole page
            sessionsCache = []; // Clear cache to refresh hasReport
            renderSessionDetail(sessionId);
          }
        } else {
          status.textContent = data.error || 'Failed';
          status.className = 'regen-status regen-err';
        }
      }).catch(function(err) {
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
        return;
      }
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
          pollRegenAllProgress(sessionId, regenBtn, regenAllBtn, status);
        } else {
          status.textContent = data.error || 'Failed to start';
          status.className = 'regen-status regen-err';
          regenAllBtn.disabled = false;
          if (regenBtn) regenBtn.disabled = false;
        }
      }).catch(function(err) {
        status.textContent = err.message;
        status.className = 'regen-status regen-err';
        regenAllBtn.disabled = false;
        if (regenBtn) regenBtn.disabled = false;
      });
    });
  }
}

function pollRegenAllProgress(sessionId, regenBtn, regenAllBtn, statusEl) {
  var poll = setInterval(function() {
    fetch('/api/regenerate-all/status').then(function(r) { return r.json(); }).then(function(data) {
      if (data.status === 'running') {
        statusEl.textContent = 'Regenerating ' + data.current + '/' + data.total + '...';
        statusEl.className = 'regen-status';
      } else if (data.status === 'done') {
        clearInterval(poll);
        statusEl.textContent = 'Done \u2014 ' + data.generated + ' generated' + (data.failed > 0 ? ', ' + data.failed + ' failed' : '');
        statusEl.className = 'regen-status regen-ok';
        regenAllBtn.disabled = false;
        if (regenBtn) regenBtn.disabled = false;
        sessionsCache = [];
        var iframe = document.getElementById('report-iframe');
        if (iframe) iframe.src = iframe.src.split('?')[0] + '?t=' + Date.now();
      } else if (data.status === 'error') {
        clearInterval(poll);
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
