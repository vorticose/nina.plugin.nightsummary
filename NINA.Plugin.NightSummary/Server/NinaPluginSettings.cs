using System.Reflection;
using NINA.Core.Utility;
using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;

namespace NINA.Plugin.NightSummary.Server;

internal sealed class NinaPluginSettings : IPluginSettings {
    public NightSummarySettings Current => SettingsManager.Instance.Current;
    public void Save() => SettingsManager.Instance.Save();
    public string PluginVersion =>
        typeof(NinaPluginSettings).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion?.Split('+')[0] ?? "";
    public string Mode => "primary";
    // NINA exposes its assembly version via CoreUtil.Version (e.g. "3.2.0.9001").
    // Surfaced through /api/companion/info so the pairing wizard can show
    // which NINA build it's connecting to.
    public string NinaVersion => CoreUtil.Version ?? "";
}
