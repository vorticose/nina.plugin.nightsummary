using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;

namespace NINA.Plugin.NightSummary.Server {

    // The "stageable" half of the in-app update — everything that downloads,
    // unpacks, and validates the new build WITHOUT replacing the running process.
    // Split out of DashboardServer.Companion.cs so it's unit-testable: these
    // methods never call Environment.Exit, spawn a helper, or touch the live
    // install dir. The process-handoff tail (swap + exit/relaunch) stays in the
    // server and is covered by the platform E2E tests instead.
    public static class UpdateInstaller {

        // Extract a Windows update .zip into destDir and return the path to the
        // new NightSummaryCompanion.exe (zip layout: NightSummaryCompanion/...exe).
        public static string ExtractZipFindExe(string zipPath, string destDir) {
            ZipFile.ExtractToDirectory(zipPath, destDir);
            return Directory.GetFiles(destDir, "NightSummaryCompanion.exe", SearchOption.AllDirectories).FirstOrDefault()
                   ?? throw new FileNotFoundException("NightSummaryCompanion.exe not found in update zip");
        }

        // Extract a Linux update .tar.gz into destDir and return the new binary
        // plus its launcher (tarball layout: NightSummaryCompanion/{-bin, launcher}).
        // launcher is null if the archive omits it (the in-place swap then keeps
        // the existing launcher, which is fine — it only execs -bin).
        public static (string bin, string? launcher) ExtractTarGzFindBin(string tarPath, string destDir) {
            // TarFile.ExtractToDirectory (unlike ZipFile) requires destDir to exist.
            Directory.CreateDirectory(destDir);
            using (var fs = File.OpenRead(tarPath))
            using (var gz = new GZipStream(fs, CompressionMode.Decompress)) {
                TarFile.ExtractToDirectory(gz, destDir, overwriteFiles: true);
            }
            var bin = Directory.GetFiles(destDir, "NightSummaryCompanion-bin", SearchOption.AllDirectories).FirstOrDefault()
                      ?? throw new FileNotFoundException("NightSummaryCompanion-bin not found in update tarball");
            var launcher = Directory.GetFiles(destDir, "NightSummaryCompanion", SearchOption.AllDirectories)
                             .FirstOrDefault(p => !p.EndsWith("-bin", StringComparison.Ordinal));
            return (bin, launcher);
        }

        // Find the expected SHA-256 for assetName in a checksums.txt body. Lines
        // are sha256sum's default "<hash>  <name>" format. Null if not listed.
        public static string? ParseChecksum(string checksumsText, string assetName) {
            if (string.IsNullOrEmpty(checksumsText)) return null;
            foreach (var line in checksumsText.Split('\n')) {
                var parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && string.Equals(parts[1], assetName, StringComparison.OrdinalIgnoreCase)) {
                    return parts[0];
                }
            }
            return null;
        }

        public static string ComputeSha256(string filePath) {
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(filePath);
            return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
        }

        // Verify a downloaded file against a checksums.txt body. Returns true when
        // the hash matches OR there's nothing to check against (no checksums file,
        // or the asset isn't listed — older releases) — `skipped` distinguishes
        // those so the caller can log "verified" vs "skipped". Returns false ONLY
        // on a genuine mismatch, which must abort the swap.
        public static bool VerifyChecksum(string filePath, string? checksumsText, string assetName,
                                          out bool skipped, out string? detail) {
            skipped = false;
            detail = null;
            if (string.IsNullOrEmpty(checksumsText)) {
                skipped = true; detail = "no checksums.txt on release";
                return true;
            }
            var expected = ParseChecksum(checksumsText, assetName);
            if (expected == null) {
                skipped = true; detail = $"{assetName} not in checksums.txt";
                return true;
            }
            var actual = ComputeSha256(filePath);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) {
                detail = $"checksum mismatch for {assetName}: expected {expected}, got {actual}";
                return false;
            }
            detail = "verified";
            return true;
        }
    }
}
