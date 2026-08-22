using System;
using System.IO;
using SkiaSharp;

namespace NINA.Plugin.NightSummary.Reporting;

// Cross-platform JPEG rescaling for the companion's livestack embed path.
// Plugin side uses WPF's JpegBitmapEncoder (LiveStackCapture.ScaleJpegForReport)
// which only exists on Windows + WPF. Companion needs the same behavior on
// macOS/Linux to keep regenerated HTML the same size as primary's instead
// of embedding 2000px masters as base64 (≈4× HTML inflation).
//
// SkiaSharp matches the plugin's defaults: 760px width, q75. Returns the
// original bytes unchanged when the input is already at or below the target
// width — no point re-encoding at smaller quality.
public static class JpegRescaler {

    public const int ReportWidthPx = 760;
    public const int ReportQuality = 75;

    public static byte[] ScaleForReport(byte[] masterJpeg) {
        if (masterJpeg == null || masterJpeg.Length == 0) return masterJpeg ?? Array.Empty<byte>();

        try {
            using var input = SKBitmap.Decode(masterJpeg);
            if (input == null) return masterJpeg;  // decode failed — fall back to master
            if (input.Width <= ReportWidthPx) return masterJpeg;

            int targetW = ReportWidthPx;
            int targetH = (int)Math.Round(input.Height * (double)targetW / input.Width);

            var info = new SKImageInfo(targetW, targetH, SKColorType.Rgba8888, SKAlphaType.Opaque);
            using var resized = input.Resize(info, SKFilterQuality.High);
            if (resized == null) return masterJpeg;

            using var image = SKImage.FromBitmap(resized);
            using var data  = image.Encode(SKEncodedImageFormat.Jpeg, ReportQuality);
            return data.ToArray();
        } catch {
            // Any decode/encode failure: hand back the master. The HTML grows
            // a bit but the report still renders. Bug-fixing the failure case
            // would only matter for an exotic JPEG variant Skia can't parse.
            return masterJpeg;
        }
    }
}
