using NINA.Plugin.NightSummary.Reporting;
using System;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {
    public class MoonPhaseTests {

        [Fact]
        public void Format_AtReferenceNewMoon_IsExactlyZeroPercentWaxing() {
            // At the exact reference new moon the formula is 0, so Format is
            // deterministically "0% ↑". The 0/1% band is only for arbitrary dates.
            var newMoon = new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);
            Assert.Equal("0% \u2191", MoonPhase.Format(newMoon));
        }

        [Fact]
        public void Format_AtApproxFullMoon_IsNear100PercentWaning() {
            var fullMoon = new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc)
                               .AddDays(14.77);
            var s = MoonPhase.Format(fullMoon);
            Assert.EndsWith(" \u2193", s);
            var pct = int.Parse(s.Split('%')[0]);
            Assert.InRange(pct, 95, 100);
        }

        [Fact]
        public void Format_UsesInvariantCulturePercent() {
            var newMoon = new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);
            var s = MoonPhase.Format(newMoon);
            Assert.DoesNotContain(",", s);
            Assert.Matches(@"^\d+% [↑↓]$", s);
        }
    }
}
