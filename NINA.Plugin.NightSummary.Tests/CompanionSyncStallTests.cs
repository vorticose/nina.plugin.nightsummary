using NINA.Plugin.NightSummary.Companion;
using NINA.Plugin.NightSummary.Companion.Adapters;
using NINA.Plugin.NightSummary.Companion.Sync;
using NINA.Plugin.NightSummary.Dashboard.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    /// <summary>
    /// A rig that drops off the network mid-download must not wedge the companion.
    ///
    /// Field case (2026-11-03): the rig went offline about 4 MB into a sync's
    /// download and never closed the connection. HttpClient.Timeout does not cover
    /// a body read with ResponseHeadersRead, so the read waited forever, every
    /// later scheduled and recovery sync joined the stuck one, and the companion
    /// showed no new sessions for three days.
    ///
    /// The fake rig here is a raw TcpListener: it answers health and manifest,
    /// then starts the reports zip, sends a few bytes, and goes silent (or trickles)
    /// with the socket held open, which is what a vanished peer looks like from
    /// the companion's side.
    /// </summary>
    public class CompanionSyncStallTests : IAsyncLifetime {

        // Generous ceiling for a sync that should give up within a few seconds.
        // Without the guards the sync never returns, so the test fails here
        // instead of hanging the run.
        private static readonly TimeSpan MustFinishWithin = TimeSpan.FromSeconds(20);

        private enum BodyMode { GoSilent, Trickle }

        private TcpListener _listener = null!;
        private readonly CancellationTokenSource _stop = new();
        private readonly List<TcpClient> _held = new();
        private BodyMode _mode;
        private string _companionRoot = null!;

        public Task InitializeAsync() {
            _companionRoot = Path.Combine(Path.GetTempPath(), $"ns_sync_stall_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_companionRoot);
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _ = Task.Run(AcceptLoop);
            return Task.CompletedTask;
        }

        public Task DisposeAsync() {
            _stop.Cancel();
            try { _listener.Stop(); } catch { }
            lock (_held) { foreach (var c in _held) { try { c.Dispose(); } catch { } } }
            try { if (Directory.Exists(_companionRoot)) Directory.Delete(_companionRoot, true); } catch { }
            return Task.CompletedTask;
        }

        // ── Tests ────────────────────────────────────────────────────────────

        [Fact]
        public async Task Sync_RigGoesSilentMidDownload_FailsInsteadOfHanging() {
            _mode = BodyMode.GoSilent;
            var (engine, paths, http) = MakeEngine();
            using var _ = http;
            engine.StallTimeout = TimeSpan.FromSeconds(1);

            var result = await RunWithCeiling(engine);

            Assert.False(result.Success);
            Assert.Contains("no data from the rig", result.Error);
            // The failure is persisted, so the scheduler's next pass uses the short
            // failure interval and the dashboard shows the error.
            var state = SyncState.Load(Path.Combine(paths.DataDir, "last_synced.json"));
            Assert.Contains("no data from the rig", state.LastError);
        }

        [Fact]
        public async Task Sync_RigTricklesForever_StoppedBySyncDeadline() {
            _mode = BodyMode.Trickle;
            var (engine, paths, http) = MakeEngine();
            using var _ = http;
            // Trickle never stalls long enough to trip the per-read guard, so only
            // the whole-sync deadline can end this one.
            engine.StallTimeout = TimeSpan.FromSeconds(30);
            engine.SyncDeadline = TimeSpan.FromSeconds(2);

            var result = await RunWithCeiling(engine);

            Assert.False(result.Success);
            Assert.Contains("longer than", result.Error);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static async Task<SyncEngine.SyncResult> RunWithCeiling(SyncEngine engine) {
            var sync = engine.SyncAsync();
            var winner = await Task.WhenAny(sync, Task.Delay(MustFinishWithin));
            Assert.True(winner == sync, $"sync still running after {MustFinishWithin.TotalSeconds:0} s: it is hung on the stalled download");
            return await sync;
        }

        private (SyncEngine, CompanionPaths, HttpClient) MakeEngine() {
            var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            var config = new CompanionConfig {
                DataDir = _companionRoot,
                Nina = new CompanionConfig.NinaConfig { Host = "127.0.0.1", Port = port, PairingToken = "STALLTEST16CHARS" },
            };
            var paths = new CompanionPaths(_companionRoot);
            paths.EnsureExists();
            // Same shape as production: a long client timeout that does not cover
            // body reads once headers have arrived.
            var http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromMinutes(30) };
            return (new SyncEngine(config, paths, new SilentLogger(), http), paths, http);
        }

        private async Task AcceptLoop() {
            while (!_stop.IsCancellationRequested) {
                TcpClient client;
                try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
                catch { return; }
                lock (_held) _held.Add(client);
                _ = Task.Run(() => Serve(client));
            }
        }

        // Minimal HTTP/1.1 responder, one request per connection is all SyncEngine
        // needs to reach the reports download.
        private async Task Serve(TcpClient client) {
            try {
                var stream = client.GetStream();
                var path = await ReadRequestPath(stream);
                switch (path) {
                    case "/api/health":
                        await WriteJson(stream, "{\"version\":\"test\",\"schemaVersion\":1}");
                        break;
                    case "/api/export/manifest":
                        await WriteJson(stream, "{\"files\":[]}");
                        break;
                    case "/api/export/reports":
                        await WriteAscii(stream, "HTTP/1.1 200 OK\r\nContent-Type: application/zip\r\nContent-Length: 10000000\r\n\r\n");
                        await stream.WriteAsync(new byte[4096], _stop.Token);
                        if (_mode == BodyMode.Trickle) {
                            while (!_stop.IsCancellationRequested) {
                                await Task.Delay(200, _stop.Token);
                                await stream.WriteAsync(new byte[1], _stop.Token);
                            }
                        }
                        // GoSilent: return with the socket still open (held in _held),
                        // like a peer that vanished without a FIN or RST.
                        break;
                    default:
                        await WriteAscii(stream, "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                        break;
                }
            } catch { /* the companion abandoning the connection is the point */ }
        }

        private static async Task<string> ReadRequestPath(NetworkStream stream) {
            var buf = new byte[8192];
            var sb = new StringBuilder();
            while (!sb.ToString().Contains("\r\n\r\n")) {
                int n = await stream.ReadAsync(buf);
                if (n == 0) break;
                sb.Append(Encoding.ASCII.GetString(buf, 0, n));
            }
            var target = sb.ToString().Split(' ')[1];
            var q = target.IndexOf('?');
            return q >= 0 ? target[..q] : target;
        }

        private Task WriteJson(NetworkStream stream, string json) =>
            WriteAscii(stream, $"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(json)}\r\nConnection: close\r\n\r\n{json}");

        private async Task WriteAscii(NetworkStream stream, string s) =>
            await stream.WriteAsync(Encoding.UTF8.GetBytes(s), _stop.Token);

        private sealed class SilentLogger : IDashboardLogger {
            public void Info(string m) { }
            public void Warn(string m) { }
            public void Error(string m, Exception? ex = null) { }
            public void Debug(string m) { }
        }
    }
}
