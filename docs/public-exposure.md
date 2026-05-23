---
layout: default
title: Public Exposure
nav_order: 14
---

# Exposing the Dashboard Publicly (Safely)

The Local Dashboard is designed for use on your LAN or over a private VPN like Tailscale or ZeroTier. If you want to expose it to the public internet — to view your dashboard from anywhere without a VPN, or to share it with friends — Night Summary provides a **Read-Only Mirror** that runs alongside the main dashboard on a second port and refuses every write action at the server level.

This page covers four ways to put that read-only mirror behind a public HTTPS hostname.

---

## Safety Rules

> **Never point a public-exposure tool at your main dashboard port (default 8181).**
>
> The main dashboard has write surfaces — regenerate reports, edit Project Stats overrides, reset projects. Anyone with the public URL could trigger them.
>
> **Always point your reverse proxy or tunnel at the Read-Only Mirror port (default 8281).** It's the same dashboard UI but rejects every POST/PUT/DELETE with HTTP 403 and hides destructive buttons via CSS.

The read-only mirror is enforced server-side at a single chokepoint, so any new write endpoint added later is auto-blocked without needing to be added to an allowlist.

---

## Enabling the Read-Only Mirror

1. Open **Options > Night Summary Settings** and scroll to the **Local Dashboard** section
2. Make sure **Enable Local Dashboard** is checked (the main dashboard must be running for the mirror to work)
3. Check **Read-Only Mirror**
4. Set the mirror port (default: **8281**) — must be different from your main dashboard port
5. **Restart NINA** — the mirror only binds at plugin startup

After restart, the read-only mirror runs on `http://localhost:8281/` (or whatever port you chose). Visiting it directly shows the same dashboard with a small "Read-Only" pill in the header. Write buttons are gone.

You can verify the mode by checking the `X-Read-Only: true` HTTP response header on any request.

---

## Option 1: Tailscale Funnel

[Tailscale Funnel](https://tailscale.com/kb/1223/funnel) exposes a port on your tailnet device to the public internet over HTTPS. Free tier includes Funnel with soft bandwidth caps.

**Prerequisites:**
- Tailscale installed and logged in on the NINA machine
- HTTPS enabled for your tailnet (Admin Console → DNS → HTTPS Certificates)
- Funnel allowed for the device in your tailnet ACL (`nodeAttrs: [{ target: ["your-machine"], attr: ["funnel"] }]`)

**Enable:**
```bash
tailscale funnel 8281
```

**Public URL:**
```
https://your-machine-name.tailXXXXX.ts.net/
```

**Disable:**
```bash
tailscale funnel off
```

---

## Option 2: Cloudflare Tunnel

[Cloudflare Tunnel](https://developers.cloudflare.com/cloudflare-one/connections/connect-networks/) (`cloudflared`) gives you a public hostname without opening any ports or running a reverse proxy. Free tier covers personal use.

**Setup:**
1. Sign up for Cloudflare and add a domain (free DNS plan is fine)
2. Install `cloudflared` on the NINA machine
3. Run `cloudflared tunnel login` and create a tunnel
4. Configure the tunnel to point at the read-only mirror port:

`%USERPROFILE%\.cloudflared\config.yml`:
```yaml
tunnel: <your-tunnel-id>
credentials-file: C:\Users\You\.cloudflared\<your-tunnel-id>.json

ingress:
  - hostname: nightsummary.example.com
    service: http://localhost:8281
  - service: http_status:404
```

5. Add a CNAME record `nightsummary` → `<your-tunnel-id>.cfargotunnel.com`
6. Run as a service: `cloudflared service install`

Cloudflare handles TLS automatically. Optional: enable Cloudflare Access for an extra auth layer.

---

## Option 3: Caddy Reverse Proxy

[Caddy](https://caddyserver.com/) is a single-binary reverse proxy with automatic Let's Encrypt TLS. You'll need:
- A domain pointed at your home IP (or use a dynamic-DNS service)
- Ports 80 and 443 forwarded to the NINA machine

**Caddyfile:**
```
nightsummary.example.com {
    reverse_proxy localhost:8281
}
```

Run `caddy run` (or install as a Windows service via `caddy-service.exe`). Caddy fetches a TLS cert on first request and auto-renews.

---

## Option 4: nginx Reverse Proxy

Classic option. Same prerequisites as Caddy (domain + open ports).

**nginx site config (`/etc/nginx/sites-available/nightsummary` on Linux, or equivalent on Windows):**
```nginx
server {
    listen 443 ssl;
    server_name nightsummary.example.com;

    ssl_certificate     /etc/letsencrypt/live/nightsummary.example.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/nightsummary.example.com/privkey.pem;

    location / {
        proxy_pass         http://localhost:8281;
        proxy_set_header   Host $host;
        proxy_set_header   X-Real-IP $remote_addr;
    }
}
```

Use `certbot --nginx` for TLS.

---

## Verifying It Works

1. Hit the public URL in a browser → you should see the dashboard with a **Read-Only** pill in the header.
2. Inspect the response headers (browser devtools → Network tab) → look for `X-Read-Only: true`.
3. Try to POST to a mutation endpoint:
   ```bash
   curl -X POST https://nightsummary.example.com/api/regenerate-all
   ```
   You should get `HTTP 403 — Read-only mode — write actions disabled`.

If you see writes succeed, the proxy is pointing at the wrong port — re-check it's pointing at the **read-only mirror** port, not the main dashboard.

---

## Revoking Public Access

| Method | Revoke |
|---|---|
| Tailscale Funnel | `tailscale funnel off` |
| Cloudflare Tunnel | Stop the `cloudflared` service or delete the DNS record |
| Caddy / nginx | Stop the proxy service or remove the site config |
| All | Uncheck **Read-Only Mirror** in Options + restart NINA — the mirror port stops listening entirely |

The fastest universal kill switch is unchecking **Read-Only Mirror** in Options and restarting NINA. The port closes; no public-exposer can reach a dashboard that isn't running.

---

## Troubleshooting

**Port conflict on startup.** If the read-only mirror port is already in use, the plugin logs a warning and skips starting the mirror — the main dashboard keeps running. Pick a different port in Options.

**Tailscale: "Funnel is not allowed for this node."** Add `funnel` to the device's `nodeAttrs` in your tailnet ACL.

**Cloudflare Tunnel: 502 Bad Gateway.** `cloudflared` is running but the read-only mirror isn't. Check NINA is open, Local Dashboard + Read-Only Mirror both enabled, NINA was restarted after enabling.

**Certificate renewal failed (Caddy/nginx).** Standard Let's Encrypt / certbot debugging — Caddy and certbot logs explain. Both auto-renew when working.

**The dashboard loads but write buttons are still visible.** The proxy is pointing at the main dashboard port (8181). Re-point at the mirror port (8281 default).
