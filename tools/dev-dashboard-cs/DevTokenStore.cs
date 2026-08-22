using System;
using System.Collections.Generic;
using System.Linq;
using NINA.Plugin.NightSummary.Data;

namespace NINA.Plugin.NightSummary.DevHost;

// Dev-only in-memory ICompanionTokenStore so the dev harness can act as a REAL
// primary that a companion pairs with and pulls /api/export/* from. Seeded with
// one already-paired plain token via --pair-token; FindByToken compares the
// plain value directly (no hashing — this is a local test fixture, not prod).
internal sealed class DevTokenStore : ICompanionTokenStore {
    private readonly List<CompanionTokenEntry> _entries = new();
    private readonly Dictionary<string, string> _plainById = new();

    public DevTokenStore(string seedToken) {
        var e = new CompanionTokenEntry {
            Id = "dev-" + Guid.NewGuid().ToString("N").Substring(0, 8),
            Name = "dev",
            Hash = seedToken,                 // store plain for direct compare
            CreatedAt = DateTime.UtcNow,
            PairedAt = DateTime.UtcNow,        // pre-paired so export auth passes immediately
            CompanionName = "dev-companion",
        };
        _entries.Add(e);
        _plainById[e.Id] = seedToken;
    }

    public CompanionTokenEntry Add(string plainToken, string? name = null) {
        var e = new CompanionTokenEntry { Id = "dev-" + Guid.NewGuid().ToString("N").Substring(0, 8), Name = name, Hash = plainToken, CreatedAt = DateTime.UtcNow };
        _entries.Add(e);
        _plainById[e.Id] = plainToken;
        return e;
    }

    public CompanionTokenEntry? FindByToken(string plainToken) =>
        _entries.FirstOrDefault(e => e.Hash == plainToken && !e.IsRevoked);

    public CompanionTokenEntry? FindById(string id) => _entries.FirstOrDefault(e => e.Id == id);
    public IReadOnlyList<CompanionTokenEntry> List() => _entries;

    public bool Revoke(string id) {
        var e = FindById(id); if (e == null) return false; e.RevokedAt = DateTime.UtcNow; return true;
    }
    public bool MarkPaired(string id, string companionName) {
        var e = FindById(id); if (e == null) return false;
        e.PairedAt ??= DateTime.UtcNow; e.CompanionName = companionName; return true;
    }
    public bool TouchLastUsed(string id) {
        var e = FindById(id); if (e == null) return false; e.LastUsedAt = DateTime.UtcNow; return true;
    }
    public bool UpdatePushUrl(string id, string? pushUrl) {
        var e = FindById(id); if (e == null) return false; e.PushUrl = pushUrl; return true;
    }
}
