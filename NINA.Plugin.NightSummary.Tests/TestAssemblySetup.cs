using NINA.Core.Utility;
using NINA.Plugin.NightSummary.Data;
using System;
using System.IO;
using System.Runtime.CompilerServices;
using Xunit;

// Settings.Default is a shared singleton. Running test classes in parallel
// causes races where one class's constructor resets settings mid-test in
// another class. Disable parallelism at the assembly level to prevent this.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace NINA.Plugin.NightSummary.Tests {

    /// <summary>
    /// Process-wide safety floor: redirects SettingsManager.Instance to an isolated,
    /// all-channels-disabled settings object before a single test runs.
    ///
    /// WHY THIS EXISTS AT ASSEMBLY LEVEL. Delivery channels are gated on
    /// SettingsManager.Instance.Current, the static singleton, which by default points
    /// at the host's real %LOCALAPPDATA%\NINA\NightSummary\settings.json. Individual
    /// test classes were each expected to remember SettingsManager.UseInstanceForTesting.
    /// That convention failed twice: SessionServiceOrphanRecoveryTests never called it at
    /// all, and the per-class drain in Dispose is Task.WhenAny(..., Task.Delay(10s)) — it
    /// gives up after ten seconds and releases the override while report generation is
    /// still in flight, which on a machine with sky thumbnails enabled (network fetches)
    /// is easily exceeded. Running the suite on the observatory PC on 2026-08-23 posted
    /// real Discord messages to a live server as a result.
    ///
    /// A per-class convention cannot be made reliable by adding more per-class calls.
    /// This floor makes the unsafe state unreachable instead: because the override slot
    /// restores LIFO, any class-level override nests ABOVE this one and pops back down to
    /// it, never to production. A class that forgets entirely still gets it.
    ///
    /// Bonus: SettingsManager.Instance is `_testOverride ?? _instance.Value` where
    /// _instance is Lazy. Installing this before anything reads Instance means the real
    /// settings.json is never opened by the test process at all, so the host's DPAPI
    /// secrets are never decrypted into it.
    ///
    /// It also makes local runs match CI, where no production settings.json exists and
    /// tests already run against defaults.
    ///
    /// Report HTML writes are gated separately via the SessionService reportsDirectory
    /// constructor seam (see 9d0d882). This initializer also redirects
    /// CoreUtil.APPLICATIONTEMPPATH before NINA.Core.Utility.Logger's static constructor
    /// runs, so the suite cannot drop a NINA-format log into the host's real
    /// %LOCALAPPDATA%\NINA\Logs folder. That leak is how four full-suite runs on the
    /// observatory PC on 2026-08-23 put 855 KB of test-fixture logs into the nightly
    /// rig-log bundle. Logger also DirectoryCleanup's that folder on first use (90-day
    /// retention), so pointing it at production would be a custody hazard, not just
    /// noise. Do not scan or delete production Logs from tests to "fix" leftovers.
    /// </summary>
    internal static class TestSendGuard {

        // Fixed filename rather than a per-run GUID so repeated runs overwrite one temp
        // file instead of accumulating. Never holds secrets — only disabled flags.
        private static readonly string GuardSettingsPath =
            Path.Combine(Path.GetTempPath(), "ns_test_send_guard_settings.json");

        /// <summary>Isolated NINA home (Logs, etc.) for the test process. Exposed so a
        /// test can assert Logger is not writing into the host's real LOCALAPPDATA.</summary>
        internal static string IsolatedNinaHome { get; private set; } = "";

        /// <summary>The isolated manager installed as the floor. Exposed so a test can
        /// assert the floor is actually in place.</summary>
        internal static SettingsManager Installed { get; private set; }

        [ModuleInitializer]
        internal static void InstallDeliveryChannelFloor() {
            // Must run before any Logger.Info/Warning/Error. Logger's static constructor
            // captures CoreUtil.APPLICATIONTEMPPATH once and opens a file there; there is
            // no later seam. SettingsManager.Load below can log, so this comes first.
            IsolatedNinaHome = Path.Combine(Path.GetTempPath(), "ns_test_nina_home");
            Directory.CreateDirectory(Path.Combine(IsolatedNinaHome, "Logs"));
            CoreUtil.APPLICATIONTEMPPATH = IsolatedNinaHome;

            var mgr = new SettingsManager(GuardSettingsPath, attemptMigration: false);
            mgr.Load();
            mgr.Current.EmailEnabled       = false;
            mgr.Current.DiscordEnabled     = false;
            mgr.Current.PushoverEnabled    = false;
            mgr.Current.SaveReportLocally  = false;
            mgr.Save();

            // Intentionally never disposed. This is the bottom of the override stack for
            // the lifetime of the test process; releasing it would re-expose production.
            SettingsManager.UseInstanceForTesting(mgr);
            Installed = mgr;
        }
    }
}
