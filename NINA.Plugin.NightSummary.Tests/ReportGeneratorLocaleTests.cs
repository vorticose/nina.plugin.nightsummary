using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Reporting;
using NINA.Plugin.NightSummary.Tests.Fixtures;
using System;
using System.Globalization;
using System.Threading.Tasks;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    /// <summary>
    /// Regression tests for locale-sensitive numeric formatting in the report.
    /// The report is machine-readable markup (SVG coordinates, HiPS2FITS URLs, data-* attributes)
    /// and must always use '.' as the decimal separator. On a comma-decimal locale (de-DE, fr-FR)
    /// the old code leaked the host's CurrentCulture into :F1/:F2/:F6 interpolations, corrupting
    /// SVG transforms (comma is also the SVG argument separator) and silently breaking thumbnail
    /// URLs. GenerateHtmlReport now forces InvariantCulture for the whole generation flow, so the
    /// Duration probe below — and every other numeric interpolation in the method — stays dotted
    /// regardless of the host locale.
    /// </summary>
    public class ReportGeneratorLocaleTests {

        [Fact]
        public async Task GenerateHtmlReport_UnderCommaDecimalLocale_UsesDotDecimalSeparator() {
            var original = CultureInfo.CurrentCulture;
            try {
                // German uses ',' as the decimal separator — the case that breaks on real users' machines.
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");

                // Keep network-bound / heavy sections off so the test is fast and deterministic;
                // the header's Duration line always renders and lives inside the culture guard.
                SettingsManager.Instance.Current.ReportDetailLevel    = 2;
                SettingsManager.Instance.Current.ShowSkyThumbnails     = false;
                SettingsManager.Instance.Current.ShowAltitudeChart     = false;
                SettingsManager.Instance.Current.ShowNextNightPreview  = false;
                SettingsManager.Instance.Current.ShowEquipmentProfile  = false;

                var gen  = TestDeps.NewReportGenerator();
                var data = TestDataFactory.MakeReportData(imageCount: 5);

                // Deterministic 2.5-hour session so Duration formats as a known fraction.
                data.Session.SessionStart = new DateTime(2026, 1, 15, 22, 0, 0);
                data.Session.SessionEnd   = new DateTime(2026, 1, 16, 0, 30, 0);

                var html = await gen.GenerateHtmlReport(data);

                Assert.Contains("2.5 hours", html);       // invariant '.' separator
                Assert.DoesNotContain("2,5 hours", html);  // the leaked-locale artifact
            } finally {
                CultureInfo.CurrentCulture = original;
            }
        }
    }
}
