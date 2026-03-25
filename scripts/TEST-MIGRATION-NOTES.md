# Migration Test Script -- Developer Notes

Hard-won lessons from building test-migration.ps1.

## Windows file locking is brutal for SQLite testing

The NightSummary SQLite file gets locked by multiple processes after NINA runs:
- **WebView2 (msedgewebview2.exe)** detaches from the NINA process tree and survives
  `taskkill /T`. Must be killed separately by image name after NINA exits.
- **Windows Search (SearchIndexer)** and **Windows Defender** hold directory handles
  on newly created files. This prevents both file deletion AND directory renaming,
  even 20+ seconds after NINA has fully exited.
- **SQLite connection pooling** in System.Data.SQLite keeps file handles open even
  after calling Close() and Dispose(). Always open with `Pooling=False` in the
  connection string, or call SQLiteConnection.ClearAllPools() before file operations.

## Use directory junctions to sidestep file locks

Trying to delete or rename the NightSummary directory between tests will fail
intermittently because SearchIndexer/Defender hold directory handles. The solution
is to make NightSummary a directory junction and swap what it points to:

- Deleting a junction (`cmd /c "rd <path>"`) modifies the PARENT directory, not the
  target. SearchIndexer watching NightSummary cannot prevent you from removing it.
- Each test gets its own numbered target directory (NightSummary_test_1, _2, etc.)
  so stale locked files from previous tests are never in the way.
- Always restore the junction to the real data directory at the end of the suite,
  and self-heal a broken/missing junction at startup.

## PowerShell script encoding must be pure ASCII

PowerShell on Windows will fail to parse scripts containing non-ASCII characters
(em dashes, box-drawing chars, smart quotes) even in comments. The GitHub raw CDN
also has aggressive caching that can serve stale content for several minutes.
Always author scripts with plain ASCII and use the GitHub Contents API
(`/repos/.../contents/...`) rather than raw.githubusercontent.com for reliable
fresh downloads.

## PowerShell variable interpolation quirks

- `"$varName_suffix"` treats the underscore as part of the variable name.
  Use `"${varName}_suffix"` or plain concatenation: `$varName + "_suffix"`.
- `$ErrorActionPreference = "Stop"` causes native commands (taskkill, etc.) that
  exit with non-zero codes to throw terminating errors. Temporarily set it to
  `"SilentlyContinue"` around native command calls.
- When a function returns a single-element array, PowerShell unwraps it. Use
  `@(Query-All ...)[0]` (with the `@()`) to force array semantics before indexing.

## Killing NINA reliably

```powershell
taskkill /PID $nina.Id /T /F         # kill process tree
$nina.WaitForExit(15000)              # wait for main process
taskkill /IM msedgewebview2.exe /F    # WebView2 survives /T, kill by name
# Loop until all WebView2 gone -- they can respawn briefly after kill
$deadline = (Get-Date).AddSeconds(15)
do {
    taskkill /IM msedgewebview2.exe /F 2>&1 | Out-Null
    Start-Sleep -Milliseconds 500
} while ((Get-Process -Name "msedgewebview2" -ErrorAction SilentlyContinue) `
         -and (Get-Date) -lt $deadline)
Start-Sleep -Seconds 2  # OS needs time to release directory handles
```

## Test count checks should use SessionId filters

Total COUNT(*) checks are flaky because NINA may write its own session record
during startup before being killed. Always filter counts by the specific
SessionId values the test inserted:

```sql
SELECT COUNT(*) FROM Sessions WHERE SessionId IN ('id1','id2','id3')
SELECT COUNT(*) FROM Images   WHERE SessionId IN ('id1','id2','id3')
```
