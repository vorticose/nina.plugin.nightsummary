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

    print(f"\nDone. {total} sessions captured to {out_dir}")


if __name__ == "__main__":
    main()
