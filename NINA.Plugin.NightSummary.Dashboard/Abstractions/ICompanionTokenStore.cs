using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace NINA.Plugin.NightSummary.Data {

    /// <summary>
    /// One pairing token entry in <c>companion_tokens.json</c>. Only the
    /// SHA-256 hash of the plain token is persisted — the plain token is
    /// shown to the user once at generation time and never re-read.
    ///
    /// Lives in the Dashboard assembly so both the WPF plugin (writer) and
    /// the dashboard endpoints (reader) can share the type without a project
    /// reference back to NINA-bound code.
    /// </summary>
    public class CompanionTokenEntry {
        public string Id { get; set; } = "";
        public string? Name { get; set; }
        public string Hash { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime? PairedAt { get; set; }
        public DateTime? LastUsedAt { get; set; }
        public string? CompanionName { get; set; }
        public DateTime? RevokedAt { get; set; }

        // Reverse-direction URL the primary uses for session-end push triggers.
        // Auto-detected from the companion's TCP peer IP + X-Companion-Dashboard-Port
        // header at pair time and refreshed on every authenticated request, so it
        // self-heals across DHCP renewals, port edits, and network switches.
        // Format: "http://<host>:<port>". Null until first auth'd request lands.
        public string? PushUrl { get; set; }

        [JsonIgnore] public bool IsRevoked => RevokedAt.HasValue;
        [JsonIgnore] public bool IsPaired  => PairedAt.HasValue;
    }

    /// <summary>
    /// Abstraction over <c>CompanionTokenStore</c> so the Dashboard assembly
    /// can drive pair/revoke flows without referencing NINA-specific code.
    /// Plugin side implements with the file-backed singleton; tests use a
    /// fresh-tempdir instance.
    /// </summary>
    public interface ICompanionTokenStore {
        CompanionTokenEntry Add(string plainToken, string? name = null);
        CompanionTokenEntry? FindByToken(string plainToken);
        CompanionTokenEntry? FindById(string id);
        IReadOnlyList<CompanionTokenEntry> List();
        bool Revoke(string id);
        bool MarkPaired(string id, string companionName);
        bool TouchLastUsed(string id);
        // Updates push URL only when the new value differs from the stored one
        // (avoids disk churn from per-request rewrites). Returns true if the
        // entry exists, false if id not found.
        bool UpdatePushUrl(string id, string? pushUrl);
    }
}
