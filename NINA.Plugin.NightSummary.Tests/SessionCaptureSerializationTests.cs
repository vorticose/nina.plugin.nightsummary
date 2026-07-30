using NINA.Core.Model;
using NINA.Image.ImageData;
using NINA.Plugin.SessionCapture.Models;
using NINA.Plugin.SessionCapture.Serialization;
using System;
using System.Text.Json;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    /// <summary>
    /// Regression tests for the nightly "positive and negative infinity cannot be
    /// written as valid JSON" SaveRecording failure. NINA initializes unavailable
    /// metadata (camera temp, telescope altitude, weather, ...) to NaN/Infinity;
    /// recordings must serialize those as null and read them back as NaN.
    /// </summary>
    public class SessionCaptureSerializationTests {

        [Fact]
        public void FreshImageMetaData_WithNaNDefaults_SerializesWithoutThrowing() {
            // A fresh ImageMetaData carries NINA's NaN defaults — this is the exact
            // payload SaveRecording failed on every night.
            var recording = new CaptureRecording {
                RecordedAt = DateTime.Now,
                Events = {
                    new CaptureEvent {
                        Timestamp = DateTime.Now,
                        Type = "ImageSaved",
                        Data = new ImageSavedSnapshot {
                            MetaData = new ImageMetaData(),
                            Duration = 300
                        }
                    }
                }
            };

            var json = JsonSerializer.Serialize(recording, CaptureJson.Options);

            Assert.DoesNotContain("Infinity", json);
            Assert.DoesNotContain("NaN", json);
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void NonFiniteDouble_WritesNull_ReadsBackAsNaN(double value) {
            var json = JsonSerializer.Serialize(new ImageSavedSnapshot { Duration = value }, CaptureJson.Options);
            Assert.Contains("\"Duration\": null", json);

            var back = JsonSerializer.Deserialize<ImageSavedSnapshot>(json, CaptureJson.Options);
            Assert.True(double.IsNaN(back.Duration));
        }

        [Fact]
        public void FiniteDouble_RoundTripsUnchanged() {
            var json = JsonSerializer.Serialize(new ImageSavedSnapshot { Duration = 300.5 }, CaptureJson.Options);
            var back = JsonSerializer.Deserialize<ImageSavedSnapshot>(json, CaptureJson.Options);
            Assert.Equal(300.5, back.Duration);
        }

        [Fact]
        public void RmsWithNaN_SerializesNullAndReadsBackAsNaN() {
            var rms = new RMS { RA = double.NaN, Dec = 0.42, Total = double.PositiveInfinity };

            var json = JsonSerializer.Serialize(rms, CaptureJson.Options);
            var back = JsonSerializer.Deserialize<RMS>(json, CaptureJson.Options);

            Assert.True(double.IsNaN(back.RA));
            Assert.Equal(0.42, back.Dec);
            Assert.True(double.IsNaN(back.Total));
        }

        [Fact]
        public void StarDetectionAnalysisWithNaNHfr_Serializes() {
            var analysis = new StarDetectionAnalysis {
                HFR = double.NaN,
                HFRStDev = double.NaN,
                DetectedStars = 0
            };

            var json = JsonSerializer.Serialize<NINA.Image.Interfaces.IStarDetectionAnalysis>(
                analysis, CaptureJson.Options);

            Assert.Contains("\"HFR\": null", json);
            Assert.Contains("\"DetectedStars\": 0", json);
        }
    }
}
