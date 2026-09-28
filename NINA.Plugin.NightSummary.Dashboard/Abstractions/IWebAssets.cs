using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Dashboard.Abstractions;

public interface IWebAssets {
    // logicalName examples: "dashboard.html", "dashboard.css", "dashboard.js", "report-icon.png".
    // Returns null if the asset doesn't exist.
    Task<byte[]?> ReadAsync(string logicalName, CancellationToken ct = default);

    // True for disk-backed dev assets where files change between requests; the server
    // skips its assembled-HTML cache so edits show up on browser refresh without restart.
    // False for embedded prod assets (immutable for the life of the process).
    bool HotReload => false;
}
