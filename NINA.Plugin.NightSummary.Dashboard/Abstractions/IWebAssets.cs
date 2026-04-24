using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Dashboard.Abstractions;

public interface IWebAssets {
    // logicalName examples: "dashboard.html", "dashboard.css", "dashboard.js", "plugin-icon.png".
    // Returns null if the asset doesn't exist.
    Task<byte[]?> ReadAsync(string logicalName, CancellationToken ct = default);
}
