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
