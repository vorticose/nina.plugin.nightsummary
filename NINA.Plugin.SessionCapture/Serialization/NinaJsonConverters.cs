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
    /// NINA initializes unavailable metadata values (camera temp, telescope
    /// altitude, weather data, ...) to NaN or ±Infinity, which System.Text.Json
    /// refuses to write as numbers. These helpers write non-finite values as
    /// null and read null back as NaN so recordings stay valid, portable JSON.
    /// </summary>
    internal static class NonFiniteJson {

        public static void WriteNumber(Utf8JsonWriter writer, string name, double value) {
            if (double.IsFinite(value)) writer.WriteNumber(name, value);
            else writer.WriteNull(name);
        }

        public static void WriteNumberValue(Utf8JsonWriter writer, double value) {
            if (double.IsFinite(value)) writer.WriteNumberValue(value);
            else writer.WriteNullValue();
        }

        public static double GetDoubleOrNaN(JsonElement element) =>
            element.ValueKind == JsonValueKind.Number ? element.GetDouble() : double.NaN;
    }

    /// <summary>
    /// Sanitizes every reflection-serialized double (e.g. the full ImageMetaData
    /// graph): non-finite values are written as null, null reads back as NaN.
    /// </summary>
    public class SanitizedDoubleConverter : JsonConverter<double> {
        public override bool HandleNull => true;

        public override double Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Null ? double.NaN : reader.GetDouble();

        public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) =>
            NonFiniteJson.WriteNumberValue(writer, value);
    }

    /// <summary>Float twin of <see cref="SanitizedDoubleConverter"/>.</summary>
    public class SanitizedFloatConverter : JsonConverter<float> {
        public override bool HandleNull => true;

        public override float Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Null ? float.NaN : reader.GetSingle();

        public override void Write(Utf8JsonWriter writer, float value, JsonSerializerOptions options) {
            if (float.IsFinite(value)) writer.WriteNumberValue(value);
            else writer.WriteNullValue();
        }
    }

    /// <summary>
    /// Serializes Coordinates as {ra, dec, epoch}. Coordinates has no parameterless
    /// constructor so System.Text.Json can't handle it natively.
    /// </summary>
    public class CoordinatesConverter : JsonConverter<Coordinates> {
        public override Coordinates Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            var ra = NonFiniteJson.GetDoubleOrNaN(root.GetProperty("RA"));
            var dec = NonFiniteJson.GetDoubleOrNaN(root.GetProperty("Dec"));
            return new Coordinates(ra, dec, Epoch.J2000, Coordinates.RAType.Hours);
        }

        public override void Write(Utf8JsonWriter writer, Coordinates value, JsonSerializerOptions options) {
            writer.WriteStartObject();
            NonFiniteJson.WriteNumber(writer, "RA", value.RA);
            NonFiniteJson.WriteNumber(writer, "Dec", value.Dec);
            writer.WriteString("Epoch", value.Epoch.ToString());
            NonFiniteJson.WriteNumber(writer, "RADegrees", value.RADegrees);
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
            if (root.TryGetProperty("Scale", out var scale) && scale.ValueKind == JsonValueKind.Number)
                rms.SetScale(scale.GetDouble());
            if (root.TryGetProperty("RA", out var ra))
                rms.RA = NonFiniteJson.GetDoubleOrNaN(ra);
            if (root.TryGetProperty("Dec", out var dec))
                rms.Dec = NonFiniteJson.GetDoubleOrNaN(dec);
            if (root.TryGetProperty("Total", out var total))
                rms.Total = NonFiniteJson.GetDoubleOrNaN(total);
            if (root.TryGetProperty("PeakRA", out var peakRa))
                rms.PeakRA = NonFiniteJson.GetDoubleOrNaN(peakRa);
            if (root.TryGetProperty("PeakDec", out var peakDec))
                rms.PeakDec = NonFiniteJson.GetDoubleOrNaN(peakDec);
            return rms;
        }

        public override void Write(Utf8JsonWriter writer, RMS value, JsonSerializerOptions options) {
            writer.WriteStartObject();
            NonFiniteJson.WriteNumber(writer, "RA", value.RA);
            NonFiniteJson.WriteNumber(writer, "Dec", value.Dec);
            NonFiniteJson.WriteNumber(writer, "Total", value.Total);
            NonFiniteJson.WriteNumber(writer, "Scale", value.Scale);
            NonFiniteJson.WriteNumber(writer, "PeakRA", value.PeakRA);
            NonFiniteJson.WriteNumber(writer, "PeakDec", value.PeakDec);
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
                analysis.HFR = NonFiniteJson.GetDoubleOrNaN(hfr);
            if (root.TryGetProperty("DetectedStars", out var stars))
                analysis.DetectedStars = stars.GetInt32();
            if (root.TryGetProperty("HFRStDev", out var hfrStdev))
                analysis.HFRStDev = NonFiniteJson.GetDoubleOrNaN(hfrStdev);
            return analysis;
        }

        public override void Write(Utf8JsonWriter writer, IStarDetectionAnalysis value, JsonSerializerOptions options) {
            writer.WriteStartObject();
            NonFiniteJson.WriteNumber(writer, "HFR", value.HFR);
            NonFiniteJson.WriteNumber(writer, "HFRStDev", value.HFRStDev);
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
                    NonFiniteJson.WriteNumber(writer, propName, val);
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
            NonFiniteJson.WriteNumber(writer, "Mean", value.Mean);
            NonFiniteJson.WriteNumber(writer, "Median", value.Median);
            NonFiniteJson.WriteNumber(writer, "StDev", value.StDev);
            NonFiniteJson.WriteNumber(writer, "MedianAbsoluteDeviation", value.MedianAbsoluteDeviation);
            writer.WriteNumber("Max", value.Max);
            writer.WriteNumber("MaxOccurrences", value.MaxOccurrences);
            writer.WriteNumber("Min", value.Min);
            writer.WriteNumber("MinOccurrences", value.MinOccurrences);

            // Histogram as array of [x, y] pairs
            if (value.Histogram != null) {
                writer.WriteStartArray("Histogram");
                foreach (var point in value.Histogram) {
                    writer.WriteStartArray();
                    NonFiniteJson.WriteNumberValue(writer, point.X);
                    NonFiniteJson.WriteNumberValue(writer, point.Y);
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
