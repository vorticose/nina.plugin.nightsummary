using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;

namespace NINA.Plugin.NightSummary.Dashboard.WebAssets;

// Loads web assets directly from disk. Used by the dev harness so HTML/CSS/JS edits show up
// on browser refresh without a rebuild.
//
// Each ReadAsync call hits the filesystem fresh - no caching - so any save propagates instantly.
public sealed class DiskWebAssets : IWebAssets {
    private readonly string webRoot;

    public DiskWebAssets(string webRoot) {
        if (string.IsNullOrWhiteSpace(webRoot)) {
            throw new ArgumentException("webRoot must be a non-empty path", nameof(webRoot));
        }
        this.webRoot = webRoot;
    }

    public async Task<byte[]?> ReadAsync(string logicalName, CancellationToken ct = default) {
        var path = Path.Combine(webRoot, logicalName);
        if (!File.Exists(path)) return null;
        return await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
    }
}
