using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;

namespace NINA.Plugin.NightSummary.DevHost;

// Dev-only wrapper: hides the newest N distinct session dates from
// GetAllSessionsAsync so --fake-rigs can show divergent "latest" nights
// without a second physical DB. Everything else (detail, thumbs, charts,
// livestack) passes through — sliced-out sessions are just omitted from
// the list. Always keeps at least one night so a small snapshot cannot
// empty a fake rig.
internal sealed class DevStaggeredSessionSource : IDashboardDataSource {
    private readonly IDashboardDataSource _inner;
    private readonly int _dropNewestNights;

    public DevStaggeredSessionSource(IDashboardDataSource inner, int dropNewestNights) {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _dropNewestNights = dropNewestNights;
    }

    public async Task<IReadOnlyList<SessionRecord>> GetAllSessionsAsync(CancellationToken ct = default) {
        var all = await _inner.GetAllSessionsAsync(ct);
        if (_dropNewestNights <= 0 || all.Count == 0) return all;

        var dates = all.Select(s => s.SessionStart.Date).Distinct().OrderByDescending(d => d).ToList();
        int drop = Math.Min(_dropNewestNights, dates.Count - 1);
        if (drop <= 0) return all;

        var cut = dates.Skip(drop).ToHashSet();
        return all.Where(s => cut.Contains(s.SessionStart.Date)).ToList();
    }

    public Task<SessionRecord?> GetSessionAsync(string sessionId, CancellationToken ct = default)
        => _inner.GetSessionAsync(sessionId, ct);
    public Task<IReadOnlyList<ImageRecord>> GetImagesAsync(string sessionId, CancellationToken ct = default)
        => _inner.GetImagesAsync(sessionId, ct);
    public Task<IReadOnlyList<SessionEvent>> GetEventsAsync(string sessionId, CancellationToken ct = default)
        => _inner.GetEventsAsync(sessionId, ct);
    public Task<IReadOnlyList<TimingEvent>> GetTimingEventsAsync(string sessionId, CancellationToken ct = default)
        => _inner.GetTimingEventsAsync(sessionId, ct);
    public Task<IReadOnlyList<TargetDetail>> GetTargetDetailsAsync(CancellationToken ct = default)
        => _inner.GetTargetDetailsAsync(ct);
    public Task<IReadOnlyList<TargetSessionDetail>> GetSessionsForTargetAsync(string targetName, CancellationToken ct = default)
        => _inner.GetSessionsForTargetAsync(targetName, ct);
    public Task<IReadOnlyDictionary<string, double>> GetLatestPositionAnglesAsync(CancellationToken ct = default)
        => _inner.GetLatestPositionAnglesAsync(ct);
    public Task<bool> IsTargetSchedulerAvailableAsync(CancellationToken ct = default)
        => _inner.IsTargetSchedulerAvailableAsync(ct);
    public Task<IReadOnlyList<TsProjectInfo>> GetTSProjectsAsync(CancellationToken ct = default)
        => _inner.GetTSProjectsAsync(ct);
    public Task<TsApiSettings?> GetTSApiSettingsAsync(CancellationToken ct = default)
        => _inner.GetTSApiSettingsAsync(ct);
    public Task<TsImageAugment?> GetTsImageAugmentAsync(string targetName, string filterName, DateTime timestamp, int windowSeconds, double exposureDurationSeconds, CancellationToken ct = default)
        => _inner.GetTsImageAugmentAsync(targetName, filterName, timestamp, windowSeconds, exposureDurationSeconds, ct);
    public Task<int> ResyncTsGradingAsync(string sessionId, CancellationToken ct = default)
        => _inner.ResyncTsGradingAsync(sessionId, ct);
    public Task<string?> LoadReportHtmlAsync(string sessionId, CancellationToken ct = default)
        => _inner.LoadReportHtmlAsync(sessionId, ct);
    public Task<byte[]?> LoadLivestackImageAsync(string sessionId, string filename, CancellationToken ct = default)
        => _inner.LoadLivestackImageAsync(sessionId, filename, ct);
    public Task<string?> LoadLivestackManifestAsync(string sessionId, CancellationToken ct = default)
        => _inner.LoadLivestackManifestAsync(sessionId, ct);
}
