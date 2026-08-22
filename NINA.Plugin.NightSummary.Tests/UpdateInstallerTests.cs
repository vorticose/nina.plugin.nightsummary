using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using NINA.Plugin.NightSummary.Server;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    /// <summary>
    /// Tests for the "stageable" half of the in-app update —
    /// <see cref="UpdateInstaller"/>: archive extraction + binary location, and
    /// checksum parse/verify. These exercise everything the updater does BEFORE
    /// the process handoff (download → unpack → validate). The swap + relaunch
    /// tail is packaging-dependent and covered by the platform E2E tests instead.
    /// </summary>
    public class UpdateInstallerTests : IDisposable {

        private readonly string _work;
        public UpdateInstallerTests() {
            _work = Path.Combine(Path.GetTempPath(), "ns-update-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_work);
        }
        public void Dispose() {
            try { Directory.Delete(_work, recursive: true); } catch { /* best-effort */ }
        }

        // ── Windows zip ─────────────────────────────────────────────────────
        [Fact]
        public void ExtractZipFindExe_finds_the_exe_in_the_nested_layout() {
            // Real release layout: NightSummaryCompanion/NightSummaryCompanion.exe
            var srcRoot = Path.Combine(_work, "src");
            var inner   = Path.Combine(srcRoot, "NightSummaryCompanion");
            Directory.CreateDirectory(inner);
            File.WriteAllText(Path.Combine(inner, "NightSummaryCompanion.exe"), "NEW-EXE-BYTES");
            File.WriteAllText(Path.Combine(inner, "README.txt"), "readme");
            var zip = Path.Combine(_work, "NightSummaryCompanion-win-x64.zip");
            ZipFile.CreateFromDirectory(srcRoot, zip);

            var exe = UpdateInstaller.ExtractZipFindExe(zip, Path.Combine(_work, "extract"));

            Assert.EndsWith("NightSummaryCompanion.exe", exe);
            Assert.Equal("NEW-EXE-BYTES", File.ReadAllText(exe));
        }

        [Fact]
        public void ExtractZipFindExe_throws_when_exe_absent() {
            var srcRoot = Path.Combine(_work, "src");
            Directory.CreateDirectory(srcRoot);
            File.WriteAllText(Path.Combine(srcRoot, "not-the-exe.txt"), "x");
            var zip = Path.Combine(_work, "bad.zip");
            ZipFile.CreateFromDirectory(srcRoot, zip);

            Assert.Throws<FileNotFoundException>(() =>
                UpdateInstaller.ExtractZipFindExe(zip, Path.Combine(_work, "extract")));
        }

        // ── Linux tar.gz ────────────────────────────────────────────────────
        [Fact]
        public void ExtractTarGzFindBin_finds_bin_and_launcher_distinctly() {
            // Real layout: NightSummaryCompanion/{NightSummaryCompanion-bin, NightSummaryCompanion, companion.png}
            var srcRoot = Path.Combine(_work, "NightSummaryCompanion");
            Directory.CreateDirectory(srcRoot);
            File.WriteAllText(Path.Combine(srcRoot, "NightSummaryCompanion-bin"), "BIN-BYTES");
            File.WriteAllText(Path.Combine(srcRoot, "NightSummaryCompanion"), "LAUNCHER-BYTES");
            File.WriteAllText(Path.Combine(srcRoot, "companion.png"), "PNG");
            var tgz = Path.Combine(_work, "NightSummaryCompanion-linux-x64.tar.gz");
            using (var fs = File.Create(tgz))
            using (var gz = new GZipStream(fs, CompressionMode.Compress)) {
                TarFile.CreateFromDirectory(srcRoot, gz, includeBaseDirectory: true);
            }

            var (bin, launcher) = UpdateInstaller.ExtractTarGzFindBin(tgz, Path.Combine(_work, "extract"));

            Assert.EndsWith("NightSummaryCompanion-bin", bin);
            Assert.Equal("BIN-BYTES", File.ReadAllText(bin));
            Assert.NotNull(launcher);
            Assert.False(launcher!.EndsWith("-bin", StringComparison.Ordinal));
            Assert.Equal("LAUNCHER-BYTES", File.ReadAllText(launcher));
        }

        [Fact]
        public void ExtractTarGzFindBin_throws_when_bin_absent() {
            var srcRoot = Path.Combine(_work, "NightSummaryCompanion");
            Directory.CreateDirectory(srcRoot);
            File.WriteAllText(Path.Combine(srcRoot, "companion.png"), "PNG");
            var tgz = Path.Combine(_work, "bad.tar.gz");
            using (var fs = File.Create(tgz))
            using (var gz = new GZipStream(fs, CompressionMode.Compress)) {
                TarFile.CreateFromDirectory(srcRoot, gz, includeBaseDirectory: true);
            }

            Assert.Throws<FileNotFoundException>(() =>
                UpdateInstaller.ExtractTarGzFindBin(tgz, Path.Combine(_work, "extract")));
        }

        // ── Checksums ───────────────────────────────────────────────────────
        [Fact]
        public void ParseChecksum_finds_asset_case_insensitively() {
            var text = "aaaa  OtherFile.zip\n" +
                       "bbbbcccc  NightSummaryCompanion-win-x64.zip\n";
            Assert.Equal("bbbbcccc", UpdateInstaller.ParseChecksum(text, "nightsummarycompanion-win-x64.ZIP"));
            Assert.Null(UpdateInstaller.ParseChecksum(text, "NotListed.dmg"));
            Assert.Null(UpdateInstaller.ParseChecksum("", "anything"));
        }

        [Fact]
        public void VerifyChecksum_passes_on_match() {
            var file = Path.Combine(_work, "asset.bin");
            File.WriteAllText(file, "the-real-bytes");
            var hash = UpdateInstaller.ComputeSha256(file);
            var checks = $"{hash}  asset.bin\n0000  other";

            var ok = UpdateInstaller.VerifyChecksum(file, checks, "asset.bin", out var skipped, out _);

            Assert.True(ok);
            Assert.False(skipped);
        }

        [Fact]
        public void VerifyChecksum_fails_on_mismatch() {
            var file = Path.Combine(_work, "asset.bin");
            File.WriteAllText(file, "the-real-bytes");
            var wrong = new string('0', 64);

            var ok = UpdateInstaller.VerifyChecksum(file, $"{wrong}  asset.bin", "asset.bin", out var skipped, out var detail);

            Assert.False(ok);
            Assert.False(skipped);
            Assert.Contains("mismatch", detail);
        }

        [Theory]
        [InlineData(null)]              // no checksums.txt on the release (older builds)
        [InlineData("abcd  other.zip")] // asset not listed
        public void VerifyChecksum_skips_gracefully_when_nothing_to_check(string? checksumsText) {
            var file = Path.Combine(_work, "asset.bin");
            File.WriteAllText(file, "bytes");

            var ok = UpdateInstaller.VerifyChecksum(file, checksumsText, "asset.bin", out var skipped, out _);

            Assert.True(ok);       // skipping is not a failure — HTTPS is the fallback trust
            Assert.True(skipped);
        }
    }
}
