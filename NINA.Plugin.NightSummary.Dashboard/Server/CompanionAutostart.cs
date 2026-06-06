using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace NINA.Plugin.NightSummary.Server {

    /// <summary>
    /// Manages the companion's "start at login" autostart entry, per-OS, with no
    /// code-signing or admin required (all user-domain mechanisms):
    ///   macOS   -> ~/Library/LaunchAgents/com.nightsummary.companion.plist + launchctl
    ///   Windows -> a .lnk in the user's Startup folder pointing at the .vbs launcher
    ///   Linux   -> a systemd --user unit + systemctl --user enable
    ///
    /// All three point at the watchdog LAUNCHER (not the raw -bin), so the
    /// dashboard Restart (exit 88) / Quit (exit 0) semantics keep working. The
    /// macOS LaunchAgent uses KeepAlive=false on purpose: KeepAlive=true would
    /// resurrect the process on a clean Quit (exit 0) — the original autostart bug.
    /// </summary>
    internal static class CompanionAutostart {

        private const string MacLabel  = "com.nightsummary.companion";
        private const string LinuxUnit = "nightsummary-companion";

        internal sealed class Status {
            public bool   supported  { get; set; }
            public bool   enabled    { get; set; }
            public string mechanism  { get; set; } = "";
            public string detail     { get; set; } = "";   // human note / why unsupported
        }

        // ── Launcher discovery ───────────────────────────────────────────────
        // The running process is the self-contained -bin; the autostart entry
        // must point at the sibling watchdog launcher so exit-88/exit-0 work.
        private static string? LauncherPath(out string detail) {
            detail = "";
            var binPath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(binPath)) { detail = "process path unavailable"; return null; }
            var dir = Path.GetDirectoryName(binPath);
            if (string.IsNullOrEmpty(dir))     { detail = "process directory unavailable"; return null; }

            string launcher;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                launcher = binPath; // the WinExe is its own double-click target (no .vbs/.cmd)
            } else {
                // Running from a Linux AppImage? $APPIMAGE is the STABLE path to the
                // .AppImage file; the mount dir (where ProcessPath lives) is a fresh
                // temp path each run, so an autostart unit must target the AppImage
                // file itself, not the throwaway mount.
                var appImage = Environment.GetEnvironmentVariable("APPIMAGE");
                if (!string.IsNullOrEmpty(appImage) && File.Exists(appImage))
                    launcher = appImage;
                else
                    launcher = Path.Combine(dir, "NightSummaryCompanion"); // mac + linux tarball watchdog
            }

            if (!File.Exists(launcher)) {
                detail = $"launcher not found at {launcher} (running unpackaged / dev build?)";
                return null;
            }
            return launcher;
        }

        // ── Public API ───────────────────────────────────────────────────────

        internal static Status GetStatus() {
            var st = new Status();
            try {
                var launcher = LauncherPath(out var detail);
                if (launcher == null) { st.supported = false; st.detail = detail; return st; }
                st.supported = true;

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                    st.mechanism = "LaunchAgent";
                    st.enabled   = File.Exists(MacPlistPath());
                } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                    st.mechanism = "Startup shortcut";
                    st.enabled   = File.Exists(WinShortcutPath());
                } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
                    st.mechanism = "systemd --user";
                    st.enabled   = RunCapture("systemctl", $"--user is-enabled {LinuxUnit}", out var o) && o.Trim() == "enabled";
                } else {
                    st.supported = false; st.detail = "unsupported OS";
                }
            } catch (Exception ex) { st.supported = false; st.detail = ex.Message; }
            return st;
        }

        internal static (bool ok, string? error) Enable() {
            try {
                var launcher = LauncherPath(out var detail);
                if (launcher == null) return (false, detail);

                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))     return MacEnable(launcher);
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return WinEnable(launcher);
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))   return LinuxEnable(launcher);
                return (false, "unsupported OS");
            } catch (Exception ex) { return (false, ex.Message); }
        }

        internal static (bool ok, string? error) Disable() {
            try {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))     return MacDisable();
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return WinDisable();
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))   return LinuxDisable();
                return (false, "unsupported OS");
            } catch (Exception ex) { return (false, ex.Message); }
        }

        // ── macOS ────────────────────────────────────────────────────────────

        private static string MacPlistPath() =>
            Path.Combine(Home(), "Library", "LaunchAgents", $"{MacLabel}.plist");

        private static (bool, string?) MacEnable(string launcher) {
            var plist = MacPlistPath();
            Directory.CreateDirectory(Path.GetDirectoryName(plist)!);
            var logDir = Path.Combine(Home(), "Library", "Logs", "NightSummaryCompanion");
            Directory.CreateDirectory(logDir);

            File.WriteAllText(plist,
$@"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0"">
<dict>
    <key>Label</key><string>{MacLabel}</string>
    <key>ProgramArguments</key>
    <array><string>{Xml(launcher)}</string><string>serve</string></array>
    <key>RunAtLoad</key><true/>
    <key>KeepAlive</key><false/>
    <key>ProcessType</key><string>Background</string>
    <key>StandardOutPath</key><string>{Xml(Path.Combine(logDir, "launchd.out"))}</string>
    <key>StandardErrorPath</key><string>{Xml(Path.Combine(logDir, "launchd.err"))}</string>
</dict>
</plist>
");
            var uid = GetUid();
            // Re-bootstrap cleanly. bootstrap is the modern API; from the GUI
            // (Aqua) session it succeeds. load -w is the proven fallback.
            Run("launchctl", $"bootout gui/{uid}/{MacLabel}");
            if (Run("launchctl", $"bootstrap gui/{uid} \"{plist}\"")) return (true, null);
            if (Run("launchctl", $"load -w \"{plist}\""))             return (true, null);
            // Plist is written, so it WILL autostart next login even if the live
            // load failed; report success but note it.
            return (true, "saved; will take effect on next login (live load unavailable)");
        }

        private static (bool, string?) MacDisable() {
            var plist = MacPlistPath();
            var uid = GetUid();
            Run("launchctl", $"bootout gui/{uid}/{MacLabel}");
            Run("launchctl", $"unload \"{plist}\"");
            if (File.Exists(plist)) File.Delete(plist);
            return (true, null);
        }

        // ── Windows ──────────────────────────────────────────────────────────

        private static string WinShortcutPath() =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                         "Night Summary Companion.lnk");

        private static (bool, string?) WinEnable(string exeLauncher) {
            var lnk = WinShortcutPath();
            // Create the .lnk via WScript.Shell COM through PowerShell (no COM ref
            // needed, no admin). Target = the WinExe launcher itself — it has no
            // console (WinExe subsystem) and carries the embedded brand icon, so
            // the shortcut inherits the icon automatically. Working dir stays the
            // app folder so the bundled native dlls resolve.
            var ps =
                "$s=(New-Object -ComObject WScript.Shell).CreateShortcut('" + lnk.Replace("'", "''") + "');" +
                "$s.TargetPath='" + exeLauncher.Replace("'", "''") + "';" +
                "$s.WorkingDirectory='" + (Path.GetDirectoryName(exeLauncher) ?? "").Replace("'", "''") + "';" +
                "$s.Description='Night Summary Companion';" +
                "$s.Save()";
            if (Run("powershell", $"-NoProfile -NonInteractive -Command \"{ps}\"") && File.Exists(lnk))
                return (true, null);
            return (false, "failed to create startup shortcut");
        }

        private static (bool, string?) WinDisable() {
            var lnk = WinShortcutPath();
            if (File.Exists(lnk)) File.Delete(lnk);
            return (true, null);
        }

        // ── Linux ────────────────────────────────────────────────────────────

        private static string LinuxUnitPath() =>
            Path.Combine(Home(), ".config", "systemd", "user", $"{LinuxUnit}.service");

        private static (bool, string?) LinuxEnable(string launcher) {
            var unit = LinuxUnitPath();
            Directory.CreateDirectory(Path.GetDirectoryName(unit)!);
            File.WriteAllText(unit,
$@"[Unit]
Description=Night Summary Companion dashboard
After=network-online.target
Wants=network-online.target

[Service]
Type=simple
WorkingDirectory={Path.GetDirectoryName(launcher)}
ExecStart={launcher} serve
Restart=on-failure
RestartSec=5

[Install]
WantedBy=default.target
");
            Run("systemctl", "--user daemon-reload");
            Run("loginctl", $"enable-linger {Environment.UserName}"); // survive logout; may need policykit, best-effort
            if (Run("systemctl", $"--user enable --now {LinuxUnit}")) return (true, null);
            return (true, "unit saved; enable failed (no systemd --user session?) — will work under a logind session");
        }

        private static (bool, string?) LinuxDisable() {
            Run("systemctl", $"--user disable --now {LinuxUnit}");
            var unit = LinuxUnitPath();
            if (File.Exists(unit)) File.Delete(unit);
            Run("systemctl", "--user daemon-reload");
            return (true, null);
        }

        // ── helpers ──────────────────────────────────────────────────────────

        private static string Home() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        private static string Xml(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        private static string GetUid() => RunCapture("id", "-u", out var o) ? o.Trim() : "501";

        private static bool Run(string file, string args) => RunCapture(file, args, out _);

        private static bool RunCapture(string file, string args, out string stdout) {
            stdout = "";
            try {
                using var p = new Process();
                p.StartInfo = new ProcessStartInfo {
                    FileName = file, Arguments = args,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    UseShellExecute = false, CreateNoWindow = true,
                };
                if (!p.Start()) return false;
                stdout = p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit(15000);
                return p.HasExited && p.ExitCode == 0;
            } catch { return false; }
        }
    }
}
