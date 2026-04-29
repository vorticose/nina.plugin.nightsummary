using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;

namespace NINA.Plugin.NightSummary.Dashboard.WebAssets;

// Loads web assets from the classlib's embedded resources. Used by the plugin (prod path).
public sealed class EmbeddedWebAssets : IWebAssets {
    private readonly Assembly assembly;

    public EmbeddedWebAssets() {
        // Resources are embedded in this classlib assembly with logical names like "dashboard.html".
        assembly = typeof(EmbeddedWebAssets).Assembly;
    }

    public async Task<byte[]?> ReadAsync(string logicalName, CancellationToken ct = default) {
        await using var stream = assembly.GetManifestResourceStream(logicalName);
        if (stream == null) return null;
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
        return ms.ToArray();
    }
}
