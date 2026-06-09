using NINA.Core.Utility;
using NINA.Core.Utility.Notification;
using NINA.Plugin;
using NINA.Plugin.Interfaces;
using NINA.Plugin.NightSummary.Dashboard.WebAssets;
using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Reporting;
using NINA.Plugin.NightSummary.Server;
using NINA.Plugin.NightSummary.Session;
using NINA.Profile.Interfaces;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace NINA.Plugin.NightSummary {

    [Export(typeof(IPluginManifest))]
    public class NightSummaryPlugin : PluginBase, INotifyPropertyChanged {

        private readonly SessionService sessionService;
        private readonly IProfileService profileService;
        private readonly string liveDbPath;
        private DashboardServer dashboardServer;
        // Optional second instance bound to ReadOnlyMirrorPort when EnableReadOnlyMirror
        // is true. Reads the same DB and serves the same dashboard HTML but refuses every
        // non-GET method at the server level (see DashboardServer ctor `readOnly` param)
        // and tags responses with X-Read-Only + a data-readonly attribute on the HTML so
        // CSS hides destructive UI. Designed to sit behind a user-managed reverse proxy
        // (Caddy / nginx / CF Tunnel) or Tailscale Funnel for safe public exposure.
        private DashboardServer readOnlyMirrorServer;
        private ObservableCollection<SessionRecord> _availableSessions = new ObservableCollection<SessionRecord>();
        public ObservableCollection<SessionRecord> AvailableSessions {
            get => _availableSessions;
            private set { _availableSessions = value; RaisePropertyChanged(); }
        }

        private SessionRecord _selectedSession;
        public SessionRecord SelectedSession {
            get => _selectedSession;
            set { _selectedSession = value; RaisePropertyChanged(); }
        }

        private DateTime _searchFrom = DateTime.Today.AddMonths(-1);
        public DateTime SearchFrom {
            get => _searchFrom;
            set { _searchFrom = value; RaisePropertyChanged(); }
        }

        private DateTime _searchTo = DateTime.Today;
        public DateTime SearchTo {
            get => _searchTo;
            set { _searchTo = value; RaisePropertyChanged(); }
        }

        private string _searchResultText = "";
        public string SearchResultText {
            get => _searchResultText;
            set { _searchResultText = value; RaisePropertyChanged(); }
        }

        public ButtonStatus EmailTestStatus      { get; } = new ButtonStatus();
        public ButtonStatus DiscordTestStatus    { get; } = new ButtonStatus();
        public ButtonStatus PushoverTestStatus   { get; } = new ButtonStatus();
        public ButtonStatus DashboardTestStatus  { get; } = new ButtonStatus();
        public ButtonStatus DashboardUploadStatus{ get; } = new ButtonStatus();
        public ButtonStatus ResendStatus         { get; } = new ButtonStatus();
        public ButtonStatus TestReportStatus     { get; } = new ButtonStatus();

        // ── Raw image thumbnails ─────────────────────────────────────────────
        // Populated by ImportTsThumbnailsCommand; null when idle. The XAML binds
        // to .Text so a plain string would also work, but ButtonStatus matches the
        // existing pattern used for similar deferred-result UIs.
        private string _tsImportStatus = "";
        public string TsImportStatus {
            get => _tsImportStatus;
            set { _tsImportStatus = value; RaisePropertyChanged(); }
        }

        // Set up the SQLitePCLRaw native-library resolver before any code path
        // touches Microsoft.Data.Sqlite. The runtimes/win-x64/native convention
        // doesn't fire in NINA's plugin AssemblyLoadContext (the host's runtime
        // identifier resolver doesn't recurse into plugin folders), so the
        // default Batteries_V2.Init throws TypeInitializationException with a
        // "cannot load e_sqlite3" inner. Pointing the resolver at the plugin's
        // own bundled native shim fixes it for every consumer in this assembly
        // and every assembly it ProjectReferences (Dashboard).
        static NightSummaryPlugin() {
            try {
                var pluginDir = System.IO.Path.GetDirectoryName(typeof(NightSummaryPlugin).Assembly.Location);
                if (string.IsNullOrEmpty(pluginDir)) return;
                var nativePath = System.IO.Path.Combine(pluginDir, "runtimes", "win-x64", "native", "e_sqlite3.dll");
                if (!System.IO.File.Exists(nativePath)) return;
                // The P/Invoke "e_sqlite3" import lives on the SQLitePCLRaw provider
                // assembly — register the resolver there, not on Microsoft.Data.Sqlite.
                var providerAsm = typeof(SQLitePCL.SQLite3Provider_e_sqlite3).Assembly;
                System.Runtime.InteropServices.NativeLibrary.SetDllImportResolver(providerAsm,
                    (name, asm, path) => name == "e_sqlite3"
                        ? System.Runtime.InteropServices.NativeLibrary.Load(nativePath)
                        : IntPtr.Zero);
            } catch (Exception ex) {
                // Static ctor must never throw — log via NINA Logger if available,
                // and let Batteries_V2.Init take its normal failure path so the
                // dashboard's existing error surface still triggers.
                try { NINA.Core.Utility.Logger.Warning($"NightSummary: SQLite resolver setup failed — {ex.Message}"); } catch { }
            }
        }

        [ImportingConstructor]
        public NightSummaryPlugin(
            IProfileService profileService,
            IOptionsVM options,
            IImageSaveMediator imageSaveMediator,
            SessionService sessionService) {

            this.sessionService = sessionService;
            this.profileService = profileService;

            // Populate the pairing token lists once at startup so the Options
            // panel renders the current state without waiting for a user action.
            RefreshCompanionTokens();

            // A companion claiming a token happens over HTTP on a server thread
            // (pair endpoint -> CompanionTokenStore.MarkPaired), out of band from
            // this panel — so without this the Paired/Unpaired lists only updated
            // on the next NINA restart. Refresh live whenever the store changes.
            CompanionTokenStore.Instance.Changed += OnCompanionTokensChanged;

            TestEmailCommand = new RelayCommand(async () => {
                EmailTestStatus.Text = "";
                var senderAddr = S.SenderAddress;
                var password   = S.SmtpPassword;
                var recipient  = S.RecipientAddress;
                var smtpHost   = S.SmtpHost;
                if (string.IsNullOrWhiteSpace(senderAddr) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(recipient)) {
                    EmailTestStatus.Text = "✗ Fill in all email fields first";
                    return;
                }
                if (!senderAddr.Contains("@")) {
                    EmailTestStatus.Text = "✗ Sender doesn't look like an email address";
                    return;
                }
                if (!recipient.Contains("@")) {
                    EmailTestStatus.Text = "✗ Recipient doesn't look like an email address";
                    return;
                }
                if (string.IsNullOrWhiteSpace(smtpHost)) {
                    EmailTestStatus.Text = "✗ SMTP server is required";
                    return;
                }
                bool useGmail = S.UseGmailSmtp;
                var sender = new EmailSender(
                    useGmail ? "smtp.gmail.com" : smtpHost,
                    useGmail ? 587 : S.SmtpPort,
                    useGmail ? true : S.SmtpSsl,
                    senderAddr, password, recipient);
                bool ok = await sender.SendTestAsync();
                EmailTestStatus.Text = ok ? "✓ Sent" : "✗ Failed — check NINA log";
            });

            TestDiscordCommand = new RelayCommand(async () => {
                DiscordTestStatus.Text = "";
                var url = S.DiscordWebhookUrl;
                if (string.IsNullOrWhiteSpace(url)) {
                    DiscordTestStatus.Text = "✗ Webhook URL is empty";
                    return;
                }

                var sender = new DiscordSender(url);
                bool ok = await sender.SendTestAsync();
                DiscordTestStatus.Text = ok ? "✓ Sent" : "✗ Failed — check NINA log";
            });

            TestPushoverCommand = new RelayCommand(async () => {
                PushoverTestStatus.Text = "";
                var appToken = S.PushoverAppToken;
                var userKey  = S.PushoverUserKey;
                if (string.IsNullOrWhiteSpace(appToken) || string.IsNullOrWhiteSpace(userKey)) {
                    PushoverTestStatus.Text = "✗ App token or user key is empty";
                    return;
                }
                if (appToken.Length < 20 || appToken.Contains(" ")) {
                    PushoverTestStatus.Text = "✗ App token looks wrong — double-check you copied it correctly";
                    return;
                }
                if (userKey.Length < 20 || userKey.Contains(" ")) {
                    PushoverTestStatus.Text = "✗ User key looks wrong — double-check you copied it correctly";
                    return;
                }
                var sender = new PushoverSender(appToken, userKey);
                bool ok = await sender.SendAsync("Night Summary", "Pushover is configured correctly!");
                PushoverTestStatus.Text = ok ? "✓ Sent" : "✗ Failed — check NINA log";
            });

            TestDashboardCommand = new RelayCommand(async () => {
                DashboardTestStatus.Text = "";
                var url = S.DashboardUrl;
                if (string.IsNullOrWhiteSpace(url)) {
                    DashboardTestStatus.Text = "✗ Dashboard URL is empty";
                    return;
                }
                var sender = new DashboardSender(url, S.DashboardApiKey ?? "");
                bool ok = await sender.TestConnectionAsync();
                DashboardTestStatus.Text = ok ? "✓ Connected" : "✗ Failed — check NINA log";
            });

            UploadAllToDashboardCommand = new RelayCommand(async () => {
                DashboardUploadStatus.Text = "";
                if (!File.Exists(liveDbPath)) {
                    DashboardUploadStatus.Text = "✗ No session database found";
                    return;
                }
                var url = S.DashboardUrl;
                if (string.IsNullOrWhiteSpace(url)) {
                    DashboardUploadStatus.Text = "✗ Dashboard URL is empty";
                    return;
                }
                DashboardUploadStatus.Text = "Uploading...";
                var (uploaded, skipped, failed) = await this.sessionService.UploadAllToDashboardAsync(
                    liveDbPath,
                    (current, total) => {
                        System.Windows.Application.Current.Dispatcher.Invoke(() => {
                            DashboardUploadStatus.Text = $"Uploading {current}/{total}...";
                        });
                    });
                DashboardUploadStatus.Text = $"✓ Done — {uploaded} uploaded, {skipped} skipped, {failed} failed";
            });

            SendTestReportCommand = new RelayCommand(async () => {
                TestReportStatus.Text = "";
                var testDbPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NINA", "NightSummary", "test", "nightsummary.sqlite");

                if (!File.Exists(testDbPath)) {
                    Logger.Warning($"NightSummary: Test database not found at {testDbPath}");
                    TestReportStatus.Text = "✗ Test database not found";
                    return;
                }

                await this.sessionService.SendFromDatabaseAsync(testDbPath);
                TestReportStatus.Text = "✓ Sent";
            });

            liveDbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NINA", "NightSummary", "nightsummary.sqlite");

            RefreshSessionsCommand = new RelayCommand(async () => {
                SearchResultText = "";
                await Task.Run(() => LoadSessions());
            });

            SearchSessionsCommand = new RelayCommand(async () => {
                if (!File.Exists(liveDbPath)) return;
                await Task.Run(() => {
                    try {
                        var db       = new SessionDatabase(liveDbPath);
                        var sessions = db.GetSessionsByDateRange(SearchFrom, SearchTo);
                        System.Windows.Application.Current.Dispatcher.Invoke(() => {
                            AvailableSessions.Clear();
                            foreach (var s in sessions)
                                AvailableSessions.Add(s);
                            SelectedSession = AvailableSessions.Count > 0 ? AvailableSessions[0] : null;
                            SearchResultText = sessions.Count == 0
                                ? "No sessions found in that range"
                                : $"{sessions.Count} session{(sessions.Count == 1 ? "" : "s")} found";
                        });
                    } catch (Exception ex) {
                        Logger.Error($"NightSummary: Failed to search sessions. {ex.Message}");
                    }
                });
            });

            ClearSearchCommand = new RelayCommand(async () => {
                SearchResultText = "";
                await Task.Run(() => LoadSessions());
            });

            ResendSessionCommand = new RelayCommand(async () => {
                ResendStatus.Text = "";
                if (!File.Exists(liveDbPath)) {
                    Logger.Warning($"NightSummary: Live database not found at {liveDbPath}");
                    ResendStatus.Text = "✗ No session database found";
                    return;
                }
                if (SelectedSession == null) {
                    ResendStatus.Text = "✗ No session selected";
                    return;
                }
                await this.sessionService.SendFromDatabaseAsync(liveDbPath, SelectedSession.SessionId);
                ResendStatus.Text = "✓ Sent";
            });

            DeleteSessionCommand = new RelayCommand(async () => {
                ResendStatus.Text = "";
                if (!File.Exists(liveDbPath)) {
                    ResendStatus.Text = "✗ No session database found";
                    return;
                }
                if (SelectedSession == null) {
                    ResendStatus.Text = "✗ No session selected";
                    return;
                }

                var result = System.Windows.MessageBox.Show(
                    "Are you sure you want to delete this session? This action cannot be undone.",
                    "Delete Session",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);
                if (result != System.Windows.MessageBoxResult.Yes) return;

                var sessionIdToDelete = SelectedSession.SessionId;
                try {
                    await Task.Run(() => new SessionDatabase(liveDbPath).DeleteSession(sessionIdToDelete));
                    LoadSessions();
                    ResendStatus.Text = "✓ Deleted";
                } catch (Exception ex) {
                    Logger.Error($"NightSummary: Failed to delete session. {ex.Message}");
                    ResendStatus.Text = "✗ Delete failed — check NINA log";
                }
            });

            // Keep old name pointing to the same command for backwards compat
            ResendLastSessionCommand = ResendSessionCommand;

            RefreshFiltersCommand = new RelayCommand(async () => {
                await Task.Run(() => LoadFilterClassifications());
            });

            PreviewReportCommand = new RelayCommand(async () => {
                var window = new PreviewWindow(sessionService);
                window.Show();
            });

            ImportTsThumbnailsCommand = new RelayCommand(async () => {
                TsImportStatus = "Starting…";
                try {
                    var thumbsRoot = Data.Thumbnails.GetThumbnailsRoot(S?.ThumbnailStorageDir);
                    var dispatcher = System.Windows.Application.Current?.Dispatcher;
                    var startTime = DateTime.UtcNow;
                    Action<int, int> onProgress = (n, total) => {
                        // ETA from rolling rate, only after we have a few rows of data
                        // so the estimate isn't wild on the first callback.
                        string eta = "";
                        if (n >= 10 && n < total) {
                            var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
                            var perRow  = elapsed / n;
                            var remain  = TimeSpan.FromSeconds(perRow * (total - n));
                            eta = $" — ~{FormatRemaining(remain)} left";
                        }
                        var msg = total > 0 ? $"Importing {n}/{total}{eta}…" : "Importing…";
                        // Property setter raises PropertyChanged which WPF needs on the UI thread.
                        if (dispatcher != null && !dispatcher.CheckAccess()) {
                            dispatcher.BeginInvoke(new Action(() => TsImportStatus = msg));
                        } else {
                            TsImportStatus = msg;
                        }
                    };
                    var result = await Task.Run(() => ThumbnailImporter.ImportFromTargetScheduler(liveDbPath, thumbsRoot, onProgress));

                    // Re-run with nothing left to do is the most common path after the
                    // first successful import — surface the existing count so the user
                    // doesn't see a bare "0 of 0" and wonder if anything got pulled.
                    if (result.Candidates == 0) {
                        TsImportStatus = result.AlreadyImported > 0
                            ? $"✓ Already imported — {result.AlreadyImported} thumbnails on disk"
                            : "✓ Nothing to import";
                    } else {
                        var totalAfter = result.AlreadyImported + result.Imported;
                        TsImportStatus = $"✓ Imported {result.Imported} of {result.Candidates} ({result.Skipped} skipped, {result.Failed} failed) — {totalAfter} total on disk";
                    }
                } catch (Exception ex) {
                    TsImportStatus = $"✗ {ex.Message}";
                    Logger.Error($"NightSummary: TS thumbnail import failed: {ex.Message}\n{ex.StackTrace}");
                }
            });

            StartLocalServerCommand = new RelayCommand(async () => {
                LocalServerStatus.Text = "";
                try {
                    await StartLocalServerAsync();
                    LocalServerStatus.Text = "✓ Running";
                    // Persist the intent so the server auto-starts on next NINA launch.
                    // Keeps the Start button and the "Enable Local Dashboard" checkbox in sync.
                    if (!S.LocalServerEnabled) {
                        S.LocalServerEnabled = true;
                        SaveSettings();
                        RaisePropertyChanged(nameof(LocalServerEnabled));
                    }
                    RaisePropertyChanged(nameof(IsLocalServerRunning));
                    RaisePropertyChanged(nameof(LocalServerUrl));
                    RaisePropertyChanged(nameof(TailscaleUrl));
                    RaisePropertyChanged(nameof(HasTailscaleUrl));
                    RaisePropertyChanged(nameof(ZeroTierUrl));
                    RaisePropertyChanged(nameof(HasZeroTierUrl));
                } catch (Exception ex) {
                    LocalServerStatus.Text = $"✗ {ex.Message}";
                }
            });

            StopLocalServerCommand = new RelayCommand(async () => {
                LocalServerStatus.Text = "";
                await StopLocalServerAsync();
                LocalServerStatus.Text = "Stopped";
                Notification.ShowInformation("Night Summary dashboard stopped");
                RaisePropertyChanged(nameof(IsLocalServerRunning));
                RaisePropertyChanged(nameof(LocalServerUrl));
                RaisePropertyChanged(nameof(TailscaleUrl));
                RaisePropertyChanged(nameof(HasTailscaleUrl));
                RaisePropertyChanged(nameof(ZeroTierUrl));
                RaisePropertyChanged(nameof(HasZeroTierUrl));
            });

            GenerateAllDashboardReportsCommand = new RelayCommand(async () => {
                GenerateDashboardReportsStatus.Text = "";
                if (!File.Exists(liveDbPath)) {
                    GenerateDashboardReportsStatus.Text = "✗ No session database found";
                    return;
                }
                GenerateDashboardReportsStatus.Text = "Generating...";
                var (generated, skipped, failed) = await this.sessionService.GenerateAllDashboardReportsAsync(
                    liveDbPath,
                    (current, total) => {
                        System.Windows.Application.Current.Dispatcher.Invoke(() => {
                            GenerateDashboardReportsStatus.Text = $"Generating {current}/{total}...";
                        });
                    });
                GenerateDashboardReportsStatus.Text = $"✓ Done — {generated} generated, {skipped} already existed, {failed} failed";
            });

            LoadSessions();
            LoadFilterClassifications();

            // Auto-start local dashboard if enabled. Track the Task so Teardown can
            // wait for the start to finish before tearing down — without this, NINA
            // closing during a slow port-bind could orphan the listener and cause
            // "port already in use" on the next launch.
            if (S.LocalServerEnabled) {
                _serverStartTask = Task.Run(async () => {
                    try {
                        await StartLocalServerAsync();
                    } catch (Exception ex) {
                        Logger.Error($"NightSummary: Failed to auto-start local dashboard. {ex.Message}");
                    }
                });
            }

            // Apply thumbnail retention on startup — catches orphan dirs from
            // sessions that crashed before the EndSession sweep ran.
            // Best-effort; never fail plugin init on a cleanup error.
            try {
                var thumbsRoot = Data.Thumbnails.GetThumbnailsRoot(S?.ThumbnailStorageDir);
                if (Directory.Exists(thumbsRoot)) {
                    var db = new SessionDatabase(liveDbPath);
                    Data.ThumbnailRetention.Apply(thumbsRoot, S, sid => db.GetSession(sid)?.SessionStart);
                }
            } catch (Exception ex) {
                Logger.Warning($"NightSummary: ThumbnailRetention startup pass failed: {ex.Message}");
            }

            Logger.Info("NightSummary: Plugin initialized successfully");
        }

        // Holds the auto-start Task so Teardown can await it before stopping.
        private Task _serverStartTask;

        public override async Task Teardown() {
            if (_serverStartTask != null) {
                try { await _serverStartTask; } catch { /* already logged in the start path */ }
            }
            // Let any in-flight EndSession report-generation finish so a quick
            // close-after-session doesn't drop the email/Discord send mid-flight.
            try {
                await sessionService.WaitForPendingReportsAsync(TimeSpan.FromSeconds(30));
            } catch (Exception ex) {
                Logger.Warning($"NightSummary: Error waiting for in-flight reports: {ex.Message}");
            }
            await StopLocalServerAsync();
            SettingsManager.Instance.Save();
            Logger.Info("NightSummary: Plugin torn down");
            await base.Teardown();
        }

        private async Task StartLocalServerAsync() {
            if (dashboardServer?.IsRunning == true) return;
            var paths = new NinaDashboardPaths();
            dashboardServer = new DashboardServer(
                data:        new NinaDashboardDataSource(paths.DatabasePath),
                settings:    new NinaPluginSettings(profileService),
                webAssets:   new EmbeddedWebAssets(),
                externalLog: new NinaDashboardLogger(),
                paths:       paths,
                regen:       new NinaReportRegenerator(this.sessionService, paths.DatabasePath, paths.ReportsDir),
                tokenStore:  CompanionTokenStore.Instance);
            await dashboardServer.StartAsync(S.LocalServerPort);
            var notifyUrl = dashboardServer.TailscaleUrl ?? dashboardServer.ZeroTierUrl ?? dashboardServer.Url;
            Notification.ShowInformation($"Night Summary dashboard live: {notifyUrl}");

            // If a read-only mirror port is configured, spin a second DashboardServer
            // bound to that port with readOnly: true. Reads the same DB and same web
            // assets — only difference is the constructor flag.
            //
            // Validation checks happen here at start time (not in the setter) so the
            // user can adjust both fields atomically in Options without an intermediate
            // invalid state triggering a failed start.
            if (S.EnableReadOnlyMirror) {
                await StartReadOnlyMirrorAsync(paths);
            }
        }

        private async Task StartReadOnlyMirrorAsync(NinaDashboardPaths paths) {
            if (readOnlyMirrorServer?.IsRunning == true) return;

            int port = S.ReadOnlyMirrorPort;
            // Surface validation failures as a toast in addition to the log line —
            // users who fix the port in Options then hit Start expect feedback when
            // the mirror still won't bind. The silent log-only path bit us when a
            // tester picked a colliding port and saw nothing happen.
            if (port < 1024 || port > 65535) {
                var msg = $"Read-only mirror port {port} is out of range (1024-65535); mirror not started";
                Logger.Warning("NightSummary: " + msg);
                Notification.ShowError("Night Summary: " + msg);
                return;
            }
            if (port == S.LocalServerPort) {
                var msg = $"Read-only mirror port {port} matches main dashboard port ({S.LocalServerPort}); mirror not started. Pick a different port.";
                Logger.Warning("NightSummary: " + msg);
                Notification.ShowError("Night Summary: " + msg);
                return;
            }

            try {
                readOnlyMirrorServer = new DashboardServer(
                    data:        new NinaDashboardDataSource(paths.DatabasePath),
                    settings:    new NinaPluginSettings(profileService),
                    webAssets:   new EmbeddedWebAssets(),
                    externalLog: new NinaDashboardLogger(),
                    paths:       paths,
                    regen:       null,   // regen path is a no-op in read-only — POSTs are 403'd
                    readOnly:    true);
                await readOnlyMirrorServer.StartAsync(port);
                Logger.Info($"NightSummary: Read-only mirror dashboard started on port {port}");
                var mirrorUrl = readOnlyMirrorServer.TailscaleUrl ?? readOnlyMirrorServer.ZeroTierUrl ?? readOnlyMirrorServer.Url;
                Notification.ShowInformation($"Night Summary read-only mirror live: {mirrorUrl}");
            } catch (Exception ex) {
                Logger.Warning($"NightSummary: Failed to start read-only mirror on port {port}: {ex.Message}");
                Notification.ShowError($"Night Summary: Read-only mirror failed to bind on port {port}: {ex.Message}");
                readOnlyMirrorServer = null;
            }
        }

        // Stop + (optionally) restart the read-only mirror in response to a runtime toggle
        // from Options. Called by the EnableReadOnlyMirror setter so users don't have to
        // restart NINA to start/stop the mirror.
        private async Task ApplyReadOnlyMirrorStateAsync() {
            try {
                if (readOnlyMirrorServer != null) {
                    await readOnlyMirrorServer.StopAsync();
                    readOnlyMirrorServer = null;
                    Logger.Info("NightSummary: Read-only mirror stopped");
                }
                // Only start a fresh mirror if the user wants it enabled AND the main
                // dashboard is already running — without the main server the mirror has
                // nothing meaningful to mirror, and the auto-start path in
                // StartLocalServerAsync will pick it up the next time the main starts.
                if (S.EnableReadOnlyMirror && dashboardServer?.IsRunning == true) {
                    await StartReadOnlyMirrorAsync(new NinaDashboardPaths());
                }
            } catch (Exception ex) {
                Logger.Warning($"NightSummary: Failed to apply read-only mirror state change: {ex.Message}");
            }
        }

        private async Task StopLocalServerAsync() {
            if (readOnlyMirrorServer != null) {
                try { await readOnlyMirrorServer.StopAsync(); } catch { /* logged at start path */ }
                readOnlyMirrorServer = null;
            }
            if (dashboardServer != null) {
                await dashboardServer.StopAsync();
                dashboardServer = null;
            }
        }

        // Settings properties bound to the Options UI
        private NightSummarySettings S => SettingsManager.Instance.Current;
        private void SaveSettings() => SettingsManager.Instance.Save();

        public bool UseGmailSmtp {
            get => S.UseGmailSmtp;
            set { S.UseGmailSmtp = value; SaveSettings(); RaisePropertyChanged(); RaisePropertyChanged(nameof(UseCustomSmtp)); }
        }

        public bool UseCustomSmtp {
            get => !S.UseGmailSmtp;
            set { S.UseGmailSmtp = !value; SaveSettings(); RaisePropertyChanged(); RaisePropertyChanged(nameof(UseGmailSmtp)); }
        }

        public string SenderAddress {
            get => S.SenderAddress;
            set { S.SenderAddress = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public string SmtpPassword {
            get => S.SmtpPassword;
            set { S.SmtpPassword = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public string SmtpHost {
            get => S.SmtpHost;
            set { S.SmtpHost = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public int SmtpPort {
            get => S.SmtpPort;
            set { S.SmtpPort = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool SmtpSsl {
            get => S.SmtpSsl;
            set { S.SmtpSsl = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public string RecipientAddress {
            get => S.RecipientAddress;
            set { S.RecipientAddress = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool SaveReportLocally {
            get => S.SaveReportLocally;
            set { S.SaveReportLocally = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public string SaveReportPath {
            get => S.SaveReportPath;
            set { S.SaveReportPath = value; SaveSettings(); RaisePropertyChanged(); RaisePropertyChanged(nameof(SaveReportPatternPreview)); }
        }

        public bool ShowEquipmentProfile {
            get => S.ShowEquipmentProfile;
            set { S.ShowEquipmentProfile = value; SaveSettings(); RaisePropertyChanged(); }
        }

        // Equipment override properties — parse from/serialize to comma-separated string
        private string GetEquipmentOverride(string key) {
            var overrides = Session.SessionService.ParseEquipmentOverrides(S.EquipmentOverrides);
            return overrides.TryGetValue(key, out var val) ? val : "";
        }
        private void SetEquipmentOverride(string key, string value) {
            var overrides = Session.SessionService.ParseEquipmentOverrides(S.EquipmentOverrides);
            if (string.IsNullOrWhiteSpace(value))
                overrides.Remove(key);
            else
                overrides[key] = value.Trim();
            S.EquipmentOverrides = string.Join(",", overrides.Select(kv => $"{kv.Key}:{kv.Value}"));
            SaveSettings();
        }

        public string EquipmentCamera        { get => GetEquipmentOverride("Camera");         set { SetEquipmentOverride("Camera", value);         RaisePropertyChanged(); } }
        public string EquipmentTelescope     { get => GetEquipmentOverride("Telescope");      set { SetEquipmentOverride("Telescope", value);      RaisePropertyChanged(); } }
        public string EquipmentMount         { get => GetEquipmentOverride("Mount");           set { SetEquipmentOverride("Mount", value);           RaisePropertyChanged(); } }
        public string EquipmentFilterWheel   { get => GetEquipmentOverride("Filter Wheel");   set { SetEquipmentOverride("Filter Wheel", value);   RaisePropertyChanged(); } }
        public string EquipmentFocuser       { get => GetEquipmentOverride("Focuser");        set { SetEquipmentOverride("Focuser", value);        RaisePropertyChanged(); } }
        public string EquipmentRotator       { get => GetEquipmentOverride("Rotator");        set { SetEquipmentOverride("Rotator", value);        RaisePropertyChanged(); } }
        public string EquipmentGuider        { get => GetEquipmentOverride("Guider");         set { SetEquipmentOverride("Guider", value);         RaisePropertyChanged(); } }
        public string EquipmentDome          { get => GetEquipmentOverride("Dome");           set { SetEquipmentOverride("Dome", value);           RaisePropertyChanged(); } }
        public string EquipmentFlatPanel     { get => GetEquipmentOverride("Flat Panel");     set { SetEquipmentOverride("Flat Panel", value);     RaisePropertyChanged(); } }
        public string EquipmentSafetyMonitor { get => GetEquipmentOverride("Safety Monitor"); set { SetEquipmentOverride("Safety Monitor", value); RaisePropertyChanged(); } }
        public string EquipmentWeather       { get => GetEquipmentOverride("Weather");        set { SetEquipmentOverride("Weather", value);        RaisePropertyChanged(); } }
        public string EquipmentSwitch        { get => GetEquipmentOverride("Switch");         set { SetEquipmentOverride("Switch", value);         RaisePropertyChanged(); } }

        // Per-field visibility toggles
        private bool IsEquipmentFieldVisible(string key) =>
            (S.EquipmentVisibleFields ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Contains(key, StringComparer.OrdinalIgnoreCase);

        private void SetEquipmentFieldVisible(string key, bool visible) {
            var fields = new HashSet<string>(
                (S.EquipmentVisibleFields ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
                StringComparer.OrdinalIgnoreCase);
            if (visible) fields.Add(key); else fields.Remove(key);
            S.EquipmentVisibleFields = string.Join(",", fields);
            SaveSettings();
        }

        public bool ShowCamera        { get => IsEquipmentFieldVisible("Camera");         set { SetEquipmentFieldVisible("Camera", value);         RaisePropertyChanged(); } }
        public bool ShowTelescope     { get => IsEquipmentFieldVisible("Telescope");      set { SetEquipmentFieldVisible("Telescope", value);      RaisePropertyChanged(); } }
        public bool ShowMount         { get => IsEquipmentFieldVisible("Mount");           set { SetEquipmentFieldVisible("Mount", value);           RaisePropertyChanged(); } }
        public bool ShowFilterWheel   { get => IsEquipmentFieldVisible("Filter Wheel");   set { SetEquipmentFieldVisible("Filter Wheel", value);   RaisePropertyChanged(); } }
        public bool ShowFocuser       { get => IsEquipmentFieldVisible("Focuser");        set { SetEquipmentFieldVisible("Focuser", value);        RaisePropertyChanged(); } }
        public bool ShowRotator       { get => IsEquipmentFieldVisible("Rotator");        set { SetEquipmentFieldVisible("Rotator", value);        RaisePropertyChanged(); } }
        public bool ShowGuider        { get => IsEquipmentFieldVisible("Guider");         set { SetEquipmentFieldVisible("Guider", value);         RaisePropertyChanged(); } }
        public bool ShowDome          { get => IsEquipmentFieldVisible("Dome");           set { SetEquipmentFieldVisible("Dome", value);           RaisePropertyChanged(); } }
        public bool ShowFlatPanel     { get => IsEquipmentFieldVisible("Flat Panel");     set { SetEquipmentFieldVisible("Flat Panel", value);     RaisePropertyChanged(); } }
        public bool ShowSafetyMonitor { get => IsEquipmentFieldVisible("Safety Monitor"); set { SetEquipmentFieldVisible("Safety Monitor", value); RaisePropertyChanged(); } }
        public bool ShowWeather       { get => IsEquipmentFieldVisible("Weather");        set { SetEquipmentFieldVisible("Weather", value);        RaisePropertyChanged(); } }
        public bool ShowSwitch        { get => IsEquipmentFieldVisible("Switch");         set { SetEquipmentFieldVisible("Switch", value);         RaisePropertyChanged(); } }

        public string SaveReportFilePattern {
            get => S.SaveReportFilePattern;
            set { S.SaveReportFilePattern = value; SaveSettings(); RaisePropertyChanged(); RaisePropertyChanged(nameof(SaveReportPatternPreview)); }
        }

        public string SaveReportPatternPreview {
            get {
                var pattern = S.SaveReportFilePattern;
                if (string.IsNullOrWhiteSpace(pattern)) return "NightSummary_<timestamp>.html";
                var preview = new Dictionary<string, string> {
                    ["$$CAMERA$$"] = "ZWO ASI2600MM",
                    ["$$TELESCOPE$$"] = "My Telescope",
                    ["$$SEQUENCETITLE$$"] = "MySequence"
                };
                var resolved = Session.SessionService.ResolveFilePattern(pattern, preview) + ".html";
                return resolved.Replace("\\", " \u203A ");
            }
        }

        public bool EmailEnabled {
            get => S.EmailEnabled;
            set { S.EmailEnabled = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool PushoverEnabled {
            get => S.PushoverEnabled;
            set { S.PushoverEnabled = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public string PushoverAppToken {
            get => S.PushoverAppToken;
            set { S.PushoverAppToken = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public string PushoverUserKey {
            get => S.PushoverUserKey;
            set { S.PushoverUserKey = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool DiscordEnabled {
            get => S.DiscordEnabled;
            set { S.DiscordEnabled = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public string DiscordWebhookUrl {
            get => S.DiscordWebhookUrl;
            set { S.DiscordWebhookUrl = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool DashboardEnabled {
            get => S.DashboardEnabled;
            set { S.DashboardEnabled = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public string DashboardUrl {
            get => S.DashboardUrl;
            set { S.DashboardUrl = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public string DashboardApiKey {
            get => S.DashboardApiKey;
            set { S.DashboardApiKey = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool LocalServerEnabled {
            get => S.LocalServerEnabled;
            set { S.LocalServerEnabled = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public int LocalServerPort {
            get => S.LocalServerPort;
            set { S.LocalServerPort = value; SaveSettings(); RaisePropertyChanged(); }
        }

        // Read-only mirror exposure. Toggling EnableReadOnlyMirror applies immediately
        // via ApplyReadOnlyMirrorStateAsync — the user doesn't have to restart NINA.
        // Port changes ALSO apply immediately (stop + restart on the new port) provided
        // the mirror is enabled. The XAML TextBox commits on LostFocus (default trigger)
        // so the setter doesn't fire mid-typing — keeps the rebind from chasing every
        // keystroke from "8" to "8281".
        public bool EnableReadOnlyMirror {
            get => S.EnableReadOnlyMirror;
            set {
                if (S.EnableReadOnlyMirror == value) return;
                S.EnableReadOnlyMirror = value;
                SaveSettings();
                RaisePropertyChanged();
                _ = ApplyReadOnlyMirrorStateAsync();
            }
        }

        public int ReadOnlyMirrorPort {
            get => S.ReadOnlyMirrorPort;
            set {
                if (S.ReadOnlyMirrorPort == value) return;
                S.ReadOnlyMirrorPort = value;
                SaveSettings();
                RaisePropertyChanged();
                // Auto-restart when enabled so the user doesn't have to remember to
                // toggle the checkbox off+on after changing the port. No-op when the
                // mirror is disabled or the main dashboard isn't running — the next
                // StartLocalServerAsync picks up the new port.
                if (S.EnableReadOnlyMirror) _ = ApplyReadOnlyMirrorStateAsync();
            }
        }

        public bool IsLocalServerRunning => dashboardServer?.IsRunning == true;
        public string LocalServerUrl => dashboardServer?.Url ?? "";
        public string TailscaleUrl => dashboardServer?.TailscaleUrl ?? "";
        public bool HasTailscaleUrl => !string.IsNullOrEmpty(dashboardServer?.TailscaleUrl);
        public string ZeroTierUrl => dashboardServer?.ZeroTierUrl ?? "";
        public bool HasZeroTierUrl => !string.IsNullOrEmpty(dashboardServer?.ZeroTierUrl);
        public ButtonStatus LocalServerStatus { get; } = new ButtonStatus();

        public ICommand CopyLocalUrlCommand => new RelayCommand(async () => {
            if (!string.IsNullOrEmpty(LocalServerUrl))
                System.Windows.Clipboard.SetText(LocalServerUrl);
        });

        public ICommand CopyTailscaleUrlCommand => new RelayCommand(async () => {
            if (!string.IsNullOrEmpty(TailscaleUrl))
                System.Windows.Clipboard.SetText(TailscaleUrl);
        });

        public ICommand CopyZeroTierUrlCommand => new RelayCommand(async () => {
            if (!string.IsNullOrEmpty(ZeroTierUrl))
                System.Windows.Clipboard.SetText(ZeroTierUrl);
        });

        // ── Companion Pairing ────────────────────────────────────────────────
        // New per-companion token flow. Replaces the shared CompanionApiKey
        // long-term — see COMPANION_PAIRING_DESIGN.md. The plain token is
        // shown ONCE at generation time and never re-read; only the SHA-256
        // hash is persisted (in companion_tokens.json, separate from settings).

        public ObservableCollection<CompanionTokenView> PairedCompanions { get; } = new();
        public ObservableCollection<CompanionTokenView> UnpairedTokens   { get; } = new();

        public bool HasPairedCompanions => PairedCompanions.Count > 0;
        public bool HasUnpairedTokens   => UnpairedTokens.Count > 0;
        public bool HasAnyTokens        => HasPairedCompanions || HasUnpairedTokens;

        private string _newCompanionToken = "";
        // Plain token, hyphenated for display. Bound read-only to a TextBox so
        // the user can copy it. Cleared when the user dismisses the panel.
        public string NewCompanionToken {
            get => _newCompanionToken;
            private set { _newCompanionToken = value; RaisePropertyChanged(); }
        }

        private bool _showNewCompanionToken;
        // Drives a DataTrigger on the "new token" panel's Visibility. Flipped
        // true by GenerateCompanionTokenCommand; flipped false by Dismiss.
        public bool ShowNewCompanionToken {
            get => _showNewCompanionToken;
            private set { _showNewCompanionToken = value; RaisePropertyChanged(); }
        }

        public ICommand GenerateCompanionTokenCommand => new RelayCommand(async () => {
            var plain = GeneratePairingTokenPlain();
            CompanionTokenStore.Instance.Add(plain);   // fires Changed -> OnCompanionTokensChanged refreshes the lists
            NewCompanionToken     = FormatPairingTokenForDisplay(plain);
            ShowNewCompanionToken = true;
            await Task.CompletedTask;
        });

        public ICommand CopyNewCompanionTokenCommand => new RelayCommand(async () => {
            if (!string.IsNullOrEmpty(_newCompanionToken))
                System.Windows.Clipboard.SetText(_newCompanionToken);
            await Task.CompletedTask;
        });

        public ICommand DismissNewCompanionTokenCommand => new RelayCommand(async () => {
            NewCompanionToken     = "";
            ShowNewCompanionToken = false;
            await Task.CompletedTask;
        });

        // Called by Options.xaml.cs revoke-button click handler with the
        // entry's Id from the Button Tag. Confirms, revokes, refreshes.
        public void RevokeCompanionToken(string id) {
            if (string.IsNullOrEmpty(id)) return;
            var entry = CompanionTokenStore.Instance.FindById(id);
            var label = entry?.CompanionName ?? entry?.Name ?? id;
            var ok = System.Windows.MessageBox.Show(
                $"Revoke pairing for \"{label}\"?\n\nThe companion will stop syncing immediately and must be re-paired to resume.",
                "Revoke Companion Token",
                System.Windows.MessageBoxButton.OKCancel,
                System.Windows.MessageBoxImage.Warning);
            if (ok != System.Windows.MessageBoxResult.OK) return;
            CompanionTokenStore.Instance.Revoke(id);   // fires Changed -> OnCompanionTokensChanged refreshes the lists
        }

        // Store-change handler. Fires from a server thread when a companion pairs
        // or revokes over HTTP, or inline (UI thread) when the user generates /
        // revokes here. Marshals to the UI thread because RefreshCompanionTokens
        // edits ObservableCollections, which must happen on the dispatcher.
        private void OnCompanionTokensChanged() {
            var disp = System.Windows.Application.Current?.Dispatcher;
            if (disp != null && !disp.CheckAccess())
                disp.BeginInvoke(new Action(RefreshCompanionTokens));
            else
                RefreshCompanionTokens();
        }

        // Rebuilds PairedCompanions + UnpairedTokens from the store. Cheap —
        // store reads from JSON once on first access and cache-hits after.
        public void RefreshCompanionTokens() {
            var entries = CompanionTokenStore.Instance.List();
            PairedCompanions.Clear();
            UnpairedTokens.Clear();
            foreach (var e in entries) {
                if (e.IsRevoked) continue;
                var view = new CompanionTokenView(e);
                if (e.IsPaired) PairedCompanions.Add(view);
                else            UnpairedTokens.Add(view);
            }
            RaisePropertyChanged(nameof(HasPairedCompanions));
            RaisePropertyChanged(nameof(HasUnpairedTokens));
            RaisePropertyChanged(nameof(HasAnyTokens));
        }

        // 16-char token from base32 minus visually ambiguous characters
        // (0/O/1/I/L). Matches COMPANION_PAIRING_DESIGN — 80 bits of entropy.
        private static string GeneratePairingTokenPlain() {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var bytes = new byte[16];
            System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
            var sb = new System.Text.StringBuilder(16);
            foreach (var b in bytes) sb.Append(alphabet[b % alphabet.Length]);
            return sb.ToString();
        }

        // 4-4-4-4 with hyphens for human readability. The wizard strips
        // hyphens on submit so either form works on paste.
        private static string FormatPairingTokenForDisplay(string raw) {
            if (raw == null || raw.Length != 16) return raw ?? "";
            return $"{raw.Substring(0, 4)}-{raw.Substring(4, 4)}-{raw.Substring(8, 4)}-{raw.Substring(12, 4)}";
        }

        public int ReportDetailLevel {
            get => S.ReportDetailLevel;
            set {
                S.ReportDetailLevel     = value;
                S.ShowOverheadBreakdown = true;
                S.ShowSkyThumbnails     = true;
                S.ShowLiveStackImages   = true;
                S.ShowAltitudeChart     = true;
                S.ShowMoonCurve         = true;
                S.ShowMinAltitude       = true;
                S.ShowTSProgressBars    = true;
                S.ShowSessionHistory    = true;
                S.ShowStarCountCV       = true;
                S.ShowHFRGraph          = true;
                S.ShowChartTargetChips  = true;
                S.ShowChartFilterChips  = true;
                S.ShowChartAfMarkers    = true;
                S.ShowChartFlipMarkers  = true;
                S.ShowChartRoofMarkers  = false;
                S.ShowPerTargetIQ       = true;
                S.ShowNextNightPreview    = true;
                S.PreviewAltitudeDefault  = true;
                S.TimelineAltitudeDefault = true;
                SaveSettings();
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(ShowOverheadBreakdown));
                RaisePropertyChanged(nameof(ShowSkyThumbnails));
                RaisePropertyChanged(nameof(ShowLiveStackImages));
                RaisePropertyChanged(nameof(ShowAltitudeChart));
                RaisePropertyChanged(nameof(ShowMoonCurve));
                RaisePropertyChanged(nameof(ShowMinAltitude));
                RaisePropertyChanged(nameof(ShowTSProgressBars));
                RaisePropertyChanged(nameof(ShowSessionHistory));
                RaisePropertyChanged(nameof(ShowStarCountCV));
                RaisePropertyChanged(nameof(ShowHFRGraph));
                RaisePropertyChanged(nameof(ShowChartTargetChips));
                RaisePropertyChanged(nameof(ShowChartFilterChips));
                RaisePropertyChanged(nameof(ShowChartAfMarkers));
                RaisePropertyChanged(nameof(ShowChartFlipMarkers));
                RaisePropertyChanged(nameof(ShowChartRoofMarkers));
                RaisePropertyChanged(nameof(ShowPerTargetIQ));
                RaisePropertyChanged(nameof(ShowNextNightPreview));
                RaisePropertyChanged(nameof(PreviewAltitudeDefault));
                RaisePropertyChanged(nameof(TimelineAltitudeDefault));
            }
        }

        public bool ShowOverheadBreakdown {
            get => S.ShowOverheadBreakdown;
            set { S.ShowOverheadBreakdown = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool ShowSkyThumbnails {
            get => S.ShowSkyThumbnails;
            set { S.ShowSkyThumbnails = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool ShowLiveStackImages {
            get => S.ShowLiveStackImages;
            set { S.ShowLiveStackImages = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool ShowMoonCurve {
            get => S.ShowMoonCurve;
            set { S.ShowMoonCurve = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool ShowMinAltitude {
            get => S.ShowMinAltitude;
            set { S.ShowMinAltitude = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool ShowSessionHistory {
            get => S.ShowSessionHistory;
            set { S.ShowSessionHistory = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool ShowAltitudeChart {
            get => S.ShowAltitudeChart;
            set { S.ShowAltitudeChart = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool ShowTSProgressBars {
            get => S.ShowTSProgressBars && IsTsInstalled;
            set { S.ShowTSProgressBars = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool ShowStarCountCV {
            get => S.ShowStarCountCV;
            set { S.ShowStarCountCV = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool ShowHFRGraph {
            get => S.ShowHFRGraph;
            set { S.ShowHFRGraph = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool ShowChartAfMarkers {
            get => S.ShowChartAfMarkers;
            set { S.ShowChartAfMarkers = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool ShowChartFlipMarkers {
            get => S.ShowChartFlipMarkers;
            set { S.ShowChartFlipMarkers = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool ShowChartTargetChips {
            get => S.ShowChartTargetChips;
            set { S.ShowChartTargetChips = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool ShowChartFilterChips {
            get => S.ShowChartFilterChips;
            set { S.ShowChartFilterChips = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool ShowChartRoofMarkers {
            get => S.ShowChartRoofMarkers;
            set { S.ShowChartRoofMarkers = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool ShowPerTargetIQ {
            get => S.ShowPerTargetIQ;
            set { S.ShowPerTargetIQ = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool ReportLightMode {
            get => S.ReportLightMode;
            set { S.ReportLightMode = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool ExpandSectionsDefault {
            get => S.ExpandSectionsDefault;
            set { S.ExpandSectionsDefault = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public int ChartXAxisMetric {
            get => S.ChartXAxisMetric;
            set { S.ChartXAxisMetric = value; SaveSettings(); RaisePropertyChanged(); }
        }

        // ── Raw image thumbnails ─────────────────────────────────────────────
        // Off-by-default master toggle. When on, NS encodes a JPEG thumb per LIGHT
        // frame at save time — see RAW_THUMBNAILS_DESIGN.md.
        public bool CaptureRawThumbnails {
            get => S.CaptureRawThumbnails;
            set { S.CaptureRawThumbnails = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool CaptureMediumThumbnails {
            get => S.CaptureMediumThumbnails;
            set { S.CaptureMediumThumbnails = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public string ThumbnailRetentionMode {
            get => S.ThumbnailRetentionMode;
            set { S.ThumbnailRetentionMode = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public int ThumbnailRetentionDays {
            get => S.ThumbnailRetentionDays;
            set { S.ThumbnailRetentionDays = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public double ThumbnailRetentionMaxGB {
            get => S.ThumbnailRetentionMaxGB;
            set { S.ThumbnailRetentionMaxGB = value; SaveSettings(); RaisePropertyChanged(); }
        }

        // Custom thumbnail storage directory; empty = default. Path resolution lives
        // in Thumbnails.GetThumbnailsRoot — call sites use that helper, not this raw value.
        public string ThumbnailStorageDir {
            get => S.ThumbnailStorageDir;
            set { S.ThumbnailStorageDir = value ?? ""; SaveSettings(); RaisePropertyChanged(); }
        }

        public int ChartPrimaryMetric {
            get => S.ChartPrimaryMetric;
            set { S.ChartPrimaryMetric = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public int ChartSecondaryMetric {
            get => S.ChartSecondaryMetric;
            set { S.ChartSecondaryMetric = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public const int MaxAdditionalCharts = 4;

        private static readonly List<string> _primaryMetricNames = new List<string> {
            "HFR", "FWHM", "Guiding RMS", "Eccentricity", "Star Count",
            "Focuser Temp (°C)", "Ambient Temp (°C)", "Camera Temp (°C)", "Cooler Setpoint (°C)",
            "Altitude (°)", "Azimuth (°)", "Airmass",
            "Position Angle (°)", "Rotator Position (°)", "Focuser Position (steps)",
            "Seeing FWHM (arcsec)", "Sky Quality (mag/arcsec²)", "Sky Brightness (Lux)", "Cloud Cover (%)", "Sky Temp (°C)",
            "Humidity (%)", "Dew Point (°C)", "Wind Speed (m/s)", "Wind Gust (m/s)", "Wind Direction (°)", "Pressure (hPa)",
            "Exposure (s)", "Gain", "Offset",
            "Median ADU", "Mean ADU", "Std Deviation (ADU)", "MAD (ADU)", "Min ADU", "Max ADU"
        };

        private static readonly List<string> _secondaryMetricNames = new List<string> {
            "None", "HFR", "FWHM", "Guiding RMS", "Eccentricity", "Star Count",
            "Focuser Temp (°C)", "Ambient Temp (°C)", "Camera Temp (°C)", "Cooler Setpoint (°C)",
            "Altitude (°)", "Azimuth (°)", "Airmass",
            "Position Angle (°)", "Rotator Position (°)", "Focuser Position (steps)",
            "Seeing FWHM (arcsec)", "Sky Quality (mag/arcsec²)", "Sky Brightness (Lux)", "Cloud Cover (%)", "Sky Temp (°C)",
            "Humidity (%)", "Dew Point (°C)", "Wind Speed (m/s)", "Wind Gust (m/s)", "Wind Direction (°)", "Pressure (hPa)",
            "Exposure (s)", "Gain", "Offset",
            "Median ADU", "Mean ADU", "Std Deviation (ADU)", "MAD (ADU)", "Min ADU", "Max ADU"
        };

        private static readonly List<string> _xAxisMetricNames = new List<string> {
            "Time", "Frame Index",
            "HFR", "FWHM", "Guiding RMS", "Eccentricity", "Star Count",
            "Focuser Temp (°C)", "Ambient Temp (°C)", "Camera Temp (°C)", "Cooler Setpoint (°C)",
            "Altitude (°)", "Azimuth (°)", "Airmass",
            "Position Angle (°)", "Rotator Position (°)", "Focuser Position (steps)",
            "Seeing FWHM (arcsec)", "Sky Quality (mag/arcsec²)", "Sky Brightness (Lux)", "Cloud Cover (%)", "Sky Temp (°C)",
            "Humidity (%)", "Dew Point (°C)", "Wind Speed (m/s)", "Wind Gust (m/s)", "Wind Direction (°)", "Pressure (hPa)",
            "Exposure (s)", "Gain", "Offset",
            "Median ADU", "Mean ADU", "Std Deviation (ADU)", "MAD (ADU)", "Min ADU", "Max ADU"
        };

        public IReadOnlyList<string> PrimaryMetricNames  => _primaryMetricNames;
        public IReadOnlyList<string> SecondaryMetricNames => _secondaryMetricNames;
        public IReadOnlyList<string> XAxisMetricNames     => _xAxisMetricNames;

        private ObservableCollection<ChartConfig> _additionalCharts;
        public ObservableCollection<ChartConfig> AdditionalCharts {
            get {
                if (_additionalCharts == null) {
                    _additionalCharts = DeserializeChartConfigs(S.AdditionalChartConfigs);
                    _additionalCharts.CollectionChanged += (_, __) => {
                        SerializeChartConfigs();
                        RaisePropertyChanged(nameof(CanAddChart));
                    };
                }
                return _additionalCharts;
            }
        }

        public bool CanAddChart => AdditionalCharts.Count < MaxAdditionalCharts;

        public void AddAdditionalChart() {
            if (AdditionalCharts.Count >= MaxAdditionalCharts) return;
            AdditionalCharts.Add(new ChartConfig(0, 0, SerializeChartConfigs));
            RenumberCharts();
        }

        public void RemoveAdditionalChart(ChartConfig config) {
            AdditionalCharts.Remove(config);
            RenumberCharts();
        }

        private void RenumberCharts() {
            for (int i = 0; i < AdditionalCharts.Count; i++)
                AdditionalCharts[i].ChartNumber = i + 2;
        }

        private void SerializeChartConfigs() {
            S.AdditionalChartConfigs =
                string.Join("|", AdditionalCharts.Select(c => $"{c.Primary}:{c.Secondary}:{c.XAxis}"));
            SaveSettings();
        }

        private ObservableCollection<ChartConfig> DeserializeChartConfigs(string raw) {
            var col = new ObservableCollection<ChartConfig>();
            if (string.IsNullOrWhiteSpace(raw)) return col;
            foreach (var part in raw.Split('|')) {
                var tokens = part.Split(':');
                if (tokens.Length >= 2
                    && int.TryParse(tokens[0], out int p) && p >= 0 && p < _primaryMetricNames.Count
                    && int.TryParse(tokens[1], out int s) && s >= 0 && s <= _secondaryMetricNames.Count) {
                    int xAxis = tokens.Length >= 3 && int.TryParse(tokens[2], out int a) ? a : 0;
                    col.Add(new ChartConfig(p, s, SerializeChartConfigs, xAxis));
                }
            }
            for (int i = 0; i < col.Count; i++)
                col[i].ChartNumber = i + 2;
            return col;
        }

        public bool ShowNextNightPreview {
            get => S.ShowNextNightPreview && IsTsInstalled && IsTsApiEnabled;
            set { S.ShowNextNightPreview = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool PreviewAltitudeDefault {
            get => S.PreviewAltitudeDefault;
            set { S.PreviewAltitudeDefault = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool TimelineAltitudeDefault {
            get => S.TimelineAltitudeDefault;
            set { S.TimelineAltitudeDefault = value; SaveSettings(); RaisePropertyChanged(); }
        }

        public bool IsTsInstalled => TargetSchedulerDatabase.IsPluginInstalled;

        public bool IsTsApiEnabled {
            get {
                if (!IsTsInstalled) return false;
                var tsDb = new TargetSchedulerDatabase();
                if (!tsDb.IsAvailable) return false;
                var profileId = profileService?.ActiveProfile?.Id.ToString();
                var (enabled, _) = tsDb.GetApiSettings(profileId);
                return enabled;
            }
        }

        // ── Filter classification ──

        private ObservableCollection<FilterClassificationItem> _filterItems = new ObservableCollection<FilterClassificationItem>();
        public ObservableCollection<FilterClassificationItem> FilterItems {
            get => _filterItems;
            private set { _filterItems = value; RaisePropertyChanged(); }
        }

        public ICommand RefreshFiltersCommand { get; private set; }

        private bool _loadingFilters;

        private void LoadFilterClassifications() {
            try {
                var filters = profileService?.ActiveProfile?.FilterWheelSettings?.FilterWheelFilters;
                if (filters == null || filters.Count == 0) return;

                var saved      = ParseFilterClassifications(S.FilterClassifications);
                var savedTypes = ParseFilterClassifications(S.FilterTypeOverrides);

                // Also preserve any classifications/types already in the UI (for refresh)
                foreach (var existing in FilterItems) {
                    if (existing.Classification != "A" && !saved.ContainsKey(existing.Name))
                        saved[existing.Name] = existing.Classification;
                    if (existing.FilterType != "A" && !savedTypes.ContainsKey(existing.Name))
                        savedTypes[existing.Name] = existing.FilterType;
                }

                _loadingFilters = true;
                try {
                    System.Windows.Application.Current.Dispatcher.Invoke(() => {
                        // Build new filter names from profile
                        var profileNames = new HashSet<string>(
                            filters.Where(f => !string.IsNullOrWhiteSpace(f.Name)).Select(f => f.Name));

                        // Remove filters no longer in profile
                        for (int i = FilterItems.Count - 1; i >= 0; i--) {
                            if (!profileNames.Contains(FilterItems[i].Name))
                                FilterItems.RemoveAt(i);
                        }

                        // Add new filters, preserve existing
                        var existingNames = new HashSet<string>(FilterItems.Select(f => f.Name));
                        foreach (var f in filters) {
                            if (string.IsNullOrWhiteSpace(f.Name) || existingNames.Contains(f.Name)) continue;
                            var item = new FilterClassificationItem(f.Name, this);
                            if (saved.TryGetValue(f.Name, out var cls))
                                item.Classification = cls;
                            if (savedTypes.TryGetValue(f.Name, out var tp))
                                item.FilterType = tp;
                            FilterItems.Add(item);
                        }

                        // Restore classifications/types for existing items that may have been reset
                        foreach (var item in FilterItems) {
                            if (saved.TryGetValue(item.Name, out var cls) && item.Classification != cls)
                                item.Classification = cls;
                            if (savedTypes.TryGetValue(item.Name, out var tp) && item.FilterType != tp)
                                item.FilterType = tp;
                        }
                    });
                } finally {
                    _loadingFilters = false;
                }
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to load filter classifications. {ex.Message}");
            }
        }

        internal void SaveFilterClassifications() {
            if (_loadingFilters) return;
            var classParts = FilterItems
                .Where(f => f.Classification != "A")
                .Select(f => $"{f.Name}={f.Classification}");
            S.FilterClassifications = string.Join(",", classParts);

            var typeParts = FilterItems
                .Where(f => f.FilterType != "A")
                .Select(f => $"{f.Name}={f.FilterType}");
            S.FilterTypeOverrides = string.Join(",", typeParts);

            SaveSettings();
        }

        internal static Dictionary<string, string> ParseFilterClassifications(string raw) =>
            FilterHelper.ParseClassifications(raw);

        private void LoadSessions() {
            try {
                if (!File.Exists(liveDbPath)) return;
                var db       = new SessionDatabase(liveDbPath);
                var sessions = db.GetRecentSessions(30);
                System.Windows.Application.Current.Dispatcher.Invoke(() => {
                    // Reset selection before repopulating so delete/refresh always lands
                    // on the newest session rather than holding a dangling reference.
                    SelectedSession = null;
                    AvailableSessions.Clear();
                    foreach (var s in sessions)
                        AvailableSessions.Add(s);
                    if (AvailableSessions.Count > 0)
                        SelectedSession = AvailableSessions[0];
                });
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to load session list. {ex.Message}");
            }
        }

        public ICommand TestEmailCommand { get; }
        public ICommand TestDiscordCommand { get; }
        public ICommand TestPushoverCommand { get; }
        public ICommand TestDashboardCommand { get; }
        public ICommand UploadAllToDashboardCommand { get; }
        public ICommand SendTestReportCommand { get; }
        public ICommand ResendLastSessionCommand { get; }
        public ICommand ResendSessionCommand { get; }
        public ICommand DeleteSessionCommand { get; }
        public ICommand RefreshSessionsCommand { get; }
        public ICommand SearchSessionsCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand PreviewReportCommand { get; private set; }
        public ICommand StartLocalServerCommand { get; }
        public ICommand StopLocalServerCommand { get; }
        public ICommand GenerateAllDashboardReportsCommand { get; }
        public ICommand ImportTsThumbnailsCommand { get; private set; }
        public ButtonStatus GenerateDashboardReportsStatus { get; } = new ButtonStatus();

        // Compact mm:ss / h:mm formatter for the import ETA. Sub-minute durations
        // use seconds so users see live countdown on small libraries.
        private static string FormatRemaining(TimeSpan ts) {
            if (ts.TotalSeconds < 60)  return $"{(int)Math.Ceiling(ts.TotalSeconds)}s";
            if (ts.TotalMinutes < 60)  return $"{(int)ts.TotalMinutes}m {ts.Seconds:00}s";
            return $"{(int)ts.TotalHours}h {ts.Minutes:00}m";
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void RaisePropertyChanged([CallerMemberName] string propertyName = null) {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Bindable status indicator for action buttons in the Options UI.
    /// Text starting with "✓" is shown in green; anything else in red.
    /// </summary>
    public class ButtonStatus : INotifyPropertyChanged {
        private string _text = "";
        public string Text {
            get => _text;
            set {
                _text = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Foreground)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Visibility)));
            }
        }

        public System.Windows.Media.Brush Foreground =>
            _text.StartsWith("✓")
                ? System.Windows.Media.Brushes.LightGreen
                : System.Windows.Media.Brushes.Salmon;

        public System.Windows.Visibility Visibility =>
            string.IsNullOrEmpty(_text)
                ? System.Windows.Visibility.Collapsed
                : System.Windows.Visibility.Visible;

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>
    /// Minimal async-capable relay command for the Options UI.
    /// </summary>
    /// <summary>
    /// UI projection of <see cref="CompanionTokenEntry"/> for the Options
    /// panel's pairing lists. Just enough fields for the row template +
    /// a humanized "paired 2d ago" string; the Id is what the Revoke
    /// button passes back through its Tag.
    /// </summary>
    public class CompanionTokenView {
        public string Id            { get; }
        public string DisplayName   { get; }
        public string TimestampText { get; }
        public bool   IsPaired      { get; }

        public CompanionTokenView(CompanionTokenEntry e) {
            Id          = e.Id;
            IsPaired    = e.IsPaired;
            DisplayName = !string.IsNullOrWhiteSpace(e.CompanionName) ? e.CompanionName!
                        : !string.IsNullOrWhiteSpace(e.Name)          ? e.Name!
                        : "(unnamed)";

            if (e.IsPaired && e.PairedAt.HasValue) {
                TimestampText = $"paired {Relative(DateTime.UtcNow - e.PairedAt.Value)}";
            } else {
                TimestampText = $"created {Relative(DateTime.UtcNow - e.CreatedAt)} — not yet claimed";
            }
        }

        private static string Relative(TimeSpan span) {
            if (span.TotalSeconds < 60) return "just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
            if (span.TotalHours   < 24) return $"{(int)span.TotalHours}h ago";
            if (span.TotalDays    < 30) return $"{(int)span.TotalDays}d ago";
            return $"{(int)(span.TotalDays / 30)}mo ago";
        }
    }

    internal class RelayCommand : ICommand {
        private readonly Func<Task> execute;
        private bool isExecuting;

        public RelayCommand(Func<Task> execute) {
            this.execute = execute;
        }

        public bool CanExecute(object parameter) => !isExecuting;

        public async void Execute(object parameter) {
            isExecuting = true;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            try {
                await execute();
            } catch (Exception ex) {
                // async-void exceptions otherwise vanish into the SynchronizationContext.
                // Log here so a failed Delete/Search command surfaces in the NINA log
                // instead of looking like a silent no-op in the UI.
                Logger.Error($"NightSummary: RelayCommand failed. {ex}");
            } finally {
                isExecuting = false;
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler CanExecuteChanged;
    }

    /// <summary>
    /// Represents a single filter with its user-assigned classification.
    /// Classification values: A=Auto (first-letter matching), B=Broadband, N=Narrowband, X=Exclude.
    /// </summary>
    public class FilterClassificationItem : INotifyPropertyChanged {
        private readonly NightSummaryPlugin plugin;
        private string _classification = "A";
        private string _filterType     = "A";

        private static readonly string[] TypeCodes = { "A", "L", "R", "G", "B", "H", "S", "O" };

        public FilterClassificationItem(string name, NightSummaryPlugin plugin) {
            Name = name;
            this.plugin = plugin;
        }

        public string Name { get; }

        public string Classification {
            get => _classification;
            set {
                if (_classification == value) return;
                _classification = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Classification)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ClassificationIndex)));
                plugin?.SaveFilterClassifications();
            }
        }

        /// <summary>ComboBox binding: 0=Auto, 1=Broadband, 2=Narrowband, 3=Exclude</summary>
        public int ClassificationIndex {
            get {
                switch (_classification) {
                    case "B": return 1;
                    case "N": return 2;
                    case "X": return 3;
                    default:  return 0;
                }
            }
            set {
                switch (value) {
                    case 1:  Classification = "B"; break;
                    case 2:  Classification = "N"; break;
                    case 3:  Classification = "X"; break;
                    default: Classification = "A"; break;
                }
            }
        }

        /// <summary>
        /// Canonical filter type for dashboard color pills (A=Auto, L, R, G, B, H, S, O).
        /// Auto falls back to first-letter matching in the dashboard.
        /// </summary>
        public string FilterType {
            get => _filterType;
            set {
                if (_filterType == value) return;
                _filterType = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FilterType)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FilterTypeIndex)));
                plugin?.SaveFilterClassifications();
            }
        }

        /// <summary>ComboBox binding: 0=Auto, 1=L, 2=R, 3=G, 4=B, 5=H, 6=S, 7=O</summary>
        public int FilterTypeIndex {
            get {
                var idx = Array.IndexOf(TypeCodes, _filterType);
                return idx >= 0 ? idx : 0;
            }
            set {
                FilterType = (value >= 0 && value < TypeCodes.Length) ? TypeCodes[value] : "A";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class ChartConfig : INotifyPropertyChanged {
        private readonly Action _onChanged;
        private int _primary;
        private int _secondary;
        private int _xAxis;

        public ChartConfig(int primary, int secondary, Action onChanged, int xAxis = 0) {
            _primary   = primary;
            _secondary = secondary;
            _xAxis     = xAxis;
            _onChanged = onChanged;
        }

        public int Primary {
            get => _primary;
            set { _primary = value; OnPropertyChanged(); _onChanged(); }
        }

        public int Secondary {
            get => _secondary;
            set { _secondary = value; OnPropertyChanged(); _onChanged(); }
        }

        public int XAxis {
            get => _xAxis;
            set { _xAxis = value; OnPropertyChanged(); _onChanged(); }
        }

        private int _chartNumber;
        public int ChartNumber {
            get => _chartNumber;
            set { _chartNumber = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
