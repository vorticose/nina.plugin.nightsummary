#!/usr/bin/env python3
"""Development server for the Night Summary dashboard.

Serves the dashboard HTML/CSS/JS from the source tree (re-read on every request
for instant hot reload) backed by snapshotted API data from the live server.

Usage:
    python server.py                        # defaults: port 8182, data/ directory
    python server.py -p 9000                # custom port
    python server.py -d /path/to/data       # custom data directory
    python server.py -w /path/to/Server/Web # serve web assets from another location
"""

import argparse
import base64
import datetime
import json
import os
import re
import sys
from http.server import HTTPServer, BaseHTTPRequestHandler
from socketserver import ThreadingMixIn

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
        ct = content_type + "; charset=utf-8" if content_type == "application/json" else content_type
        self.send_header("Content-Type", ct)
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
            # Unique timestamp per response — defeats bfcache and any proxy caching
            html = html.replace("</head>", f'<!-- built: {datetime.datetime.utcnow().isoformat()} -->\n</head>', 1)

            body = html.encode("utf-8")
            self.send_response(200)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
            self.send_header("Cache-Control", "no-cache, no-store, must-revalidate")
            self.send_header("Pragma", "no-cache")
            self.send_header("Expires", "0")
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
        # /api/stats/targets — merge TS project data from ts-projects.json before serving
        if path == "/api/stats/targets":
            self.serve_stats_targets_with_ts_merge()
            return

        global_map = {
            "/api/sessions": "sessions.json",
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

        # Phase 3a: TS status override / target link POST endpoints (dev server persists
        # to data/ts-dashboard-meta.json so overrides survive reloads)
        if path == "/api/stats/ts/override" and method == "POST":
            self.handle_ts_override()
            return
        if path == "/api/stats/ts/link" and method == "POST":
            self.handle_ts_link()
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

        # /api/stats/projects/{guid} — project detail for Phase 3c
        if len(parts) == 5 and parts[1] == "api" and parts[2] == "stats" and parts[3] == "projects":
            import urllib.parse
            project_guid = urllib.parse.unquote(parts[4])
            self.serve_project_stats(project_guid)
            return

        # /api/stats/targets/{name}/sessions — built on the fly from snapshot data
        if len(parts) == 6 and parts[1] == "api" and parts[2] == "stats" and parts[3] == "targets" and parts[5] == "sessions":
            import urllib.parse
            target_name = urllib.parse.unquote(parts[4])
            self.serve_target_sessions(target_name)
            return

        # Not found
        self.send_json(404, {"error": f"Not found: {path}"})

    # ── Phase 3a: TS project merge + override/link POST handlers ──

    def _load_ts_meta(self):
        """Load dashboard metadata (status overrides + manual target links) from disk."""
        path = os.path.join(self.data_dir, "ts-dashboard-meta.json")
        try:
            if os.path.isfile(path):
                with open(path, "r", encoding="utf-8") as f:
                    return json.load(f)
        except Exception:
            pass
        return {"statusOverrides": {}, "targetLinks": {}}

    def _save_ts_meta(self, meta):
        path = os.path.join(self.data_dir, "ts-dashboard-meta.json")
        try:
            with open(path, "w", encoding="utf-8") as f:
                json.dump(meta, f, indent=2)
        except Exception as e:
            sys.stderr.write(f"  failed to save ts-dashboard-meta.json: {e}\n")

    def _load_ts_projects(self):
        """Load the mock TS project tree from ts-projects.json. Returns (tsStatus, projects)."""
        path = os.path.join(self.data_dir, "ts-projects.json")
        if not os.path.isfile(path):
            return "not_installed", []
        try:
            with open(path, "r", encoding="utf-8") as f:
                d = json.load(f)
            return d.get("tsStatus", "available"), d.get("projects", [])
        except Exception as e:
            sys.stderr.write(f"  failed to load ts-projects.json: {e}\n")
            return "error", []

    def serve_stats_targets_with_ts_merge(self):
        """Merge TS project data into /api/stats/targets response, mirroring the real server's logic."""
        try:
            targets_path = os.path.join(self.data_dir, "stats-targets.json")
            if not os.path.isfile(targets_path):
                self.send_json(200, {"targets": [], "tsStatus": "not_installed"})
                return
            with open(targets_path, "r", encoding="utf-8") as f:
                targets_data = json.load(f)
            targets = targets_data.get("targets", [])

            ts_status, ts_projects = self._load_ts_projects()
            meta = self._load_ts_meta()
            status_overrides = meta.get("statusOverrides", {}) or {}
            manual_links     = meta.get("targetLinks", {}) or {}

            # Build lookups: lowercase-name → (project, target), guid → (project, target)
            ts_by_name = {}
            ts_by_guid = {}
            for p in ts_projects:
                for t in p.get("targets", []):
                    name = (t.get("name") or "").lower()
                    if name and name not in ts_by_name:
                        ts_by_name[name] = (p, t)
                    g = t.get("guid")
                    if g:
                        ts_by_guid[g] = (p, t)

            enriched = []
            for row in targets:
                target_name = row.get("target") or ""
                ts_match = None
                matched_by = None

                # 1. Manual link wins
                linked = manual_links.get(target_name.lower())
                if linked and linked in ts_by_guid:
                    ts_match = ts_by_guid[linked]
                    matched_by = "manual"
                # 2. Case-insensitive exact-name auto-match
                elif target_name.lower() in ts_by_name:
                    ts_match = ts_by_name[target_name.lower()]
                    matched_by = "name"

                ts_obj = None
                if ts_match:
                    proj, tgt = ts_match
                    plans = tgt.get("exposurePlans", [])
                    total_desired  = sum(int(e.get("desired")  or 0) for e in plans)
                    total_accepted = sum(int(e.get("accepted") or 0) for e in plans)
                    project_percent = None
                    if total_desired > 0:
                        project_percent = round(min(100.0, (total_accepted * 100.0) / total_desired), 1)

                    goals = []
                    for e in plans:
                        desired  = int(e.get("desired") or 0)
                        accepted = int(e.get("accepted") or 0)
                        pct = round(min(100.0, (accepted * 100.0) / desired), 1) if desired > 0 else None
                        goals.append({
                            "filter":       e.get("filter"),
                            "templateName": e.get("templateName"),
                            "exposureSec":  e.get("exposureSec"),
                            "desired":      desired,
                            "acquired":     int(e.get("acquired") or 0),
                            "accepted":     accepted,
                            "percentComplete": pct,
                        })

                    # Effective state: override > inferred Completed (Closed + 100%) > raw
                    raw_state = proj.get("state") or "Draft"
                    override  = status_overrides.get(proj.get("guid") or "")
                    if override:
                        eff_state = override
                        eff_src   = "override"
                    elif raw_state == "Closed" and project_percent is not None and project_percent >= 100.0:
                        eff_state = "Completed"
                        eff_src   = "inferred"
                    else:
                        eff_state = raw_state
                        eff_src   = "raw"

                    ts_obj = {
                        "project": {
                            "id":              proj.get("id"),
                            "guid":            proj.get("guid"),
                            "profileId":       proj.get("profileId"),
                            "name":            proj.get("name"),
                            "description":     proj.get("description"),
                            "rawState":        raw_state,
                            "state":           eff_state,
                            "stateSource":     eff_src,
                            "priority":        proj.get("priority"),
                            "isMosaic":        bool(proj.get("isMosaic")),
                            "createDate":      proj.get("createDate"),
                            "activeDate":      proj.get("activeDate"),
                            "inactiveDate":    proj.get("inactiveDate"),
                            "minimumAltitude": proj.get("minimumAltitude") or 0,
                            "maximumAltitude": proj.get("maximumAltitude") or 0,
                            "targetCount":     len(proj.get("targets", [])),
                            "percentComplete": project_percent,
                        },
                        "target": {
                            "id":       tgt.get("id"),
                            "guid":     tgt.get("guid"),
                            "name":     tgt.get("name"),
                            "active":   bool(tgt.get("active")),
                            "ra":       tgt.get("ra")       or 0,
                            "dec":      tgt.get("dec")      or 0,
                            "rotation": tgt.get("rotation") or 0,
                        },
                        "goals":     goals,
                        "matchedBy": matched_by,
                    }

                enriched_row = dict(row)
                enriched_row["ts"] = ts_obj
                enriched.append(enriched_row)

            # Summary of all TS projects for manual-link picker UI
            ts_projects_summary = None
            if ts_projects:
                ts_projects_summary = [{
                    "guid":        p.get("guid"),
                    "name":        p.get("name"),
                    "state":       p.get("state"),
                    "isMosaic":    bool(p.get("isMosaic")),
                    "targetCount": len(p.get("targets", [])),
                    "targets": [{
                        "guid": t.get("guid"),
                        "name": t.get("name"),
                    } for t in p.get("targets", [])],
                } for p in ts_projects]

            self.send_json(200, {
                "targets":    enriched,
                "tsStatus":   ts_status,
                "tsError":    None,
                "tsProjects": ts_projects_summary,
            })
        except Exception as e:
            self.send_json(500, {"error": f"ts merge failed: {e}"})

    def _read_post_body(self):
        raw = getattr(self, "_post_body", b"") or b""
        try:
            return json.loads(raw.decode("utf-8")) if raw else {}
        except Exception:
            return {}

    def handle_ts_override(self):
        body = self._read_post_body()
        project_guid = body.get("projectGuid")
        status       = body.get("status")
        if not project_guid:
            self.send_json(400, {"error": "projectGuid required"})
            return
        meta = self._load_ts_meta()
        overrides = meta.setdefault("statusOverrides", {})
        if not status:
            overrides.pop(project_guid, None)
        else:
            allowed = {"Draft", "Active", "Inactive", "Closed", "Completed"}
            if status not in allowed:
                self.send_json(400, {"error": "invalid status"})
                return
            overrides[project_guid] = status
        self._save_ts_meta(meta)
        self.send_json(200, {"ok": True, "projectGuid": project_guid, "status": status})

    def handle_ts_link(self):
        body = self._read_post_body()
        session_target_name = body.get("sessionTargetName")
        ts_target_guid      = body.get("tsTargetGuid")
        if not session_target_name:
            self.send_json(400, {"error": "sessionTargetName required"})
            return
        meta = self._load_ts_meta()
        links = meta.setdefault("targetLinks", {})
        key = session_target_name.lower()
        if not ts_target_guid:
            links.pop(key, None)
        else:
            links[key] = ts_target_guid
        self._save_ts_meta(meta)
        self.send_json(200, {"ok": True, "sessionTargetName": session_target_name, "tsTargetGuid": ts_target_guid})

    def serve_project_stats(self, project_guid):
        """Return project detail for Phase 3c panel. Mirrors HandleGetProjectStats in DashboardServer.cs."""
        try:
            ts_status, ts_projects = self._load_ts_projects()
            if ts_status != "available" or not ts_projects:
                self.send_json(404, {"error": "Target Scheduler not available"})
                return

            proj = next((p for p in ts_projects
                         if (p.get("guid") or "").lower() == project_guid.lower()), None)
            if proj is None:
                self.send_json(404, {"error": f"Project '{project_guid}' not found"})
                return

            meta = self._load_ts_meta()
            status_overrides = meta.get("statusOverrides", {}) or {}

            # Effective state
            raw_state = proj.get("state") or "Draft"
            override = status_overrides.get(proj.get("guid") or "")
            total_desired_proj = sum(int(e.get("desired") or 0)
                                     for t in proj.get("targets", [])
                                     for e in t.get("exposurePlans", []))
            total_accepted_proj = sum(int(e.get("accepted") or 0)
                                      for t in proj.get("targets", [])
                                      for e in t.get("exposurePlans", []))
            pct_proj = None
            if total_desired_proj > 0:
                pct_proj = round(min(100.0, (total_accepted_proj * 100.0) / total_desired_proj), 1)

            if override:
                eff_state = override
            elif raw_state == "Closed" and pct_proj is not None and pct_proj >= 100.0:
                eff_state = "Completed"
            else:
                eff_state = raw_state

            # Load sessions list for per-target stats
            sessions_path = os.path.join(self.data_dir, "sessions.json")
            all_sessions = []
            if os.path.isfile(sessions_path):
                with open(sessions_path, "r", encoding="utf-8") as f:
                    all_sessions = json.load(f)
            session_index = {s["sessionId"]: s for s in all_sessions if "sessionId" in s}

            # Load real camera data from session detail.json files.
            # detail.json has cameraInfo: {xSize, ySize, pixelSizeMicrons, focalLengthMm}.
            # We cache per-session to avoid re-reading the same file multiple times.
            _detail_cache = {}
            def _get_cam_from_detail(sid):
                if sid in _detail_cache:
                    return _detail_cache[sid]
                detail_path = os.path.join(self.data_dir, "sessions", sid, "detail.json")
                result = None
                try:
                    if os.path.isfile(detail_path):
                        with open(detail_path, "r", encoding="utf-8") as f:
                            d = json.load(f)
                        ci = d.get("cameraInfo") or {}
                        x = ci.get("xSize") or 0
                        y = ci.get("ySize") or 0
                        ps = ci.get("pixelSizeMicrons") or 0
                        fl = ci.get("focalLengthMm") or 0
                        if x > 0 and y > 0 and ps > 0 and fl > 0:
                            scale = round((ps / fl) * 206.265, 4)
                            result = {
                                "camXSize": x,
                                "camYSize": y,
                                "pixelSizeMicrons": ps,
                                "focalLengthMm": fl,
                                "pixelScaleArcSec": scale,
                                "fovWidthDeg":  round(x * scale / 3600.0, 4),
                                "fovHeightDeg": round(y * scale / 3600.0, 4),
                            }
                except Exception:
                    pass
                _detail_cache[sid] = result
                return result

            panels = []
            agg_frames = 0
            agg_seconds = 0.0
            agg_sessions = 0
            agg_last_imaged = None

            for tgt in proj.get("targets", []):
                tgt_name = tgt.get("name") or ""
                tgt_lower = tgt_name.lower()

                # Find sessions containing this target (ordered newest-first)
                tgt_session_ids = [
                    s["sessionId"] for s in sorted(all_sessions,
                        key=lambda x: x.get("sessionStart") or "", reverse=True)
                    if tgt_lower in [t.lower() for t in (s.get("targets") or [])]
                ]

                total_sec = 0.0
                total_frames = 0
                sess_count = 0
                last_imaged = None
                filters_agg = {}
                best_cam = None  # camera data from the most-recent session with valid info

                for sid in tgt_session_ids:
                    if best_cam is None:
                        best_cam = _get_cam_from_detail(sid)

                    images_path = os.path.join(self.data_dir, "sessions", sid, "images.json")
                    if not os.path.isfile(images_path):
                        continue
                    with open(images_path, "r", encoding="utf-8") as f:
                        imgs = json.load(f)
                    matching = [
                        i for i in imgs
                        if (i.get("targetName") or "").lower() == tgt_lower
                        and (i.get("imageType") in (None, "", "LIGHT"))
                    ]
                    if not matching:
                        continue
                    sess_count += 1
                    accepted = [i for i in matching if i.get("accepted")]
                    total_sec += sum(float(i.get("exposureDuration") or 0) for i in accepted)
                    total_frames += len(accepted)
                    s_start = (session_index.get(sid) or {}).get("sessionStart")
                    if s_start:
                        if last_imaged is None or s_start > last_imaged:
                            last_imaged = s_start

                    for i in matching:
                        if not i.get("accepted"):
                            continue
                        fn = i.get("filter") or "Unknown"
                        fe = filters_agg.setdefault(fn, {"totalSeconds": 0.0, "frames": 0})
                        fe["totalSeconds"] += float(i.get("exposureDuration") or 0)
                        fe["frames"] += 1

                agg_seconds  += total_sec
                agg_frames   += total_frames
                agg_sessions += sess_count
                if last_imaged and (agg_last_imaged is None or last_imaged > agg_last_imaged):
                    agg_last_imaged = last_imaged

                filters_out = sorted([
                    {
                        "filter": fn,
                        "totalHours": round(fe["totalSeconds"] / 3600.0, 2),
                        "acceptedFrames": fe["frames"],
                    }
                    for fn, fe in filters_agg.items()
                ], key=lambda x: -x["totalHours"])

                panel = {
                    "guid":     tgt.get("guid"),
                    "name":     tgt_name,
                    "active":   bool(tgt.get("active")),
                    "ra":       tgt.get("ra") or 0,
                    "dec":      tgt.get("dec") or 0,
                    "rotation": tgt.get("rotation") or 0,
                    "positionAngle": tgt.get("rotation"),  # use TS rotation as stand-in
                    "totalIntegrationHours": round(total_sec / 3600.0, 2),
                    "acceptedFrames": total_frames,
                    "sessionCount": sess_count,
                    "lastImaged": last_imaged,
                    "filters": filters_out,
                }
                if best_cam:
                    panel.update(best_cam)
                panels.append(panel)

            self.send_json(200, {
                "project": {
                    "guid":            proj.get("guid"),
                    "name":            proj.get("name"),
                    "description":     proj.get("description"),
                    "state":           eff_state,
                    "rawState":        raw_state,
                    "isMosaic":        bool(proj.get("isMosaic")),
                    "priority":        proj.get("priority"),
                    "createDate":      proj.get("createDate"),
                    "activeDate":      proj.get("activeDate"),
                    "inactiveDate":    proj.get("inactiveDate"),
                    "minimumAltitude": proj.get("minimumAltitude") or 0,
                    "maximumAltitude": proj.get("maximumAltitude") or 0,
                    "percentComplete": pct_proj,
                },
                "panels": panels,
                "aggregate": {
                    "totalIntegrationHours": round(agg_seconds / 3600.0, 2),
                    "acceptedFrames":        agg_frames,
                    "sessionCount":          agg_sessions,
                    "lastImaged":            agg_last_imaged,
                },
            })
        except Exception as e:
            self.send_json(500, {"error": f"project stats: {e}"})

    def serve_target_sessions(self, target_name):
        """Build per-session detail for a target by aggregating the snapshot image files."""
        try:
            sessions_path = os.path.join(self.data_dir, "sessions.json")
            if not os.path.isfile(sessions_path):
                self.send_json(200, {"target": target_name, "sessions": []})
                return
            with open(sessions_path, "r", encoding="utf-8") as f:
                all_sessions = json.load(f)

            target_lower = target_name.lower()
            result_sessions = []

            for s in all_sessions:
                sid = s.get("sessionId")
                if not sid:
                    continue
                # Only sessions that imaged this target
                targets = [t.lower() for t in (s.get("targets") or [])]
                if target_lower not in targets:
                    continue

                images_path = os.path.join(self.data_dir, "sessions", sid, "images.json")
                if not os.path.isfile(images_path):
                    continue
                with open(images_path, "r", encoding="utf-8") as f:
                    imgs = json.load(f)

                # Only LIGHT frames for this target
                matching = [
                    i for i in imgs
                    if (i.get("targetName") or "").lower() == target_lower
                    and (i.get("imageType") in (None, "", "LIGHT"))
                ]
                if not matching:
                    continue

                # Session-level aggregates
                accepted = [i for i in matching if i.get("accepted")]
                integ_sec = sum(float(i.get("exposureDuration") or 0) for i in accepted)
                hfrs = [i["hfr"] for i in accepted if i.get("hfr") and i["hfr"] > 0]
                guides = [i["guidingRmsTotal"] for i in accepted if i.get("guidingRmsTotal") and i["guidingRmsTotal"] > 0]
                avg_hfr = sum(hfrs) / len(hfrs) if hfrs else None
                avg_guide = sum(guides) / len(guides) if guides else None

                # Per-filter breakdown
                by_filter = {}
                for i in matching:
                    f_name = i.get("filter") or "Unknown"
                    fe = by_filter.setdefault(f_name, {
                        "filter": f_name, "integrationSeconds": 0.0,
                        "frames": 0, "totalFrames": 0,
                        "_hfrs": [], "_guides": []
                    })
                    fe["totalFrames"] += 1
                    if i.get("accepted"):
                        fe["frames"] += 1
                        fe["integrationSeconds"] += float(i.get("exposureDuration") or 0)
                        if i.get("hfr") and i["hfr"] > 0:
                            fe["_hfrs"].append(i["hfr"])
                        if i.get("guidingRmsTotal") and i["guidingRmsTotal"] > 0:
                            fe["_guides"].append(i["guidingRmsTotal"])

                filters_out = []
                for f_name, fe in by_filter.items():
                    fh = sum(fe["_hfrs"]) / len(fe["_hfrs"]) if fe["_hfrs"] else None
                    fg = sum(fe["_guides"]) / len(fe["_guides"]) if fe["_guides"] else None
                    filters_out.append({
                        "filter": f_name,
                        "integrationSeconds": fe["integrationSeconds"],
                        "integrationHours": round(fe["integrationSeconds"] / 3600.0, 2),
                        "frames": fe["frames"],
                        "totalFrames": fe["totalFrames"],
                        "avgHFR": round(fh, 2) if fh else None,
                        "avgGuidingRMS": round(fg, 2) if fg else None,
                    })
                filters_out.sort(key=lambda x: -x["integrationSeconds"])

                # Session duration in minutes (from session start/end strings)
                start_str = s.get("sessionStart")
                end_str = s.get("sessionEnd")
                dur_min = 0
                try:
                    if start_str and end_str:
                        d1 = datetime.datetime.fromisoformat(start_str)
                        d2 = datetime.datetime.fromisoformat(end_str)
                        dur_min = int(round((d2 - d1).total_seconds() / 60.0))
                except Exception:
                    pass

                result_sessions.append({
                    "sessionId": sid,
                    "sessionStart": start_str,
                    "sessionEnd": end_str,
                    "durationMinutes": dur_min,
                    "integrationHours": round(integ_sec / 3600.0, 2),
                    "integrationSeconds": integ_sec,
                    "frames": len(accepted),
                    "totalFrames": len(matching),
                    "avgHFR": round(avg_hfr, 2) if avg_hfr else None,
                    "avgGuidingRMS": round(avg_guide, 2) if avg_guide else None,
                    "moonPhase": s.get("moonPhase"),
                    "filters": filters_out,
                })

            # Sort newest-first
            result_sessions.sort(key=lambda x: x["sessionStart"] or "", reverse=True)

            # Overall aggregates
            total_sec = sum(x["integrationSeconds"] for x in result_sessions)
            total_frames = sum(x["frames"] for x in result_sessions)
            all_hfrs = [x["avgHFR"] for x in result_sessions if x["avgHFR"]]
            all_guides = [x["avgGuidingRMS"] for x in result_sessions if x["avgGuidingRMS"]]
            first_sess = min((x["sessionStart"] for x in result_sessions if x["sessionStart"]), default=None)
            last_sess = max((x["sessionStart"] for x in result_sessions if x["sessionStart"]), default=None)

            self.send_json(200, {
                "target": target_name,
                "totalIntegrationHours": round(total_sec / 3600.0, 2),
                "totalFrames": total_frames,
                "sessionCount": len(result_sessions),
                "firstSession": first_sess,
                "lastSession": last_sess,
                "avgHFR": round(sum(all_hfrs) / len(all_hfrs), 2) if all_hfrs else None,
                "avgGuidingRMS": round(sum(all_guides) / len(all_guides), 2) if all_guides else None,
                "sessions": result_sessions,
            })
        except Exception as e:
            self.send_json(500, {"error": f"target sessions: {e}"})

    def do_GET(self):
        self.route("GET")

    def do_HEAD(self):
        self.route("HEAD")

    def do_POST(self):
        # Read body once and stash on self so handlers can access it via _post_body
        length = int(self.headers.get("Content-Length", 0))
        self._post_body = self.rfile.read(length) if length > 0 else b""
        self.route("POST")


def main():
    parser = argparse.ArgumentParser(description="Night Summary Dashboard Dev Server")
    parser.add_argument("-p", "--port", type=int, default=8182,
                        help="Listen port (default: 8182)")
    parser.add_argument("-d", "--data", default=None,
                        help="Data directory (default: data/ next to this script)")
    parser.add_argument("-w", "--webdir", default=None,
                        help="Web assets directory containing dashboard.html/css/js "
                             "(default: auto-detected from repo tree)")
    args = parser.parse_args()

    global WEB_DIR
    if args.webdir:
        WEB_DIR = os.path.normpath(os.path.abspath(args.webdir))

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

    class ThreadedServer(ThreadingMixIn, HTTPServer):
        daemon_threads = True

    server = ThreadedServer(("0.0.0.0", args.port), DashboardHandler)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nShutting down.")
        server.shutdown()


if __name__ == "__main__":
    main()
