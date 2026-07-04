using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace NINA.Plugin.NightSummary.Reporting {
    /// <summary>
    /// Grades each light frame's measured sky background against the imager's own
    /// darkest historical sky for the same capture setup ("x darkest"). Purely
    /// empirical — it reads NINA's already-computed per-frame median ADU
    /// (Images.StatMedian) and never re-reads pixels.
    ///
    /// The floor (== "your darkest") is the 10th percentile of median ADU within a
    /// cohort of directly-comparable frames. Moon geometry is used only to *select*
    /// which real frames define that floor (prefer moonless frames), never to grade.
    ///
    /// EXPERIMENTAL / prototype — lives on experiment/sky-background, not wired into
    /// the report yet.
    /// </summary>
    public sealed class SkyBackgroundCalculator {

        /// <summary>Minimum frames in a cohort before its floor is trusted (cold-start gate).</summary>
        public const int MinCohortSamples = 20;

        /// <summary>The floor percentile. 0.10 = "darkest" without letting one flukey-low frame define it.</summary>
        public const double FloorPercentile = 0.10;

        /// <summary>Moon illumination below this fraction counts as "moon not a factor".</summary>
        public const double DarkMoonIllumMax = 0.20;

        /// <summary>Everything that must be equal for two frames' raw median ADU to be directly comparable.</summary>
        public readonly record struct CohortKey(
            string Filter, int Gain, int Offset, int Binning, int ExposureSec, int BitDepth);

        /// <summary>One light frame's inputs (a projection of an Images row).</summary>
        public sealed record FrameRow(
            DateTime TimestampUtc, string Filter, int Gain, int Offset, int Binning,
            double ExposureDuration, int BitDepth, double SkyMedianAdu,
            double RaHours, double DecDegrees);

        /// <summary>A single frame plotted on the trend chart.</summary>
        public sealed record SkyFramePoint(
            DateTime TimestampUtc, string Filter, double SkyMedianAdu, double? TimesDarkest);

        /// <summary>Per-filter rollup for the session table pill.</summary>
        public sealed record SkyFilterSummary(
            string Filter, int FrameCount, double SessionMedianAdu,
            double? TimesDarkest, double? FloorAdu, int CohortSamples,
            bool BaselineReady, bool FloorIsMoonless);

        public sealed record SkyBackgroundResult(
            IReadOnlyList<SkyFramePoint> Points, IReadOnlyList<SkyFilterSummary> Filters);

        // Per-cohort computed reference values.
        private sealed record CohortFloor(double FloorAdu, int SampleCount, bool BaselineReady, bool IsMoonless);

        // ── Pure core (no DB) ───────────────────────────────────────────────────

        /// <summary>
        /// Computes chart points and per-filter summaries. <paramref name="historyFrames"/> should
        /// INCLUDE this session's frames (decision: tonight's frames stay in the floor pool; P10
        /// barely moves and a record-dark night correctly reads ~1x).
        /// </summary>
        public static SkyBackgroundResult ComputeFromFrames(
            IReadOnlyList<FrameRow> sessionFrames,
            IReadOnlyList<FrameRow> historyFrames,
            double siteLatDeg, double siteLonDeg) {

            if (sessionFrames == null) throw new ArgumentNullException(nameof(sessionFrames));
            if (historyFrames == null) throw new ArgumentNullException(nameof(historyFrames));

            // Bucket history into cohorts, tracking the full sample and the moonless subset.
            var full     = new Dictionary<CohortKey, List<double>>();
            var moonless = new Dictionary<CohortKey, List<double>>();
            foreach (var r in historyFrames) {
                var key = KeyOf(r);
                if (!full.TryGetValue(key, out var fl)) { fl = new List<double>(); full[key] = fl; }
                fl.Add(r.SkyMedianAdu);
                if (IsMoonlessCondition(r, siteLatDeg, siteLonDeg)) {
                    if (!moonless.TryGetValue(key, out var ml)) { ml = new List<double>(); moonless[key] = ml; }
                    ml.Add(r.SkyMedianAdu);
                }
            }

            // Resolve a floor per cohort: prefer the moonless subset when it is itself deep enough.
            var floors = new Dictionary<CohortKey, CohortFloor>();
            foreach (var kv in full) {
                var key       = kv.Key;
                var fullList  = kv.Value;
                moonless.TryGetValue(key, out var mlList);
                bool useMoonless = mlList != null && mlList.Count >= MinCohortSamples;
                var pool  = useMoonless ? mlList : fullList;
                var floor = Percentile(pool, FloorPercentile);
                floors[key] = new CohortFloor(floor, fullList.Count, fullList.Count >= MinCohortSamples, useMoonless);
            }

            // Chart points, in capture order.
            var points = new List<SkyFramePoint>(sessionFrames.Count);
            foreach (var r in sessionFrames) {
                floors.TryGetValue(KeyOf(r), out var cf);
                double? timesDarkest = (cf != null && cf.BaselineReady && cf.FloorAdu > 0)
                    ? r.SkyMedianAdu / cf.FloorAdu
                    : (double?)null;
                points.Add(new SkyFramePoint(r.TimestampUtc, r.Filter ?? "", r.SkyMedianAdu, timesDarkest));
            }

            // Per-filter summaries, reported against each filter's dominant cohort tonight.
            var summaries = new List<SkyFilterSummary>();
            foreach (var grp in sessionFrames.GroupBy(f => f.Filter ?? "")) {
                var frames         = grp.ToList();
                var dominantKey    = frames.GroupBy(KeyOf).OrderByDescending(g => g.Count()).First().Key;
                floors.TryGetValue(dominantKey, out var cf);

                double sessionMedian = Median(frames.Select(f => f.SkyMedianAdu).ToList());
                var ratios = frames
                    .Where(f => cf != null && cf.BaselineReady && cf.FloorAdu > 0)
                    .Select(f => f.SkyMedianAdu / cf.FloorAdu)
                    .ToList();
                double? timesDarkest = ratios.Count > 0 ? Median(ratios) : (double?)null;

                summaries.Add(new SkyFilterSummary(
                    Filter:          grp.Key,
                    FrameCount:      frames.Count,
                    SessionMedianAdu: sessionMedian,
                    TimesDarkest:    timesDarkest,
                    FloorAdu:        cf?.FloorAdu,
                    CohortSamples:   cf?.SampleCount ?? 0,
                    BaselineReady:   cf?.BaselineReady ?? false,
                    FloorIsMoonless: cf?.IsMoonless ?? false));
            }

            return new SkyBackgroundResult(points, summaries);
        }

        // ── Helpers (internal for unit tests) ───────────────────────────────────

        internal static CohortKey KeyOf(FrameRow r) => new CohortKey(
            r.Filter ?? "", r.Gain, r.Offset, r.Binning,
            (int)Math.Round(r.ExposureDuration), r.BitDepth);

        /// <summary>True when the moon contributes negligibly: near-new (&lt;20% illum) or below the horizon.</summary>
        internal static bool IsMoonlessCondition(FrameRow r, double siteLatDeg, double siteLonDeg) {
            if (MoonIllumFraction(r.TimestampUtc) < DarkMoonIllumMax) return true;
            return MoonAltitudeDeg(r.TimestampUtc, siteLatDeg, siteLonDeg) < 0.0;
        }

        /// <summary>Illuminated fraction of the moon's disk (0..1) from sun-moon elongation. No location needed.</summary>
        internal static double MoonIllumFraction(DateTime utc) {
            var (sunRa, sunDec)   = AltitudeCalculator.GetSunPosition(utc);
            var (moonRa, moonDec) = AltitudeCalculator.GetMoonPosition(utc);
            double elongDeg = AltitudeCalculator.AngularSeparation(sunRa, sunDec, moonRa, moonDec);
            return (1.0 - Math.Cos(elongDeg * Math.PI / 180.0)) / 2.0;
        }

        internal static double MoonAltitudeDeg(DateTime utc, double latDeg, double lonDeg) {
            var (moonRa, moonDec) = AltitudeCalculator.GetMoonPosition(utc);
            // GetAltitude converts its argument via ToUniversalTime(); a Utc-kind value makes that a no-op.
            return AltitudeCalculator.GetAltitude(moonRa, moonDec, latDeg, lonDeg,
                DateTime.SpecifyKind(utc, DateTimeKind.Utc));
        }

        /// <summary>Linear-interpolated percentile (Excel PERCENTILE.INC / type-7). Input need not be sorted.</summary>
        internal static double Percentile(IReadOnlyList<double> values, double p) {
            if (values == null || values.Count == 0) return double.NaN;
            var sorted = values.OrderBy(v => v).ToArray();
            if (sorted.Length == 1) return sorted[0];
            double rank = p * (sorted.Length - 1);
            int lo = (int)Math.Floor(rank);
            int hi = (int)Math.Ceiling(rank);
            double frac = rank - lo;
            return sorted[lo] + (sorted[hi] - sorted[lo]) * frac;
        }

        internal static double Median(IReadOnlyList<double> values) {
            if (values == null || values.Count == 0) return double.NaN;
            var sorted = values.OrderBy(v => v).ToArray();
            int mid = sorted.Length / 2;
            return (sorted.Length % 2 == 1)
                ? sorted[mid]
                : (sorted[mid - 1] + sorted[mid]) / 2.0;
        }

        // ── DB wrapper ──────────────────────────────────────────────────────────

        /// <summary>
        /// Reads this session's light frames plus all historical light frames for the same filters,
        /// then grades them. <paramref name="siteLatDeg"/>/<paramref name="siteLonDeg"/> come from the
        /// report layer (it already has observer location); for a fixed rig they stand in for all history.
        /// </summary>
        public static SkyBackgroundResult ComputeFromDatabase(
            string dbPath, string sessionId, double siteLatDeg, double siteLonDeg) {

            const string cols =
                "Timestamp, Filter, Gain, Offset, Binning, ExposureDuration, StatBitDepth, StatMedian, RaHours, DecDegrees";
            const string lightFilter =
                "(ImageType = 'LIGHT' OR ImageType IS NULL) AND StatMedian IS NOT NULL AND ExposureDuration > 0";

            using var conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly;");
            conn.Open();

            List<FrameRow> session;
            using (var cmd = new SqliteCommand(
                $"SELECT {cols} FROM Images WHERE SessionId = @sid AND {lightFilter} ORDER BY Timestamp", conn)) {
                cmd.Parameters.AddWithValue("@sid", sessionId);
                session = ReadFrames(cmd);
            }

            var filters = session.Select(f => f.Filter ?? "").Distinct().ToList();
            if (session.Count == 0 || filters.Count == 0)
                return new SkyBackgroundResult(Array.Empty<SkyFramePoint>(), Array.Empty<SkyFilterSummary>());

            var inClause = string.Join(",", filters.Select((_, i) => "@f" + i));
            List<FrameRow> history;
            using (var cmd = new SqliteCommand(
                $"SELECT {cols} FROM Images WHERE {lightFilter} AND IFNULL(Filter,'') IN ({inClause})", conn)) {
                for (int i = 0; i < filters.Count; i++) cmd.Parameters.AddWithValue("@f" + i, filters[i]);
                history = ReadFrames(cmd);
            }

            return ComputeFromFrames(session, history, siteLatDeg, siteLonDeg);
        }

        private static List<FrameRow> ReadFrames(SqliteCommand cmd) {
            var rows = new List<FrameRow>();
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) {
                rows.Add(new FrameRow(
                    TimestampUtc:     ParseUtc(reader.GetString(0)),
                    Filter:           reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Gain:             reader.IsDBNull(2) ? -1 : reader.GetInt32(2),
                    Offset:           reader.IsDBNull(3) ? -1 : reader.GetInt32(3),
                    Binning:          reader.IsDBNull(4) ? 0  : reader.GetInt32(4),
                    ExposureDuration: reader.IsDBNull(5) ? 0  : reader.GetDouble(5),
                    BitDepth:         reader.IsDBNull(6) ? 16 : reader.GetInt32(6),
                    SkyMedianAdu:     reader.GetDouble(7),
                    RaHours:          reader.IsDBNull(8) ? 0  : reader.GetDouble(8),
                    DecDegrees:       reader.IsDBNull(9) ? 0  : reader.GetDouble(9)));
            }
            return rows;
        }

        // Timestamps are stored round-trippable; parse with RoundtripKind then normalise to UTC
        // (matches the locale-invariant read path used elsewhere).
        private static DateTime ParseUtc(string s) =>
            DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
                ? dt.ToUniversalTime()
                : DateTime.SpecifyKind(DateTime.Parse(s, CultureInfo.InvariantCulture), DateTimeKind.Utc);
    }
}
