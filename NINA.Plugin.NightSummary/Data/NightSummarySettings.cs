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
        public bool   SaveReportLocally      { get; set; } = false;
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

        // ── Report display ────────────────────────────────────────────────────
        public int    ReportDetailLevel      { get; set; } = 2;
        public bool   ReportLightMode        { get; set; } = false;
        public bool   ExpandSectionsDefault  { get; set; } = false;
        public bool   ShowMoonCurve          { get; set; } = true;
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
        public int    ChartPrimaryMetric     { get; set; } = 0;
        public int    ChartSecondaryMetric   { get; set; } = 0;
        public string AdditionalChartConfigs { get; set; } = "";
        public int    ChartXAxisMetric     { get; set; } = 0;

        // ── Filter classification ─────────────────────────────────────────────
        public string FilterClassifications  { get; set; } = "";

        // ── Equipment overrides ──────────────────────────────────────────────
        // Comma-separated key:value pairs, e.g. "Camera:My ASI2600,Telescope:Esprit 100ED"
        public string EquipmentOverrides { get; set; } = "";
        public bool   ShowEquipmentProfile { get; set; } = true;
        // Comma-separated list of equipment fields to show in the report
        public string EquipmentVisibleFields { get; set; } = "Camera,Telescope,Mount,Filter Wheel,Focuser,Rotator,Guider";
    }
}
