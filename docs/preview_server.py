#!/usr/bin/env python3
"""
Night Summary docs live-preview server.
  - Reads .md files dynamically on every request
  - Pushes SSE reload event when any .md file changes
  - Run: python preview_server.py
  - Open: http://localhost:4000
"""

import http.server
import os
import re
import threading
import time
import markdown
from pathlib import Path

DOCS_DIR  = Path(__file__).parent
PORT      = 4000
MD_EXT    = ['tables', 'fenced_code', 'attr_list', 'sane_lists', 'md_in_html']

MIME = {
    '.png': 'image/png', '.jpg': 'image/jpeg', '.jpeg': 'image/jpeg',
    '.gif': 'image/gif', '.svg': 'image/svg+xml', '.ico': 'image/x-icon',
    '.webp': 'image/webp',
}

# ── Page registry (controls sidebar order + status badges) ────────────────────
PAGES = [
    ('index',                      'Home',                         'unchanged'),
    ('getting-started',            'Getting Started',              'updated'),
    ('report-sections',            'Report Sections',              'unchanged'),
    ('dashboard',                  'Live Dashboard',               'new'),
    ('delivery-channels',          'Delivery Channels',            'unchanged'),
    ('settings-reference',         'Settings Reference',           'updated'),
    ('equipment-profile',          'Equipment Profile',            'unchanged'),
    ('overhead-breakdown',         'Yield and Overhead Analysis',  'unchanged'),
    ('file-naming-patterns',       'File Naming Patterns',         'unchanged'),
    ('live-stack-integration',     'Live Stack Integration',       'unchanged'),
    ('target-scheduler-integration','Target Scheduler Integration','unchanged'),
    ('metric-charts',              'Metric Charts',                'unchanged'),
    ('faq',                        'FAQ & Troubleshooting',        'updated'),
]

PAGE_NOTES = {
    'index':            'Added "Live dashboard" to Key Features. Version bumped to v3.0+.',
    'getting-started':  'Added Live Dashboard link to the Next Steps section.',
    'settings-reference': 'Added new <em>Local Dashboard</em> section.',
    'faq':              'Added new <em>Live Dashboard</em> troubleshooting section.',
    'dashboard':        'Entirely new page — documents the built-in local web dashboard.',
}

# ── SSE broadcast ─────────────────────────────────────────────────────────────
_sse_clients = []
_sse_lock    = threading.Lock()

def broadcast_reload():
    with _sse_lock:
        dead = []
        for q in _sse_clients:
            try:
                q.append('reload')
            except Exception:
                dead.append(q)
        for d in dead:
            _sse_clients.remove(d)

# ── File watcher ──────────────────────────────────────────────────────────────
def _watch():
    mtimes = {}
    while True:
        changed = False
        for f in DOCS_DIR.glob('*.md'):
            mtime = f.stat().st_mtime
            if mtimes.get(f) != mtime:
                if f in mtimes:
                    print(f'  ↺  {f.name} changed — reloading browsers')
                    changed = True
                mtimes[f] = mtime
        if changed:
            broadcast_reload()
        time.sleep(0.5)

threading.Thread(target=_watch, daemon=True).start()

# ── Markdown rendering ────────────────────────────────────────────────────────
def strip_frontmatter(text):
    """Remove Jekyll YAML front matter (--- ... ---)."""
    return re.sub(r'^---\s*\n.*?\n---\s*\n', '', text, count=1, flags=re.DOTALL)

def preprocess_callouts(text):
    """
    Convert Kramdown-style callouts:
      {: .note }
      > text
    → <div class="callout callout-note">text</div>
    """
    def replace(m):
        cls  = m.group(1).strip()
        body = m.group(2).strip()
        # strip leading '> ' from each line
        body = re.sub(r'^> ?', '', body, flags=re.MULTILINE)
        # process inline markdown (bold, italic, code, links)
        body = markdown.markdown(body, extensions=MD_EXT)
        # strip wrapping <p> tags for single-paragraph callouts
        body = re.sub(r'^<p>(.*)</p>$', r'\1', body.strip(), flags=re.DOTALL)
        return f'<div class="callout callout-{cls}">{body}</div>\n'
    return re.sub(r'\{:\s*\.([\w-]+)\s*\}\n((?:> .+\n?)+)', replace, text)

def preprocess_image_refs(text):
    """Pass images through as-is — assets are served from /assets/."""
    return text

def preprocess_jekyll_links(text):
    """Convert {% link foo.md %} to just the page title placeholder."""
    return re.sub(r'\{%\s*link\s+([^\s%]+\.md)\s*%\}', lambda m: '#', text)

def render_md(slug):
    path = DOCS_DIR / f'{slug}.md'
    if not path.exists():
        return f'<p><em>File not found: {slug}.md</em></p>'
    raw = path.read_text(encoding='utf-8')
    raw = strip_frontmatter(raw)
    raw = preprocess_callouts(raw)
    raw = preprocess_image_refs(raw)
    raw = preprocess_jekyll_links(raw)
    html = markdown.markdown(raw, extensions=MD_EXT)
    return html

# ── HTML shell ────────────────────────────────────────────────────────────────
CSS = """
*,*::before,*::after{box-sizing:border-box;margin:0;padding:0}
:root{
  --sidebar-w:248px;
  --sidebar-bg:#141425;
  --sidebar-border:#2d2d5e;
  --sidebar-text:#8888aa;
  --sidebar-active:#7eb8f7;
  --sidebar-active-bg:rgba(126,184,247,0.08);
  --bg:#1a1a2e;
  --text:#d0d0e0;
  --heading:#e8e8f0;
  --link:#7eb8f7;
  --border:#2d2d5e;
  --code-bg:#0f0f20;
  --table-bg:#16213e;
  --table-head:#1e2a45;
  --table-even:#1a2540;
  --note-bg:rgba(84,174,255,0.08);
  --note-border:#54aeff;
  --imp-bg:rgba(210,153,34,0.1);
  --imp-border:#d29922;
}
body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif;
  font-size:16px;line-height:1.7;color:var(--text);background:var(--bg);
  display:flex;min-height:100vh}

/* ── Sidebar ── */
#sidebar{width:var(--sidebar-w);min-width:var(--sidebar-w);
  background:var(--sidebar-bg);border-right:1px solid var(--sidebar-border);
  position:sticky;top:0;height:100vh;overflow-y:auto;
  display:flex;flex-direction:column;flex-shrink:0}

.site-header{
  padding:16px 16px 14px;
  border-bottom:1px solid var(--sidebar-border);
  display:flex;align-items:center;gap:10px}
.site-header-icon{width:32px;height:32px;flex-shrink:0;opacity:.9}
.site-title{font-size:15px;font-weight:700;color:#e0e4f0;line-height:1.2}
.site-subtitle{font-size:11px;color:var(--sidebar-active);margin-top:2px}

.site-nav{padding:10px 0 20px;flex:1}
.nav-list{list-style:none}
.nav-list-item{margin:0}
.nav-list-link{
  display:block;padding:5px 16px;font-size:14px;
  color:var(--sidebar-text);text-decoration:none;cursor:pointer;
  border:none;background:none;width:100%;text-align:left;
  border-left:2px solid transparent;
  transition:color .12s,background .12s}
.nav-list-link:hover{color:#c0c4d8;background:rgba(255,255,255,.03)}
.nav-list-link.active{
  color:var(--sidebar-active);font-weight:600;
  border-left-color:var(--sidebar-active);
  background:var(--sidebar-active-bg)}

/* ── Main ── */
#main{flex:1;min-width:0;overflow-y:auto;background:var(--bg)}

/* ── Content ── */
.main-content{
  max-width:800px;padding:36px 28px 80px 32px;
  margin:0 auto}

h1{font-size:2rem;font-weight:700;line-height:1.25;
   color:var(--heading);margin-bottom:.8rem;
   border-bottom:1px solid var(--border);padding-bottom:.5rem}
h2{font-size:1.375rem;font-weight:700;line-height:1.3;
   color:var(--heading);margin:2rem 0 .5rem}
h3{font-size:1.125rem;font-weight:600;line-height:1.4;
   color:var(--heading);margin:1.5rem 0 .4rem}
h4{font-size:1rem;font-weight:600;color:var(--heading);margin:1.2rem 0 .3rem}
p{margin-bottom:.9rem}
ul,ol{margin:0 0 .9rem 1.4rem}
li{margin-bottom:.3rem}
li>ul,li>ol{margin-top:.3rem;margin-bottom:.2rem}
a{color:var(--link);text-decoration:none}
a:hover{text-decoration:underline}
strong{font-weight:700;color:#e0e0ee}
em{font-style:italic}
hr{border:none;border-top:1px solid var(--border);margin:1.8rem 0}

/* code */
code{background:var(--code-bg);border:1px solid var(--border);
     border-radius:4px;padding:1px 5px;font-size:.875rem;
     font-family:'Cascadia Code','Consolas','Courier New',monospace;color:#c9d1d9}
pre{background:var(--code-bg);border:1px solid var(--border);border-radius:6px;
    padding:14px 16px;overflow-x:auto;margin-bottom:1rem}
pre code{background:none;border:none;padding:0;font-size:.875rem}

/* tables */
table{width:100%;border-collapse:collapse;margin-bottom:1.1rem;font-size:.9375rem;
      display:block;overflow-x:auto}
thead{background:var(--table-head)}
th{text-align:left;padding:8px 13px;border:1px solid var(--border);
   font-weight:600;font-size:.875rem;color:var(--heading)}
td{padding:7px 13px;border:1px solid var(--border);vertical-align:top;
   background:var(--table-bg);color:var(--text)}
tr:nth-child(even) td{background:var(--table-even)}

/* callouts */
.callout{border-radius:5px;padding:12px 16px;margin:1rem 0;font-size:.9375rem;line-height:1.6}
.callout p:last-child{margin-bottom:0}
.callout-note{background:var(--note-bg);border-left:4px solid var(--note-border)}
.callout-important{background:var(--imp-bg);border-left:4px solid var(--imp-border)}

/* images */
img{max-width:100%;height:auto;border-radius:6px;border:1px solid var(--border);display:block;margin:.6rem 0}
img.no-lightbox{border:none;border-radius:0;display:inline;margin:0}

/* audit badges (sidebar) */
.badge-new{display:inline-block;font-size:9px;font-weight:700;letter-spacing:.04em;
  padding:1px 5px;border-radius:3px;margin-left:6px;vertical-align:middle;
  background:rgba(84,174,255,0.18);color:#54aeff;border:1px solid rgba(84,174,255,0.3)}
.badge-upd{display:inline-block;font-size:9px;font-weight:700;letter-spacing:.04em;
  padding:1px 5px;border-radius:3px;margin-left:6px;vertical-align:middle;
  background:rgba(210,153,34,0.18);color:#d29922;border:1px solid rgba(210,153,34,0.3)}

/* legend (sidebar footer) */
.legend{padding:12px 16px 16px;border-top:1px solid var(--sidebar-border);
  font-size:11px;color:var(--sidebar-text)}
.legend-title{font-weight:600;color:#6666aa;margin-bottom:6px}
.legend-row{display:flex;align-items:center;gap:6px;margin-bottom:4px}

/* page banners */
.banner{border-radius:5px;padding:10px 14px;margin-bottom:1.4rem;
  font-size:.9rem;display:flex;align-items:flex-start;gap:10px;line-height:1.5}
.banner-new{background:rgba(84,174,255,0.07);border:1px solid rgba(84,174,255,0.22);
  border-left:4px solid #54aeff}
.banner-upd{background:rgba(210,153,34,0.07);border:1px solid rgba(210,153,34,0.22);
  border-left:4px solid #d29922}
.banner-icon{font-size:1.05rem;line-height:1.5;flex-shrink:0}

/* live-reload badge */
#live-badge{position:fixed;bottom:16px;right:16px;
  background:rgba(45,164,78,.9);color:#fff;
  font-size:11px;font-weight:700;padding:5px 13px;border-radius:100px;
  opacity:0;transition:opacity .3s;pointer-events:none;z-index:999}
#live-badge.show{opacity:1}
"""

JS = """
var currentPage = null;

function loadPage(slug) {
  currentPage = slug;
  document.querySelectorAll('.nav-item').forEach(function(el) {
    el.classList.toggle('active', el.dataset.slug === slug);
  });
  var content = document.getElementById('content');
  content.innerHTML = '<p style="color:#888;padding:20px 0">Loading...</p>';
  fetch('/page/' + slug)
    .then(function(r) { return r.text(); })
    .then(function(html) {
      content.innerHTML = html;
      window.scrollTo(0, 0);
      content.scrollTop = 0;
    });
}

// SSE live reload
var es = new EventSource('/events');
es.onmessage = function(e) {
  if (e.data === 'reload' && currentPage) {
    var badge = document.getElementById('live-badge');
    badge.classList.add('show');
    loadPage(currentPage);
    setTimeout(function() { badge.classList.remove('show'); }, 1500);
  }
};

// Load first page on startup
window.addEventListener('DOMContentLoaded', function() {
  var first = document.querySelector('.nav-item[data-slug="dashboard"]');
  if (first) loadPage('dashboard');
});
"""

def build_sidebar():
    items = []
    for slug, title, status in PAGES:
        badge = ''
        if status == 'new':
            badge = '<span class="badge-new">NEW</span>'
        elif status == 'updated':
            badge = '<span class="badge-upd">UPD</span>'
        items.append(
            f'<button class="nav-list-link nav-item" data-slug="{slug}" onclick="loadPage(\'{slug}\')">'
            f'{title}{badge}</button>'
        )
    return '\n'.join(items)

SHELL = f"""<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Night Summary Docs — Live Preview</title>
<style>{CSS}</style>
</head>
<body>
<div id="sidebar">
  <div class="site-header">
    <div class="site-title">Night Summary</div>
    <div class="site-subtitle">v3.0 Beta Docs</div>
  </div>
  <nav class="site-nav">{build_sidebar()}</nav>
  <div class="legend">
    <div class="legend-title">v3 changes</div>
    <div class="legend-row"><span class="badge-new">NEW</span> New page</div>
    <div class="legend-row"><span class="badge-upd">UPD</span> Updated</div>
  </div>
</div>
<div id="main">
  <div class="main-content" id="content">
    <p style="color:#888;padding:20px 0">Loading...</p>
  </div>
</div>
<div id="live-badge">↺ reloaded</div>
<script>{JS}</script>
</body>
</html>"""

# ── Page endpoint ─────────────────────────────────────────────────────────────
def build_page_html(slug):
    status_map = {s: (t, st) for s, t, st in PAGES}
    _, status = status_map.get(slug, (slug, 'unchanged'))
    note = PAGE_NOTES.get(slug, '')
    body = render_md(slug)

    if status == 'new':
        banner = (
            f'<div class="banner banner-new">'
            f'<div class="banner-icon">🆕</div>'
            f'<div><strong>New page for v3</strong>'
            + (f' — {note}' if note else '') +
            f'</div></div>'
        )
    elif status == 'updated':
        banner = (
            f'<div class="banner banner-upd">'
            f'<div class="banner-icon">✏️</div>'
            f'<div><strong>Updated for v3</strong>'
            + (f' — {note}' if note else '') +
            f'</div></div>'
        )
    else:
        banner = ''

    return f'{banner}{body}'

# ── HTTP handler ──────────────────────────────────────────────────────────────
class Handler(http.server.BaseHTTPRequestHandler):

    def log_message(self, fmt, *args):
        pass  # silence default access log

    def do_GET(self):
        if self.path == '/':
            self._send(200, 'text/html; charset=utf-8', SHELL.encode())

        elif self.path.startswith('/assets/'):
            fname = self.path[8:].split('?')[0]
            fpath = DOCS_DIR / 'assets' / fname
            if fpath.exists() and fpath.is_file():
                ct = MIME.get(fpath.suffix.lower(), 'application/octet-stream')
                self._send(200, ct, fpath.read_bytes())
            else:
                self._send(404, 'text/plain', b'Not found')

        elif self.path.startswith('/page/'):
            slug = self.path[6:].split('?')[0].strip('/')
            html = build_page_html(slug)
            self._send(200, 'text/html; charset=utf-8', html.encode())

        elif self.path == '/events':
            self.send_response(200)
            self.send_header('Content-Type', 'text/event-stream')
            self.send_header('Cache-Control', 'no-cache')
            self.send_header('Connection', 'keep-alive')
            self.send_header('Access-Control-Allow-Origin', '*')
            self.end_headers()
            q = []
            with _sse_lock:
                _sse_clients.append(q)
            try:
                self.wfile.write(b'data: connected\n\n')
                self.wfile.flush()
                while True:
                    if q:
                        msg = q.pop(0)
                        self.wfile.write(f'data: {msg}\n\n'.encode())
                        self.wfile.flush()
                    else:
                        time.sleep(0.1)
            except (BrokenPipeError, ConnectionResetError):
                pass
            finally:
                with _sse_lock:
                    try:
                        _sse_clients.remove(q)
                    except ValueError:
                        pass

        else:
            self._send(404, 'text/plain', b'Not found')

    def _send(self, code, ct, body):
        self.send_response(code)
        self.send_header('Content-Type', ct)
        self.send_header('Content-Length', str(len(body)))
        self.end_headers()
        self.wfile.write(body)


# ── Entry point ───────────────────────────────────────────────────────────────
if __name__ == '__main__':
    server = http.server.ThreadingHTTPServer(('localhost', PORT), Handler)
    print(f'Night Summary docs preview')
    print(f'  http://localhost:{PORT}')
    print(f'  Watching: {DOCS_DIR}/*.md')
    print(f'  Ctrl-C to stop\n')
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print('\nStopped.')
