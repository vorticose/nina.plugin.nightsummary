using System.Text.Json;
using System.Text.Json.Serialization;

namespace NINA.Plugin.SessionCapture.Serialization {

    /// <summary>
    /// The serializer options used for recording files. Shared so tests can
    /// exercise exactly what CaptureService writes to disk.
    /// </summary>
    public static class CaptureJson {

        public static readonly JsonSerializerOptions Options = new() {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = {
                new JsonStringEnumConverter(),
                new SanitizedDoubleConverter(),
                new SanitizedFloatConverter(),
                new CoordinatesConverter(),
                new RmsConverter(),
                new StarDetectionAnalysisConverter(),
                new ImageStatisticsConverter(),
                new BitmapSourceSkipConverter()
            }
        };
    }
}
