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

## Linux — verified PASS 2026-08-04

Ran on `fios-exit-node` (an always-on Linux box that, unexpectedly, already runs
a real production companion instance via `.deb` at `/opt/nightsummary-companion`,
port 8182 — discovered mid-test when OLD's default port collided with it. The
real service was never touched: it just failed to bind and stayed put. The test
install instead used `~/.local/share/nightsummary-companion` (the same path
`install-companion.sh` uses) with `companion.json`'s `port`/`readOnlyMirrorPort`
repointed to 18282/18283, fully isolated from the real instance's ports/paths.

```bash
# 1. OLD and NEW both built from THIS checkout (only VersionPrefix differs) --
#    unlike macOS, the Linux swap logic lives in the C# binary itself, not a
#    separately-fetched script, so OLD must already contain the fix being tested.
dotnet publish NINA.Plugin.NightSummary.Companion/*.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true
# -> pack as OLD tarball (layout NightSummaryCompanion/{NightSummaryCompanion-bin, NightSummaryCompanion (watchdog)})
dotnet publish NINA.Plugin.NightSummary.Companion/*.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:VersionPrefix=9.9.9
# -> pack as NEW tarball the same way; sha256 -> release/download/checksums.txt

# 2. Install OLD via the real installer (creates ~/.local/share/nightsummary-companion)
NSC_TARBALL=./NightSummaryCompanion-linux-x64-OLD.tar.gz sh install-companion.sh

# 3. Repoint the isolated install's companion.json to unused ports, serve + drive
python3 tools/fake-release-server.py ./release 18299 &
NS_UPDATE_BASE_URL=http://127.0.0.1:18299 ~/.local/share/nightsummary-companion/NightSummaryCompanion serve --no-browser &
curl  http://127.0.0.1:18282/api/companion/update-check   # strategy:LinuxTarballInPlace, canSelfUpdate:true
curl -X POST http://127.0.0.1:18282/api/companion/update
# poll /api/health until version == 9.9.9
```

Assert: `/api/health` version flips `3.2.1 -> 9.9.9` on the **first** poll (no
retry loop needed — the fix means the swap just works). Log confirms the exact
sequence: `starting in-app update -> checksum verified -> Linux binary replaced
in place; exiting 88 for watchdog respawn`. Pre-fix, this last step failed
silently (Linux rejects overwriting a running executable in place with
`ETXTBSY`) — see the code review that found it. No negative/checksum-mismatch
test run for Linux (time-boxed to the positive path; Windows already covers
that gate's logic, which is platform-independent).

Cleanup: quit via `/api/companion/quit`, remove the isolated install dir +
XDG data dir + `~/.local/bin` symlink + `.desktop` entry, kill the fake server.
Verified the real production instance's PIDs and `/api/health` were unchanged
throughout.

## macOS — verified PASS 2026-08-04

Ran against the **real production install** on the Mac mini (per the "confirm
before touching the production app" note above) — backed up first, restored
after. NEW (9.9.9) built + signed + packaged as a `.dmg` matching
`build-companion-mac.ps1`'s steps exactly (publish -> assemble `.app` -> ad-hoc
codesign -> `hdiutil create`).

```bash
dotnet publish NINA.Plugin.NightSummary.Companion/*.csproj -c Release -r osx-arm64 --self-contained true -p:PublishSingleFile=true -p:VersionPrefix=9.9.9
# -> assemble .app bundle, codesign --force --deep --sign -, hdiutil create ... -format UDZO
curl -X POST http://127.0.0.1:8182/api/companion/quit          # stop the real running install
cp -R /Applications/NightSummaryCompanion.app /Applications/NightSummaryCompanion.app.e2e-backup-<ts>
python3 tools/fake-release-server.py ./release 18199 &
NS_UPDATE_BASE_URL=http://127.0.0.1:18199 /Applications/NightSummaryCompanion.app/Contents/MacOS/NightSummaryCompanion serve --no-browser &
curl  http://127.0.0.1:8182/api/companion/update-check          # strategy:MacAppReplace
curl -X POST http://127.0.0.1:8182/api/companion/update
# poll /api/health until version == 9.9.9, then restore the backup + relaunch normally
```

Assert: version flips `3.2.1 -> 9.9.9` on the first poll; `/Applications`
shows no leftover `.new`/`.old` staging dirs after the swap (the fixed
`install-companion-mac.sh` cleans up both on success). Log confirms
`checksum verified -> launched detached mac installer -> exiting 0`. Since the
mac swap logic lives in the **shell script** (freshly fetched from the fake
server each time), the real running 3.2.1 binary was a valid OLD install here
— its C# update-trigger code is unchanged by the fix, only the script content
differs. Restored the real install from the pre-test backup afterward and
confirmed `/api/health` + rig config were unaffected. No negative/checksum test
run here either, same reasoning as Linux above.
