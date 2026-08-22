using System;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Server {

    // How the companion would apply an update on THIS install, decided from the
    // OS + how the binary is packaged. Not every install can safely self-replace:
    //   WindowsZipSwap       — detached helper waits our exit, swaps the .exe, relaunches.
    //   MacAppReplace        — detached install-companion-mac.sh re-installs the .app + relaunches.
    //   LinuxTarballInPlace  — overwrite -bin + launcher in the (user-writable) install dir, exit 88.
    //   NotifyOnly           — we can't touch the install (AppImage read-only mount, root-owned
    //                          /usr or /opt from a .deb, or a non-writable dir). Surface the
    //                          release link and let the user re-run their installer by hand.
    public enum UpdateStrategy {
        NotifyOnly,
        WindowsZipSwap,
        MacAppReplace,
        LinuxTarballInPlace,
    }

    // Result of an update check, shaped for the /api/companion/update-check wire
    // payload. All fields are plain data so the dashboard can render the banner
    // without a second round-trip.
    public sealed class UpdateInfo {
        public string Current { get; init; } = "";
        public string Latest { get; init; } = "";
        public bool UpdateAvailable { get; init; }
        public string ReleaseUrl { get; init; } = "";
        public string Notes { get; init; } = "";
        public string AssetName { get; init; } = "";
        public string AssetUrl { get; init; } = "";
        public bool CanSelfUpdate { get; init; }
        public string Strategy { get; init; } = nameof(UpdateStrategy.NotifyOnly);
        public string? Error { get; init; }
    }

    // Polls the project's GitHub Releases for a newer companion build and decides
    // whether (and how) this install could self-update. The version-comparison and
    // asset/strategy resolution are pure static methods (unit-tested); only
    // CheckAsync touches the network. A successful check is cached for 24 h so a
    // busy dashboard doesn't hammer the unauthenticated GitHub API (60 req/h/IP).
    // Failures use a short TTL so a blip cannot hide a real update for a day.
    public sealed class UpdateChecker {

        // Public repo slug — same one the install scripts target. Not a secret.
        public const string Repo = "vorticose/nina.plugin.nightsummary";

        // Test seam: when NS_UPDATE_BASE_URL is set, the check + checksums + mac-
        // installer URLs are built from it instead of github.com, so a local fake-
        // release server can drive a real end-to-end update without publishing.
        // Unset in production → real GitHub. The asset download URL itself comes
        // from the release JSON's browser_download_url, so the fake server controls
        // that by what it serves; only these three fixed URLs need the override.
        public static string? BaseOverride() => Environment.GetEnvironmentVariable("NS_UPDATE_BASE_URL");

        // GET endpoint for the latest release metadata.
        public static string ReleaseApiUrl() {
            var ovr = BaseOverride();
            return string.IsNullOrEmpty(ovr)
                ? $"https://api.github.com/repos/{Repo}/releases/latest"
                : $"{ovr.TrimEnd('/')}/releases/latest";
        }

        // Stable download URL for a named release asset (checksums.txt, the mac
        // installer script). releases/latest/download/<name> always resolves to
        // the current release's asset.
        public static string DownloadUrl(string fileName) {
            var ovr = BaseOverride();
            return string.IsNullOrEmpty(ovr)
                ? $"https://github.com/{Repo}/releases/latest/download/{fileName}"
                : $"{ovr.TrimEnd('/')}/releases/latest/download/{fileName}";
        }

        // GitHub requires a User-Agent on API requests or it 403s.
        private const string UserAgent = "NightSummaryCompanion-UpdateChecker";

        public static readonly TimeSpan SuccessCacheTtl = TimeSpan.FromHours(24);
        public static readonly TimeSpan ErrorCacheTtl = TimeSpan.FromMinutes(15);

        // How long to keep a CheckAsync result. Errors must not sit in the
        // 24 h bucket: a GitHub blip would hide a real release until tomorrow.
        public static TimeSpan TtlFor(UpdateInfo info) =>
            string.IsNullOrEmpty(info?.Error) ? SuccessCacheTtl : ErrorCacheTtl;

        private readonly object _lock = new();
        private UpdateInfo? _cached;
        private DateTime _cachedUntilUtc = DateTime.MinValue;
        private readonly Func<HttpClient> _httpFactory;

        public UpdateChecker(Func<HttpClient>? httpFactory = null) {
            // Default factory: a short-timeout client with the required UA. Injectable
            // so a test (or a caller that wants a shared client) can substitute one.
            _httpFactory = httpFactory ?? (() => {
                var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
                http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                return http;
            });
        }

        // Check for a newer release. Returns the cached result until its TTL
        // unless force=true. Never throws — a network/parse failure comes back
        // as an UpdateInfo with Error set and UpdateAvailable=false, so the
        // banner just stays hidden rather than the dashboard breaking.
        public async Task<UpdateInfo> CheckAsync(string currentVersion, bool force, CancellationToken ct) {
            lock (_lock) {
                if (!force && _cached != null && DateTime.UtcNow < _cachedUntilUtc) {
                    return _cached;
                }
            }

            UpdateInfo result;
            try {
                using var http = _httpFactory();
                var json = await http.GetStringAsync(ReleaseApiUrl(), ct);
                result = BuildFromReleaseJson(json, currentVersion,
                    RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
                    RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
                    RuntimeInformation.IsOSPlatform(OSPlatform.OSX),
                    RuntimeInformation.OSArchitecture,
                    DecideStrategy());
            } catch (Exception ex) {
                result = new UpdateInfo {
                    Current = currentVersion ?? "",
                    Error   = ex.Message,
                };
            }

            lock (_lock) {
                _cached = result;
                _cachedUntilUtc = DateTime.UtcNow + TtlFor(result);
            }
            return result;
        }

        // ── Pure helpers (unit-tested) ───────────────────────────────────────

        // Parse the GitHub releases/latest payload into an UpdateInfo. Separated
        // from the HTTP call so tests can feed canned JSON. `os*` flags + arch +
        // strategy are passed in (not read from the environment) so a test can
        // exercise every platform's asset resolution on one machine.
        public static UpdateInfo BuildFromReleaseJson(
                string json,
                string currentVersion,
                bool isWindows,
                bool isLinux,
                bool isMac,
                Architecture arch,
                UpdateStrategy strategy) {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var t) ? (t.GetString() ?? "") : "";
            var url = root.TryGetProperty("html_url", out var h) ? (h.GetString() ?? "") : "";
            var notes = root.TryGetProperty("body", out var b) ? (b.GetString() ?? "") : "";

            var assetName = ResolveAssetName(isWindows, isLinux, isMac, arch);
            string assetUrl = "";
            if (!string.IsNullOrEmpty(assetName)
                && root.TryGetProperty("assets", out var assets)
                && assets.ValueKind == JsonValueKind.Array) {
                foreach (var a in assets.EnumerateArray()) {
                    if (a.TryGetProperty("name", out var n)
                        && string.Equals(n.GetString(), assetName, StringComparison.OrdinalIgnoreCase)
                        && a.TryGetProperty("browser_download_url", out var d)) {
                        assetUrl = d.GetString() ?? "";
                        break;
                    }
                }
            }

            var available = IsNewer(currentVersion, tag);
            // Self-update is only offered when the install is replaceable AND we
            // actually found the matching asset to download.
            var canSelf = available
                          && strategy != UpdateStrategy.NotifyOnly
                          && !string.IsNullOrEmpty(assetUrl);

            return new UpdateInfo {
                Current         = NormalizeVersion(currentVersion),
                Latest          = NormalizeVersion(tag),
                UpdateAvailable = available,
                ReleaseUrl      = url,
                Notes           = notes,
                AssetName       = assetName,
                AssetUrl        = assetUrl,
                CanSelfUpdate   = canSelf,
                Strategy        = strategy.ToString(),
            };
        }

        // Release asset filename for a platform/arch, matching what the build
        // scripts (build-companion-*.ps1) and GH Actions workflow attach to each
        // release. Empty string for an unsupported combo (e.g. Linux arm64).
        public static string ResolveAssetName(bool isWindows, bool isLinux, bool isMac, Architecture arch) {
            if (isWindows) {
                return arch == Architecture.X64 ? "NightSummaryCompanion-win-x64.zip" : "";
            }
            if (isMac) {
                return arch == Architecture.Arm64
                    ? "NightSummaryCompanion-mac-arm64.dmg"
                    : "NightSummaryCompanion-mac-x64.dmg";
            }
            if (isLinux) {
                // Self-update path ships the tarball; AppImage/.deb installs fall
                // back to NotifyOnly upstream, so the tarball name is correct here.
                return arch == Architecture.X64 ? "NightSummaryCompanion-linux-x64.tar.gz" : "";
            }
            return "";
        }

        // Decide the update strategy for the running process from OS + packaging.
        // Reads the environment (process path, $APPIMAGE, dir writability); kept
        // separate from BuildFromReleaseJson so the JSON parsing stays pure.
        public static UpdateStrategy DecideStrategy() =>
            DecideStrategy(
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX),
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux),
                Environment.GetEnvironmentVariable("APPIMAGE"),
                System.IO.Path.GetDirectoryName(Environment.ProcessPath),
                DirIsWritable);

        // Pure overload — every input is passed in so all branches (incl. the
        // Linux AppImage / non-writable cases) are unit-testable on any OS.
        public static UpdateStrategy DecideStrategy(
                bool isWindows, bool isMac, bool isLinux,
                string? appImageEnv, string? processDir, Func<string, bool> dirWritable) {
            if (isWindows) return UpdateStrategy.WindowsZipSwap;
            if (isMac)     return UpdateStrategy.MacAppReplace;
            if (isLinux) {
                // AppImage runs from a read-only FUSE mount ($APPIMAGE points at the
                // real .AppImage file, the binary lives under /tmp/.mount_*). Can't
                // swap in place.
                if (!string.IsNullOrEmpty(appImageEnv)) return UpdateStrategy.NotifyOnly;
                // .deb installs under /usr or /opt are root-owned — not writable by
                // the user the companion runs as.
                if (string.IsNullOrEmpty(processDir) || !dirWritable(processDir))
                    return UpdateStrategy.NotifyOnly;
                return UpdateStrategy.LinuxTarballInPlace;
            }
            return UpdateStrategy.NotifyOnly;
        }

        private static bool DirIsWritable(string dir) {
            try {
                var probe = System.IO.Path.Combine(dir, ".ns-update-probe-" + Guid.NewGuid().ToString("N"));
                System.IO.File.WriteAllText(probe, "");
                System.IO.File.Delete(probe);
                return true;
            } catch {
                return false;
            }
        }

        // ── Version comparison ───────────────────────────────────────────────

        // Strip a leading "v"/"V" and any build/pre-release suffix, returning the
        // dotted numeric core (e.g. "v3.2.1-beta+abc" -> "3.2.1"). Empty in →
        // empty out.
        public static string NormalizeVersion(string raw) {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var s = raw.Trim();
            if (s.Length > 0 && (s[0] == 'v' || s[0] == 'V')) s = s.Substring(1);
            // Cut at the first pre-release/build separator.
            var cut = s.IndexOfAny(new[] { '-', '+', ' ' });
            if (cut >= 0) s = s.Substring(0, cut);
            return s;
        }

        private static bool TryParse(string raw, out Version version) {
            version = new Version(0, 0, 0, 0);
            var norm = NormalizeVersion(raw);
            if (norm.Length == 0) return false;
            // System.Version needs at least Major.Minor; pad a bare "3" to "3.0".
            if (!norm.Contains('.')) norm += ".0";
            return Version.TryParse(norm, out version!);
        }

        // True iff `latest` is a strictly higher version than `current`. Any parse
        // failure (blank current on a dev build, weird tag) returns false so we
        // never nag with a bogus "update available".
        public static bool IsNewer(string current, string latest) {
            if (!TryParse(latest, out var l)) return false;
            if (!TryParse(current, out var c)) return false;
            return l > c;
        }
    }
}
