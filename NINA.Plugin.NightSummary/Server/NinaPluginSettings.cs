using System.Reflection;
using NINA.Core.Utility;
using NINA.Plugin.NightSummary.Data;
using NINA.Profile.Interfaces;
// Alias to disambiguate from NINA.Profile.Interfaces.IPluginSettings (NINA's
// own plugin-settings concept, unrelated to our dashboard abstraction).
using DashboardPluginSettings = NINA.Plugin.NightSummary.Dashboard.Abstractions.IPluginSettings;

namespace NINA.Plugin.NightSummary.Server;

internal sealed class NinaPluginSettings : DashboardPluginSettings {
    private readonly IProfileService _profileService;

    // Default ctor kept for legacy call sites; profile-aware ctor lets the
    // plugin pass through the live IProfileService so observer coords flow
    // into the Tonight's Preview response without a global lookup.
    public NinaPluginSettings() { }
    public NinaPluginSettings(IProfileService profileService) {
        _profileService = profileService;
    }

    public NightSummarySettings Current => SettingsManager.Instance.Current;
    public void Save() => SettingsManager.Instance.Save();
    // Prefer the informational version (e.g. "3.2.0-dev" on dev builds), but the
    // release process strips that attribute to drop the "-dev" suffix, leaving it
    // absent on shipped builds. Fall back to the auto-generated AssemblyVersion
    // (Major.Minor.Build, e.g. "3.2.0") so the dashboard/companion never reports a
    // blank version ("primary v?"). Never returns empty for a real build.
    public string PluginVersion {
        get {
            var info = typeof(NinaPluginSettings).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion?.Split('+')[0];
            if (!string.IsNullOrWhiteSpace(info)) return info;
            var v = typeof(NinaPluginSettings).Assembly.GetName().Version;
            return v != null ? $"{v.Major}.{v.Minor}.{v.Build}" : "";
        }
    }
    public string Mode => "primary";
    // NINA exposes its assembly version via CoreUtil.Version (e.g. "3.2.0.9001").
    // Surfaced through /api/companion/info so the pairing wizard can show
    // which NINA build it's connecting to.
    public string NinaVersion => CoreUtil.Version ?? "";

    // Read live from NINA's active profile AstrometrySettings — matches the
    // values SessionService stamps into ReportData for the per-session
    // altitude charts. Returns 0 when no profile is loaded yet (early boot,
    // tests) which the dashboard JS treats as "hide curves".
    public double ObserverLatitude  => _profileService?.ActiveProfile?.AstrometrySettings?.Latitude  ?? 0;
    public double ObserverLongitude => _profileService?.ActiveProfile?.AstrometrySettings?.Longitude ?? 0;
}
