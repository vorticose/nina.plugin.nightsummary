namespace NINA.Plugin.NightSummary.Data {

    /// <summary>
    /// All persisted plugin settings. Serialized to JSON in the NightSummary data folder
    /// so settings survive plugin updates and NINA version changes.
    /// </summary>
    public class NightSummarySettings {

        // ── Email ──────────────────────────────────────────────────────────────
        public bool   UseGmailSmtp      { get; set; } = true;
        public string SenderAddress     { get; set; } = "";
        public string SmtpPassword      { get; set; } = "";
        public string SmtpHost          { get; set; } = "smtp.gmail.com";
        public int    SmtpPort          { get; set; } = 587;
        public bool   SmtpSsl           { get; set; } = true;
        public string RecipientAddress  { get; set; } = "";
        public bool   EmailEnabled      { get; set; } = false;

        // ── Local save ────────────────────────────────────────────────────────
        public bool   SaveReportLocally      { get; set; } = true;
        public string SaveReportPath         { get; set; } = "";
        public string SaveReportFilePattern  { get; set; } = "NightSummary_$$DATEMINUS12$$";

        // ── Pushover ──────────────────────────────────────────────────────────
        public bool   PushoverEnabled   { get; set; } = false;
        public string PushoverAppToken  { get; set; } = "";
        public string PushoverUserKey   { get; set; } = "";

        // ── Discord ───────────────────────────────────────────────────────────
        public bool   DiscordEnabled    { get; set; } = false;
        public string DiscordWebhookUrl { get; set; } = "";

        // ── Dashboard ─────────────────────────────────────────────────────────
        public bool   DashboardEnabled  { get; set; } = false;
        public string DashboardUrl      { get; set; } = "";
        public string DashboardApiKey   { get; set; } = "";

        // ── Local Dashboard Server ────────────────────────────────────────────
        public bool   LocalServerEnabled { get; set; } = false;
        public int    LocalServerPort    { get; set; } = 8181;

        // ── Read-only mirror (public exposure) ───────────────────────────────
        // Parallel DashboardServer instance bound to a separate port with all
        // POST/PUT/DELETE routes refused via a single 403 short-circuit. Designed
        // to sit behind a user-managed reverse proxy (Caddy / nginx / Cloudflare
        // Tunnel) or Tailscale Funnel so the public-facing dashboard cannot
        // mutate state. The main LocalServerPort stays LAN-only.
        public bool   EnableReadOnlyMirror { get; set; } = false;
        public int    ReadOnlyMirrorPort   { get; set; } = 8281;

        // ── Report display ────────────────────────────────────────────────────
        public int    ReportDetailLevel      { get; set; } = 2;
        public bool   ReportLightMode        { get; set; } = false;
        public bool   ExpandSectionsDefault  { get; set; } = false;
        public bool   ShowMoonCurve          { get; set; } = true;
        // EXPERIMENTAL (experiment/sky-background): defaults on for the prototype rig.
        // No WPF/dashboard toggle wired yet — flip via settings JSON if needed.
        public bool   ShowSkyBackground      { get; set; } = true;
        public bool   ShowOverheadBreakdown  { get; set; } = true;
        public bool   ShowSkyThumbnails      { get; set; } = true;
        public bool   ShowLiveStackImages   { get; set; } = true;
        public bool   ShowSessionHistory     { get; set; } = true;
        public bool   ShowAltitudeChart      { get; set; } = true;
        public bool   ShowMinAltitude        { get; set; } = true;
        public bool   ShowTSProgressBars     { get; set; } = true;
        public bool   ShowStarCountCV        { get; set; } = true;
        public bool   ShowHFRGraph           { get; set; } = true;
        public bool   ShowPerTargetIQ        { get; set; } = true;
        public bool   ShowNextNightPreview   { get; set; } = true;
        public bool   PreviewAltitudeDefault { get; set; } = true;
        public bool   TimelineAltitudeDefault { get; set; } = true;
        public int    ChartPrimaryMetric     { get; set; } = 0;
        public int    ChartSecondaryMetric   { get; set; } = 0;
        public string AdditionalChartConfigs { get; set; } = "";
        public int    ChartXAxisMetric     { get; set; } = 0;
        public bool   ShowChartTargetChips { get; set; } = true;
        public bool   ShowChartFilterChips { get; set; } = true;
        public bool   ShowChartAfMarkers   { get; set; } = true;
        public bool   ShowChartFlipMarkers { get; set; } = true;
        public bool   ShowChartRoofMarkers { get; set; } = false;

        // ── Filter classification ─────────────────────────────────────────────
        public string FilterClassifications  { get; set; } = "";
        // Comma-separated "Name=Type" pairs mapping filter names to canonical types
        // (L/R/G/B/H/S/O). Used by the dashboard stats page for filter pill colors.
        public string FilterTypeOverrides    { get; set; } = "";

        // ── Equipment overrides ──────────────────────────────────────────────
        // Comma-separated key:value pairs, e.g. "Camera:My ASI2600,Telescope:Esprit 100ED"
        public string EquipmentOverrides { get; set; } = "";
        public bool   ShowEquipmentProfile { get; set; } = true;
        // Comma-separated list of equipment fields to show in the report
        public string EquipmentVisibleFields { get; set; } = "Camera,Telescope,Mount,Filter Wheel,Focuser,Rotator,Guider";

        // ── Raw image thumbnails ─────────────────────────────────────────────
        // Master toggle. When true, NS encodes a small JPEG thumbnail per LIGHT
        // frame at save time and stores it under %LOCALAPPDATA%\NINA\NightSummary\thumbs\.
        // OFF by default — opt-in to avoid surprising existing users with new
        // disk usage. See RAW_THUMBNAILS_DESIGN.md.
        public bool   CaptureRawThumbnails    { get; set; } = false;
        // When true (and CaptureRawThumbnails=true), also encode an 800px
        // thumbnail used for the dashboard lightbox. ~80 KB/frame vs ~15 KB.
        public bool   CaptureMediumThumbnails { get; set; } = false;
        // Retention policy for thumb dirs. Values: "KeepAll" | "RolloverByDays" | "RolloverByGB".
        public string ThumbnailRetentionMode  { get; set; } = "KeepAll";
        public int    ThumbnailRetentionDays  { get; set; } = 90;
        public double ThumbnailRetentionMaxGB { get; set; } = 5.0;
        // Custom storage directory for thumbnails. Empty = default
        // (%LOCALAPPDATA%\NINA\NightSummary\thumbs). Lets users park large
        // collections on a different drive. Changing this orphans existing
        // thumbs at the old path — they must be moved manually.
        public string ThumbnailStorageDir     { get; set; } = "";
    }
}
