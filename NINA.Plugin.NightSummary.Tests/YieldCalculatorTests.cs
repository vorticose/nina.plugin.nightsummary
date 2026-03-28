using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Reporting;
using System;
using System.Collections.Generic;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {
    public class YieldCalculatorTests {

        private static readonly DateTime SessionStart = new DateTime(2025, 1, 15, 21, 0, 0);
        private static readonly DateTime SessionEnd   = new DateTime(2025, 1, 16,  3, 0, 0);

        // Helper: 3 images at 22:00 / 22:30 / 23:00, each 300s
        // Window = 60min = 3600s, total exposure = 900s, base yield = 25%
        private static List<ImageRecord> ThreeImages(string sid) => new List<ImageRecord> {
            Img(sid, new DateTime(2025, 1, 15, 22,  0, 0)),
            Img(sid, new DateTime(2025, 1, 15, 22, 30, 0)),
            Img(sid, new DateTime(2025, 1, 15, 23,  0, 0))
        };

        private static ImageRecord Img(string sid, DateTime ts, double exp = 300) =>
            new ImageRecord {
                SessionId        = sid,
                Timestamp        = ts,
                ExposureDuration = exp,
                ImageType        = "LIGHT",
                TargetName       = "M31",
                Filter           = "Ha"
            };

        private static SessionEvent Evt(string sid, string type, DateTime ts) =>
            new SessionEvent {
                SessionId   = sid,
                EventType   = type,
                Timestamp   = ts,
                Description = ""
            };

        // ── No images ───────────────────────────────────────────────────────

        [Fact]
        public void NoImages_ReturnsZeroYield() {
            var result = YieldCalculator.Calculate(new List<ImageRecord>(), new List<SessionEvent>(), SessionStart, SessionEnd);
            Assert.Equal(0, result.YieldPct);
        }

        [Fact]
        public void NoImages_HasSafetyMonitorIsFalse() {
            var result = YieldCalculator.Calculate(new List<ImageRecord>(), new List<SessionEvent>(), SessionStart, SessionEnd);
            Assert.False(result.HasSafetyMonitor);
        }

        // ── Null / empty events ─────────────────────────────────────────────

        [Fact]
        public void NullEvents_DoesNotThrow() {
            var sid    = Guid.NewGuid().ToString();
            var result = YieldCalculator.Calculate(ThreeImages(sid), null!, SessionStart, SessionEnd);
            Assert.True(result.YieldPct > 0);
        }

        // ── HasSafetyMonitor flag ───────────────────────────────────────────

        [Fact]
        public void NoRoofEvents_HasSafetyMonitorIsFalse() {
            var sid    = Guid.NewGuid().ToString();
            var events = new List<SessionEvent> { Evt(sid, "AutoFocus", new DateTime(2025, 1, 15, 22, 30, 0)) };
            var result = YieldCalculator.Calculate(ThreeImages(sid), events, SessionStart, SessionEnd);
            Assert.False(result.HasSafetyMonitor);
        }

        [Fact]
        public void RoofEvents_HasSafetyMonitorIsTrue() {
            var sid    = Guid.NewGuid().ToString();
            var events = new List<SessionEvent> {
                Evt(sid, "RoofClosed", new DateTime(2025, 1, 15, 22, 15, 0)),
                Evt(sid, "RoofOpen",   new DateTime(2025, 1, 15, 22, 30, 0))
            };
            var result = YieldCalculator.Calculate(ThreeImages(sid), events, SessionStart, SessionEnd);
            Assert.True(result.HasSafetyMonitor);
        }

        // ── Basic yield calculation ─────────────────────────────────────────

        [Fact]
        public void NoEvents_YieldCalculatedCorrectly() {
            // window=3600s, exposure=900s → yield=25%
            var sid    = Guid.NewGuid().ToString();
            var result = YieldCalculator.Calculate(ThreeImages(sid), new List<SessionEvent>(), SessionStart, SessionEnd);
            Assert.InRange(result.YieldPct, 24.9, 25.1);
        }

        [Fact]
        public void SingleImage_WindowIsZero_ReturnsZeroYield() {
            // firstImage == lastImage → window=0 → effectiveWindow=0 → yield=0
            var sid    = Guid.NewGuid().ToString();
            var images = new List<ImageRecord> { Img(sid, new DateTime(2025, 1, 15, 22, 0, 0)) };
            var result = YieldCalculator.Calculate(images, new List<SessionEvent>(), SessionStart, SessionEnd);
            Assert.Equal(0, result.YieldPct);
        }

        [Fact]
        public void YieldCappedAt100Percent() {
            // 2 images 1 minute apart, each 300s → exposure > window → capped at 100%
            var sid    = Guid.NewGuid().ToString();
            var images = new List<ImageRecord> {
                Img(sid, new DateTime(2025, 1, 15, 22, 0, 0)),
                Img(sid, new DateTime(2025, 1, 15, 22, 1, 0))
            };
            var result = YieldCalculator.Calculate(images, new List<SessionEvent>(), SessionStart, SessionEnd);
            Assert.Equal(100.0, result.YieldPct);
        }

        // ── Roof closed/open deduction ──────────────────────────────────────

        [Fact]
        public void MatchedRoofCycle_ReducesEffectiveWindow() {
            // Closed 22:15, open 22:45 → 30min deducted → effective=1800s
            // yield = 900/1800 * 100 = 50%
            var sid    = Guid.NewGuid().ToString();
            var events = new List<SessionEvent> {
                Evt(sid, "RoofClosed", new DateTime(2025, 1, 15, 22, 15, 0)),
                Evt(sid, "RoofOpen",   new DateTime(2025, 1, 15, 22, 45, 0))
            };
            var result = YieldCalculator.Calculate(ThreeImages(sid), events, SessionStart, SessionEnd);
            Assert.InRange(result.YieldPct, 49.9, 50.1);
        }

        [Fact]
        public void MultipleCycles_AllDeducted() {
            // Cycle 1: closed 22:10, open 22:20 (10min); Cycle 2: closed 22:40, open 22:50 (10min)
            // Total deducted=1200s, effective=2400s, yield=900/2400*100=37.5%
            var sid    = Guid.NewGuid().ToString();
            var events = new List<SessionEvent> {
                Evt(sid, "RoofClosed", new DateTime(2025, 1, 15, 22, 10, 0)),
                Evt(sid, "RoofOpen",   new DateTime(2025, 1, 15, 22, 20, 0)),
                Evt(sid, "RoofClosed", new DateTime(2025, 1, 15, 22, 40, 0)),
                Evt(sid, "RoofOpen",   new DateTime(2025, 1, 15, 22, 50, 0))
            };
            var result = YieldCalculator.Calculate(ThreeImages(sid), events, SessionStart, SessionEnd);
            Assert.InRange(result.YieldPct, 37.0, 38.0);
        }

        // ── Overlap clamping ────────────────────────────────────────────────

        [Fact]
        public void RoofClosedBeforeFirstImage_OverlapClamped() {
            // Roof closed 21:00 (before first image 22:00), open 22:30
            // overlapStart clamped to firstImage=22:00 → deducted=30min=1800s → yield=50%
            var sid    = Guid.NewGuid().ToString();
            var events = new List<SessionEvent> {
                Evt(sid, "RoofClosed", new DateTime(2025, 1, 15, 21,  0, 0)),
                Evt(sid, "RoofOpen",   new DateTime(2025, 1, 15, 22, 30, 0))
            };
            var result = YieldCalculator.Calculate(ThreeImages(sid), events, SessionStart, SessionEnd);
            Assert.InRange(result.YieldPct, 49.9, 50.1);
        }

        [Fact]
        public void RoofOpenAfterLastImage_OverlapClamped() {
            // Roof closed 22:30, open 00:00 (after lastImage 23:00)
            // overlapEnd clamped to lastImage=23:00 → deducted=30min=1800s → yield=50%
            var sid    = Guid.NewGuid().ToString();
            var events = new List<SessionEvent> {
                Evt(sid, "RoofClosed", new DateTime(2025, 1, 15, 22, 30, 0)),
                Evt(sid, "RoofOpen",   new DateTime(2025, 1, 16,  0,  0, 0))
            };
            var result = YieldCalculator.Calculate(ThreeImages(sid), events, SessionStart, SessionEnd);
            Assert.InRange(result.YieldPct, 49.9, 50.1);
        }

        [Fact]
        public void RoofClosedAfterLastImage_NotDeducted() {
            // Roof closed after last image → closedAt.Value < lastImage check fails → no deduction
            var sid    = Guid.NewGuid().ToString();
            var events = new List<SessionEvent> {
                Evt(sid, "RoofClosed", new DateTime(2025, 1, 15, 23, 30, 0))
            };
            var result = YieldCalculator.Calculate(ThreeImages(sid), events, SessionStart, SessionEnd);
            Assert.InRange(result.YieldPct, 24.9, 25.1);
        }

        // ── Edge cases ──────────────────────────────────────────────────────

        [Fact]
        public void RoofOpenWithoutPrecedingClose_IsIgnored() {
            // RoofOpen with no preceding RoofClosed → closedAt is null → no deduction
            var sid    = Guid.NewGuid().ToString();
            var events = new List<SessionEvent> {
                Evt(sid, "RoofOpen", new DateTime(2025, 1, 15, 22, 30, 0))
            };
            var result = YieldCalculator.Calculate(ThreeImages(sid), events, SessionStart, SessionEnd);
            Assert.InRange(result.YieldPct, 24.9, 25.1);
        }

        [Fact]
        public void RoofClosedNoMatchingOpen_DeductsToLastImage() {
            // Closed 22:30, no RoofOpen → deducts (lastImage 23:00 - closedAt 22:30) = 30min → yield=50%
            var sid    = Guid.NewGuid().ToString();
            var events = new List<SessionEvent> {
                Evt(sid, "RoofClosed", new DateTime(2025, 1, 15, 22, 30, 0))
            };
            var result = YieldCalculator.Calculate(ThreeImages(sid), events, SessionStart, SessionEnd);
            Assert.InRange(result.YieldPct, 49.9, 50.1);
        }
    }
}
