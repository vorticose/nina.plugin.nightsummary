using NINA.Plugin.NightSummary.Data;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    public class MosaicProjectHelperTests {

        [Theory]
        [InlineData("North America Panel 1", "North America")]
        [InlineData("North America Panel 2", "North America")]
        [InlineData("Sh2-27 Panel 2", "Sh2-27")]
        [InlineData("Sh2-27 P1", "Sh2-27")]
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
    }
}
