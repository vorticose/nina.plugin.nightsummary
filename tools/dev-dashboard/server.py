#!/usr/bin/env python3
"""Development server for the Night Summary dashboard.

Serves the dashboard HTML/CSS/JS from the source tree (re-read on every request
for instant hot reload) backed by snapshotted API data from the live server.

Usage:
    python server.py                  # defaults: port 8182, data/ directory
    python server.py -p 9000          # custom port
    python server.py -d /path/to/data # custom data directory
"""

import argparse
import base64
import json
import os
import re
import sys
from http.server import HTTPServer, BaseHTTPRequestHandler

# Paths computed relative to this script's location
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
WEB_DIR = os.path.normpath(os.path.join(SCRIPT_DIR, "..", "..", "NINA.Plugin.NightSummary", "Server", "Web"))
ICON_PATH = os.path.normpath(os.path.join(SCRIPT_DIR, "..", "..", "assets", "plugin-icon.png"))

# Cached at startup (icon never changes during dev)
ICON_DATA_URI = ""


def load_icon():
    """Load and base64-encode the plugin icon once at startup."""
    global ICON_DATA_URI
    if os.path.exists(ICON_PATH):
        with open(ICON_PATH, "rb") as f:
            ICON_DATA_URI = "data:image/png;base64," + base64.b64encode(f.read()).decode("ascii")


class DashboardHandler(BaseHTTPRequestHandler):
    data_dir = ""

    def log_message(self, format, *args):
        """Compact log format."""
        sys.stderr.write(f"  {args[0]}\n")

    def send_json(self, status, obj):
        body = json.dumps(obj, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def serve_file(self, filepath, content_type):
        if not os.path.isfile(filepath):
            self.send_json(404, {"error": "Not found"})
            return
        with open(filepath, "rb") as f:
            data = f.read()
        self.send_response(200)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(data)))
        if content_type == "image/jpeg":
            self.send_header("Cache-Control", "public, max-age=3600")
        else:
            self.send_header("Cache-Control", "no-cache, no-store, must-revalidate")
        self.end_headers()
        self.wfile.write(data)

    def serve_dashboard(self):
        """Assemble dashboard HTML from source tree files (re-read every time)."""
        try:
            html_path = os.path.join(WEB_DIR, "dashboard.html")
            css_path = os.path.join(WEB_DIR, "dashboard.css")
            js_path = os.path.join(WEB_DIR, "dashboard.js")

            with open(html_path, "r", encoding="utf-8") as f:
                html = f.read()
            with open(css_path, "r", encoding="utf-8") as f:
                css = f.read()
            with open(js_path, "r", encoding="utf-8") as f:
                js = f.read()

            html = html.replace("{{STYLES}}", css)
            html = html.replace("{{SCRIPTS}}", js)
            html = html.replace("{{ICON}}", ICON_DATA_URI)

            body = html.encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
            self.send_header("Cache-Control", "no-cache, no-store, must-revalidate")
            self.send_header("Pragma", "no-cache")
            self.end_headers()
            self.wfile.write(body)
        except FileNotFoundError as e:
            self.send_json(500, {"error": f"Source file not found: {e.filename}"})

    def route(self, method):
        path = self.path.split("?")[0].rstrip("/")  # strip query params and trailing slash
        parts = path.split("/")
        data_dir = self.data_dir

        # Root - serve assembled dashboard HTML
        if path == "" or path == "/":
            if method == "GET":
                self.serve_dashboard()
            else:
                self.send_json(405, {"error": "Method not allowed"})
            return

        # Health check
        if path == "/api/health":
            self.send_json(200, {"status": "ok"})
            return

        # Global endpoints
        global_map = {
            "/api/sessions": "sessions.json",
            "/api/stats/targets": "stats-targets.json",
            "/api/stats/summary": "stats-summary.json",
            "/api/filters": "filters.json",
            "/api/settings": "settings.json",
        }
        if path in global_map:
            self.serve_file(os.path.join(data_dir, global_map[path]), "application/json")
            return

        # Regenerate-all status (mock)
        if path == "/api/regenerate-all/status":
            self.send_json(200, {
                "status": "idle", "current": 0, "total": 0,
                "generated": 0, "failed": 0, "error": None
            })
            return

        # Regenerate-all POST (mock)
        if path == "/api/regenerate-all" and method == "POST":
            self.send_json(200, {"status": "ok", "total": 0})
            return

        # Per-session endpoints: /api/sessions/{id}/...
        # Match: /api/sessions/{id}/livestack/{filename} (6 segments)
        if len(parts) == 6 and parts[1] == "api" and parts[2] == "sessions" and parts[4] == "livestack":
            session_id = parts[3]
            filename = parts[5]
            if ".." in session_id or ".." in filename:
                self.send_json(400, {"error": "Invalid path"})
                return
            filepath = os.path.join(data_dir, "sessions", session_id, "livestack", filename)
            self.serve_file(filepath, "image/jpeg")
            return

        # Match: /api/sessions/{id}/{endpoint} (5 segments)
        if len(parts) == 5 and parts[1] == "api" and parts[2] == "sessions":
            session_id = parts[3]
            endpoint = parts[4]
            if ".." in session_id:
                self.send_json(400, {"error": "Invalid path"})
                return

            # POST regenerate (mock)
            if endpoint == "regenerate" and method == "POST":
                self.send_json(200, {"status": "ok", "sessionId": session_id})
                return

            endpoint_map = {
                "thumbnails": ("thumbnails.json", "application/json"),
                "livestack": ("livestack.json", "application/json"),
                "altitude-chart": ("altitude-chart.json", "application/json"),
                "images": ("images.json", "application/json"),
                "events": ("events.json", "application/json"),
                "timing": ("timing.json", "application/json"),
                "settings": ("settings.json", "application/json"),
                "report": ("report.html", "text/html; charset=utf-8"),
            }
            if endpoint in endpoint_map:
                filename, content_type = endpoint_map[endpoint]
                filepath = os.path.join(data_dir, "sessions", session_id, filename)
                self.serve_file(filepath, content_type)
                return

        # Match: /api/sessions/{id} (4 segments)
        if len(parts) == 4 and parts[1] == "api" and parts[2] == "sessions":
            session_id = parts[3]
            if ".." in session_id:
                self.send_json(400, {"error": "Invalid path"})
                return
            filepath = os.path.join(data_dir, "sessions", session_id, "detail.json")
            self.serve_file(filepath, "application/json")
            return

        # Not found
        self.send_json(404, {"error": f"Not found: {path}"})

    def do_GET(self):
        self.route("GET")

    def do_POST(self):
        # Read and discard body
        length = int(self.headers.get("Content-Length", 0))
        if length > 0:
            self.rfile.read(length)
        self.route("POST")


def main():
    parser = argparse.ArgumentParser(description="Night Summary Dashboard Dev Server")
    parser.add_argument("-p", "--port", type=int, default=8182,
                        help="Listen port (default: 8182)")
    parser.add_argument("-d", "--data", default=None,
                        help="Data directory (default: data/ next to this script)")
    args = parser.parse_args()

    data_dir = args.data or os.path.join(SCRIPT_DIR, "data")
    data_dir = os.path.normpath(os.path.abspath(data_dir))
    DashboardHandler.data_dir = data_dir

    # Startup validation
    html_path = os.path.join(WEB_DIR, "dashboard.html")
    if not os.path.isfile(html_path):
        print(f"ERROR: dashboard.html not found at {WEB_DIR}")
        print(f"  This script must be run from within the nina.plugin.template repo.")
        sys.exit(1)

    sessions_path = os.path.join(data_dir, "sessions.json")
    session_count = 0
    if os.path.isfile(sessions_path):
        with open(sessions_path, "r", encoding="utf-8") as f:
            session_count = len(json.load(f))
    else:
        print(f"WARNING: No snapshot data found at {data_dir}")
        print(f"  Run: python snapshot.py")
        print()

    load_icon()

    print(f"Night Summary Dev Dashboard")
    print(f"  Source:   {WEB_DIR}")
    print(f"  Data:     {data_dir}")
    print(f"  Sessions: {session_count} (from snapshot)")
    print(f"  Icon:     {'loaded' if ICON_DATA_URI else 'not found'}")
    print(f"  Server:   http://localhost:{args.port}")
    print()

    server = HTTPServer(("0.0.0.0", args.port), DashboardHandler)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nShutting down.")
        server.shutdown()


if __name__ == "__main__":
    main()
