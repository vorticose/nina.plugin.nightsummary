# Companion Pairing — Manual Smoke Test

End-to-end checklist for the five-PR pairing rollout on `feature/companion-rd`. Run this before merging to `dev`. Estimated time: ~30 min if everything works first try; ~60 min with the dual-auth / revoke / failure-mode coverage.

Repo state assumed: branch `feature/companion-rd` at `d7b55c5` or later, full Release solution built (`dotnet build NINA.Plugin.NightSummary.sln -c Release` succeeds), 818 tests passing.

## Prerequisites

- [ ] NINA running on the rig (RBFocus or your test box) with the plugin built from this branch deployed to `%LOCALAPPDATA%\NINA\Plugins\3.0.0\Night Summary\`. Deploy *every* DLL the build produces — not just `NINA.Plugin.NightSummary.dll` (Dashboard, deps.json, Microsoft.Data.Sqlite stack — see `feedback_deploy_all_dlls`).
- [ ] Companion binary built from this branch and runnable on your Mac mini (or any non-rig machine). The companion's `companion.json` exists; *don't* pre-populate `nina.apiKey` or `nina.pairingToken` — leave both blank for the fresh-install path.
- [ ] Both machines reachable on the same Tailscale tailnet (or LAN).
- [ ] A browser open on the machine that'll drive the wizard (typically the same machine as the companion, but the wizard works from any device on the tailnet).

If anything in this section fails, stop — the rest of the checklist depends on it.

---

## 1. Plugin Options panel (step 4 — WPF)

Open NINA → Options → Plugins → Night Summary → **Dashboard Server** section → scroll to the new **Companion Pairing** subsection (right after **Companion App**).

### Generate
- [ ] Click **+ Generate Token**.
- [ ] A purple-bordered panel appears showing a 16-char token formatted as `XXXX-XXXX-XXXX-XXXX`.
- [ ] The TextBox is read-only — cursor visible but no edits stick.
- [ ] Click **Copy**. Paste into Notepad — matches the displayed string exactly.
- [ ] Click **Done**. Panel disappears. The plain token is *not* shown anywhere else.

### Unpaired token list
- [ ] After Generate but before pairing: the **Unpaired tokens** list shows one row: `(unnamed) · created just now — not yet claimed · [Revoke]`.

### Revoke (unclaimed)
- [ ] Click **Revoke** on the unpaired row.
- [ ] Confirmation MessageBox appears with the entry name and warning text.
- [ ] Cancel → row stays.
- [ ] OK → row disappears. Unpaired list shows `· —` placeholder.

### Sidecar file
- [ ] Verify `%LOCALAPPDATA%\NINA\NightSummary\companion_tokens.json` exists and contains the revoked entry with `revokedAt` set (soft-delete; row hidden but kept for audit).
- [ ] The JSON does **not** contain the plain token string — only its SHA-256 hash.

If any of these fail, file an issue and stop. The wizard depends on the Options panel working.

---

## 2. Setup wizard (step 5 — companion side)

With both machines running, the companion's `companion.json` blank, and a fresh token generated in NINA Options:

### Welcome + connect
- [ ] Browser → `http://<companion-host>:8182/` (default companion dashboard port).
- [ ] Page redirects to `/setup`. URL bar updates.
- [ ] Step indicator shows `● ○ ○ ○ ○`.
- [ ] **Get Started** → step 2.

### Step 2 — Connect to NINA
- [ ] Enter the rig's host/IP + dashboard port (default 8181).
- [ ] Click **Test Connection**.
- [ ] Green ✓ message includes the actual NS version and NINA version from `/api/companion/info`.
- [ ] Step indicator: `● ● ○ ○ ○`. (Pulses to active on next step.)
- [ ] **Next** is enabled. Click → step 3.

### Step 2 failure modes (rerun for each, then go back to the good values)
- [ ] Bad host (e.g. `nonexistent.invalid`) → red message "Can't reach NINA" or similar. **Next** stays disabled.
- [ ] Right host, wrong port (e.g. `9999`) → connection refused message.
- [ ] Pre-3.3 NS (if you have a way to point at one) → "server responded but does not support pairing" message.

### Step 3 — Pair
- [ ] Generate a *fresh* token in NINA (the original is fine if you didn't revoke it).
- [ ] Paste it into the **Token** field. Companion name pre-fills with the machine name; accept it or change it.
- [ ] **Pair** → "Pairing…" → green "✓ Paired successfully." → auto-advances to step 4 after ~400ms.

### Step 3 failure modes
- [ ] Type a random 16-char string instead of a real token → "That token is not recognized."
- [ ] Use a token you just revoked → "That token has been revoked."
- [ ] Use a token already paired with a *different* companion name (pair from a second machine first, then try the same token here with a new name) → "That token is already paired with '<other>'."

### Step 4 — Sync settings
- [ ] Pick a schedule radio (default "Every 4 hours" is fine for the test).
- [ ] **Save & Run First Sync** → step 5.

### Step 5 — First sync
- [ ] Progress text shows "Starting sync…" then "✓ Setup complete — pulled X files, Y thumbnails."
- [ ] Green message "Redirecting to the dashboard…"
- [ ] After ~1.5s: URL is `/`, full dashboard loads with your synced data.

### Post-setup checks
- [ ] `companion.json` now contains `nina.pairingToken` (the plain token) and `nina.host` + `nina.port` you entered. **No** `nina.apiKey` was set (you started blank).
- [ ] In NINA Options, the **Paired companions** list now shows your companion name with "paired just now". The unpaired-tokens row is gone.
- [ ] Manually visit `http://<companion>:8182/setup` → redirects back to `/`. Wizard can't be re-entered post-setup.

---

## 3. End-to-end sync with the new token

After pairing, exercise normal sync:

- [ ] In the companion dashboard, click the sync button (or wait for a scheduled tick). Sync completes without errors.
- [ ] On the rig, tail the NINA log (`%LOCALAPPDATA%\NINA\Logs\<latest>`). The Companion sync request should hit `/api/export/*` and **not** trigger the legacy-apiKey deprecation warning (the wizard set up `pairingToken`, not `apiKey`).
- [ ] In NINA Options → Companion Pairing → Paired companions: timestamp updates to "paired just now" again (lastUsedAt bumped by the auth shim on every authenticated request).

---

## 4. Dual-auth shim (step 3 — backwards compat)

Critical: existing users with old `companion.json` files configured before this branch must keep working.

- [ ] On the companion, stop the daemon. Edit `companion.json`:
  - Set `nina.apiKey` to the value from NINA Options → **API Key** (the old shared-key field). Copy/paste it.
  - Clear `nina.pairingToken` (set to empty string).
- [ ] Restart the companion. Sync runs successfully.
- [ ] On the rig, NINA log shows a one-shot warning: **"NightSummary: Companion authenticated via legacy CompanionApiKey — this fallback will be removed next release. Re-pair the companion to migrate to a pairing token."**
- [ ] Trigger several more syncs. The warning fires **only once** per NINA session — subsequent legacy-auth requests succeed silently.
- [ ] Restart NINA. Trigger another legacy-auth sync. The warning fires again (once per server lifetime).
- [ ] Re-set `companion.json` back to using the pairing token. Sync runs. No warning. Confirm the warning didn't fire in the log this time.

---

## 5. Revoke a live pairing

- [ ] With the companion paired and syncing, in NINA Options → Companion Pairing → **Revoke** the active paired companion.
- [ ] Confirm in the MessageBox. The row disappears from **Paired companions**.
- [ ] Within ~10s (the companion's ping loop), the companion dashboard's banner flips to "primary unreachable" or shows a sync error on the next attempt.
- [ ] The companion does **not** automatically redirect back to `/setup` — passive revocation per design. The user can manually visit `/setup` to re-pair (and they need a fresh token, since the old one is soft-deleted).
- [ ] Trigger a sync from the companion. It returns 401 / "unauthorized."

---

## 6. Negative / edge cases

- [ ] Visit `/setup` on the *primary* dashboard URL (NINA's `http://rig:8181/setup`). Returns 404 with message "setup wizard only runs in companion mode."
- [ ] Visit `/api/companion/pair` via curl against the primary with bad JSON: returns 400 with "invalid json: …"
- [ ] Visit `/api/companion/info` on the primary (no auth). Returns 200 with version info — confirms the unauthenticated probe still works.
- [ ] Visit `/api/companion/revoke` with a stolen (unknown) bearer token. Returns 401.

---

## Sign-off

If everything above passed:

- [ ] Squash-merge or `--no-ff` merge `feature/companion-rd` → `dev` (your preference per branching strategy).
- [ ] Update `CHANGELOG_DRAFT.md` to fold the companion-pairing section into the appropriate release bucket (currently drafted as `Unreleased — Companion pairing (feature/companion-rd)`).
- [ ] Tick off the relevant entries on the release checklist when the time comes to ship.

If anything failed, capture the failing step number + relevant logs (`%LOCALAPPDATA%\NINA\Logs\` on the rig and the companion's own log file under its data dir) before filing an issue or asking for fixes.
