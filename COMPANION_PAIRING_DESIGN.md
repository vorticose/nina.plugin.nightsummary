# Companion Pairing — Design

Replaces today's "copy `nina.apiKey` from plugin settings into `companion.json`" cliff with a browser-driven setup wizard backed by a short, revocable pairing token. Sub-document of `COMPANION_PLAN.md`.

---

## Goals

- First-time setup completed entirely in the browser, no JSON edits.
- Pairing token is short (~16 chars) and typeable from a phone or sticky note.
- Tokens are revocable per-companion; multiple companions can pair against one primary independently.
- Wizard surfaces specific failure modes (wrong host, NS not installed, NS too old, token bad, token in use) — never a generic "connection failed."
- Existing `nina.apiKey` field deprecated cleanly; old `companion.json` files keep working until the user re-pairs.

## Non-goals

- mDNS / Tailscale auto-discovery (user types host).
- QR code for token entry (user is at a keyboard during setup).
- Auto-update of companion binary (Phase C, skipped — see `project_companion_distribution`).
- Browser-side companion install (companion is a desktop CLI; phone is viewer only).

---

## Token

**Format:** 16 chars from base32 alphabet `ABCDEFGHJKLMNPQRSTUVWXYZ23456789` (no `0/O/1/I/L`), grouped 4-4-4-4 with hyphens for display: `K4M2-9N3X-7QR5-8VH2`. ~80 bits entropy. Input field accepts unhyphenated, lowercase, whitespace — normalized on submit.

**Lifecycle:**

1. **Generated** in NS Options ("Companion Pairing → Generate Token"). Plain token shown ONCE with a copy button. Primary stores only `SHA-256(token)` plus metadata.
2. **Unpaired** until a companion calls `POST /api/companion/pair` with the matching token. Unpaired tokens visible in NS Options but flagged as "not yet claimed."
3. **Paired** after first successful claim — `companionName` and `pairedAt` recorded.
4. **Revoked** via NS Options "Revoke" button. Soft-delete (entry kept for audit, `revokedAt` set). Next companion sync attempt 401s → wizard reopens.

**Why not JWT/signed token:** primary already controls both sides of the trust boundary; signing adds nothing over "hash it and check on every request." JWTs only earn their cost when one side can't reach the other to verify.

---

## Storage on primary

New sidecar file: `%LOCALAPPDATA%\NINA\NightSummary\companion_tokens.json`. Outside the main SQLite intentionally — so it doesn't sync to the companion (companion has no business knowing other companion tokens), doesn't roll into backups by default, and survives DB migrations.

```json
{
  "version": 1,
  "tokens": [
    {
      "id": "1ab8c2",
      "name": null,
      "hash": "9f7e2c…",
      "createdAt": "2026-05-22T18:00:00Z",
      "pairedAt": "2026-05-22T18:00:42Z",
      "lastUsedAt": "2026-05-22T19:13:08Z",
      "companionName": "Mac mini",
      "revokedAt": null
    }
  ]
}
```

- `id`: short random handle for revocation URLs / UI; not secret.
- `name`: optional user-set label entered at generation time (default null — companion fills it via `companionName` on first pair).
- `hash`: SHA-256 of the plain token (hex).
- File written atomically: write to `.tmp`, fsync, rename.
- Locked behind same instance lock NS uses for `settings.json` to avoid torn writes when two NS instances race.

---

## Primary endpoints

All three new. Live in `DashboardServer.cs` next to existing companion routes.

### `GET /api/companion/info`

Unauthenticated. Lets the wizard's "Test Connection" step distinguish "wrong host" from "wrong software."

**Response 200:**
```json
{
  "ninaVersion": "3.2.0.9001",
  "nsVersion": "3.1.1",
  "hasNs": true,
  "minCompanionVersion": "0.5.0",
  "pairedCount": 1
}
```

`minCompanionVersion` future-proofs against breaking sync-protocol changes — companion compares against its own version and refuses to pair if too old, prompting user to upgrade. Defaults to `"0.0.0"` until needed.

### `POST /api/companion/pair`

Body:
```json
{ "token": "K4M29N3X7QR58VH2", "companionName": "Mac mini" }
```

Logic:
1. Normalize token (strip whitespace/hyphens, upcase).
2. Hash and look up in `companion_tokens.json`.
3. If not found → 401 `{ error: "unknown_token" }`.
4. If revoked → 401 `{ error: "revoked" }`.
5. If already paired with a *different* `companionName` and `pairedAt` is recent (< 7 days) → 409 `{ error: "already_paired", companionName: "<other>" }`. Wizard offers "revoke that pairing and continue" or "use a new token."
6. If already paired but stale (> 7 days since `lastUsedAt`) → silently re-bind to new `companionName`. Covers the "I rebuilt the Mac mini" case.
7. Else → set `pairedAt`, `companionName`, `lastUsedAt`. Return:

```json
{ "companionId": "1ab8c2", "ninaVersion": "3.2.0.9001", "nsVersion": "3.1.1" }
```

### `POST /api/companion/revoke`

Body `{ "id": "1ab8c2" }`. Auth: requires either (a) a current valid bearer token (companion revoking itself) or (b) being called from the NS Options panel via in-process IPC (no token needed). Sets `revokedAt`. Returns 204.

### Bearer auth on existing sync endpoints

All `/api/companion/sync/*` and `/api/companion/push/*` routes accept `Authorization: Bearer <token>`. Logic:
- Hash incoming token, look up, reject if missing/revoked.
- Update `lastUsedAt`.
- For a transition window (one release), also accept the legacy `apiKey` header → log a one-time warning → unblock the request. Removed in the release after.

---

## Companion wizard

Companion serves `/setup` page when `CompanionConfig.IsComplete() == false`. Main dashboard redirects to `/setup` until setup completes. Once complete, `/setup` redirects to `/`.

State machine (steps numbered; `←` and `→` denote back/next):

```
1 Welcome ──→ 2 Connect ──→ 3 Pair ──→ 4 Sync settings ──→ 5 First sync ──→ Dashboard
              ←──            ←──        ←──                  (no back)
```

### Step 1 — Welcome

- Heading: "Set up Night Summary Companion"
- Body: "We'll connect to your NINA machine, pair securely, and pull your data. Takes about 30 seconds."
- Single button: **Get Started** → step 2.
- No back button.

### Step 2 — Connect to NINA

- Field: **Host or IP** — placeholder `"100.x.x.x (Tailscale) or rig.local"`.
- Field: **Dashboard port** — default `8181`.
- Inline hint: "This is the NINA machine's address. If you use Tailscale, the IP shown in the Tailscale app works."
- Button: **Test Connection** → `GET <host>:<port>/api/companion/info`
  - 200 + `hasNs: true` + version OK → green ✓ with `nsVersion`, **Next** button enabled.
  - 200 + `hasNs: false` → ✗ "Reached the server but Night Summary isn't installed. Install the plugin in NINA first."
  - 200 + `nsVersion < minCompanionVersion (this side)` → ✗ "Night Summary on the NINA machine is too old. Update the plugin to at least vX.Y.Z."
  - 200 + companion is older than primary's `minCompanionVersion` → ✗ "This companion is too old for that NINA. Update the companion to at least vA.B.C."
  - Connection refused → ✗ "Can't reach NINA. Check the host/port and that NINA is running."
  - Timeout → ✗ "NINA didn't respond. If both machines are on Tailscale, make sure they're online."
  - Cert/TLS errors → ✗ specific message (deferred — HTTP only at first).
- **Next** advances; host/port persisted to in-memory wizard state.

### Step 3 — Pair

- Heading: "Pair with NINA"
- Body (with inline screenshot once docs land):
  > 1. In NINA, open the Night Summary plugin settings.
  > 2. Scroll to **Companion Pairing**.
  > 3. Click **Generate Token**.
  > 4. Paste the token below.
- Field: **Token** — autoformats hyphens as user types (1234 → 1234- after 4 chars), trims/normalizes on submit.
- Field: **Companion name** — defaults to `Environment.MachineName`. Shown in NS Options after pairing.
- Button: **Pair** → `POST /api/companion/pair`
  - 200 → token saved to `companion.json` (under new `nina.pairingToken` field, not `apiKey`), advance.
  - 401 `unknown_token` → "That token isn't recognized. Generate a fresh one in NINA and try again." Token field cleared.
  - 401 `revoked` → "That token was revoked. Generate a fresh one."
  - 409 `already_paired` → "That token is already paired with '<other companion>'. Revoke that pairing in NINA first, or generate a new token." Buttons: **I revoked it, retry** / **Use a new token**.
  - Network error → "Lost connection during pairing. Check NINA is still running." (Back button stays available.)
- **Back** to step 2 always available.

### Step 4 — Sync settings

- Field: **Sync schedule** — radio:
  - "Hourly while computer is on" (default)
  - "Every 4 hours"
  - "Once daily"
  - "Manual only"
- Checkbox: **Sync immediately when companion starts** (default on).
- Field: **Dashboard port** — companion's local port, default `8182`. Inline note if port collides with something well-known.
- Button: **Save & Run First Sync** → writes `companion.json`, advances.
- **Back** to step 3.

### Step 5 — First sync

- Live progress bar reusing existing sync-status pipeline (already wired in `dashboard.js` L9304+).
- Phases shown: "Connecting → Downloading database → Downloading reports → Downloading thumbnails → Done."
- Success → "Setup complete." → 2s delay → redirect to main dashboard.
- Failure → "Sync failed: <specific error>." Buttons: **Retry** / **Back to settings**. Token already saved, so retry is one click.
- **No back** to step 4 once sync started — too late to be safe (config already written).

### Cross-cutting

- Top of every step: progress dots `● ● ○ ○ ○` (1–5).
- Browser refresh mid-wizard: in-memory state is per-session on the companion server (1-hour TTL). Refresh during step 2–4 returns to step 1 with fields blank. Refresh during step 5 picks up sync status (already persistent).
- Mobile-friendly: single column, max-width 480px, large tap targets. User might run companion on a headless Mac mini and complete setup from phone on tailnet.
- No client-side framework — vanilla JS in the existing `dashboard.js`. New file `wizard.js` lazily loaded for `/setup` only to keep the main bundle clean.

---

## NS Options panel (primary)

New section in `Options.xaml` between existing sections (suggest: after "Dashboard Server", before "Email"). Mock:

```
┌─ Companion Pairing ──────────────────────────────────────┐
│                                                          │
│  [ + Generate Token ]                                    │
│                                                          │
│  Paired companions:                                      │
│   • Mac mini       paired 2 days ago    [ Revoke ]      │
│   • Office laptop  paired just now      [ Revoke ]      │
│                                                          │
│  Unpaired tokens:                                        │
│   • —                                                    │
│                                                          │
└──────────────────────────────────────────────────────────┘
```

After Generate:

```
┌─ New Companion Token ────────────────────────────────────┐
│                                                          │
│  K4M2-9N3X-7QR5-8VH2          [ 📋 Copy ]               │
│                                                          │
│  Copy this token now — it won't be shown again.         │
│  Paste it into the companion's setup wizard.            │
│                                                          │
│  [ Done ]                                                │
└──────────────────────────────────────────────────────────┘
```

Implementation: WPF code-behind generates token via `RandomNumberGenerator`, calls a new `CompanionTokenStore.Add(plainToken)` (which hashes + persists), shows plain token in a modal-style sub-section. Plain token never re-read from storage.

---

## Migration from `nina.apiKey`

1. **Today (pre-wizard release):** `companion.json` schema has `nina.apiKey`. Push-to-companion uses it as a shared secret. Verify with grep (`grep -rn apiKey` across plugin + companion) before deprecating; if grep shows uses outside companion-pairing, design needs a wider story.
2. **Wizard release:**
   - `CompanionConfig` gains `nina.pairingToken` field, retains `nina.apiKey` for compat.
   - `IsComplete()` returns true if *either* is set.
   - Companion HTTP client prefers `pairingToken` (sent as bearer); falls back to `apiKey` (sent as header) if only that's present.
   - Primary accepts both, logs a one-time deprecation warning per session when the apiKey path fires.
3. **Wizard +1 release:** `apiKey` field still parsed but `IsComplete()` only returns true if `pairingToken` is set. Old configs trip the "setup needed" UX → user runs the wizard once → token replaces key.
4. **Wizard +2 release:** Remove `nina.apiKey` from schema, drop the bearer-or-header logic, log a one-time error if an old config still has the field set.

Three-release transition gives users with auto-update disabled time to migrate without surprise breakage.

---

## Failure modes & recovery

| Scenario | What user sees | What they do |
|----------|----------------|--------------|
| Primary host changes IP | Sync starts 401-ing or timing out | Wizard reopens (passive revocation OR connection error). User updates host, optionally re-pairs. |
| User wiped primary | All tokens lost | All existing companions 401, wizard reopens, generate fresh token, re-pair. |
| User wiped companion | Token in `companion.json` lost | Fresh wizard run on new install, generate fresh token (old one still in NS Options — revoke manually). |
| NS upgrade breaks compat | Companion shows "version too old, upgrade companion to vX" | User downloads new companion zip from Releases. |
| Companion downgrade | Primary returns 401 (companion too old for current API) — covered by `minCompanionVersion` exchange. |
| User revokes their own active pairing in NS Options | Next companion sync 401s, wizard reopens. | User generates a new token and re-pairs. |

Passive revocation throughout — companion never proactively polls `/whoami` for revocation status. Cheap and avoids a background-polling pattern that drains battery on the Mac mini.

---

## Security

- Tokens transit over plain HTTP today (NS dashboard is HTTP, not HTTPS). Acceptable on a Tailscale tailnet (Tailscale provides E2E encryption at the transport layer). NOT acceptable over a hostile network — document this. Future work: TLS via NINA itself, or recommend Tailscale exclusively in setup wizard hints.
- Token stored on companion in `companion.json` plaintext. Same threat model as today's apiKey — companion data dir is local to a trusted machine. Don't bother encrypting; if the disk is compromised the threat is much bigger than a sync token.
- SHA-256 of token stored on primary. Constant-time comparison when hashing incoming requests (use `CryptographicOperations.FixedTimeEquals`) — defeats timing-attack discovery of valid hashes.
- Rate-limit `POST /api/companion/pair` to ~10/min per source IP. Prevents brute-forcing the 80-bit space, though entropy already makes that infeasible — cheap insurance.

---

## Implementation order

Five bite-sized PRs against `feature/companion-rd`. Each lands independently.

1. **`CompanionTokenStore` + sidecar file** — storage layer, no UI, no endpoints. Unit tests for add/lookup/revoke/atomic write.
2. **Primary endpoints** — `/api/companion/info`, `/api/companion/pair`, `/api/companion/revoke`. Tests for each happy path + every documented failure mode.
3. **Bearer-auth shim on existing sync endpoints** — accept both `pairingToken` (bearer) and `apiKey` (header). Log deprecation warning. Tests confirm both paths work.
4. **NS Options panel** — generate / list / revoke UI in `Options.xaml`. WPF code-behind. No unit tests (WPF code-behind not testable without live NINA — covered by manual smoke).
5. **Companion wizard** — `/setup` page, 5-step flow, new `wizard.js`. Tests for state machine transitions (vanilla JS unit tests via existing test harness if one exists; manual otherwise).

Total estimate (Claude execution time, per `feedback_claude_velocity_estimates`): ~3–4 hours wall-clock across the PRs.

---

## Open questions

1. **Token storage location on multi-user Windows boxes.** `%LOCALAPPDATA%` is per-user, which is correct — but if NINA was installed system-wide and runs under a different account than the user in NS Options, the WPF panel and the dashboard endpoints will see different files. Verify NINA's process model before committing. (Likely fine — NINA runs as the logged-in user.)
2. **Should "Generate Token" let the user pre-name the entry**, or only post-pair via the companion-supplied `companionName`? Current design favors post-pair (less friction). Could add an optional name field in the generate modal if naming-before-claim is a useful pattern in practice.
3. **Auto-generate a token at NS first install?** Would let users skip step 3 entirely for the common "single Mac mini" case — the dashboard's "Set up Companion" button could prefill the token. Tradeoff: one less manual step, but the token is then "always-on" in `settings.json` until used, which is a mild surface-area expansion. Defer.
4. **Companion CLI flag for non-interactive pairing.** Power users running companion in CI/scripted environments might want `NightSummaryCompanion pair --host X --token Y --name Z` to bypass the wizard. Easy add later — not blocking.

---

## Out of scope (for now)

- Multi-NINA support (one companion paired with several NINA machines). Today's `companion.json` assumes one primary. Schema would need to become a list.
- HTTPS / cert pinning between companion and primary.
- TOTP-style ephemeral codes (numeric, 6-digit, 60s expiry). Heavier UX (user must complete pairing within the window), no real benefit at current threat model.
- WebAuthn / passkey-based pairing. Wildly over-engineered for "let my Mac talk to my NINA box."

---

## Review checklist before implementation starts

- [ ] Grep confirms `nina.apiKey` is only used by companion sync paths (open question #2 above).
- [ ] Decide on `minCompanionVersion` semantics — semver-string-compare, or numeric only.
- [ ] Confirm NS Options panel layout fits within existing UI standards (button widths, section grouping — see `CLAUDE.md` UI standards section).
- [ ] Confirm passive-revocation UX is correct: 401 from sync → companion drops back to wizard automatically, *not* a hard error page (would lose the user).
- [ ] Verify token hashing approach: SHA-256 vs `Rfc2898DeriveBytes` (PBKDF2). Tokens have high entropy → SHA-256 is fine; PBKDF2 only helps when input is low-entropy (passwords).
