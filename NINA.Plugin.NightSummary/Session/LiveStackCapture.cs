using NINA.Core.Utility;
using NINA.Plugin.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NINA.Plugin.NightSummary.Session {

    // LiveStackImage DTO lives in NINA.Plugin.NightSummary.Dashboard/Data/LiveStackImage.cs
    // so the cross-platform ReportGenerator can reference it without WPF deps.
    // Namespace kept identical so this file's references work unchanged.

    /// <summary>
    /// Subscribes to Live Stack's IMessageBroker broadcasts and captures the latest
    /// stacked image per target+filter as compressed JPEG bytes. No compile-time
    /// dependency on the Live Stack plugin — all content access uses dynamic typing.
    /// </summary>
    public class LiveStackCapture : ISubscriber {
        private const string StackUpdateTopic = "Livestack_LivestackDockable_StackUpdateBroadcast";
        private const int ReportWidthPx = 760;
        private const int MasterWidthPx = 2000;
        private const int ReportJpegQualityHigh = 75;
        private const int ReportJpegQualityLow = 60;
        private const int MasterJpegQuality = 90;
        private const int MaxReportJpegBytes = 500_000;

        private readonly IMessageBroker broker;
        private readonly ConcurrentDictionary<(string targetUpper, string filter), LiveStackImage> images = new();

        public LiveStackCapture(IMessageBroker broker) {
            this.broker = broker;
            broker.Subscribe(StackUpdateTopic, this);
            Logger.Info("NightSummary: LiveStackCapture subscribed to broadcast topic");
        }

        public Task OnMessageReceived(IMessage message) {
            try {
                Logger.Info($"NightSummary: LiveStack broadcast received — Topic={message.Topic}, ContentType={message.Content?.GetType().FullName ?? "null"}");

                dynamic content = message.Content;
                string target = content.Target;
                string filter = content.Filter;
                bool isMono = content.IsMonochrome;
                BitmapSource bitmap = content.Image;

                Logger.Info($"NightSummary: LiveStack broadcast — Target={target}, Filter={filter}, IsMono={isMono}, HasImage={bitmap != null}");

                if (bitmap == null || string.IsNullOrEmpty(target)) {
                    Logger.Warning($"NightSummary: LiveStack broadcast skipped — bitmap={bitmap != null}, target='{target}'");
                    return Task.CompletedTask;
                }

                int stackCount = 0;
                int? redCount = null, greenCount = null, blueCount = null;

                if (isMono) {
                    stackCount = (int)(content.StackCount ?? 0);
                } else {
                    redCount = (int?)content.RedStackCount;
                    greenCount = (int?)content.GreenStackCount;
                    blueCount = (int?)content.BlueStackCount;
                    stackCount = (redCount ?? 0) + (greenCount ?? 0) + (blueCount ?? 0);
                }

                Logger.Info($"NightSummary: LiveStack converting to JPEG — {bitmap.PixelWidth}x{bitmap.PixelHeight}, StackCount={stackCount}");

                // Report-embed version (760px, q75)
                var jpeg = ConvertToJpeg(bitmap, ReportWidthPx, ReportJpegQualityHigh);
                if (jpeg.Length > MaxReportJpegBytes) {
                    Logger.Info($"NightSummary: LiveStack report JPEG too large ({jpeg.Length / 1024}KB), re-encoding at quality {ReportJpegQualityLow}");
                    jpeg = ConvertToJpeg(bitmap, ReportWidthPx, ReportJpegQualityLow);
                }

                // Master archive version (2000px, q90)
                var master = ConvertToJpeg(bitmap, MasterWidthPx, MasterJpegQuality);
                Logger.Info($"NightSummary: LiveStack JPEG stored — {target}/{filter}, report={jpeg.Length / 1024}KB, master={master.Length / 1024}KB, {stackCount} frames");

                var img = new LiveStackImage {
                    Target = target,
                    Filter = filter,
                    IsMonochrome = isMono,
                    JpegData = jpeg,
                    MasterJpegData = master,
                    StackCount = stackCount,
                    RedStackCount = redCount,
                    GreenStackCount = greenCount,
                    BlueStackCount = blueCount
                };

                images[(target.ToUpperInvariant(), filter)] = img;

            } catch (Exception ex) {
                Logger.Warning($"NightSummary: Failed to process Live Stack broadcast: {ex.GetType().Name}: {ex.Message}");
                Logger.Warning($"NightSummary: Live Stack broadcast stack trace: {ex.StackTrace}");
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// Unsubscribes from the broker and returns all captured images.
        /// </summary>
        public List<LiveStackImage> StopAndCollect() {
            broker.Unsubscribe(StackUpdateTopic, this);
            var result = images.Values.ToList();
            if (result.Count > 0) {
                var targets = result.Select(i => $"{i.Target}/{i.Filter}").Distinct();
                Logger.Info($"NightSummary: LiveStackCapture collected {result.Count} image(s): {string.Join(", ", targets)}");
            } else {
                Logger.Warning("NightSummary: LiveStackCapture collected 0 images — Live Stack plugin may not be installed or no stacks were broadcast during the session");
            }
            return result;
        }

        /// <summary>
        /// Re-scales a master JPEG (from disk) down to report-embed resolution.
        /// </summary>
        public static byte[] ScaleJpegForReport(byte[] masterJpeg) {
            using var input = new MemoryStream(masterJpeg);
            var decoder = new JpegBitmapDecoder(input, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames[0];

            if (frame.PixelWidth <= ReportWidthPx) return masterJpeg;

            var resized = ResizeToWidth(frame, ReportWidthPx);
            var encoder = new JpegBitmapEncoder { QualityLevel = ReportJpegQualityHigh };
            encoder.Frames.Add(BitmapFrame.Create(resized));

            using var output = new MemoryStream();
            encoder.Save(output);
            return output.ToArray();
        }

        private static byte[] ConvertToJpeg(BitmapSource source, int targetWidth, int quality) {
            var resized = ResizeToWidth(source, targetWidth);
            var encoder = new JpegBitmapEncoder { QualityLevel = quality };
            encoder.Frames.Add(BitmapFrame.Create(resized));

            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }

        private static BitmapSource ResizeToWidth(BitmapSource source, int targetWidth) {
            if (source.PixelWidth <= targetWidth) return source;

            double scale = (double)targetWidth / source.PixelWidth;
            var transformed = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            transformed.Freeze();
            return transformed;
        }
    }
}
