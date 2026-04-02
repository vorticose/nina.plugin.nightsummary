using NINA.Astrometry;
using NINA.Core.Model;
using NINA.Image.Interfaces;
using OxyPlot;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NINA.Plugin.SessionCapture.Serialization {

    /// <summary>
    /// Serializes Coordinates as {ra, dec, epoch}. Coordinates has no parameterless
    /// constructor so System.Text.Json can't handle it natively.
    /// </summary>
    public class CoordinatesConverter : JsonConverter<Coordinates> {
        public override Coordinates Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            var ra = root.GetProperty("RA").GetDouble();
            var dec = root.GetProperty("Dec").GetDouble();
            return new Coordinates(ra, dec, Epoch.J2000, Coordinates.RAType.Hours);
        }

        public override void Write(Utf8JsonWriter writer, Coordinates value, JsonSerializerOptions options) {
            writer.WriteStartObject();
            writer.WriteNumber("RA", value.RA);
            writer.WriteNumber("Dec", value.Dec);
            writer.WriteString("Epoch", value.Epoch.ToString());
            writer.WriteNumber("RADegrees", value.RADegrees);
            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// Serializes RMS with all public numeric properties. Scale has a private setter
    /// and BaseINPC text properties (RAText etc.) would fail without localization loaded.
    /// </summary>
    public class RmsConverter : JsonConverter<RMS> {
        public override RMS Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            var rms = new RMS();
            if (root.TryGetProperty("Scale", out var scale))
                rms.SetScale(scale.GetDouble());
            if (root.TryGetProperty("RA", out var ra))
                rms.RA = ra.GetDouble();
            if (root.TryGetProperty("Dec", out var dec))
                rms.Dec = dec.GetDouble();
            if (root.TryGetProperty("Total", out var total))
                rms.Total = total.GetDouble();
            if (root.TryGetProperty("PeakRA", out var peakRa))
                rms.PeakRA = peakRa.GetDouble();
            if (root.TryGetProperty("PeakDec", out var peakDec))
                rms.PeakDec = peakDec.GetDouble();
            return rms;
        }

        public override void Write(Utf8JsonWriter writer, RMS value, JsonSerializerOptions options) {
            writer.WriteStartObject();
            writer.WriteNumber("RA", value.RA);
            writer.WriteNumber("Dec", value.Dec);
            writer.WriteNumber("Total", value.Total);
            writer.WriteNumber("Scale", value.Scale);
            writer.WriteNumber("PeakRA", value.PeakRA);
            writer.WriteNumber("PeakDec", value.PeakDec);
            writer.WriteNumber("DataPoints", value.DataPoints);
            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// Serializes IStarDetectionAnalysis interface properties plus optional
    /// FWHM/Eccentricity (from Hocus Focus plugin, accessed via reflection).
    /// </summary>
    public class StarDetectionAnalysisConverter : JsonConverter<IStarDetectionAnalysis> {
        public override IStarDetectionAnalysis Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            var analysis = new NINA.Image.ImageData.StarDetectionAnalysis();
            if (root.TryGetProperty("HFR", out var hfr))
                analysis.HFR = hfr.GetDouble();
            if (root.TryGetProperty("DetectedStars", out var stars))
                analysis.DetectedStars = stars.GetInt32();
            if (root.TryGetProperty("HFRStDev", out var hfrStdev))
                analysis.HFRStDev = hfrStdev.GetDouble();
            return analysis;
        }

        public override void Write(Utf8JsonWriter writer, IStarDetectionAnalysis value, JsonSerializerOptions options) {
            writer.WriteStartObject();
            writer.WriteNumber("HFR", value.HFR);
            writer.WriteNumber("HFRStDev", value.HFRStDev);
            writer.WriteNumber("DetectedStars", value.DetectedStars);

            // Capture FWHM/Eccentricity via reflection (Hocus Focus plugin)
            if (value != null) {
                var type = value.GetType();
                WriteReflectionDouble(writer, type, value, "FWHM");
                WriteReflectionDouble(writer, type, value, "Eccentricity");
            }

            writer.WriteEndObject();
        }

        private static void WriteReflectionDouble(Utf8JsonWriter writer, Type type, object obj, string propName) {
            var prop = type.GetProperty(propName);
            if (prop != null) {
                try {
                    var val = Convert.ToDouble(prop.GetValue(obj));
                    writer.WriteNumber(propName, val);
                } catch { }
            }
        }
    }

    /// <summary>
    /// Serializes IImageStatistics interface properties including the histogram.
    /// </summary>
    public class ImageStatisticsConverter : JsonConverter<IImageStatistics> {
        public override IImageStatistics Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
            // Skip deserialization for now — replay doesn't need to reconstruct this
            using var doc = JsonDocument.ParseValue(ref reader);
            return null;
        }

        public override void Write(Utf8JsonWriter writer, IImageStatistics value, JsonSerializerOptions options) {
            writer.WriteStartObject();
            writer.WriteNumber("BitDepth", value.BitDepth);
            writer.WriteNumber("Mean", value.Mean);
            writer.WriteNumber("Median", value.Median);
            writer.WriteNumber("StDev", value.StDev);
            writer.WriteNumber("MedianAbsoluteDeviation", value.MedianAbsoluteDeviation);
            writer.WriteNumber("Max", value.Max);
            writer.WriteNumber("MaxOccurrences", value.MaxOccurrences);
            writer.WriteNumber("Min", value.Min);
            writer.WriteNumber("MinOccurrences", value.MinOccurrences);

            // Histogram as array of [x, y] pairs
            if (value.Histogram != null) {
                writer.WriteStartArray("Histogram");
                foreach (var point in value.Histogram) {
                    writer.WriteStartArray();
                    writer.WriteNumberValue(point.X);
                    writer.WriteNumberValue(point.Y);
                    writer.WriteEndArray();
                }
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// Skips BitmapSource during serialization — we don't capture pixel data.
    /// </summary>
    public class BitmapSourceSkipConverter : JsonConverter<System.Windows.Media.Imaging.BitmapSource> {
        public override System.Windows.Media.Imaging.BitmapSource Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
            reader.Skip();
            return null;
        }

        public override void Write(Utf8JsonWriter writer, System.Windows.Media.Imaging.BitmapSource value, JsonSerializerOptions options) {
            writer.WriteNullValue();
        }
    }
}
