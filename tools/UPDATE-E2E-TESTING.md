# Companion in-app update — end-to-end test runbook

Tests the full `download -> verify -> swap -> relaunch` of the companion updater
**without publishing a real release**, using the `NS_UPDATE_BASE_URL` seam and
`tools/fake-release-server.py`.

The trick: build a second copy of the companion with an artificially higher
version (`-p:VersionPrefix=9.9.9`), serve it as the "latest release," and point
the running (lower-version) install at the fake server. The updater sees a newer
version, downloads it, verifies the checksum, swaps the install, and relaunches.

> The swap + relaunch is **packaging-dependent**, so the running install must be
> a *properly packaged* build, not `dotnet Companion.dll`:
> - **Windows**: a published single-file `.exe` (self-respawn is in-process).
> - **Linux**: the tarball install layout from `install-companion.sh` (the bash
>   **watchdog** must be present for the exit-88 respawn).
> - **macOS**: the `.app` (launcher + watchdog).

## Windows — verified PASS 2026-06-16

```bash
E2E=C:/tmp/nsc-e2e ; PORT=18099 ; CPORT=18182
# 1. Build OLD (current version) and NEW (9.9.9) single-file exes
dotnet publish NINA.Plugin.NightSummary.Companion/*.csproj -c Release -r win-x64 -o $E2E/oldpub
dotnet publish NINA.Plugin.NightSummary.Companion/*.csproj -c Release -r win-x64 -p:VersionPrefix=9.9.9 -o $E2E/newpub
# 2. Stage fake release: zip NEW as NightSummaryCompanion/NightSummaryCompanion.exe
#    -> $E2E/release/download/NightSummaryCompanion-win-x64.zip
#    sha256 -> $E2E/release/download/checksums.txt  ("<hash>  <name>")
#    $E2E/release/releases-latest.json  (tag v9.9.9, asset browser_download_url -> 127.0.0.1:$PORT)
#    copy OLD exe -> $E2E/install/NightSummaryCompanion.exe
# 3. Serve + run + drive
python tools/fake-release-server.py $E2E/release $PORT &
NS_UPDATE_BASE_URL=http://127.0.0.1:$PORT $E2E/install/NightSummaryCompanion.exe \
    serve --no-browser --no-sync --config $E2E/companion.json &
curl  http://127.0.0.1:$CPORT/api/companion/update-check   # updateAvailable:true, latest 9.9.9, canSelfUpdate:true
curl -X POST http://127.0.0.1:$CPORT/api/companion/update   # {ok:true}
# poll /api/health until version == 9.9.9  (swap + relaunch done)
```

Assert: `/api/health` version flips `3.2.1 -> 9.9.9`, and the on-disk install exe
sha256 changes to the NEW build's. Dashboard log shows
`starting in-app update -> downloading -> checksum verified -> helper launched`.

**Negative test (integrity gate):** overwrite `checksums.txt` with a wrong hash,
reset the install to the OLD exe, repeat. The update must **abort**: version stays
`3.2.1`, exe untouched, log shows `in-app update failed: checksum mismatch ...`.
(Only `checksums.txt` differs from the passing run, so this isolates the gate.)

## Linux (TODO — WSL2 Debian / Ubuntu VM)

Install OLD via `install-companion.sh` (tarball -> `~/.local/share/...` with the
bash watchdog). Build NEW with `-r linux-x64 -p:VersionPrefix=9.9.9`, pack the
`.tar.gz` (layout `NightSummaryCompanion/{NightSummaryCompanion-bin, launcher}`),
serve it, run the installed launcher under `NS_UPDATE_BASE_URL`, POST update.
The watchdog respawns on exit 88. AppImage/`.deb` are NotifyOnly — just confirm
the Download banner; there's no swap to test.

## macOS (TODO — Mac mini, with care)

The installer targets `/Applications/NightSummaryCompanion.app`, so point the fake
server at a build of **our** newer code (moves the install *forward*, not a
downgrade to the public release) and confirm before touching the production app —
or test against a temp-renamed `.app`.
