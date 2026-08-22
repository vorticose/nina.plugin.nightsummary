using NINA.Plugin.NightSummary.Data;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    public class MosaicProjectHelperTests {

        [Theory]
        [InlineData("North America Panel 1", "North America")]
        [InlineData("North America Panel 2", "North America")]
        [InlineData("Sh2-27 Panel 2", "Sh2-27")]
        [InlineData("Sh2-27 P1", "Sh2-27")]
        [InlineData("Sh2-27", "Sh2-27")]
        [InlineData("NA Nebula_1", "NA Nebula")]
        [InlineData("NA Nebula-12", "NA Nebula")]
        [InlineData("NGC 7000 #2", "NGC 7000")]
        [InlineData("M31", "M31")]
        [InlineData("  M42  ", "M42")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void StripPanelSuffix_common_patterns(string? input, string expected) {
            Assert.Equal(expected, MosaicProjectHelper.StripPanelSuffix(input));
        }

        [Fact]
        public void SuggestName_shared_panel_prefix() {
            var name = MosaicProjectHelper.SuggestName(new[] {
                "North America Panel 1",
                "North America Panel 2",
                "North America Panel 3"
            });
            Assert.Equal("North America", name);
        }

        [Fact]
        public void SuggestName_underscore_panel_numbers() {
            Assert.Equal("Heart Nebula", MosaicProjectHelper.SuggestName(new[] {
                "Heart Nebula_1", "Heart Nebula_2"
            }));
        }

        [Fact]
        public void SuggestName_unrelated_targets_falls_back() {
            Assert.Equal("Mosaic", MosaicProjectHelper.SuggestName(new[] { "M31", "M42" }));
        }

        [Fact]
        public void SuggestName_single_target_strips_suffix() {
            Assert.Equal("North America", MosaicProjectHelper.SuggestName(new[] { "North America Panel 1" }));
        }

        [Fact]
        public void SuggestName_empty_is_Mosaic() {
            Assert.Equal("Mosaic", MosaicProjectHelper.SuggestName(null));
            Assert.Equal("Mosaic", MosaicProjectHelper.SuggestName(System.Array.Empty<string>()));
        }

        [Fact]
        public void EffectiveFovAngle_prefers_plate_solve() {
            Assert.Equal(100.0, MosaicProjectHelper.EffectiveFovAngle(100.0, 45.0, 280.0));
        }

        [Fact]
        public void EffectiveFovAngle_uses_ts_rotation_when_no_plate_solve() {
            Assert.Equal(45.0, MosaicProjectHelper.EffectiveFovAngle(null, 45.0, 280.0));
        }

        [Fact]
        public void EffectiveFovAngle_uses_rotator_when_ts_rotation_is_zero() {
            Assert.Equal(280.5, MosaicProjectHelper.EffectiveFovAngle(null, 0.0, 280.5));
        }

        [Fact]
        public void EffectiveFovAngle_null_when_nothing_known() {
            Assert.Null(MosaicProjectHelper.EffectiveFovAngle(null, 0.0, null));
        }

        [Fact]
        public void CoalesceSiblingAngles_fills_nulls_from_known_panel() {
            var angles = new double?[] { 100.0, null, null, null };
            MosaicProjectHelper.CoalesceSiblingAngles(angles);
            Assert.Equal(new double?[] { 100.0, 100.0, 100.0, 100.0 }, angles);
        }

        [Fact]
        public void CircularMedian_unwraps_across_360() {
            Assert.Equal(10.0, MosaicProjectHelper.CircularMedian(new[] { 350.0, 10.0, 20.0 }), 1);
        }
    }
}
