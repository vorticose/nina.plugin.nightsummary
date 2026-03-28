using NINA.Plugin.NightSummary.Data;
using System;
using System.Collections.Generic;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {
    /// <summary>
    /// Tests for TargetSchedulerDatabase using a non-existent path so
    /// IsAvailable returns false and the early-exit branches are exercised
    /// without requiring a real Target Scheduler installation.
    /// </summary>
    public class TargetSchedulerDatabaseTests {

        private static readonly string NonExistentPath =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ns_ts_test_{Guid.NewGuid():N}.sqlite");

        // ── IsAvailable ───────────────────────────────────────────────────────

        [Fact]
        public void IsAvailable_NonExistentPath_ReturnsFalse() {
            var db = new TargetSchedulerDatabase(NonExistentPath);
            Assert.False(db.IsAvailable);
        }

        // ── GetProgressForTargets early exit ──────────────────────────────────

        [Fact]
        public void GetProgressForTargets_NotAvailable_ReturnsEmptyList() {
            var db     = new TargetSchedulerDatabase(NonExistentPath);
            var result = db.GetProgressForTargets(new[] { "M31" });
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        [Fact]
        public void GetProgressForTargets_NotAvailable_WithProfileId_ReturnsEmptyList() {
            var db     = new TargetSchedulerDatabase(NonExistentPath);
            var result = db.GetProgressForTargets(new[] { "M31" }, profileId: "test-profile");
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        // ── GetAcquiredImagesForDateRange early exit ──────────────────────────

        [Fact]
        public void GetAcquiredImagesForDateRange_NotAvailable_ReturnsEmptyList() {
            var db     = new TargetSchedulerDatabase(NonExistentPath);
            var result = db.GetAcquiredImagesForDateRange(
                new DateTime(2025, 1, 1), new DateTime(2025, 1, 31));
            Assert.NotNull(result);
            Assert.Empty(result);
        }

        // ── GetApiSettings early exit ─────────────────────────────────────────

        [Fact]
        public void GetApiSettings_NotAvailable_ReturnsFalseAndZeroPort() {
            var db          = new TargetSchedulerDatabase(NonExistentPath);
            var (enabled, port) = db.GetApiSettings();
            Assert.False(enabled);
            Assert.Equal(0, port);
        }

        [Fact]
        public void GetApiSettings_NotAvailable_WithProfileId_ReturnsFalseAndZeroPort() {
            var db          = new TargetSchedulerDatabase(NonExistentPath);
            var (enabled, port) = db.GetApiSettings(profileId: "some-profile");
            Assert.False(enabled);
            Assert.Equal(0, port);
        }

        // ── IsPluginInstalled ─────────────────────────────────────────────────

        [Fact]
        public void IsPluginInstalled_ReturnsBoolean_WithoutThrowing() {
            // Just verifies the property does not throw regardless of whether
            // the NINA plugins folder exists on this machine.
            var ex = Record.Exception(() => _ = TargetSchedulerDatabase.IsPluginInstalled);
            Assert.Null(ex);
        }
    }
}
