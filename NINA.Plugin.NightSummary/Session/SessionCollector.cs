using NINA.Core.Enum;
using NINA.Core.Utility;
using NINA.Image;
using NINA.Image.Interfaces;
using NINA.Plugin.NightSummary.Data;
using NINA.Sequencer.Interfaces;
using NINA.Sequencer.Interfaces.Mediator;
using NINA.Sequencer.SequenceItem;
using NINA.WPF.Base.Interfaces.Mediator;
using NINA.WPF.Base.Interfaces.ViewModel;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace NINA.Plugin.NightSummary.Session {
    public class SessionCollector {
        // Lazy access to plugin settings — SessionCollector is constructed before
        // SettingsManager init in some test paths, so resolve on demand.
        private static NightSummarySettings S => SettingsManager.Instance.Current;

        // Default %LOCALAPPDATA%\NINA\NightSummary\thumbs, or the user override
        // from S.ThumbnailStorageDir if set. Resolved per-call so a settings change
        // takes effect on the next save without restarting the collector.
        private static string ThumbsRoot => Thumbnails.GetThumbnailsRoot(S?.ThumbnailStorageDir);

        private readonly SessionDatabase database;
        private readonly IImageSaveMediator imageSaveMediator;
        private readonly ISequenceMediator sequenceMediator;
        private readonly IThumbnailVM thumbnailVM;
        private SessionRecord currentSession;
        private bool isCollecting = false;

        // Skipped exposure tracking
        private Timer skipPollTimer;
        private readonly HashSet<int> trackedItems = new HashSet<int>();
        private int skippedExposures = 0;

        // Manual grading tracking (thumbnail subscription — dormant, ThumbnailVM is internal)
        private readonly HashSet<Thumbnail> subscribedThumbnails = new HashSet<Thumbnail>();

        // File-based manual grade tracking via FileSystemWatcher
        private readonly ConcurrentDictionary<string, DateTime> _pathToTimestamp
            = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FileSystemWatcher> _directoryWatchers
            = new Dictionary<string, FileSystemWatcher>(StringComparer.OrdinalIgnoreCase);
        private readonly object _watcherLock = new object();

        public SessionDatabase Database { get; private set; }
        public int SkippedExposures => skippedExposures;

        public event EventHandler FirstImageSaved;
        private bool firstImageFired = false;

        public SessionCollector(IImageSaveMediator imageSaveMediator, ISequenceMediator sequenceMediator, SessionDatabase database, IThumbnailVM thumbnailVM = null) {
            this.imageSaveMediator = imageSaveMediator;
            this.sequenceMediator = sequenceMediator;
            this.database = database;
            this.Database = database;
            this.thumbnailVM = thumbnailVM;
        }

        public void StartSession(string profileName) {
            if (isCollecting) {
                Logger.Warning("NightSummary: StartSession called but a session is already active. Ending previous session first.");
                EndSession();
            }
            currentSession = new SessionRecord {
                SessionId = Guid.NewGuid().ToString(),
                SessionStart = Clock.Now(),
                ProfileName = profileName,
                ReportSent = false
            };
            database.CreateSession(currentSession);
            imageSaveMediator.ImageSaved += OnImageSaved;

            // Subscribe to manual grading events from NINA's thumbnail panel
            if (thumbnailVM != null) {
                ((INotifyCollectionChanged)thumbnailVM.Thumbnails).CollectionChanged += OnThumbnailsChanged;
                subscribedThumbnails.Clear();
            }

            // Reset file-based grade tracking
            _pathToTimestamp.Clear();
            lock (_watcherLock) {
                foreach (var w in _directoryWatchers.Values) { w.Renamed -= OnFileRenamed; w.Dispose(); }
                _directoryWatchers.Clear();
            }

            // Start monitoring for skipped exposures
            firstImageFired = false;
            skippedExposures = 0;
            trackedItems.Clear();
            if (!Clock.DisableSkipPolling)
                skipPollTimer = new Timer(PollRunningItems, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));

            isCollecting = true;
            Logger.Info($"NightSummary: Session started. SessionId={currentSession.SessionId}");
        }

        public void EndSession() {
            if (!isCollecting) return;
            imageSaveMediator.ImageSaved -= OnImageSaved;

            // Unsubscribe manual grading listeners
            if (thumbnailVM != null) {
                ((INotifyCollectionChanged)thumbnailVM.Thumbnails).CollectionChanged -= OnThumbnailsChanged;
                foreach (var t in subscribedThumbnails)
                    ((INotifyPropertyChanged)t).PropertyChanged -= OnThumbnailPropertyChanged;
                subscribedThumbnails.Clear();
            }

            // Dispose file-based grade watchers
            lock (_watcherLock) {
                foreach (var w in _directoryWatchers.Values) { w.Renamed -= OnFileRenamed; w.Dispose(); }
                _directoryWatchers.Clear();
            }
            _pathToTimestamp.Clear();

            // Stop skip monitoring
            skipPollTimer?.Dispose();
            skipPollTimer = null;

            if (skippedExposures > 0)
                Logger.Info($"NightSummary: {skippedExposures} exposure(s) were aborted during session");

            isCollecting = false;
            database.FinalizeSession(currentSession.SessionId, Clock.Now(), false, skippedExposures);
            Logger.Info($"NightSummary: Session ended. SessionId={currentSession.SessionId}");
            currentSession = null;
        }

        private void PollRunningItems(object state) {
            try {
                if (sequenceMediator == null || !sequenceMediator.Initialized) return;

                var items = sequenceMediator.GetAdvancedSequencerCurrentRunningItems();
                if (items == null) return;

                foreach (var item in items) {
                    if (item is IExposureItem exposureItem && exposureItem.ImageType == "LIGHT") {
                        var hash = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(item);
                        if (trackedItems.Add(hash)) {
                            ((INotifyPropertyChanged)item).PropertyChanged += OnExposureStatusChanged;
                        }
                    }
                }
            } catch (Exception ex) {
                Logger.Debug($"NightSummary: Skip monitor poll error: {ex.Message}");
            }
        }

        private void OnExposureStatusChanged(object sender, PropertyChangedEventArgs e) {
            if (e.PropertyName != "Status") return;

            var item = sender as ISequenceItem;
            if (item == null) return;

            if (item.Status == SequenceEntityStatus.SKIPPED ||
                item.Status == SequenceEntityStatus.FAILED) {
                Interlocked.Increment(ref skippedExposures);
                var reason = item.Status == SequenceEntityStatus.FAILED ? "failed" : "skipped";
                Logger.Info($"NightSummary: Exposure aborted ({reason}, total aborted: {skippedExposures})");
            }

            // Unsubscribe once we have a terminal status
            if (item.Status == SequenceEntityStatus.SKIPPED ||
                item.Status == SequenceEntityStatus.FINISHED ||
                item.Status == SequenceEntityStatus.FAILED) {
                ((INotifyPropertyChanged)item).PropertyChanged -= OnExposureStatusChanged;
            }
        }

        public string GetCurrentSessionId() {
            return currentSession?.SessionId;
        }

        private void OnImageSaved(object sender, ImageSavedEventArgs e) {
            try {
                // Only record LIGHT frames — skip darks, flats, bias, etc.
                var imageType = e.MetaData?.Image?.ImageType;
                if (!"LIGHT".Equals(imageType, StringComparison.OrdinalIgnoreCase))
                    return;

                if (!firstImageFired) {
                    firstImageFired = true;
                    FirstImageSaved?.Invoke(this, EventArgs.Empty);
                }

                // Read guiding scale from NINA - this converts pixels to arcseconds
                // Default to 1 if not available so values are still stored (as pixels)
                double guidingScale = e.MetaData?.Image?.RecordedRMS?.Scale ?? 1;

                // Read FWHM and Eccentricity via reflection — only present if Hocus Focus is installed
                double fwhm = 0, eccentricity = 0;
                var analysis = e.StarDetectionAnalysis;
                if (analysis != null) {
                    var type = analysis.GetType();
                    fwhm = ReadDouble(type.GetProperty("FWHM"), analysis);
                    eccentricity = ReadDouble(type.GetProperty("Eccentricity"), analysis);
                }

                // Focuser and ambient temperatures — null when device not connected or no sensor
                double? focuserTemp = null;
                double? ambientTemp = null;
                try {
                    var ft = e.MetaData?.Focuser?.Temperature ?? double.NaN;
                    if (!double.IsNaN(ft)) focuserTemp = ft;
                    var at = e.MetaData?.WeatherData?.Temperature ?? double.NaN;
                    if (!double.IsNaN(at)) ambientTemp = at;
                } catch { /* not critical if temperature capture fails */ }

                // Use ExposureStart from NINA's image metadata so this column means
                // the same thing as the FITS DATE-OBS header, the filename's $$DATETIME$$
                // token, and Target Scheduler's acquireddate column. Falls back to wall
                // clock if metadata is missing (defensive — should never happen on a real
                // ImageSaved event). Pre-fix legacy rows captured Clock.Now (ImageSaved
                // time, ~exposureDuration later); the importer/augment paths apply an
                // ExposureDuration offset to bridge the conventions.
                var exposureStart = e.MetaData?.Image?.ExposureStart;
                var record = new ImageRecord {
                    SessionId        = currentSession.SessionId,
                    Timestamp        = exposureStart.HasValue && exposureStart.Value > DateTime.MinValue
                                         ? exposureStart.Value.ToLocalTime()
                                         : Clock.Now(),
                    TargetName       = e.MetaData?.Target?.Name ?? "Unknown",
                    Filter           = e.MetaData?.FilterWheel?.Filter ?? "None",
                    ExposureDuration = e.MetaData?.Image?.ExposureTime ?? 0,
                    HFR              = e.StarDetectionAnalysis?.HFR ?? 0,
                    FWHM             = fwhm,
                    Eccentricity     = eccentricity,
                    StarCount        = e.StarDetectionAnalysis?.DetectedStars ?? 0,
                    // Multiply RMS by Scale to store in arcseconds
                    GuidingRMSTotal  = (e.MetaData?.Image?.RecordedRMS?.Total ?? 0) * guidingScale,
                    GuidingScale     = guidingScale,
                    Accepted         = true,
                    RaHours          = e.MetaData?.Target?.Coordinates?.RA  ?? 0,
                    DecDegrees       = e.MetaData?.Target?.Coordinates?.Dec ?? 0,
                    FocuserTemp      = focuserTemp,
                    AmbientTemp      = ambientTemp,
                    // Camera acquisition parameters
                    Gain             = e.MetaData?.Camera?.Gain   ?? -1,
                    Offset           = e.MetaData?.Camera?.Offset ?? -1,
                    Binning          = e.MetaData?.Camera?.BinX   ?? 0,
                    CameraTemp       = NullIfNaN(e.MetaData?.Camera?.Temperature),
                    CoolerSetpoint   = NullIfNaN(e.MetaData?.Camera?.SetPoint),
                    // Equipment state
                    FocuserPosition  = e.MetaData?.Focuser?.Position,
                    RotatorPosition  = NullIfNaN(e.MetaData?.Rotator?.Position),
                    PositionAngle    = NullIfNaN(e.MetaData?.Target?.PositionAngle),
                    // Extended weather
                    Humidity         = NullIfNaN(e.MetaData?.WeatherData?.Humidity),
                    DewPoint         = NullIfNaN(e.MetaData?.WeatherData?.DewPoint),
                    WindSpeed        = NullIfNaN(e.MetaData?.WeatherData?.WindSpeed),
                    Pressure         = NullIfNaN(e.MetaData?.WeatherData?.Pressure),
                    SkyBrightness    = NullIfNaN(e.MetaData?.WeatherData?.SkyBrightness),
                    SkyTemperature   = NullIfNaN(e.MetaData?.WeatherData?.SkyTemperature),
                    WindDirection    = NullIfNaN(e.MetaData?.WeatherData?.WindDirection),
                    WindGust         = NullIfNaN(e.MetaData?.WeatherData?.WindGust),
                    // TS grading fields — populated at session end via UpdateImageGradingFromTs
                    GradingStatus    = -1,
                    // Frame type
                    ImageType        = imageType ?? "",
                    // Telescope pointing
                    Altitude         = NullIfNaN(e.MetaData?.Telescope?.Altitude),
                    Azimuth          = NullIfNaN(e.MetaData?.Telescope?.Azimuth),
                    Airmass          = NullIfNaN(e.MetaData?.Telescope?.Airmass),
                    SideOfPier       = e.MetaData?.Telescope?.SideOfPier.ToString(),
                    // Camera readout mode
                    ReadoutMode      = string.IsNullOrEmpty(e.MetaData?.Camera?.ReadoutModeName) ? null : e.MetaData.Camera.ReadoutModeName,
                    // Sky conditions
                    SkyQuality       = NullIfNaN(e.MetaData?.WeatherData?.SkyQuality),
                    CloudCover       = NullIfNaN(e.MetaData?.WeatherData?.CloudCover),
                    // ASCOM seeing monitor
                    SeeingFWHM       = NullIfNaN(e.MetaData?.WeatherData?.StarFWHM),
                    // Image statistics
                    StatMedian       = NullIfNaN(e.Statistics?.Median),
                    StatMean         = NullIfNaN(e.Statistics?.Mean),
                    StatStDev        = NullIfNaN(e.Statistics?.StDev),
                    StatMAD          = NullIfNaN(e.Statistics?.MedianAbsoluteDeviation),
                    StatMin          = e.Statistics != null ? (int?)e.Statistics.Min : null,
                    StatMax          = e.Statistics != null ? (int?)e.Statistics.Max : null,
                    StatBitDepth     = e.Statistics != null ? (int?)e.Statistics.BitDepth : null
                };

                // Capture FITS path on the row itself (used by future re-stretch features).
                // FileSystemWatcher path tracking continues alongside for grade-rename detection.
                var filePath = e.PathToImage?.LocalPath;
                record.FilePath = filePath;

                long rowId = database.SaveImageRecord(record);
                Logger.Debug($"NightSummary: Recorded image - Target={record.TargetName}, Filter={record.Filter}, HFR={record.HFR:F2}, GuidingRMS={record.GuidingRMSTotal:F2}\"");

                // Track file path so FileSystemWatcher can match renames to DB records
                if (!string.IsNullOrEmpty(filePath)) {
                    _pathToTimestamp[filePath] = record.Timestamp;
                    EnsureWatching(Path.GetDirectoryName(filePath));
                }

                // Raw image thumbnail capture — gated, off by default.
                // Inline encode follows TS pattern (Thumbnails.cs in TS source). 5–15ms
                // for 192px output; not worth a background queue.
                TryCaptureThumbnails(e.Image, rowId, currentSession?.SessionId);
            } catch (Exception ex) {
                Logger.Error($"NightSummary: Failed to record image. {ex.Message}");
            }
        }

        private void EnsureWatching(string directory) {
            if (string.IsNullOrEmpty(directory)) return;
            lock (_watcherLock) {
                if (_directoryWatchers.ContainsKey(directory)) return;
                try {
                    var watcher = new FileSystemWatcher(directory) {
                        NotifyFilter = NotifyFilters.FileName,
                        EnableRaisingEvents = true
                    };
                    watcher.Renamed += OnFileRenamed;
                    _directoryWatchers[directory] = watcher;
                    Logger.Debug($"NightSummary: Watching {directory} for manual grade changes");
                } catch (Exception ex) {
                    Logger.Warning($"NightSummary: Could not watch {directory} for grade changes: {ex.Message}");
                }
            }
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e) {
            if (currentSession == null) return;
            try {
                var oldName = Path.GetFileName(e.OldFullPath);
                var newName = Path.GetFileName(e.FullPath);

                if (newName.StartsWith("BAD_", StringComparison.OrdinalIgnoreCase)) {
                    // Reject: image.fits → BAD_image.fits
                    if (_pathToTimestamp.TryRemove(e.OldFullPath, out var ts)) {
                        _pathToTimestamp[e.FullPath] = ts;
                        int rows = database.UpdateImageAccepted(currentSession.SessionId, ts, accepted: false, rejectReason: "Manual");
                        Logger.Debug($"NightSummary: Manual reject — {oldName} → {newName} ({rows} row(s) updated)");
                    }
                } else if (oldName.StartsWith("BAD_", StringComparison.OrdinalIgnoreCase)) {
                    // Un-reject: BAD_image.fits → image.fits
                    if (_pathToTimestamp.TryRemove(e.OldFullPath, out var ts)) {
                        _pathToTimestamp[e.FullPath] = ts;
                        int rows = database.UpdateImageAccepted(currentSession.SessionId, ts, accepted: true, rejectReason: "");
                        Logger.Debug($"NightSummary: Manual un-reject — {oldName} → {newName} ({rows} row(s) updated)");
                    }
                }
            } catch (Exception ex) {
                Logger.Warning($"NightSummary: Error in file rename handler: {ex.Message}");
            }
        }

        private static double ReadDouble(PropertyInfo prop, object obj) {
            if (prop == null) return 0;
            try { return Convert.ToDouble(prop.GetValue(obj)); } catch { return 0; }
        }

        private static double? NullIfNaN(double? value) =>
            value.HasValue && !double.IsNaN(value.Value) ? value : null;

        // ── Raw image thumbnail capture ─────────────────────────────────────
        // See RAW_THUMBNAILS_DESIGN.md. Gated by CaptureRawThumbnails (master) +
        // CaptureMediumThumbnails (extra _md output). Failure here never fails
        // the parent OnImageSaved — capture is best-effort.
        private void TryCaptureThumbnails(System.Windows.Media.Imaging.BitmapSource src, long imageId, string sessionId) {
            try {
                if (src == null || imageId <= 0 || string.IsNullOrEmpty(sessionId)) return;
                if (!S.CaptureRawThumbnails) return;

                int versionMask = 0;
                var smallPath = Thumbnails.GetThumbnailPath(ThumbsRoot, sessionId, imageId, Thumbnails.VersionSmall);
                var (sw, sh, sd) = Thumbnails.Encode(src, Thumbnails.SmallHeightPx);
                if (sd != null && Thumbnails.WriteToDisk(smallPath, sd))
                    versionMask |= Thumbnails.VersionSmall;

                if (S.CaptureMediumThumbnails) {
                    var medPath = Thumbnails.GetThumbnailPath(ThumbsRoot, sessionId, imageId, Thumbnails.VersionMedium);
                    var (mw, mh, md) = Thumbnails.Encode(src, Thumbnails.MediumHeightPx);
                    if (md != null && Thumbnails.WriteToDisk(medPath, md))
                        versionMask |= Thumbnails.VersionMedium;
                }

                if (versionMask != 0) {
                    database.UpdateImageThumbnailVersion(imageId, versionMask);
                    Logger.Debug($"NightSummary: thumbnail saved — id={imageId}, mask={versionMask}");
                }
            } catch (Exception ex) {
                Logger.Warning($"NightSummary: thumbnail capture failed (id={imageId}): {ex.Message}");
            }
        }

        // ── Manual rejection tracking ────────────────────────────────────────

        private void OnThumbnailsChanged(object sender, NotifyCollectionChangedEventArgs e) {
            if (e.NewItems == null) return;
            foreach (Thumbnail t in e.NewItems) {
                if (subscribedThumbnails.Add(t))
                    ((INotifyPropertyChanged)t).PropertyChanged += OnThumbnailPropertyChanged;
            }
        }

        private void OnThumbnailPropertyChanged(object sender, PropertyChangedEventArgs e) {
            if (e.PropertyName != nameof(Thumbnail.Grade)) return;
            if (currentSession == null) return;
            var t = (Thumbnail)sender;
            // NINA cycles Grade: "" (accepted) → "BAD" (rejected) → "" (accepted again)
            bool accepted = string.IsNullOrEmpty(t.Grade);
            try {
                int rows = database.UpdateImageAccepted(currentSession.SessionId, t.Date, accepted);
                Logger.Debug($"NightSummary: Manual grade '{(accepted ? "accepted" : "rejected")}' for image at {t.Date:HH:mm:ss} ({rows} row(s) updated)");
            } catch (Exception ex) {
                Logger.Warning($"NightSummary: Failed to record manual grade: {ex.Message}");
            }
        }
    }
}