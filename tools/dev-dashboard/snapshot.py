#!/usr/bin/env python3
"""Snapshot all API responses from the live Night Summary dashboard server."""

import argparse
import json
import os
import sys
import urllib.request
import urllib.error

GLOBAL_ENDPOINTS = [
    ("/api/sessions", "sessions.json"),
    ("/api/stats/targets", "stats-targets.json"),
    ("/api/stats/summary", "stats-summary.json"),
    ("/api/filters", "filters.json"),
    ("/api/settings", "settings.json"),
]

SESSION_ENDPOINTS = [
    ("", "detail.json"),
    ("/thumbnails", "thumbnails.json"),
    ("/livestack", "livestack.json"),
    ("/altitude-chart", "altitude-chart.json"),
    ("/images", "images.json"),
    ("/events", "events.json"),
    ("/timing", "timing.json"),
    ("/settings", "settings.json"),
]


def fetch(url, timeout=30):
    """Fetch a URL and return (content_bytes, content_type)."""
    req = urllib.request.Request(url)
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return resp.read(), resp.headers.get("Content-Type", "")


def fetch_json(url, timeout=30):
    """Fetch a URL and parse as JSON."""
    data, _ = fetch(url, timeout)
    return json.loads(data)


def save_bytes(path, data):
    """Write bytes to a file, creating parent directories as needed."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(data)


def save_json(path, obj):
    """Write a JSON object to a file, creating parent directories as needed."""
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8") as f:
        json.dump(obj, f, indent=2, ensure_ascii=False)


def snapshot_global(base_url, out_dir):
    """Fetch and save all global (non-session-specific) endpoints."""
    print("Fetching global endpoints...")
    for path, filename in GLOBAL_ENDPOINTS:
        url = base_url + path
        try:
            data = fetch_json(url)
            save_json(os.path.join(out_dir, filename), data)
            print(f"  {filename}")
        except Exception as e:
            print(f"  {filename} - FAILED: {e}")


def snapshot_session(base_url, out_dir, session_id, index, total):
    """Fetch and save all endpoints for a single session."""
    prefix = f"  [{index}/{total}] {session_id[:12]}..."
    session_dir = os.path.join(out_dir, "sessions", session_id)
    os.makedirs(session_dir, exist_ok=True)

    for suffix, filename in SESSION_ENDPOINTS:
        url = f"{base_url}/api/sessions/{session_id}{suffix}"
        try:
            data = fetch_json(url)
            save_json(os.path.join(session_dir, filename), data)
        except Exception as e:
            print(f"{prefix} {filename} - FAILED: {e}")
            continue

    # Fetch report HTML separately (not JSON)
    try:
        html_bytes, _ = fetch(f"{base_url}/api/sessions/{session_id}/report")
        save_bytes(os.path.join(session_dir, "report.html"), html_bytes)
    except Exception as e:
        print(f"{prefix} report.html - FAILED: {e}")

    # Download livestack JPEG files referenced by URL (not data: URIs)
    livestack_path = os.path.join(session_dir, "livestack.json")
    if os.path.exists(livestack_path):
        try:
            with open(livestack_path, "r", encoding="utf-8") as f:
                livestack_data = json.load(f)
            jpg_count = 0
            for target_name, entries in livestack_data.items():
                for entry in entries:
                    url_val = entry.get("url", "")
                    if url_val.startswith("/api/"):
                        filename = url_val.split("/")[-1]
                        img_dir = os.path.join(session_dir, "livestack")
                        os.makedirs(img_dir, exist_ok=True)
                        try:
                            img_bytes, _ = fetch(base_url + url_val)
                            save_bytes(os.path.join(img_dir, filename), img_bytes)
                            jpg_count += 1
                        except Exception as e:
                            print(f"{prefix} livestack/{filename} - FAILED: {e}")
            if jpg_count > 0:
                print(f"{prefix} OK (+{jpg_count} livestack images)")
            else:
                print(f"{prefix} OK")
        except Exception:
            print(f"{prefix} OK")
    else:
        print(f"{prefix} OK")


def enrich_target_stats(out_dir, sessions):
    """Aggregate per-target stats from downloaded session image data.

    Rewrites stats-targets.json with enriched fields (filter breakdown,
    quality metrics, coordinates, session count, etc.) matching the format
    returned by the enriched /api/stats/targets endpoint.
    """
    from collections import defaultdict

    targets = defaultdict(lambda: {
        "sessions": set(), "totalSec": 0, "frames": 0, "accepted": 0,
        "hfrs": [], "fwhms": [], "guiding": [],
        "filters": defaultdict(lambda: {"sec": 0, "frames": 0, "accepted": 0}),
        "ra": 0, "dec": 0, "lastDate": "", "lastSid": "",
    })

    for session in sessions:
        sid = session.get("sessionId", "")
        if not sid:
            continue
        img_path = os.path.join(out_dir, "sessions", sid, "images.json")
        detail_path = os.path.join(out_dir, "sessions", sid, "detail.json")
        if not os.path.exists(img_path):
            continue

        with open(img_path, "r", encoding="utf-8") as f:
            images = json.load(f)

        session_start = ""
        if os.path.exists(detail_path):
            with open(detail_path, "r", encoding="utf-8") as f:
                detail = json.load(f)
                session_start = detail.get("sessionStart", "")

        for img in images:
            name = img.get("targetName", "")
            itype = img.get("imageType", "")
            if not name or (itype and itype != "LIGHT"):
                continue

            t = targets[name]
            t["sessions"].add(sid)
            accepted = img.get("accepted", True)
            dur = img.get("exposureDuration", 0)
            t["frames"] += 1

            if accepted:
                t["accepted"] += 1
                t["totalSec"] += dur
                hfr = img.get("hfr", 0)
                fwhm = img.get("fwhm", 0)
                guide = img.get("guidingRMSTotal", 0)
                if hfr and hfr > 0:
                    t["hfrs"].append(hfr)
                if fwhm and fwhm > 0:
                    t["fwhms"].append(fwhm)
                if guide and guide > 0:
                    t["guiding"].append(guide)

            filt = img.get("filter", "") or "Unknown"
            t["filters"][filt]["frames"] += 1
            if accepted:
                t["filters"][filt]["sec"] += dur
                t["filters"][filt]["accepted"] += 1

            ra = img.get("raHours", 0)
            dec = img.get("decDegrees", 0)
            if ra and not t["ra"]:
                t["ra"] = round(ra, 4)
                t["dec"] = round(dec, 4)

            if session_start > t["lastDate"]:
                t["lastDate"] = session_start
                t["lastSid"] = sid

    result = []
    for name, t in targets.items():
        filters = []
        for fn, fd in sorted(t["filters"].items(), key=lambda x: -x[1]["sec"]):
            if fd["sec"] < 1:
                continue  # skip zero-integration filters (all frames rejected)
            filters.append({
                "filter": fn,
                "totalSeconds": fd["sec"],
                "totalHours": round(fd["sec"] / 3600, 2),
                "frameCount": fd["frames"],
                "acceptedCount": fd["accepted"],
            })
        result.append({
            "target": name,
            "totalIntegrationSeconds": t["totalSec"],
            "totalIntegrationHours": round(t["totalSec"] / 3600, 2),
            "sessionCount": len(t["sessions"]),
            "lastImaged": t["lastDate"],
            "latestSessionId": t["lastSid"],
            "totalFrames": t["frames"],
            "acceptedFrames": t["accepted"],
            "avgHFR": round(sum(t["hfrs"]) / len(t["hfrs"]), 2) if t["hfrs"] else None,
            "avgFWHM": round(sum(t["fwhms"]) / len(t["fwhms"]), 2) if t["fwhms"] else None,
            "avgGuidingRMS": round(sum(t["guiding"]) / len(t["guiding"]), 2) if t["guiding"] else None,
            "raHours": t["ra"] if t["ra"] else None,
            "decDegrees": t["dec"] if t["dec"] else None,
            "filters": filters,
        })

    result.sort(key=lambda x: -x["totalIntegrationSeconds"])
    save_json(os.path.join(out_dir, "stats-targets.json"), {"targets": result})
    print(f"\nEnriched target stats: {len(result)} targets from image data")


def main():
    parser = argparse.ArgumentParser(description="Snapshot Night Summary dashboard API data")
    parser.add_argument("--url", default="http://100.86.208.29:8181",
                        help="Base URL of the live dashboard (default: http://100.86.208.29:8181)")
    parser.add_argument("-o", "--output", default=None,
                        help="Output directory (default: data/ next to this script)")
    args = parser.parse_args()

    base_url = args.url.rstrip("/")
    if args.output:
        out_dir = args.output
    else:
        out_dir = os.path.join(os.path.dirname(os.path.abspath(__file__)), "data")

    print(f"Night Summary Dashboard Snapshot")
    print(f"  Source: {base_url}")
    print(f"  Output: {out_dir}")
    print()

    # Verify server is reachable
    try:
        fetch_json(base_url + "/api/health")
    except Exception as e:
        print(f"ERROR: Cannot reach dashboard at {base_url}")
        print(f"  {e}")
        print(f"  Make sure NINA is running with the dashboard server enabled.")
        sys.exit(1)

    os.makedirs(out_dir, exist_ok=True)

    # Global endpoints
    snapshot_global(base_url, out_dir)

    # Load session list and iterate
    sessions_path = os.path.join(out_dir, "sessions.json")
    if not os.path.exists(sessions_path):
        print("ERROR: sessions.json not saved, cannot continue with per-session snapshots.")
        sys.exit(1)

    with open(sessions_path, "r", encoding="utf-8") as f:
        sessions = json.load(f)

    total = len(sessions)
    print(f"\nFetching {total} sessions...")
    for i, session in enumerate(sessions, 1):
        sid = session.get("sessionId", "")
        if not sid:
            continue
        snapshot_session(base_url, out_dir, sid, i, total)

    # Post-process: enrich target stats from downloaded image data
    enrich_target_stats(out_dir, sessions)

    print(f"\nDone. {total} sessions captured to {out_dir}")


if __name__ == "__main__":
    main()
