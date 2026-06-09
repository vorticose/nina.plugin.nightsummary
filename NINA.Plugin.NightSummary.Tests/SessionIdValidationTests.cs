using NINA.Plugin.NightSummary.Server;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    // Guards the path-traversal fix: the segment after /api/sessions/ is the
    // session id, which several handlers interpolate straight into a filesystem
    // path ({id}.html, livestack/{id}, {id}.settings.json — including a write).
    // Only the GUID alphabet is accepted; anything carrying separators or ".."
    // must be rejected so no session-scoped path can escape the reports dir.
    public class SessionIdValidationTests {

        [Theory]
        [InlineData("d94f8c2e-1a2b-4c3d-8e9f-0a1b2c3d4e5f")] // real GUID shape
        [InlineData("ABCDEF0123456789")]
        [InlineData("a")]
        public void Accepts_GuidShapedIds(string id) {
            Assert.True(DashboardServer.IsSafeSessionId(id));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("..")]
        [InlineData("../../etc/passwd")]
        [InlineData("..\\..\\Windows\\system32")]
        [InlineData("foo/bar")]
        [InlineData("foo\\bar")]
        [InlineData("sess.html")]          // a dot would let "{id}.html" double up / climb
        [InlineData("C:")]
        [InlineData("a b")]                 // space — not a GUID char
        [InlineData("%2e%2e")]              // pre-decode form is irrelevant; raw must still fail
        public void Rejects_TraversalAndNonGuidIds(string id) {
            Assert.False(DashboardServer.IsSafeSessionId(id));
        }

        [Fact]
        public void Rejects_OverlongId() {
            Assert.False(DashboardServer.IsSafeSessionId(new string('a', 65)));
        }
    }
}
