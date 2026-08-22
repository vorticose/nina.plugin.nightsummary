# Touch 'N' Stars integration: Night Summary compat API

Status: draft, implemented on `feature/tns-integration` (NS side only).

Night Summary exposes a small, stable endpoint namespace intended for the
Touch 'N' Stars (TNS) web app, so TNS can offer the report as a delivery
channel: pick a session, view the actual Night Summary report, resend or
delete it. The design goal is zero UI drift: TNS renders the report HTML that
NS already generates (a single self-contained file with thumbnails and charts
embedded), rather than re-implementing report content natively.

There are two ways to reach the same data, pick whichever fits:
- **In-process facade** (`NightSummaryApi`, below) for another NINA plugin
  running in the same process (this is how TNS works today). No HTTP, no port,
  no server toggle.
- **HTTP endpoints** (`/api/nightsummary/*`, below) served by the local
  dashboard server, for out-of-process or networked consumers.

## In-process facade: `NightSummaryApi`

For a plugin running inside NINA, bind to the public class
`NINA.Plugin.NightSummary.Integration.NightSummaryApi` instead of reflecting
into internal types (`SessionDatabase`, `SettingsManager`), whose names change
between releases. The facade's type name and method signatures are a frozen
contract; internals are not.

- All methods are `public static` and return a **JSON string** using the same
  `{ Success, Response, Error }` envelope as the HTTP side (parse the string,
  no type coupling).
- `string ApiVersion()` -> `"1.0"` (bump signals added methods).
- `string Status()` -> Installed, Version, ApiVersion, SessionCount.
- `string Sessions(int limit)` -> recent sessions (SessionRecord shape).
- `string Session(string sessionId)` -> `{ Session, Images, Events,
  TimingEvents, SessionHistory }` (compute display stats from Images as you do
  now).
- `string ReportHtml(string sessionId)` / `string ReportPath(string sessionId)`
  -> the self-contained report HTML / its path (from the always-written reports
  dir, not the user-configurable save path).
- `string Resend(string sessionId)` -> re-fire configured delivery channels.
- `string DeleteSession(string sessionId)` -> **cleanup-aware**: removes the DB
  rows AND the report HTML, settings sidecar, livestack masters, and thumbnails
  (raw `SessionDatabase.DeleteSession` orphans those).
- `string GetSettings()` -> settings with the 5 secret fields masked (each
  removed, a `<field>Set` bool added) plus `_filterNames`; never returns
  credential values. Secret set: `SmtpPassword`, `DiscordWebhookUrl`,
  `PushoverAppToken`, `PushoverUserKey`, `DashboardApiKey`.
- `string UpdateSettings(string patchJson)` -> applies a patch with write-only
  secret semantics (blank/absent secret keeps current, non-blank replaces), then
  saves through `SettingsManager` (so the value is encrypted at rest).

Before the plugin finishes initializing (or after teardown), methods return a
`{ Success:false, Error:"Night Summary plugin not loaded" }` envelope rather
than throwing.

## Transport and discovery

- Endpoints are served by the Night Summary local dashboard server
  (`Local Server` in NS options; default port 8181, off by default).
- NS announces the port over NINA's inter-plugin message broker:
  - publishes topic `NightSummary.Port` (content: port as string) when the
    local server starts, and `"0"` when it stops: a `0` means "NS installed
    but the local server is disabled", so TNS can show an enable-the-server
    hint instead of a generic failure;
  - answers topic `NightSummary.RequestPort` by re-publishing
    `NightSummary.Port`.
  This mirrors the `AdvancedAPI.Port` / `AdvancedAPI.RequestPort` pattern the
  TNS NINA plugin already consumes, so the TNS plugin can proxy
  `/api/nightsummary/*` from its own port without user configuration and
  without a new firewall rule on the phone-facing side.
- The read-only mirror port (if enabled) serves the same GET endpoints but
  refuses resend/delete (HTTP 403), like every other non-GET route on the
  mirror.

## Envelope

All JSON endpoints wrap their payload in the envelope TNS uses elsewhere:

```json
{ "Success": true,  "Response": { ... }, "Error": "", "StatusCode": 200, "Type": "..." }
{ "Success": false, "Response": null,   "Error": "message", "StatusCode": 404, "Type": "..." }
```

Property names are PascalCase. `GET /api/nightsummary/report/{id}` is the one
exception: it returns raw `text/html` (the report itself), not JSON.

## Endpoints

### GET /api/nightsummary/status

Availability probe for the TNS frontend gate.

```json
{
  "Installed": true,
  "Version": "3.3.0",
  "ReadOnly": false,
  "CanResendAndDelete": true,
  "SessionCount": 123
}
```

### GET /api/nightsummary/sessions?limit=N

Completed sessions, newest first (default limit 100). Each entry:

```json
{
  "SessionId": "guid",
  "SessionDate": "2026-07-18T20:31:00.000-07:00",
  "SessionStart": "...",
  "SessionEnd": "...",
  "DisplayLabel": "Jul 18 · Seagull Nebula · 142 img · 4.2h",
  "ProfileName": "...",
  "Targets": ["Seagull Nebula"],
  "ImageCount": 142,
  "TotalExposureSeconds": 15120.0,
  "HasReport": true
}
```

`DisplayLabel` is pre-built server-side so the TNS session picker needs no
formatting logic. `HasReport` tells the picker whether `report/{id}` will
succeed (sessions can predate report generation or have it disabled).

### GET /api/nightsummary/report/{id}

The session's report as `text/html`. This is the exact file NS generated for
the session (same artifact delivered by email/Discord/dashboard) and is fully
self-contained: inline CSS, SVG charts, base64 thumbnails. Intended to be
displayed in an iframe/webview inside the TNS page. 404 (JSON envelope) when
the session or its report does not exist.

### POST /api/nightsummary/sessions/{id}/resend

Re-fires the configured delivery channels (email / Discord / Pushover) for a
historical session. Response reports per-channel results. 403 on the
read-only mirror.

### DELETE /api/nightsummary/sessions/{id}

Deletes the session (same behavior as the dashboard's own delete). 403 on the
read-only mirror.

## Notes for the TNS side

- Session ids are GUIDs; anything containing path separators or `..` is
  rejected with 400 before reaching handlers.
- Settings read/write endpoints are deliberately not part of this namespace
  yet; whether TNS should edit NS settings (and which subset) is an open
  product question.
- CORS: the NS server sends `Access-Control-Allow-Origin: *` on API routes,
  so a direct-from-frontend variant (no TNS-plugin proxy) also works if TNS
  ever prefers it; discovery is the only extra problem to solve in that mode.
