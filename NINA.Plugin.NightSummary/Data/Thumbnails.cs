using NINA.Core.Utility;
using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NINA.Plugin.NightSummary.Data {

    /// <summary>
    /// JPEG thumbnail encoder for raw image captures. Cribbed from Target Scheduler's
    /// <c>Thumbnails.cs</c> (which itself is from Lightbucket) — battle-tested in
    /// production for years. Inline encode on the save thread is fine; ~5–15 ms for
    /// 192px output.
    ///
    /// Companion to the raw-thumbnails feature: see RAW_THUMBNAILS_DESIGN.md.
    /// </summary>
    public static class Thumbnails {

        /// <summary>Bitmask: small thumbnail (192px tall) present.</summary>
        public const int VersionSmall  = 1;

        /// <summary>Bitmask: medium thumbnail (800px tall) present.</summary>
        public const int VersionMedium = 2;

        /// <summary>Default small thumbnail height.</summary>
        public const int SmallHeightPx  = 192;

        /// <summary>Default medium thumbnail height.</summary>
        public const int MediumHeightPx = 800;

        /// <summary>Default JPEG quality (0–100).</summary>
        public const int DefaultQuality = 85;

        /// <summary>
        /// Encodes <paramref name="src"/> as a scaled JPEG. Returns the encoded bytes
        /// plus the resulting width/height. Returns (0,0,null) on any failure.
        /// </summary>
        public static (int width, int height, byte[] data) Encode(BitmapSource src, int targetHeightPx, int quality = DefaultQuality) {
            if (src == null || targetHeightPx <= 0) return (0, 0, null);
            try {
                double scale = (double)targetHeightPx / src.Height;
                BitmapSource resized = new TransformedBitmap(src, new ScaleTransform(scale, scale));
                int w = (int)resized.Width;
                int h = (int)resized.Height;

                var encoder = new JpegBitmapEncoder { QualityLevel = quality };
                encoder.Frames.Add(BitmapFrame.Create(resized));

                using (var ms = new MemoryStream()) {
                    encoder.Save(ms);
                    return (w, h, ms.ToArray());
                }
            } catch (Exception ex) {
                Logger.Warning($"NightSummary: thumbnail encode failed (h={targetHeightPx}): {ex.Message}");
                return (0, 0, null);
            }
        }

        /// <summary>
        /// Resolves the active thumbnails storage root. Returns <paramref name="custom"/>
        /// when non-empty (after trimming/expanding env vars), otherwise the default
        /// <c>%LOCALAPPDATA%\NINA\NightSummary\thumbs</c>. Single source of truth so
        /// the importer, capture path, retention sweep, and dashboard server all agree.
        /// </summary>
        public static string GetThumbnailsRoot(string custom) {
            if (!string.IsNullOrWhiteSpace(custom)) {
                return Environment.ExpandEnvironmentVariables(custom.Trim());
            }
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NINA", "NightSummary", "thumbs");
        }

        /// <summary>
        /// Computes the on-disk path for a thumbnail under the NightSummary thumbs root.
        /// Layout: <c>{thumbsRoot}/{sessionId}/{imageId}_sm.jpg</c> (or <c>_md.jpg</c>).
        /// </summary>
        public static string GetThumbnailPath(string thumbsRoot, string sessionId, long imageId, int versionFlag) {
            var suffix = versionFlag == VersionMedium ? "_md.jpg" : "_sm.jpg";
            return Path.Combine(thumbsRoot, sessionId, imageId + suffix);
        }

        /// <summary>
        /// Writes JPEG bytes to <paramref name="path"/>, creating parent directories.
        /// Returns true on success. Catches and logs any IO failure (disk full, perm
        /// denied, etc.) — capture is best-effort, not pipeline-blocking.
        /// </summary>
        public static bool WriteToDisk(string path, byte[] data) {
            if (string.IsNullOrEmpty(path) || data == null || data.Length == 0) return false;
            try {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(path, data);
                return true;
            } catch (Exception ex) {
                Logger.Warning($"NightSummary: thumbnail write failed ({path}): {ex.Message}");
                return false;
            }
        }
    }
}
