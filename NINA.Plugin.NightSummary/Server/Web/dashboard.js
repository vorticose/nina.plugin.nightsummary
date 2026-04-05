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
var showEmptySessions = false; // hide 0-image sessions by default
var showFovOverlay = localStorage.getItem('ns-show-fov') !== 'false'; // on by default
var cardViewMode = localStorage.getItem('ns-card-view') || 'expanded'; // 'expanded' or 'compact'
var hiddenSessions = JSON.parse(localStorage.getItem('ns-hidden-sessions') || '{}'); // sessionId -> true
var showHidden = false;
var livestackMap = {}; // sessionId -> { targetName -> [{filter, url, label, isComposite}] }

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
    '<div class="filter-sort">' +
      '<select id="filter-sort">' +
        '<option value="date-desc"' + (sortBy === 'date-desc' ? ' selected' : '') + '>Newest first</option>' +
        '<option value="date-asc"' + (sortBy === 'date-asc' ? ' selected' : '') + '>Oldest first</option>' +
        '<option value="integration"' + (sortBy === 'integration' ? ' selected' : '') + '>Most integration</option>' +
        '<option value="images"' + (sortBy === 'images' ? ' selected' : '') + '>Most images</option>' +
      '</select>' +
    '</div>' +
    '<button id="filter-clear" class="filter-link">Clear filters</button>' +
    '<div class="view-toggle">' +
      '<button class="view-toggle-btn' + (cardViewMode === 'compact' ? ' active' : '') + '" data-view="compact">Compact</button>' +
      '<button class="view-toggle-btn' + (cardViewMode === 'expanded' ? ' active' : '') + '" data-view="expanded">Expanded</button>' +
    '</div>' +
    '<label class="target-check" title="Include sessions with 0 captured images"><input type="checkbox" id="filter-empty"' + (showEmptySessions ? ' checked' : '') + '><span>Show empty</span></label>' +
    '<label class="target-check' + (cardViewMode === 'compact' ? ' disabled' : '') + '" title="Show camera FOV rectangle on thumbnails"><input type="checkbox" id="filter-fov"' + (showFovOverlay ? ' checked' : '') + (cardViewMode === 'compact' ? ' disabled' : '') + '><span>Show FOV</span></label>';

  // Add hidden session controls inline if any are hidden
  var tempHiddenCount = sessionsCache.filter(function(s) { return hiddenSessions[s.sessionId]; }).length;
  if (tempHiddenCount > 0) {
    filterHtml +=
      '<label class="target-check"><input type="checkbox" id="filter-hidden"' + (showHidden ? ' checked' : '') + '><span>Show hidden (' + tempHiddenCount + ')</span></label>' +
      '<button id="unhide-all" class="filter-link">Unhide all</button>';
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
      ? s.targets.map(function(t) { return esc(t); }).join(' \u00b7 ')
      : 'No targets';

    var badge = s.hasReport ? '' : '<span class="badge badge-red">No report</span>';

    var sessionTimes = fmtTime(s.sessionStart) + ' \u2013 ' + fmtTime(s.sessionEnd);

    var statsLine = '<span class="stat-val">' + s.imageCount + '</span> imgs' +
      ' &middot; <span class="stat-val">' + fmt(s.totalIntegrationSeconds) + '</span>' +
      ' &middot; HFR <span class="stat-val">' + fmtNum(s.avgHfr) + '</span>' +
      ' &middot; <span class="stat-val">' + fmtNum(s.avgGuiding) + '&Prime;</span> guiding';

    var moonBox = s.moonPhase
      ? '<div class="card-stat"><div class="card-stat-value">' + esc(s.moonPhase) + '</div><div class="card-stat-label">Moon</div></div>'
      : '';

    var statBoxes = '<div class="card-stats">' +
      '<div class="card-stat"><div class="card-stat-value">' + s.imageCount + '</div><div class="card-stat-label">Images</div></div>' +
      '<div class="card-stat"><div class="card-stat-value">' + fmt(s.totalIntegrationSeconds) + '</div><div class="card-stat-label">Integration</div></div>' +
      '<div class="card-stat"><div class="card-stat-value">' + fmtNum(s.avgHfr) + '</div><div class="card-stat-label">HFR</div></div>' +
      '<div class="card-stat"><div class="card-stat-value">' + fmtNum(s.avgGuiding) + '&Prime;</div><div class="card-stat-label">Guiding</div></div>' +
      moonBox +
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
        '<div class="card-altitude" id="altitude-' + s.sessionId + '"></div>' +
      '</div>' +
    '</div>';
  }).join('');

  var modeClass = cardViewMode === 'compact' ? ' cards-compact' : '';
  el.innerHTML = filterHtml + '<div class="cards-container' + modeClass + '">' + cards + '</div>';
  bindListEvents();
  if (cardViewMode === 'expanded') {
    loadThumbnails(filtered);
    loadLiveStacks(filtered);
    loadAltitudeCharts(filtered);
  }
}

function loadThumbnails(sessions) {
  sessions.forEach(function(s) {
    if (!s.hasReport) return;
    var container = document.getElementById('thumbs-' + s.sessionId);
    if (!container) return;

    api('/api/sessions/' + s.sessionId + '/thumbnails').then(function(thumbs) {
      if (!thumbs || thumbs.length === 0) return;
      var el = document.getElementById('thumbs-' + s.sessionId);
      if (!el) return;
      el.innerHTML = thumbs.map(function(t) {
        var img = '<img class="card-thumb" src="' + t.dataUri + '" alt="' + esc(t.target) + '" title="' + esc(t.target) + '" loading="lazy" onerror="this.style.display=\'none\'">';
        var svg = '';
        if (t.fovSvg) {
          svg = t.fovSvg
            .replace(/width='\d+'/, "width='100%'")
            .replace(/height='\d+'/, "height='100%'")
            .replace("<svg ", "<svg viewBox='0 0 200 200' " + (showFovOverlay ? '' : "style='display:none' "));
        }
        return '<div class="card-thumb-wrap" data-target="' + esc(t.target) + '" data-session="' + esc(s.sessionId) + '">' + img + svg + '</div>';
      }).join('');
      // Reorder target names to match thumbnail order
      var targetsEl = document.getElementById('targets-' + s.sessionId);
      if (targetsEl && thumbs.length > 0) {
        var thumbOrder = thumbs.map(function(t) { return t.target; });
        // Include any targets not in the report (no thumbnail)
        var remaining = s.targets.filter(function(t) { return thumbOrder.indexOf(t) === -1; });
        targetsEl.textContent = thumbOrder.concat(remaining).join(' \u00b7 ');
      }
    }).catch(function(err) {
      logDebug('Thumb load failed for', s.sessionId, err.message);
    });
  });
}

function loadLiveStacks(sessions) {
  sessions.forEach(function(s) {
    if (!s.hasReport) return;
    api('/api/sessions/' + s.sessionId + '/livestack').then(function(data) {
      // data is { targetName: [{target, filter, url, label, isComposite}] }
      if (!data || Object.keys(data).length === 0) return;
      livestackMap[s.sessionId] = data;

      // Add badges to thumbnails that have live stack data
      var thumbsEl = document.getElementById('thumbs-' + s.sessionId);
      if (!thumbsEl) return;
      var wraps = thumbsEl.querySelectorAll('.card-thumb-wrap');
      for (var i = 0; i < wraps.length; i++) {
        var target = wraps[i].getAttribute('data-target');
        if (target && data[target]) {
          var count = data[target].length;
          var badge = document.createElement('span');
          badge.className = 'livestack-badge';
          badge.textContent = count;
          badge.title = count + ' live stack image' + (count !== 1 ? 's' : '');
          wraps[i].appendChild(badge);
          setupLiveStackHover(wraps[i], s.sessionId, target);
        }
      }
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
    thumbsContainer.appendChild(shelf);

    // Calculate position: center shelf below the hovered thumb
    // Account for the transform:scale(1.67) on hover — the visual size is larger
    var wrapRect = thumbWrap.getBoundingClientRect();
    var containerRect = thumbsContainer.getBoundingClientRect();
    var centerX = (wrapRect.left + wrapRect.width / 2) - containerRect.left;
    var topY = wrapRect.bottom - containerRect.top + 45;

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
  var el = document.getElementById('content');
  var sub = document.getElementById('page-subtitle');
  doRenderList(el, sub, '', '', 'date-desc');
}

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
  crossLine.setAttribute('stroke', '#ffffff');
  crossLine.setAttribute('stroke-width', '0.5');
  crossLine.setAttribute('stroke-dasharray', '3,3');
  crossLine.setAttribute('opacity', '0.5');
  crossLine.style.display = 'none';
  crossLine.style.pointerEvents = 'none';
  svg.appendChild(crossLine);

  var tooltip = document.createElementNS(ns, 'g');
  tooltip.style.display = 'none';
  tooltip.style.pointerEvents = 'none';
  svg.appendChild(tooltip);

  // Time label element
  var timeText = document.createElementNS(ns, 'text');
  timeText.setAttribute('fill', '#fff');
  timeText.setAttribute('font-size', '9');
  timeText.setAttribute('text-anchor', 'middle');
  timeText.setAttribute('font-weight', 'bold');
  tooltip.appendChild(timeText);

  // Pre-create dot + label for each target
  var markers = targets.map(function(t) {
    var dot = document.createElementNS(ns, 'circle');
    dot.setAttribute('r', '3');
    dot.setAttribute('fill', t.color);
    dot.setAttribute('stroke', '#fff');
    dot.setAttribute('stroke-width', '0.8');
    tooltip.appendChild(dot);
    var label = document.createElementNS(ns, 'text');
    label.setAttribute('fill', t.color);
    label.setAttribute('font-size', '8');
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

  svg.addEventListener('mousemove', function(e) {
    // Map mouse to SVG viewBox coordinates using CTM (handles preserveAspectRatio=none)
    var pt = svg.createSVGPoint();
    pt.x = e.clientX; pt.y = e.clientY;
    var ctm = svg.getScreenCTM();
    var svgPt = pt.matrixTransform(ctm.inverse());
    var sx = svgPt.x;

    // Counter-transform for text: undo horizontal squash from preserveAspectRatio=none
    var scaleRatio = ctm.d / ctm.a; // yScale / xScale
    var textTransform = 'scale(' + scaleRatio.toFixed(3) + ', 1)';

    if (sx < plotL || sx > plotR) {
      crossLine.style.display = 'none';
      tooltip.style.display = 'none';
      return;
    }

    crossLine.setAttribute('x1', sx); crossLine.setAttribute('y1', plotT);
    crossLine.setAttribute('x2', sx); crossLine.setAttribute('y2', plotB);
    crossLine.style.display = '';
    tooltip.style.display = '';

    // Time at top — position just inside visible viewBox area
    var time = xToTime(sx);
    var timeY = vbMinY + 8;
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
      markers[i].dot.style.display = '';
      var alt = yToAlt(y).toFixed(0) + '\u00b0';
      markers[i].label.textContent = alt;
      // Position label to the right, offset to avoid overlap; counter-transform text
      var lx = sx + 5, ly2 = y - 4 - i * 10;
      markers[i].label.setAttribute('x', lx);
      markers[i].label.setAttribute('y', ly2);
      markers[i].label.setAttribute('transform', 'translate(' + lx + ',' + ly2 + ') ' + textTransform + ' translate(' + (-lx) + ',' + (-ly2) + ')');
      markers[i].label.style.display = '';
    }
  });

  svg.addEventListener('mouseleave', function() {
    crossLine.style.display = 'none';
    tooltip.style.display = 'none';
    // Restore all opacities
    for (var g = 0; g < targetGroups.length; g++) targetGroups[g].style.opacity = '1';
    for (var r = 0; r < imagingWindows.length; r++) imagingWindows[r].rect.style.opacity = '0.15';
    for (var l = 0; l < windowLines.length; l++) windowLines[l].style.opacity = '0.6';
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

  // Use IntersectionObserver to trigger draw/reset as card enters/leaves view
  var observer = new IntersectionObserver(function(entries) {
    entries.forEach(function(entry) {
      if (entry.isIntersecting) {
        // Draw: animate in
        polylines.forEach(function(p) {
          p.style.transition = 'stroke-dashoffset 0.5s ease-out';
          p.style.strokeDashoffset = '0';
        });
      } else {
        // Reset: instantly hide for next scroll-in
        polylines.forEach(function(p, i) {
          p.style.transition = 'none';
          p.style.strokeDashoffset = lengths[i];
        });
      }
    });
  }, { threshold: 0.3 });

  observer.observe(container);
}

function loadAltitudeCharts(sessions) {
  sessions.forEach(function(s) {
    if (!s.hasReport) return;
    var container = document.getElementById('altitude-' + s.sessionId);
    if (!container) return;

    api('/api/sessions/' + s.sessionId + '/altitude-chart').then(function(data) {
      if (!data || !data.svg) return;
      var el = document.getElementById('altitude-' + s.sessionId);
      if (!el) return;
      // Render legend as HTML + SVG in a chart-svg-wrap
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
      // Dynamically extend chart upward based on header height
      var card = el.closest('.session-card');
      var header = card ? card.querySelector('.card-header') : null;
      if (header) {
        var headerH = header.offsetHeight;
        var headerMargin = 4;
        var cardPadTop = 8;
        // Only add clearance if header text reaches close to the chart SVG graphics
        var lastChild = header.lastElementChild;
        var textRight = lastChild ? lastChild.getBoundingClientRect().right : 0;
        var svgWrap = el.querySelector('.chart-svg-wrap');
        var chartLeft = svgWrap ? svgWrap.getBoundingClientRect().left : el.getBoundingClientRect().left;
        var clearance = (textRight > chartLeft - 15) ? 18 : 0;
        var pullUp = Math.max(0, headerH + headerMargin - cardPadTop - clearance);
        el.style.marginTop = '-' + pullUp + 'px';
      }
      // Add has-chart class to card-body so CSS can reserve space
      var body = el.parentElement;
      if (body) body.classList.add('has-chart');
    }).catch(function(err) {
      logDebug('Altitude chart load failed for', s.sessionId, err.message);
    });
  });
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
  if (sortEl) sortEl.addEventListener('change', refresh);

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
      cardViewMode = this.dataset.view;
      localStorage.setItem('ns-card-view', cardViewMode);
      refresh();
    });
  });
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

  // Row 5: Filter classifications
  if (filters && filters.length > 0) {
    html += '<div class="settings-row"><div class="settings-group">' +
      '<label class="settings-label">Filter Classifications</label>' +
      '<div class="filter-class-grid">';
    filters.forEach(function(f) {
      var code = filterClass[f] || 'A';
      var idx = CLASSIFICATION_CODES.indexOf(code);
      if (idx < 0) idx = 0;
      html += '<div class="filter-class-row">' +
        '<span class="filter-class-name">' + esc(f) + '</span>' +
        '<select class="fc-select settings-select" data-filter="' + esc(f) + '">';
      CLASSIFICATION_OPTIONS.forEach(function(opt, oi) {
        html += '<option value="' + CLASSIFICATION_CODES[oi] + '"' + (oi === idx ? ' selected' : '') + '>' + esc(opt) + '</option>';
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
    if (sel.value !== 'A') {
      fcParts.push(sel.dataset.filter + '=' + sel.value);
    }
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
            sessionsCache = []; // Clear cache to refresh hasReport
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
        sessionsCache = [];
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

    logInfo('Stats loaded:', summary.totalSessions, 'sessions,', targets.length, 'targets');
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
    logError('Failed to load stats:', err.message);
    el.innerHTML = '<div class="error">Failed to load stats: ' + esc(err.message) + '</div>';
  });
}

// ── Init ───────────────────────────────────────────────────────────────────

logInfo('Dashboard initializing');
initTheme();
document.getElementById('theme-toggle').addEventListener('click', toggleTheme);
window.addEventListener('hashchange', route);
route();
logInfo('Dashboard ready');
