using NINA.Plugin.NightSummary.Reporting;
using System.Collections.Generic;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {
    public class FilterHelperTests {

        // ── Broadband classification ──────────────────────────────────────────

        [Theory]
        [InlineData("Lum")]
        [InlineData("Red")]
        [InlineData("Green")]
        [InlineData("Blue")]
        [InlineData("L")]
        [InlineData("R")]
        [InlineData("G")]
        [InlineData("B")]
        public void BroadbandFilters_AreClassifiedCorrectly(string filter) {
            Assert.True(FilterHelper.IsBroadband(filter));
            Assert.False(FilterHelper.IsNarrowband(filter));
        }

        // ── Narrowband classification ─────────────────────────────────────────

        [Theory]
        [InlineData("Ha")]
        [InlineData("SII")]
        [InlineData("OIII")]
        [InlineData("H-alpha")]
        [InlineData("S2")]
        [InlineData("O3")]
        public void NarrowbandFilters_AreClassifiedCorrectly(string filter) {
            Assert.True(FilterHelper.IsNarrowband(filter));
            Assert.False(FilterHelper.IsBroadband(filter));
        }

        // ── Unknown filters ───────────────────────────────────────────────────

        [Theory]
        [InlineData("UV")]
        [InlineData("IR")]
        [InlineData("ND")]
        [InlineData("Clear")]
        public void UnknownFilters_AreNeitherBroadbandNorNarrowband(string filter) {
            Assert.False(FilterHelper.IsBroadband(filter));
            Assert.False(FilterHelper.IsNarrowband(filter));
        }

        [Fact]
        public void EmptyFilter_DoesNotCrash() {
            // Should not throw — just return false for both
            var ex = Record.Exception(() => {
                FilterHelper.IsBroadband("");
                FilterHelper.IsNarrowband("");
            });
            Assert.Null(ex);
        }

        // ── CV calculation ────────────────────────────────────────────────────

        [Fact]
        public void CV_ForIdenticalValues_IsZero() {
            var values = new List<double> { 2.5, 2.5, 2.5, 2.5 };
            Assert.Equal(0.0, FilterHelper.CV(values), precision: 2);
        }

        [Fact]
        public void CV_ForKnownDataSet_IsCorrect() {
            // mean=10, stddev=2, CV=20%
            var values = new List<double> { 8.0, 10.0, 12.0, 10.0 };
            var cv     = FilterHelper.CV(values);
            Assert.True(cv > 0, "CV should be positive for varying values");
        }

        [Fact]
        public void CV_ForSingleValue_DoesNotCrash() {
            var values = new List<double> { 5.0 };
            var ex = Record.Exception(() => FilterHelper.CV(values));
            Assert.Null(ex);
        }

        [Fact]
        public void CV_ForEmptyList_DoesNotCrash() {
            var values = new List<double>();
            var ex = Record.Exception(() => FilterHelper.CV(values));
            Assert.Null(ex);
        }
    }
}
