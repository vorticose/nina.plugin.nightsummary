using System;
using System.Globalization;

namespace NINA.Plugin.NightSummary.Reporting;

/// <summary>
/// Mean-anomaly moon illumination. Shared by the HTML report box and the
/// dashboard session-list JSON so neither path has to scrape the other.
/// Accurate to about 1 to 2 percent. Reference new moon: 2000-01-06 18:14 UTC.
/// </summary>
internal static class MoonPhase {
    internal static double MoonIllumination(DateTime localTime, out bool waxing) {
        const double synodicPeriod = 29.53058868;
        var referenceNewMoon = new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc);
        var utc = localTime.Kind == DateTimeKind.Utc ? localTime : localTime.ToUniversalTime();
        var daysSinceNew = (utc - referenceNewMoon).TotalDays % synodicPeriod;
        if (daysSinceNew < 0) daysSinceNew += synodicPeriod;
        waxing = daysSinceNew < synodicPeriod / 2.0;
        var phaseAngle = daysSinceNew / synodicPeriod * 2.0 * Math.PI;
        return (1.0 - Math.Cos(phaseAngle)) / 2.0 * 100.0;
    }

    /// <summary>
    /// Dashboard / JSON string. Matches the report moon box after HtmlDecode:
    /// "{illum:F0}% ↑" or "↓" (U+2191 / U+2193).
    /// </summary>
    internal static string Format(DateTime sessionStart) {
        var illum = MoonIllumination(sessionStart, out bool waxing);
        return string.Format(CultureInfo.InvariantCulture, "{0:F0}% {1}",
            illum, waxing ? "\u2191" : "\u2193");
    }
}
