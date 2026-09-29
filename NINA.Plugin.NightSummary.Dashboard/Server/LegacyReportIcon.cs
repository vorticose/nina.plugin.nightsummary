using System;

namespace NINA.Plugin.NightSummary.Server {

    /// <summary>
    /// Reports generated before the header-icon fix (dev 2026-08-23) inline the
    /// full-size brand PNG (~800 KB of base64) for a 48px header icon: about two
    /// thirds of a typical older report. When the dashboard serves such a report it
    /// swaps in the small header icon. Read-side only; the file on disk is never
    /// modified, and anything that doesn't look exactly like that header tag is
    /// left alone.
    /// </summary>
    internal static class LegacyReportIcon {
        private const string SrcPrefix = "<img src='data:image/png;base64,";
        private const string AltSuffix = "' alt='Night Summary'";

        // The small icon is ~12 KB of base64; the legacy master ~800 KB. Anything
        // over this is the legacy icon.
        internal const int LegacyMinBase64Chars = 100_000;

        public static string Shrink(string html, string smallIconDataUri) {
            if (string.IsNullOrEmpty(html) || string.IsNullOrEmpty(smallIconDataUri)) return html;
            int altAt = html.IndexOf(AltSuffix, StringComparison.Ordinal);
            if (altAt < 0) return html;
            int imgAt = html.LastIndexOf(SrcPrefix, altAt, StringComparison.Ordinal);
            if (imgAt < 0) return html;
            int b64Start = imgAt + SrcPrefix.Length;
            int b64Len = altAt - b64Start;
            if (b64Len < LegacyMinBase64Chars) return html;
            // The span must be pure base64: a quote in between means the two
            // markers belong to different tags.
            for (int i = b64Start; i < altAt; i++) {
                char c = html[i];
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9')
                          || c == '+' || c == '/' || c == '=';
                if (!ok) return html;
            }
            return string.Concat(html.AsSpan(0, imgAt), "<img src='", smallIconDataUri, html.AsSpan(altAt));
        }
    }
}
