using NINA.Plugin.NightSummary.Data;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    /// <summary>
    /// Storage-layer tests for <see cref="CompanionTokenStore"/>. Covers
    /// add/lookup/revoke/pair semantics, persistence across instances,
    /// atomic write (no torn file under crash-after-tmp), and constant-time
    /// lookup behavior (functional only — timing is not asserted).
    /// </summary>
    public class CompanionTokenStoreTests : IDisposable {

        private readonly string _path;

        public CompanionTokenStoreTests() {
            _path = Path.Combine(Path.GetTempPath(), $"ns_tokens_test_{Guid.NewGuid():N}.json");
        }

        public void Dispose() {
            foreach (var p in new[] { _path, _path + ".tmp" })
                if (File.Exists(p)) File.Delete(p);
        }

        private CompanionTokenStore Make() => new(_path);

        private static string FreshToken() {
            // 16 random base32-ish chars, matches the design's 80-bit token space.
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var bytes = new byte[16];
            RandomNumberGenerator.Fill(bytes);
            var sb = new StringBuilder(16);
            foreach (var b in bytes) sb.Append(alphabet[b % alphabet.Length]);
            return sb.ToString();
        }

        // ---- Changed event (drives live Options-panel refresh, no restart) ----

        [Fact]
        public void Add_RaisesChanged() {
            var store = Make();
            int fired = 0;
            store.Changed += () => fired++;
            store.Add(FreshToken());
            Assert.Equal(1, fired);
        }

        [Fact]
        public void MarkPaired_RaisesChanged() {
            // The bug this fixes: a companion claiming a token over HTTP marks it
            // paired; the Options panel must refresh without a NINA restart.
            var store = Make();
            var entry = store.Add(FreshToken());
            int fired = 0;
            store.Changed += () => fired++;   // subscribe AFTER the Add
            Assert.True(store.MarkPaired(entry.Id, "Mac mini"));
            Assert.Equal(1, fired);
        }

        [Fact]
        public void MarkPaired_UnknownId_DoesNotRaiseChanged() {
            var store = Make();
            int fired = 0;
            store.Changed += () => fired++;
            Assert.False(store.MarkPaired("does-not-exist", "x"));
            Assert.Equal(0, fired);
        }

        [Fact]
        public void Revoke_RaisesChanged_OnlyOnRealRevocation() {
            var store = Make();
            var entry = store.Add(FreshToken());
            int fired = 0;
            store.Changed += () => fired++;
            Assert.True(store.Revoke(entry.Id));
            Assert.False(store.Revoke(entry.Id));   // already revoked -> no-op, no event
            Assert.Equal(1, fired);
        }

        // ---- normalization + hashing -----------------------------------------

        [Theory]
        [InlineData("K4M2-9N3X-7QR5-8VH2", "K4M29N3X7QR58VH2")]
        [InlineData("k4m2-9n3x-7qr5-8vh2", "K4M29N3X7QR58VH2")]
        [InlineData("  K4M2 9N3X 7QR5 8VH2  ", "K4M29N3X7QR58VH2")]
        [InlineData("K4M29N3X7QR58VH2", "K4M29N3X7QR58VH2")]
        public void NormalizeToken_StripsWhitespaceHyphensAndUppercases(string input, string expected) {
            Assert.Equal(expected, CompanionTokenStore.NormalizeToken(input));
        }

        [Fact]
        public void HashToken_IsStableAcrossEquivalentInputs() {
            var a = CompanionTokenStore.HashToken("K4M2-9N3X-7QR5-8VH2");
            var b = CompanionTokenStore.HashToken("k4m29n3x7qr58vh2");
            var c = CompanionTokenStore.HashToken("  K4M2 9N3X 7QR5 8VH2  ");
            Assert.Equal(a, b);
            Assert.Equal(a, c);
            Assert.Equal(64, a.Length); // SHA-256 hex = 64 chars
        }

        [Fact]
        public void HashToken_DiffersForDifferentTokens() {
            Assert.NotEqual(
                CompanionTokenStore.HashToken("K4M29N3X7QR58VH2"),
                CompanionTokenStore.HashToken("ZZZZZZZZZZZZZZZZ"));
        }

        // ---- Add -------------------------------------------------------------

        [Fact]
        public void Add_PersistsEntryWithHashAndId() {
            var mgr   = Make();
            var token = FreshToken();
            var entry = mgr.Add(token);

            Assert.False(string.IsNullOrEmpty(entry.Id));
            Assert.Equal(6, entry.Id.Length);
            Assert.Equal(CompanionTokenStore.HashToken(token), entry.Hash);
            Assert.Null(entry.PairedAt);
            Assert.Null(entry.RevokedAt);
            Assert.True((DateTime.UtcNow - entry.CreatedAt).TotalSeconds < 5);
        }

        [Fact]
        public void Add_DoesNotPersistPlainToken() {
            var mgr   = Make();
            var token = FreshToken();
            mgr.Add(token);

            var raw = File.ReadAllText(_path);
            Assert.DoesNotContain(token, raw);
            Assert.DoesNotContain(token.ToLowerInvariant(), raw);
        }

        [Fact]
        public void Add_RejectsEmptyToken() {
            var mgr = Make();
            Assert.Throws<ArgumentException>(() => mgr.Add(""));
            Assert.Throws<ArgumentException>(() => mgr.Add("   "));
        }

        [Fact]
        public void Add_RejectsDuplicateHash() {
            var mgr   = Make();
            var token = FreshToken();
            mgr.Add(token);
            Assert.Throws<InvalidOperationException>(() => mgr.Add(token));
        }

        [Fact]
        public void Add_GeneratesUniqueIds() {
            var mgr = Make();
            var ids = new HashSet<string>();
            for (int i = 0; i < 50; i++) {
                ids.Add(mgr.Add(FreshToken()).Id);
            }
            Assert.Equal(50, ids.Count);
        }

        // ---- FindByToken -----------------------------------------------------

        [Fact]
        public void FindByToken_ReturnsEntry_AfterAdd() {
            var mgr   = Make();
            var token = FreshToken();
            var added = mgr.Add(token);

            var found = mgr.FindByToken(token);
            Assert.NotNull(found);
            Assert.Equal(added.Id, found!.Id);
        }

        [Fact]
        public void FindByToken_AcceptsHyphenatedAndLowercaseInput() {
            var mgr = Make();
            mgr.Add("K4M29N3X7QR58VH2");

            Assert.NotNull(mgr.FindByToken("K4M2-9N3X-7QR5-8VH2"));
            Assert.NotNull(mgr.FindByToken("k4m29n3x7qr58vh2"));
            Assert.NotNull(mgr.FindByToken("  K4M2 9N3X 7QR5 8VH2  "));
        }

        [Fact]
        public void FindByToken_ReturnsNullForUnknown() {
            var mgr = Make();
            mgr.Add(FreshToken());
            Assert.Null(mgr.FindByToken("ZZZZZZZZZZZZZZZZ"));
            Assert.Null(mgr.FindByToken(""));
            Assert.Null(mgr.FindByToken(null!));
        }

        [Fact]
        public void FindByToken_ReturnsEntryEvenIfRevoked() {
            // Caller distinguishes "unknown" (null) from "revoked" (entry with
            // RevokedAt set) — pair endpoint returns different errors for each.
            var mgr   = Make();
            var token = FreshToken();
            var added = mgr.Add(token);
            mgr.Revoke(added.Id);

            var found = mgr.FindByToken(token);
            Assert.NotNull(found);
            Assert.True(found!.IsRevoked);
        }

        // ---- Revoke ----------------------------------------------------------

        [Fact]
        public void Revoke_SetsRevokedAtAndPersists() {
            var mgr   = Make();
            var entry = mgr.Add(FreshToken());

            Assert.True(mgr.Revoke(entry.Id));
            var reloaded = Make().FindById(entry.Id);
            Assert.NotNull(reloaded);
            Assert.True(reloaded!.IsRevoked);
            Assert.True((DateTime.UtcNow - reloaded.RevokedAt!.Value).TotalSeconds < 5);
        }

        [Fact]
        public void Revoke_IsIdempotentReturningFalseSecondTime() {
            var mgr   = Make();
            var entry = mgr.Add(FreshToken());
            Assert.True(mgr.Revoke(entry.Id));
            Assert.False(mgr.Revoke(entry.Id));
        }

        [Fact]
        public void Revoke_ReturnsFalseForUnknownId() {
            var mgr = Make();
            Assert.False(mgr.Revoke("deadbe"));
        }

        // ---- MarkPaired / TouchLastUsed --------------------------------------

        [Fact]
        public void MarkPaired_SetsCompanionNameAndTimestamps() {
            var mgr   = Make();
            var entry = mgr.Add(FreshToken());

            Assert.True(mgr.MarkPaired(entry.Id, "Mac mini"));
            var reloaded = mgr.FindById(entry.Id)!;
            Assert.Equal("Mac mini", reloaded.CompanionName);
            Assert.True(reloaded.IsPaired);
            Assert.NotNull(reloaded.LastUsedAt);
        }

        [Fact]
        public void MarkPaired_PreservesOriginalPairedAtOnRebind() {
            // "I rebuilt the Mac mini" case — CompanionName is overwritten,
            // PairedAt records the first claim and stays put.
            var mgr   = Make();
            var entry = mgr.Add(FreshToken());

            mgr.MarkPaired(entry.Id, "Mac mini");
            var first = mgr.FindById(entry.Id)!.PairedAt;
            Thread.Sleep(10);
            mgr.MarkPaired(entry.Id, "Mac mini v2");

            var reloaded = mgr.FindById(entry.Id)!;
            Assert.Equal(first, reloaded.PairedAt);
            Assert.Equal("Mac mini v2", reloaded.CompanionName);
        }

        [Fact]
        public void TouchLastUsed_BumpsTimestamp() {
            var mgr   = Make();
            var entry = mgr.Add(FreshToken());
            mgr.MarkPaired(entry.Id, "Mac mini");

            var before = mgr.FindById(entry.Id)!.LastUsedAt;
            Thread.Sleep(10);
            Assert.True(mgr.TouchLastUsed(entry.Id));
            var after = mgr.FindById(entry.Id)!.LastUsedAt;

            Assert.True(after > before);
        }

        // ---- UpdatePushUrl ---------------------------------------------------

        [Fact]
        public void UpdatePushUrl_SetsValueAndPersists() {
            var mgr   = Make();
            var entry = mgr.Add(FreshToken());

            Assert.True(mgr.UpdatePushUrl(entry.Id, "http://10.0.0.5:8182"));
            var reloaded = Make().FindById(entry.Id)!;
            Assert.Equal("http://10.0.0.5:8182", reloaded.PushUrl);
        }

        [Fact]
        public void UpdatePushUrl_NoopWhenUnchanged_AvoidsDiskChurn() {
            // Per-request callers fire this on every auth; the store should
            // short-circuit when the value already matches what's on disk.
            var mgr   = Make();
            var entry = mgr.Add(FreshToken());
            mgr.UpdatePushUrl(entry.Id, "http://10.0.0.5:8182");

            var beforeMtime = File.GetLastWriteTimeUtc(_path);
            Thread.Sleep(15);
            mgr.UpdatePushUrl(entry.Id, "http://10.0.0.5:8182");
            var afterMtime = File.GetLastWriteTimeUtc(_path);

            Assert.Equal(beforeMtime, afterMtime);
        }

        [Fact]
        public void UpdatePushUrl_ChangesOverwriteOnDisk() {
            // Reflects a port change in companion.json — should refresh.
            var mgr   = Make();
            var entry = mgr.Add(FreshToken());
            mgr.UpdatePushUrl(entry.Id, "http://10.0.0.5:8182");
            mgr.UpdatePushUrl(entry.Id, "http://10.0.0.5:8186");

            Assert.Equal("http://10.0.0.5:8186", Make().FindById(entry.Id)!.PushUrl);
        }

        [Fact]
        public void UpdatePushUrl_ReturnsFalseForUnknownId() {
            var mgr = Make();
            Assert.False(mgr.UpdatePushUrl("deadbe", "http://anything"));
        }

        [Fact]
        public void UpdatePushUrl_NullValueClears() {
            var mgr   = Make();
            var entry = mgr.Add(FreshToken());
            mgr.UpdatePushUrl(entry.Id, "http://10.0.0.5:8182");
            mgr.UpdatePushUrl(entry.Id, null);

            Assert.Null(Make().FindById(entry.Id)!.PushUrl);
        }

        // ---- List ------------------------------------------------------------

        [Fact]
        public void List_ReturnsAllEntriesInsertOrder() {
            var mgr = Make();
            var a   = mgr.Add(FreshToken());
            var b   = mgr.Add(FreshToken());
            var c   = mgr.Add(FreshToken());

            var ids = mgr.List().Select(t => t.Id).ToList();
            Assert.Equal(new[] { a.Id, b.Id, c.Id }, ids);
        }

        [Fact]
        public void List_ReturnedEntriesAreSnapshotNotLive() {
            var mgr   = Make();
            var entry = mgr.Add(FreshToken());
            var snap  = mgr.List();
            mgr.Revoke(entry.Id);

            Assert.False(snap[0].IsRevoked);
            Assert.True(mgr.List()[0].IsRevoked);
        }

        // ---- Persistence -----------------------------------------------------

        [Fact]
        public void Entries_PersistAcrossInstances() {
            var token = FreshToken();
            var id    = Make().Add(token).Id;

            var found = Make().FindByToken(token);
            Assert.NotNull(found);
            Assert.Equal(id, found!.Id);
        }

        [Fact]
        public void EmptyStore_BeforeAnyAdd_LoadsCleanly() {
            var mgr = Make();
            Assert.Empty(mgr.List());
            Assert.Null(mgr.FindByToken(FreshToken()));
            Assert.False(mgr.Revoke("anything"));
        }

        [Fact]
        public void CorruptFile_DoesNotThrow_StartsEmpty() {
            File.WriteAllText(_path, "{ this is not json");
            var mgr = Make();
            Assert.Empty(mgr.List());

            // Should be able to recover by writing a fresh entry.
            var token = FreshToken();
            mgr.Add(token);
            Assert.NotNull(mgr.FindByToken(token));
        }

        // ---- Atomic write ----------------------------------------------------

        [Fact]
        public void Persist_LeavesNoTmpFileOnSuccess() {
            var mgr = Make();
            mgr.Add(FreshToken());
            Assert.False(File.Exists(_path + ".tmp"), "tmp file should be renamed away on success");
        }

        [Fact]
        public void Persist_FileOnDiskIsValidJsonAfterEveryWrite() {
            var mgr = Make();
            for (int i = 0; i < 10; i++) {
                mgr.Add(FreshToken());
                // After each write the on-disk file must parse — proves we
                // never expose a half-written file to readers.
                var json   = File.ReadAllText(_path);
                using var d = JsonDocument.Parse(json);
                Assert.Equal(JsonValueKind.Object, d.RootElement.ValueKind);
            }
        }

        [Fact]
        public void Persist_SurvivesConcurrentReads() {
            // Hammer the store with parallel adds + reads. With the internal
            // lock + atomic rename a reader should never see a torn file or
            // throw. (We don't run a process-level crash test — that needs
            // OS-level fault injection — but lock + rename are the
            // mechanisms that protect against it.)
            var mgr = Make();
            var stop   = false;
            var reader = Task.Run(() => {
                while (!stop) {
                    _ = mgr.List();
                    if (File.Exists(_path)) {
                        var json = File.ReadAllText(_path);
                        if (json.Length > 0) {
                            using var d = JsonDocument.Parse(json);
                        }
                    }
                }
            });

            for (int i = 0; i < 20; i++) mgr.Add(FreshToken());
            stop = true;
            reader.Wait(TimeSpan.FromSeconds(5));

            Assert.Equal(20, mgr.List().Count);
        }

        [Fact]
        public void Persist_RecoversFromStaleTmpFile() {
            // A crash mid-Persist could leave a .tmp file. The next write
            // must succeed regardless (FileMode.Create truncates).
            File.WriteAllText(_path + ".tmp", "leftover from crashed write");

            var mgr   = Make();
            var entry = mgr.Add(FreshToken());

            Assert.NotNull(mgr.FindById(entry.Id));
            Assert.False(File.Exists(_path + ".tmp"));
        }
    }
}
