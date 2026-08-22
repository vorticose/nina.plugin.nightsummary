using NINA.Plugin.NightSummary.Reporting;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {
    /// <summary>
    /// Regression tests for the whitespace-tolerant Target Scheduler name match.
    ///
    /// Target Scheduler writes its DB target name verbatim into image metadata, so the
    /// session (metadata) name and the TS DB name normally agree exactly. A stray leading
    /// or trailing space on the TS DB target name previously produced a false
    /// "target not found in Target Scheduler" warning, because the session name was trimmed
    /// before matching but the DB name was compared untrimmed. TargetNameMatches normalizes
    /// both sides so the surrounding whitespace no longer breaks the match.
    /// </summary>
    public class ReportGeneratorTsNameMatchTests {

        [Theory]
        // exact match (unchanged behaviour)
        [InlineData("M 31 Panel 3", "M 31 Panel 3")]
        // stray trailing space on one side (the reported bug)
        [InlineData("M 31 Panel 3 ", "M 31 Panel 3")]
        [InlineData("M 31 Panel 3", "M 31 Panel 3 ")]
        // stray leading space on one side
        [InlineData(" M 31 Panel 3", "M 31 Panel 3")]
        [InlineData("M 31 Panel 3", " M 31 Panel 3")]
        // whitespace on both sides
        [InlineData("  M 31 Panel 3  ", "M 31 Panel 3")]
        // case-insensitive (existing OrdinalIgnoreCase contract)
        [InlineData("m 31 panel 3 ", "M 31 Panel 3")]
        public void TargetNameMatches_IgnoresSurroundingWhitespace(string dbName, string sessionName) {
            Assert.True(ReportGenerator.TargetNameMatches(dbName, sessionName));
        }

        [Theory]
        // genuinely different names must NOT match
        [InlineData("M 31 Panel 3", "M 31 Panel 4")]
        // internal double-space is a real difference (only trim is applied, not collapse)
        [InlineData("M 31  Panel 3", "M 31 Panel 3")]
        public void TargetNameMatches_DistinctNames_DoNotMatch(string dbName, string sessionName) {
            Assert.False(ReportGenerator.TargetNameMatches(dbName, sessionName));
        }

        [Theory]
        [InlineData(null, "M 31 Panel 3")]
        [InlineData("M 31 Panel 3", null)]
        [InlineData(null, null)]
        public void TargetNameMatches_NullOperands_DoNotThrow(string? dbName, string? sessionName) {
            var ex = Record.Exception(() => ReportGenerator.TargetNameMatches(dbName, sessionName));
            Assert.Null(ex);
        }
    }
}
