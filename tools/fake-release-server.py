#!/usr/bin/env python3
# Minimal stand-in for GitHub Releases, for end-to-end testing the companion's
# in-app updater without publishing anything. Point the companion at it with
# NS_UPDATE_BASE_URL=http://127.0.0.1:<port> and it will resolve the update
# check, asset download, and checksums.txt here instead of github.com.
#
# Serves exactly the three paths the updater touches:
#   GET /releases/latest                       -> <root>/releases-latest.json
#   GET /releases/latest/download/<file>       -> <root>/download/<file>
# A plain static file server can't do this (the API path "releases/latest" and
# the directory "releases/latest/download" collide), hence the tiny router.
#
# Usage:  python fake-release-server.py <root-dir> <port>
import http.server
import os
import socketserver
import sys

ROOT = sys.argv[1]
PORT = int(sys.argv[2])
DOWNLOAD_PREFIX = "/releases/latest/download/"


class Handler(http.server.BaseHTTPRequestHandler):
    def log_message(self, *args):
        pass  # quiet

    def do_GET(self):
        path = self.path.split("?", 1)[0]
        if path == "/releases/latest":
            self._send(os.path.join(ROOT, "releases-latest.json"), "application/json")
        elif path.startswith(DOWNLOAD_PREFIX):
            name = path[len(DOWNLOAD_PREFIX):]
            # Guard against path traversal in the asset name.
            if "/" in name or "\\" in name or name in ("", ".", ".."):
                self.send_error(400)
                return
            self._send(os.path.join(ROOT, "download", name), "application/octet-stream")
        else:
            self.send_error(404)

    def _send(self, filepath, ctype):
        if not os.path.isfile(filepath):
            self.send_error(404)
            return
        with open(filepath, "rb") as f:
            data = f.read()
        self.send_response(200)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)


if __name__ == "__main__":
    with socketserver.TCPServer(("127.0.0.1", PORT), Handler) as httpd:
        print(f"fake-release-server: http://127.0.0.1:{PORT} serving {ROOT}", flush=True)
        httpd.serve_forever()
