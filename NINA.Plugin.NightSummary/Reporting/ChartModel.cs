using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NINA.Plugin.NightSummary.Reporting {
    /// <summary>
    /// JSON-ready data model for the metric chart. Produced in C# by
    /// <see cref="ChartGenerator.BuildChartModel"/>, embedded in the HTML report
    /// as a data attribute, and consumed by the JavaScript renderer to draw the
    /// SVG client-side. This is the single source of truth for chart rendering —
    /// the same shape is served by the dashboard API in v3.
    /// </summary>
    public class ChartModel {
        [JsonPropertyName("width")]      public int Width        { get; set; } = 800;
        [JsonPropertyName("height")]     public int Height       { get; set; } = 300;
        [JsonPropertyName("lightMode")]  public bool LightMode   { get; set; }
        [JsonPropertyName("title")]      public string Title     { get; set; } = "";

        [JsonPropertyName("primary")]    public ChartMetricInfo Primary { get; set; } = new();
        [JsonPropertyName("secondary")]  public ChartMetricInfo? Secondary { get; set; }

        [JsonPropertyName("primaryPoints")]   public List<ChartPoint> PrimaryPoints   { get; set; } = new();
        [JsonPropertyName("secondaryPoints")] public List<ChartPoint> SecondaryPoints { get; set; } = new();

        [JsonPropertyName("xAxis")]         public ChartXAxisInfo XAxis         { get; set; } = new();
        [JsonPropertyName("eventMarkers")]  public List<ChartEventMarker> EventMarkers { get; set; } = new();

        /// <summary>Distinct filter names that appear in the data, in filter sort order.</summary>
        [JsonPropertyName("filters")]       public List<string> Filters { get; set; } = new();
    }

    /// <summary>
    /// Descriptor for one plotted metric (primary or secondary). All the label,
    /// unit, and formatting strings that would have been hard-coded into the
    /// old SVG generator are now data so the JS renderer can drive presentation.
    /// </summary>
    public class ChartMetricInfo {
        /// <summary>Metric index from ChartGenerator.PrimaryXxx or SecXxx constants.</summary>
        [JsonPropertyName("index")]     public int Index { get; set; }
        [JsonPropertyName("label")]     public string Label     { get; set; } = "";
        [JsonPropertyName("axisLabel")] public string AxisLabel { get; set; } = "";
        /// <summary>Unit string appended to tooltips (e.g. " px", "\"", " °C").</summary>
        [JsonPropertyName("unit")]      public string Unit      { get; set; } = "";
        /// <summary>"F0" or "F1" — axis tick label precision.</summary>
        [JsonPropertyName("format")]        public string Format        { get; set; } = "F1";
        /// <summary>"F0", "F1", or "F2" — tooltip precision (≥ axis Format for full detail).</summary>
        [JsonPropertyName("tooltipFormat")] public string TooltipFormat { get; set; } = "F2";
        /// <summary>Minimum y-range span for nice-scale computation.</summary>
        [JsonPropertyName("minSpan")]   public double MinSpan   { get; set; } = 0.5;
        /// <summary>Non-null when this metric has &lt; 2 valid data points overall.</summary>
        [JsonPropertyName("noDataMessage")] public string? NoDataMessage { get; set; }
        /// <summary>Optional hint shown under the no-data message (e.g. "Requires Hocus Focus plugin").</summary>
        [JsonPropertyName("noDataHint")]    public string? NoDataHint { get; set; }
    }

    /// <summary>
    /// A single plotted data point. <c>x</c> is already resolved into the chart's
    /// x-axis units (seconds since session start, frame index, or metric value).
    /// </summary>
    public class ChartPoint {
        [JsonPropertyName("x")]         public double X { get; set; }
        [JsonPropertyName("y")]         public double Y { get; set; }
        [JsonPropertyName("filter")]    public string Filter { get; set; } = "";
        /// <summary>ISO timestamp for tooltip display and for event marker alignment.</summary>
        [JsonPropertyName("timestamp")] public DateTime Timestamp { get; set; }
    }

    /// <summary>
    /// X-axis configuration. <see cref="Mode"/> matches the constants in
    /// <see cref="ChartGenerator"/> (0 = Time, 1 = FrameIndex, 2+ = metric index).
    /// </summary>
    public class ChartXAxisInfo {
        [JsonPropertyName("mode")]      public int Mode { get; set; }
        /// <summary>Human label used in the chart title (e.g. "Time", "Frame", "Altitude").</summary>
        [JsonPropertyName("label")]     public string Label { get; set; } = "";
        /// <summary>Axis-bottom label (e.g. "Frame #", "HFR (px)"). Empty string for Time mode — no label drawn.</summary>
        [JsonPropertyName("axisLabel")] public string AxisLabel { get; set; } = "";
        /// <summary>When Mode ≥ 2 (metric mode), format string for axis tick labels.</summary>
        [JsonPropertyName("format")]    public string Format { get; set; } = "F1";
        /// <summary>When Mode ≥ 2 (metric mode), tooltip unit.</summary>
        [JsonPropertyName("unit")]      public string Unit { get; set; } = "";
    }

    /// <summary>
    /// A single event marker (AutoFocus, Meridian Flip, Roof Open/Closed) drawn
    /// as a vertical dashed line on Time-axis charts.
    /// </summary>
    public class ChartEventMarker {
        [JsonPropertyName("timestamp")]   public DateTime Timestamp   { get; set; }
        /// <summary>Seconds since session start — precomputed so JS can place the marker without re-doing time math.</summary>
        [JsonPropertyName("xValue")]      public double XValue        { get; set; }
        [JsonPropertyName("type")]        public string Type          { get; set; } = "";
        /// <summary>Short label drawn above the line ("AF", "MF", "S", "US").</summary>
        [JsonPropertyName("label")]       public string Label         { get; set; } = "";
        [JsonPropertyName("description")] public string Description   { get; set; } = "";
    }
}
