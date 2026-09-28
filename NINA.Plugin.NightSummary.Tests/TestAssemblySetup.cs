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
    /// This deliberately does NOT gate report FILE writes: SessionService.
    /// SaveReportForDashboardAsync hardcodes the production reports directory and
    /// consults no setting, so running the suite still litters
    /// %LOCALAPPDATA%\NINA\NightSummary\reports. Fixing that needs an injection point on
    /// SessionService and is tracked separately.
    /// </summary>
    internal static class TestSendGuard {

        // Fixed filename rather than a per-run GUID so repeated runs overwrite one temp
        // file instead of accumulating. Never holds secrets — only disabled flags.
        private static readonly string GuardSettingsPath =
            Path.Combine(Path.GetTempPath(), "ns_test_send_guard_settings.json");

        /// <summary>The isolated manager installed as the floor. Exposed so a test can
        /// assert the floor is actually in place.</summary>
        internal static SettingsManager Installed { get; private set; }

        [ModuleInitializer]
        internal static void InstallDeliveryChannelFloor() {
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
