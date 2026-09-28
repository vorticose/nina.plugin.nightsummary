using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;

namespace NINA.Plugin.NightSummary.Dashboard.WebAssets;

// Loads web assets directly from disk. Used by the dev harness so HTML/CSS/JS edits show up
// on browser refresh without a rebuild.
//
// Each ReadAsync call hits the filesystem fresh - no caching - so any save propagates instantly.
//
// Search order: webRoot first, then any fallback roots in order. Used so non-Web/ assets
// like report-icon.png (which lives under <repo>/assets/) still resolve.
public sealed class DiskWebAssets : IWebAssets {
    private readonly string[] roots;

    public bool HotReload => true;

    public DiskWebAssets(string webRoot, params string[] fallbackRoots) {
        if (string.IsNullOrWhiteSpace(webRoot)) {
            throw new ArgumentException("webRoot must be a non-empty path", nameof(webRoot));
        }
        var list = new List<string> { webRoot };
        if (fallbackRoots != null) {
            foreach (var r in fallbackRoots) {
                if (!string.IsNullOrWhiteSpace(r)) list.Add(r);
            }
        }
        roots = list.ToArray();
    }

    public async Task<byte[]?> ReadAsync(string logicalName, CancellationToken ct = default) {
        foreach (var root in roots) {
            var path = Path.Combine(root, logicalName);
            if (File.Exists(path)) {
                return await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
            }
        }
        return null;
    }
}
