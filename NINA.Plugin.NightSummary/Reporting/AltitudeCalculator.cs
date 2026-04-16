using System;
using System.Collections.Generic;

namespace NINA.Plugin.NightSummary.Reporting {
    /// <summary>
    /// Computes target altitude over time and moon position using standard spherical astronomy.
    /// No external library required — pure trig from first principles.
    /// </summary>
    public static class AltitudeCalculator {

        /// <summary>
        /// Returns altitude in degrees for a target at a given local time.
        /// </summary>
        /// <param name="raHours">Target RA in decimal hours.</param>
        /// <param name="decDeg">Target declination in decimal degrees.</param>
        /// <param name="latDeg">Observer latitude in decimal degrees.</param>
        /// <param name="lonDeg">Observer longitude in decimal degrees (positive East).</param>
        /// <param name="localTime">Local DateTime of the observation.</param>
        public static double GetAltitude(double raHours, double decDeg, double latDeg, double lonDeg, DateTime localTime) {
            double jd      = ToJulianDate(localTime.ToUniversalTime());
            double gmstDeg = GreenwichMeanSiderealTime(jd);
            double lstDeg  = ((gmstDeg + lonDeg) % 360 + 360) % 360;
            double haDeg   = ((lstDeg - raHours * 15.0) % 360 + 360) % 360;
            if (haDeg > 180) haDeg -= 360;  // normalise to -180..+180

            double decRad = decDeg * Math.PI / 180.0;
            double latRad = latDeg * Math.PI / 180.0;
            double haRad  = haDeg  * Math.PI / 180.0;

            double sinAlt = Math.Sin(decRad) * Math.Sin(latRad)
                          + Math.Cos(decRad) * Math.Cos(latRad) * Math.Cos(haRad);
            return Math.Asin(Math.Max(-1.0, Math.Min(1.0, sinAlt))) * 180.0 / Math.PI;
        }

        /// <summary>
        /// Returns a sampled altitude curve across the session window.
        /// </summary>
        public static List<(DateTime Time, double Altitude)> GetAltitudeCurve(
            double raHours, double decDeg, double latDeg, double lonDeg,
            DateTime startLocal, DateTime endLocal, int stepMinutes = 5) {

            var result = new List<(DateTime Time, double Altitude)>();
            var t = startLocal;
            while (t <= endLocal) {
                result.Add((t, GetAltitude(raHours, decDeg, latDeg, lonDeg, t)));
                t = t.AddMinutes(stepMinutes);
            }
            // Always include the exact end point
            if (result.Count == 0 || result[result.Count - 1].Time < endLocal)
                result.Add((endLocal, GetAltitude(raHours, decDeg, latDeg, lonDeg, endLocal)));
            return result;
        }

        /// <summary>
        /// Returns a sampled moon altitude curve across the given window.
        /// Moon RA/Dec is recomputed at each step since the moon moves ~0.5°/hr.
        /// </summary>
        public static List<(DateTime Time, double Altitude)> GetMoonAltitudeCurve(
            double latDeg, double lonDeg,
            DateTime startLocal, DateTime endLocal, int stepMinutes = 5) {

            var result = new List<(DateTime Time, double Altitude)>();
            var t = startLocal;
            while (t <= endLocal) {
                var (moonRa, moonDec) = GetMoonPosition(t.ToUniversalTime());
                result.Add((t, GetAltitude(moonRa, moonDec, latDeg, lonDeg, t)));
                t = t.AddMinutes(stepMinutes);
            }
            if (result.Count == 0 || result[result.Count - 1].Time < endLocal) {
                var (moonRa, moonDec) = GetMoonPosition(endLocal.ToUniversalTime());
                result.Add((endLocal, GetAltitude(moonRa, moonDec, latDeg, lonDeg, endLocal)));
            }
            return result;
        }

        /// <summary>
        /// Returns the peak (maximum) altitude in degrees for a target during a given time window.
        /// Samples every 10 minutes for performance — sufficient for multi-night trend charts.
        /// </summary>
        public static double GetPeakAltitude(double raHours, double decDeg, double latDeg, double lonDeg,
            DateTime startLocal, DateTime endLocal) {
            double peak = double.MinValue;
            var t = startLocal;
            while (t <= endLocal) {
                var alt = GetAltitude(raHours, decDeg, latDeg, lonDeg, t);
                if (alt > peak) peak = alt;
                t = t.AddMinutes(10);
            }
            var endAlt = GetAltitude(raHours, decDeg, latDeg, lonDeg, endLocal);
            if (endAlt > peak) peak = endAlt;
            return peak;
        }

        /// <summary>
        /// Returns approximate Sun RA (decimal hours) and Dec (decimal degrees) at a given UTC time.
        /// Accurate to ~0.01° — sufficient for sunset/sunrise calculations.
        /// </summary>
        public static (double RaHours, double DecDeg) GetSunPosition(DateTime utcTime) {
            double d    = ToJulianDate(utcTime) - 2451545.0;
            double L    = ((280.460 + 0.9856474 * d) % 360 + 360) % 360;
            double g    = ((357.528 + 0.9856003 * d) % 360 + 360) % 360;
            double gRad = g * Math.PI / 180.0;
            double lam  = L + 1.915 * Math.Sin(gRad) + 0.020 * Math.Sin(2 * gRad);
            double lamRad = lam  * Math.PI / 180.0;
            double epsRad = (23.439 - 0.0000004 * d) * Math.PI / 180.0;
            double ra  = Math.Atan2(Math.Cos(epsRad) * Math.Sin(lamRad), Math.Cos(lamRad));
            double dec = Math.Asin(Math.Sin(epsRad) * Math.Sin(lamRad));
            return (((ra * 180.0 / Math.PI) / 15.0 + 24.0) % 24.0, dec * 180.0 / Math.PI);
        }

        /// <summary>
        /// Returns the sun's altitude in degrees at the given local time.
        /// </summary>
        public static double GetSunAltitude(double latDeg, double lonDeg, DateTime localTime) {
            var (sunRa, sunDec) = GetSunPosition(localTime.ToUniversalTime());
            return GetAltitude(sunRa, sunDec, latDeg, lonDeg, localTime);
        }

        /// <summary>
        /// Returns the (sunset, sunrise) window for the night containing sessionStart.
        /// Uses -0.833° as the horizon to match standard nautical sunset/sunrise definition.
        /// Falls back to sessionStart-1h / sessionStart+14h if no crossing is found.
        /// </summary>
        public static (DateTime Sunset, DateTime Sunrise) FindNightWindow(
            double latDeg, double lonDeg, DateTime sessionStart) {

            const double horizon = -0.833;

            var noon = sessionStart.Hour >= 12
                ? sessionStart.Date.AddHours(12)
                : sessionStart.Date.AddHours(-12);

            DateTime? sunset = null, sunrise = null;
            double prevAlt = GetSunAltitude(latDeg, lonDeg, noon);

            for (int m = 5; m <= 24 * 60; m += 5) {
                var    t   = noon.AddMinutes(m);
                double alt = GetSunAltitude(latDeg, lonDeg, t);

                // Sunset: first descending crossing between 15:00 and 02:00
                if (sunset == null && prevAlt >= horizon && alt < horizon && m >= 3 * 60 && m <= 14 * 60)
                    sunset = t;

                // Sunrise: first ascending crossing between 00:00 and 10:00
                if (sunrise == null && prevAlt < horizon && alt >= horizon && m >= 12 * 60 && m <= 22 * 60)
                    sunrise = t;

                prevAlt = alt;
            }

            return (
                sunset  ?? sessionStart.AddHours(-1),
                sunrise ?? sessionStart.AddHours(14)
            );
        }

        /// <summary>
        /// Returns approximate Moon RA (decimal hours) and Dec (decimal degrees) at a given UTC time.
        /// Accurate to ~1° — sufficient for reporting moon separation.
        /// </summary>
        public static (double RaHours, double DecDeg) GetMoonPosition(DateTime utcTime) {
            double d = ToJulianDate(utcTime) - 2451545.0;  // days from J2000

            double L = ((218.316 + 13.176396 * d) % 360 + 360) % 360;
            double M = ((134.963 + 13.064993 * d) % 360 + 360) % 360;
            double F = (( 93.272 + 13.229350 * d) % 360 + 360) % 360;

            double mRad = M * Math.PI / 180.0;
            double fRad = F * Math.PI / 180.0;

            double lonRad = (L + 6.289 * Math.Sin(mRad)) * Math.PI / 180.0;  // ecliptic longitude
            double latRad = (5.128 * Math.Sin(fRad))     * Math.PI / 180.0;  // ecliptic latitude
            double epsRad = (23.439 - 0.0000004 * d)     * Math.PI / 180.0;  // obliquity

            double ra  = Math.Atan2(
                Math.Sin(lonRad) * Math.Cos(epsRad) - Math.Tan(latRad) * Math.Sin(epsRad),
                Math.Cos(lonRad));
            double dec = Math.Asin(
                Math.Sin(latRad) * Math.Cos(epsRad) +
                Math.Cos(latRad) * Math.Sin(epsRad) * Math.Sin(lonRad));

            double raHours = ((ra * 180.0 / Math.PI) / 15.0 + 24.0) % 24.0;
            double decDeg  = dec * 180.0 / Math.PI;
            return (raHours, decDeg);
        }

        /// <summary>
        /// Angular separation in degrees between two RA/Dec positions.
        /// </summary>
        public static double AngularSeparation(double ra1H, double dec1Deg, double ra2H, double dec2Deg) {
            double ra1  = ra1H  * 15.0 * Math.PI / 180.0;
            double ra2  = ra2H  * 15.0 * Math.PI / 180.0;
            double dec1 = dec1Deg * Math.PI / 180.0;
            double dec2 = dec2Deg * Math.PI / 180.0;
            double cos  = Math.Sin(dec1) * Math.Sin(dec2)
                        + Math.Cos(dec1) * Math.Cos(dec2) * Math.Cos(ra1 - ra2);
            return Math.Acos(Math.Max(-1.0, Math.Min(1.0, cos))) * 180.0 / Math.PI;
        }

        /// <summary>
        /// Finds the astronomical twilight window (sun below -18°) for the night of the given date.
        /// Works entirely in UTC with a fixed longitude-based offset to avoid DST discontinuities.
        /// Returns UTC times for the dusk-to-dawn interval during which true darkness holds.
        /// </summary>
        public static (DateTime Dusk, DateTime Dawn) FindAstronomicalTwilightWindow(
            double latDeg, double lonDeg, DateTime eveningDate) {

            const double threshold = -18.0;

            // Work in UTC: anchor at ~12:00 mean solar time (no local timezone involvement)
            double fixedOffsetH = Math.Round(lonDeg / 15.0);
            var noonUtc = new DateTime(eveningDate.Year, eveningDate.Month, eveningDate.Day,
                12, 0, 0, DateTimeKind.Utc).AddHours(-fixedOffsetH);

            DateTime? dusk = null, dawn = null;
            double prevAlt = GetSunAltitudeUtc(latDeg, lonDeg, noonUtc);

            for (int m = 5; m <= 24 * 60; m += 5) {
                var    utc = noonUtc.AddMinutes(m);
                double alt = GetSunAltitudeUtc(latDeg, lonDeg, utc);

                // Dusk: sun descends below -18° (evening)
                if (dusk == null && prevAlt >= threshold && alt < threshold && m >= 3 * 60 && m <= 14 * 60) {
                    // Linear interpolation for precise crossing
                    double frac = (prevAlt - threshold) / (prevAlt - alt);
                    dusk = noonUtc.AddMinutes(m - 5 + frac * 5);
                }

                // Dawn: sun ascends above -18° (morning)
                if (dawn == null && prevAlt < threshold && alt >= threshold && m >= 12 * 60 && m <= 22 * 60) {
                    double frac = (threshold - prevAlt) / (alt - prevAlt);
                    dawn = noonUtc.AddMinutes(m - 5 + frac * 5);
                }

                prevAlt = alt;
            }

            // Fallback: if no crossing found (polar regions / perpetual twilight)
            return (
                dusk ?? noonUtc.AddHours(8),
                dawn ?? noonUtc.AddHours(16)
            );
        }

        /// <summary>
        /// Computes hours a target is above minAltitude during astronomical darkness for a given night.
        /// Uses UTC throughout and interpolates threshold crossings for a smooth day-to-day curve.
        /// </summary>
        public static double GetAvailableHours(
            double raHours, double decDeg, double latDeg, double lonDeg,
            double minAltitude, DateTime eveningDate) {

            var (dusk, dawn) = FindAstronomicalTwilightWindow(latDeg, lonDeg, eveningDate);
            // dusk/dawn are UTC — sweep in UTC
            double totalMinutes = 0;
            const int step = 5;
            var t = dusk;
            double prevAlt = GetAltitudeUtc(raHours, decDeg, latDeg, lonDeg, t);
            bool prevAbove = prevAlt >= minAltitude;
            t = t.AddMinutes(step);

            while (t <= dawn) {
                double alt = GetAltitudeUtc(raHours, decDeg, latDeg, lonDeg, t);
                bool above = alt >= minAltitude;

                if (prevAbove && above) {
                    // Fully above — count entire interval
                    totalMinutes += step;
                } else if (prevAbove && !above) {
                    // Crossing downward — interpolate fraction above
                    double frac = (prevAlt - minAltitude) / (prevAlt - alt);
                    totalMinutes += frac * step;
                } else if (!prevAbove && above) {
                    // Crossing upward — interpolate fraction above
                    double frac = (minAltitude - prevAlt) / (alt - prevAlt);
                    totalMinutes += (1 - frac) * step;
                }
                // else: both below — add nothing

                prevAlt = alt;
                prevAbove = above;
                t = t.AddMinutes(step);
            }

            return totalMinutes / 60.0;
        }

        /// <summary>
        /// Computes sun altitude from a UTC time directly (no local time conversion).
        /// </summary>
        private static double GetSunAltitudeUtc(double latDeg, double lonDeg, DateTime utc) {
            var (sunRa, sunDec) = GetSunPosition(utc);
            return GetAltitudeUtc(sunRa, sunDec, latDeg, lonDeg, utc);
        }

        /// <summary>
        /// Computes target altitude from a UTC time directly (no local time conversion).
        /// </summary>
        private static double GetAltitudeUtc(double raHours, double decDeg, double latDeg, double lonDeg, DateTime utc) {
            double jd      = ToJulianDate(utc);
            double gmstDeg = GreenwichMeanSiderealTime(jd);
            double lstDeg  = ((gmstDeg + lonDeg) % 360 + 360) % 360;
            double haDeg   = ((lstDeg - raHours * 15.0) % 360 + 360) % 360;
            if (haDeg > 180) haDeg -= 360;

            double decRad = decDeg * Math.PI / 180.0;
            double latRad = latDeg * Math.PI / 180.0;
            double haRad  = haDeg  * Math.PI / 180.0;

            double sinAlt = Math.Sin(decRad) * Math.Sin(latRad)
                          + Math.Cos(decRad) * Math.Cos(latRad) * Math.Cos(haRad);
            return Math.Asin(Math.Max(-1.0, Math.Min(1.0, sinAlt))) * 180.0 / Math.PI;
        }

        /// <summary>
        /// Returns the meridian transit time for a target on the night of the given session,
        /// expressed in mean solar time (fixed UTC offset from longitude, no DST).
        /// This ensures the ~4 min/night sidereal drift produces a clean monotonic line
        /// on multi-night charts without DST discontinuities.
        /// Returns null if no crossing is found.
        /// </summary>
        public static DateTime? GetMeridianTransitTime(double raHours, double lonDeg, DateTime sessionStart) {
            // Work entirely in UTC to avoid DST issues.
            // Anchor at ~18:00 mean solar time in UTC.
            double fixedOffsetHours = Math.Round(lonDeg / 15.0);
            var eveningUtc = sessionStart.Date.ToUniversalTime().AddHours(18 - fixedOffsetHours);
            if (sessionStart.Hour < 12) eveningUtc = eveningUtc.AddDays(-1);

            double targetRaDeg = raHours * 15.0;
            double? bestTime = null;
            double bestHa = 360;

            for (int m = 0; m <= 24 * 60; m++) {
                var utc = eveningUtc.AddMinutes(m);
                double jd = ToJulianDate(utc);
                double gmstDeg = GreenwichMeanSiderealTime(jd);
                double lstDeg = ((gmstDeg + lonDeg) % 360 + 360) % 360;
                double ha = Math.Abs(((lstDeg - targetRaDeg + 180) % 360 + 360) % 360 - 180);

                if (ha < bestHa) {
                    bestHa = ha;
                    bestTime = m;
                }
            }

            if (bestTime.HasValue && bestHa < 1.0) {
                // Convert UTC transit back to mean solar time (fixed offset, no DST)
                var transitUtc = eveningUtc.AddMinutes(bestTime.Value);
                return transitUtc.AddHours(fixedOffsetHours);
            }

            return null;
        }

        private static double ToJulianDate(DateTime utc) {
            var j2000 = new DateTime(2000, 1, 1, 12, 0, 0, DateTimeKind.Utc);
            return 2451545.0 + (utc - j2000).TotalDays;
        }

        private static double GreenwichMeanSiderealTime(double jd) {
            double T    = (jd - 2451545.0) / 36525.0;
            double gmst = 280.46061837
                        + 360.98564736629 * (jd - 2451545.0)
                        + 0.000387933 * T * T
                        - T * T * T / 38710000.0;
            return ((gmst % 360.0) + 360.0) % 360.0;
        }
    }
}
