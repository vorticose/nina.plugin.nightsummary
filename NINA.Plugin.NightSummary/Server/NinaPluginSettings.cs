using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;

namespace NINA.Plugin.NightSummary.Server;

internal sealed class NinaPluginSettings : IPluginSettings {
    public NightSummarySettings Current => SettingsManager.Instance.Current;
    public void Save() => SettingsManager.Instance.Save();
}
