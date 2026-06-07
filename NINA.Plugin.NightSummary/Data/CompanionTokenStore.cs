using NINA.Core.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace NINA.Plugin.NightSummary.Data {

    internal class CompanionTokenFile {
        public int Version { get; set; } = 1;
        public List<CompanionTokenEntry> Tokens { get; set; } = new();
    }

    /// <summary>
    /// Persists companion pairing tokens to a sidecar JSON file separate from
    /// the main settings — kept out of the SQLite DB so it doesn't sync to
    /// companions and survives DB migrations. Reads, writes, and lookups are
    /// thread-safe via an internal lock; writes are atomic (temp file + rename)
    /// so a torn write can never corrupt the store.
    ///
    /// <see cref="CompanionTokenEntry"/> + <see cref="ICompanionTokenStore"/>
    /// live in the Dashboard assembly so the server endpoints can use them
    /// without a project reference back to this file.
    /// </summary>
    public class CompanionTokenStore : ICompanionTokenStore {

        public static readonly string ProductionPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NINA", "NightSummary", "companion_tokens.json");

        private static readonly Lazy<CompanionTokenStore> _instance =
            new(() => new CompanionTokenStore(ProductionPath));

        public static CompanionTokenStore Instance => _instance.Value;

        private readonly string _path;
        private readonly object _lock = new();
        private CompanionTokenFile _file = new();
        private bool _loaded;

        /// <summary>
        /// Raised after any change to the visible token set (Add / MarkPaired / Revoke)
        /// so a UI bound to the store — the WPF Options panel — can refresh without a
        /// NINA restart. The motivating case: a companion claims a token over HTTP
        /// (pair endpoint → MarkPaired) and the panel must move it from "Unpaired" to
        /// "Paired" live. Fired OUTSIDE the lock, possibly on a server thread, so
        /// handlers must marshal to their own UI thread. NOT raised for the
        /// LastUsed/PushUrl bumps on every sync (those aren't shown).
        /// </summary>
        public event Action? Changed;

        public CompanionTokenStore(string path) {
            _path = path;
        }

        /// <summary>Strip whitespace + hyphens, uppercase. Tokens are stored normalized.</summary>
        public static string NormalizeToken(string raw) {
            if (string.IsNullOrEmpty(raw)) return "";
            var sb = new StringBuilder(raw.Length);
            foreach (var c in raw) {
                if (char.IsWhiteSpace(c) || c == '-') continue;
                sb.Append(char.ToUpperInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>SHA-256 of the normalized token, hex-encoded lowercase.</summary>
        public static string HashToken(string plainToken) {
            var norm  = NormalizeToken(plainToken);
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(norm));
            var sb    = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        /// <summary>
        /// Adds a new token entry. Caller supplies the plain token (generated
        /// elsewhere with <see cref="RandomNumberGenerator"/>); the store
        /// hashes + persists. Returns the new entry (without the plain token
        /// — that's the caller's responsibility to show once and discard).
        /// </summary>
        public CompanionTokenEntry Add(string plainToken, string? name = null) {
            if (string.IsNullOrWhiteSpace(plainToken))
                throw new ArgumentException("plainToken cannot be empty", nameof(plainToken));

            var entry = new CompanionTokenEntry {
                Id        = GenerateId(),
                Name      = name,
                Hash      = HashToken(plainToken),
                CreatedAt = DateTime.UtcNow,
            };

            lock (_lock) {
                EnsureLoaded();
                if (_file.Tokens.Any(t => t.Hash == entry.Hash))
                    throw new InvalidOperationException("Token already exists in store (hash collision or duplicate).");
                _file.Tokens.Add(entry);
                Persist();
            }
            Changed?.Invoke();
            return entry;
        }

        /// <summary>
        /// Looks up an entry by plain token using constant-time hash comparison
        /// (defeats timing-attack discovery of valid hashes). Returns the entry
        /// regardless of revocation status — callers inspect <see
        /// cref="CompanionTokenEntry.IsRevoked"/> to distinguish "unknown token"
        /// from "revoked token" for the pair endpoint's response codes.
        /// </summary>
        public CompanionTokenEntry? FindByToken(string plainToken) {
            if (string.IsNullOrWhiteSpace(plainToken)) return null;

            var incomingHash = Encoding.UTF8.GetBytes(HashToken(plainToken));

            lock (_lock) {
                EnsureLoaded();
                foreach (var t in _file.Tokens) {
                    var stored = Encoding.UTF8.GetBytes(t.Hash);
                    if (stored.Length != incomingHash.Length) continue;
                    if (CryptographicOperations.FixedTimeEquals(stored, incomingHash))
                        return t;
                }
            }
            return null;
        }

        /// <summary>Returns a snapshot of the current entries (safe to enumerate outside the lock).</summary>
        public IReadOnlyList<CompanionTokenEntry> List() {
            lock (_lock) {
                EnsureLoaded();
                return _file.Tokens.Select(Clone).ToList();
            }
        }

        public CompanionTokenEntry? FindById(string id) {
            lock (_lock) {
                EnsureLoaded();
                var match = _file.Tokens.FirstOrDefault(t => t.Id == id);
                return match == null ? null : Clone(match);
            }
        }

        /// <summary>
        /// Soft-delete: sets <see cref="CompanionTokenEntry.RevokedAt"/> but
        /// keeps the entry for audit. Returns false if id not found or
        /// already revoked.
        /// </summary>
        public bool Revoke(string id) {
            lock (_lock) {
                EnsureLoaded();
                var entry = _file.Tokens.FirstOrDefault(t => t.Id == id);
                if (entry == null || entry.RevokedAt.HasValue) return false;
                entry.RevokedAt = DateTime.UtcNow;
                Persist();
            }
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Marks an entry as claimed by a companion. Idempotent — overwrites
        /// <see cref="CompanionTokenEntry.CompanionName"/> if the entry was
        /// previously paired (covers the "I rebuilt the Mac mini" case
        /// described in the design). Returns false if id not found.
        /// </summary>
        public bool MarkPaired(string id, string companionName) {
            lock (_lock) {
                EnsureLoaded();
                var entry = _file.Tokens.FirstOrDefault(t => t.Id == id);
                if (entry == null) return false;
                var now            = DateTime.UtcNow;
                entry.PairedAt    ??= now;
                entry.CompanionName = companionName;
                entry.LastUsedAt   = now;
                Persist();
            }
            Changed?.Invoke();
            return true;
        }

        /// <summary>Bumps <see cref="CompanionTokenEntry.LastUsedAt"/>. Called on every authenticated sync request.</summary>
        public bool TouchLastUsed(string id) {
            lock (_lock) {
                EnsureLoaded();
                var entry = _file.Tokens.FirstOrDefault(t => t.Id == id);
                if (entry == null) return false;
                entry.LastUsedAt = DateTime.UtcNow;
                Persist();
                return true;
            }
        }

        /// <summary>
        /// Refreshes the reverse-direction push URL the primary uses for
        /// session-end sync triggers. No-op when the value already matches
        /// what's persisted, so per-request callers don't drive disk churn.
        /// </summary>
        public bool UpdatePushUrl(string id, string? pushUrl) {
            lock (_lock) {
                EnsureLoaded();
                var entry = _file.Tokens.FirstOrDefault(t => t.Id == id);
                if (entry == null) return false;
                if (string.Equals(entry.PushUrl, pushUrl, StringComparison.Ordinal)) return true;
                entry.PushUrl = pushUrl;
                Persist();
                return true;
            }
        }

        // ---- internals --------------------------------------------------------

        private void EnsureLoaded() {
            if (_loaded) return;
            Load();
            _loaded = true;
        }

        private void Load() {
            if (!File.Exists(_path)) {
                _file = new CompanionTokenFile();
                return;
            }
            try {
                var json   = File.ReadAllText(_path);
                var loaded = JsonSerializer.Deserialize<CompanionTokenFile>(json, SerializerOptions);
                _file      = loaded ?? new CompanionTokenFile();
            } catch (Exception ex) {
                // A corrupt sidecar should not brick the plugin — log and start fresh.
                // The old file is left on disk for forensic recovery.
                Logger.Warning($"NightSummary: Could not read companion_tokens.json ({ex.Message}) — starting with empty store");
                _file = new CompanionTokenFile();
            }
        }

        private void Persist() {
            try {
                var dir = Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(_file, SerializerOptions);
                var tmp  = _path + ".tmp";

                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None)) {
                    var bytes = Encoding.UTF8.GetBytes(json);
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush(flushToDisk: true);
                }

                // File.Replace / File.Move overwrite are atomic on NTFS —
                // readers see either the old file or the new one, never a
                // half-written file. Retry a few times because an external
                // reader (text editor, sync tool) holding a read handle can
                // briefly block the rename with a sharing-violation IOException.
                AtomicRename(tmp, _path);
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to write companion_tokens.json. {ex.Message}");
                throw;
            }
        }

        private static void AtomicRename(string tmp, string dest) {
            const int maxAttempts = 8;
            for (int attempt = 1; ; attempt++) {
                try {
                    if (File.Exists(dest)) {
                        File.Replace(tmp, dest, destinationBackupFileName: null);
                    } else {
                        File.Move(tmp, dest);
                    }
                    return;
                } catch (IOException) when (attempt < maxAttempts) {
                    Thread.Sleep(15 * attempt);
                } catch (UnauthorizedAccessException) when (attempt < maxAttempts) {
                    Thread.Sleep(15 * attempt);
                }
            }
        }

        private static string GenerateId() {
            // 3 bytes → 6 hex chars. Collision-resistant within the small set
            // of tokens a single user ever generates; not a security boundary.
            var bytes = new byte[3];
            RandomNumberGenerator.Fill(bytes);
            var sb = new StringBuilder(6);
            foreach (var b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static CompanionTokenEntry Clone(CompanionTokenEntry e) => new() {
            Id            = e.Id,
            Name          = e.Name,
            Hash          = e.Hash,
            CreatedAt     = e.CreatedAt,
            PairedAt      = e.PairedAt,
            LastUsedAt    = e.LastUsedAt,
            CompanionName = e.CompanionName,
            RevokedAt     = e.RevokedAt,
            PushUrl       = e.PushUrl,
        };

        private static readonly JsonSerializerOptions SerializerOptions = new() {
            WriteIndented        = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }
}
