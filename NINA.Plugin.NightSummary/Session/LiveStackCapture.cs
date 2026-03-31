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

    /// <summary>
    /// A captured live stack image, stored as compressed JPEG bytes.
    /// </summary>
    public class LiveStackImage {
        public string Target { get; init; }
        public string Filter { get; init; }
        public bool IsMonochrome { get; init; }
        public byte[] JpegData { get; init; }
        public int StackCount { get; init; }
        public int? RedStackCount { get; init; }
        public int? GreenStackCount { get; init; }
        public int? BlueStackCount { get; init; }
    }

    /// <summary>
    /// Subscribes to Live Stack's IMessageBroker broadcasts and captures the latest
    /// stacked image per target+filter as compressed JPEG bytes. No compile-time
    /// dependency on the Live Stack plugin — all content access uses dynamic typing.
    /// </summary>
    public class LiveStackCapture : ISubscriber {
        private const string StackUpdateTopic = "Livestack_LivestackDockable_StackUpdateBroadcast";
        private const int TargetWidthPx = 760;
        private const int JpegQualityHigh = 75;
        private const int JpegQualityLow = 60;
        private const int MaxJpegBytes = 500_000;

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
                var jpeg = ConvertToJpeg(bitmap, JpegQualityHigh);
                if (jpeg.Length > MaxJpegBytes) {
                    Logger.Info($"NightSummary: LiveStack JPEG too large ({jpeg.Length / 1024}KB), re-encoding at quality {JpegQualityLow}");
                    jpeg = ConvertToJpeg(bitmap, JpegQualityLow);
                }
                Logger.Info($"NightSummary: LiveStack JPEG stored — {target}/{filter}, {jpeg.Length / 1024}KB, {stackCount} frames");

                var img = new LiveStackImage {
                    Target = target,
                    Filter = filter,
                    IsMonochrome = isMono,
                    JpegData = jpeg,
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
            Logger.Info($"NightSummary: LiveStackCapture collected {result.Count} image(s) across {result.Select(i => i.Target).Distinct().Count()} target(s)");
            return result;
        }

        private static byte[] ConvertToJpeg(BitmapSource source, int quality) {
            var resized = ResizeToWidth(source, TargetWidthPx);
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
