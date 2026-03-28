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

        // ── None filter (filterless camera) ───────────────────────────────────

        [Fact]
        public void NoneFilter_IsClassifiedAsBroadband() {
            Assert.True(FilterHelper.IsBroadband("None"));
            Assert.False(FilterHelper.IsNarrowband("None"));
        }

        [Fact]
        public void NoneFilter_CaseInsensitive_IsClassifiedAsBroadband() {
            Assert.True(FilterHelper.IsBroadband("none"));
            Assert.True(FilterHelper.IsBroadband("NONE"));
        }

        // ── IsExcluded ────────────────────────────────────────────────────────

        [Fact]
        public void IsExcluded_NullOrEmpty_ReturnsFalse() {
            Assert.False(FilterHelper.IsExcluded(""));
            Assert.False(FilterHelper.IsExcluded(null));
        }

        [Fact]
        public void IsExcluded_UnknownFilter_ReturnsFalse() {
            Assert.False(FilterHelper.IsExcluded("Ha"));
        }

        // ── ParseClassifications ──────────────────────────────────────────────

        [Fact]
        public void ParseClassifications_EmptyString_ReturnsEmptyDict() {
            var result = FilterHelper.ParseClassifications("");
            Assert.Empty(result);
        }

        [Fact]
        public void ParseClassifications_NullString_ReturnsEmptyDict() {
            var result = FilterHelper.ParseClassifications(null);
            Assert.Empty(result);
        }

        [Fact]
        public void ParseClassifications_ValidPairs_ParsesCorrectly() {
            var result = FilterHelper.ParseClassifications("Ha=N,Lum=B,UV=X");
            Assert.Equal("N", result["Ha"]);
            Assert.Equal("B", result["Lum"]);
            Assert.Equal("X", result["UV"]);
        }

        [Fact]
        public void ParseClassifications_IsCaseInsensitiveOnKey() {
            var result = FilterHelper.ParseClassifications("ha=N");
            Assert.True(result.ContainsKey("Ha"));
        }

        [Fact]
        public void ParseClassifications_MalformedEntry_IsSkipped() {
            var result = FilterHelper.ParseClassifications("Ha=N,badentry,Lum=B");
            Assert.Equal(2, result.Count);
        }

        // ── SortKey ───────────────────────────────────────────────────────────

        [Fact]
        public void SortKey_None_SortsBeforeAll() {
            Assert.True(FilterHelper.SortKey("None") < FilterHelper.SortKey("L"));
        }

        [Fact]
        public void SortKey_BroadbandBeforeNarrowband() {
            Assert.True(FilterHelper.SortKey("L") < FilterHelper.SortKey("Ha"));
            Assert.True(FilterHelper.SortKey("B") < FilterHelper.SortKey("SII"));
        }

        [Fact]
        public void SortKey_UnknownFilter_SortsLast() {
            Assert.Equal(int.MaxValue, FilterHelper.SortKey("UV"));
        }

        [Fact]
        public void SortKey_EmptyFilter_SortsLast() {
            Assert.Equal(int.MaxValue, FilterHelper.SortKey(""));
        }

        // ── StdDev ────────────────────────────────────────────────────────────

        [Fact]
        public void StdDev_KnownValues_IsCorrect() {
            // Values: 2, 4, 4, 4, 5, 5, 7, 9 → stddev = 2.0
            var values = new List<double> { 2, 4, 4, 4, 5, 5, 7, 9 };
            var result = FilterHelper.StdDev(values);
            Assert.Equal(2.0, result, precision: 1);
        }

        [Fact]
        public void StdDev_IdenticalValues_IsZero() {
            var values = new List<double> { 5, 5, 5, 5 };
            Assert.Equal(0.0, FilterHelper.StdDev(values));
        }

        [Fact]
        public void StdDev_SingleValue_IsZero() {
            var values = new List<double> { 42.0 };
            Assert.Equal(0.0, FilterHelper.StdDev(values));
        }
    }
}
