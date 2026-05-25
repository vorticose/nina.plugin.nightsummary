using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Reporting;
using NINA.Plugin.NightSummary.Session;
using NINA.Plugin.NightSummary.Tests.Fixtures;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    public class LiveStackTests : IDisposable {
        private readonly string _tempDir;

        public LiveStackTests() {
            _tempDir = Path.Combine(Path.GetTempPath(), "NightSummary_LiveStackTests_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(_tempDir);

            SettingsManager.Instance.Current.ReportLightMode        = false;
            SettingsManager.Instance.Current.ReportDetailLevel      = 2;
            SettingsManager.Instance.Current.ShowHFRGraph           = false;
            SettingsManager.Instance.Current.ShowStarCountCV        = false;
            SettingsManager.Instance.Current.ShowPerTargetIQ        = false;
            SettingsManager.Instance.Current.ShowSessionHistory     = false;
            SettingsManager.Instance.Current.ShowNextNightPreview   = false;
            SettingsManager.Instance.Current.AdditionalChartConfigs = "";
            SettingsManager.Instance.Current.ExpandSectionsDefault  = false;
            SettingsManager.Instance.Current.ShowLiveStackImages    = true;
        }

        public void Dispose() {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }

        private static LiveStackImage MakeLiveStackImage(string target, string filter, bool isMono = true, int stackCount = 5) {
            // Create a minimal valid JPEG (1x1 pixel)
            var jpegData = CreateMinimalJpeg();
            return new LiveStackImage {
                Target = target,
                Filter = filter,
                IsMonochrome = isMono,
                JpegData = jpegData,
                MasterJpegData = jpegData,
                StackCount = stackCount,
                RedStackCount = isMono ? null : stackCount,
                GreenStackCount = isMono ? null : stackCount,
                BlueStackCount = isMono ? null : stackCount
            };
        }

        private static byte[] CreateMinimalJpeg() {
            // Create a real 1x1 JPEG using WPF encoder so it can be decoded back
            var bitmap = new System.Windows.Media.Imaging.WriteableBitmap(1, 1, 96, 96,
                System.Windows.Media.PixelFormats.Gray8, null);
            bitmap.WritePixels(new System.Windows.Int32Rect(0, 0, 1, 1), new byte[] { 128 }, 1, 0);
            var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder { QualityLevel = 75 };
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }

        // ─── SaveLiveStackMasters / LoadLiveStackMasters roundtrip ───

        [Fact]
        public void SaveAndLoadMasters_Roundtrip() {
            var images = new List<LiveStackImage> {
                MakeLiveStackImage("M42", "H", stackCount: 10),
                MakeLiveStackImage("M42", "S", stackCount: 8),
                MakeLiveStackImage("M42", "O", stackCount: 6)
            };

            var reportData = TestDataFactory.MakeReportData(targets: new[] { "M42" });
            reportData.LiveStackImages = images;

            // Save
            var reportFilename = "TestReport.html";
            var sessionDir = Path.Combine(_tempDir, "TestReport");
            Directory.CreateDirectory(sessionDir);
            // Call the save method via reflection since it's private static
            var saveMethod = typeof(SessionService).GetMethod("SaveLiveStackMasters",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            saveMethod!.Invoke(null, new object[] { sessionDir, reportFilename, reportData });

            // Verify files exist
            var assetsDir = Path.Combine(sessionDir, "assets");
            Assert.True(Directory.Exists(assetsDir));
            Assert.True(File.Exists(Path.Combine(assetsDir, "livestack.json")));
            Assert.Equal(3, Directory.GetFiles(assetsDir, "*.jpg").Length);

            // Verify manifest content
            var json = File.ReadAllText(Path.Combine(assetsDir, "livestack.json"));
            var manifest = JsonSerializer.Deserialize<List<Dictionary<string, JsonElement>>>(json);
            Assert.Equal(3, manifest!.Count);
            Assert.Equal("M42", manifest[0]["target"].GetString());

            // Load
            var loaded = SessionService.LoadLiveStackMasters(sessionDir, reportFilename);
            Assert.Equal(3, loaded.Count);
            Assert.Equal("H", loaded[0].Filter);
            Assert.Equal("S", loaded[1].Filter);
            Assert.Equal("O", loaded[2].Filter);
            Assert.Equal(10, loaded[0].StackCount);
            Assert.True(loaded[0].IsMonochrome);
        }

        [Fact]
        public void LoadMasters_MissingAssetsDir_ReturnsEmpty() {
            var loaded = SessionService.LoadLiveStackMasters(_tempDir, "NonExistent.html");
            Assert.Empty(loaded);
        }

        [Fact]
        public void SaveMasters_NoImages_DoesNotCreateAssetsDir() {
            var reportData = TestDataFactory.MakeReportData();
            // LiveStackImages is empty by default

            var saveMethod = typeof(SessionService).GetMethod("SaveLiveStackMasters",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            saveMethod!.Invoke(null, new object[] { _tempDir, "Test.html", reportData });

            Assert.False(Directory.Exists(Path.Combine(_tempDir, "assets")));
        }

        [Fact]
        public void SaveMasters_ColorComposite_SavesWithCorrectMetadata() {
            var composite = new LiveStackImage {
                Target = "Seagull",
                Filter = "RGB",
                IsMonochrome = false,
                JpegData = CreateMinimalJpeg(),
                MasterJpegData = CreateMinimalJpeg(),
                StackCount = 15,
                RedStackCount = 5,
                GreenStackCount = 5,
                BlueStackCount = 5
            };

            var reportData = TestDataFactory.MakeReportData(targets: new[] { "Seagull" });
            reportData.LiveStackImages = new List<LiveStackImage> { composite };

            var sessionDir = Path.Combine(_tempDir, "CompositeTest");
            Directory.CreateDirectory(sessionDir);
            var saveMethod = typeof(SessionService).GetMethod("SaveLiveStackMasters",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            saveMethod!.Invoke(null, new object[] { sessionDir, "Test.html", reportData });

            var loaded = SessionService.LoadLiveStackMasters(sessionDir, "Test.html");
            Assert.Single(loaded);
            Assert.False(loaded[0].IsMonochrome);
            Assert.Equal(5, loaded[0].RedStackCount);
            Assert.Equal(5, loaded[0].GreenStackCount);
            Assert.Equal(5, loaded[0].BlueStackCount);
        }

        // ─── Report rendering with live stack images ───

        [Fact]
        public async Task Report_WithLiveStackImages_ContainsCollapsibleSection() {
            var data = TestDataFactory.MakeReportData(targets: new[] { "M42" });
            data.LiveStackImages = new List<LiveStackImage> {
                MakeLiveStackImage("M42", "H", stackCount: 5),
                MakeLiveStackImage("M42", "S", stackCount: 3)
            };

            var gen = TestDeps.NewReportGenerator();
            var html = await gen.GenerateHtmlReport(data);

            Assert.Contains("<details class='livestack-section' open>", html);
            Assert.Contains("<summary>Live Stack (2 images)</summary>", html);
            Assert.Contains("H", html);
            Assert.Contains("S", html);
        }

        [Fact]
        public async Task Report_WithLiveStackDisabled_OmitsSection() {
            SettingsManager.Instance.Current.ShowLiveStackImages = false;

            var data = TestDataFactory.MakeReportData(targets: new[] { "M42" });
            data.LiveStackImages = new List<LiveStackImage> {
                MakeLiveStackImage("M42", "H")
            };

            var gen = TestDeps.NewReportGenerator();
            var html = await gen.GenerateHtmlReport(data);

            Assert.DoesNotContain("<details class='livestack-section'", html);
        }

        [Fact]
        public async Task Report_WithNoLiveStackImages_OmitsSection() {
            var data = TestDataFactory.MakeReportData(targets: new[] { "M42" });
            // LiveStackImages empty by default

            var gen = TestDeps.NewReportGenerator();
            var html = await gen.GenerateHtmlReport(data);

            Assert.DoesNotContain("<details class='livestack-section'", html);
        }

        [Fact]
        public async Task Report_LiveStackComposite_CappedWidth() {
            var data = TestDataFactory.MakeReportData(targets: new[] { "M42" });
            data.LiveStackImages = new List<LiveStackImage> {
                new LiveStackImage {
                    Target = "M42", Filter = "RGB", IsMonochrome = false,
                    JpegData = CreateMinimalJpeg(), MasterJpegData = CreateMinimalJpeg(),
                    StackCount = 15, RedStackCount = 5, GreenStackCount = 5, BlueStackCount = 5
                }
            };

            var gen = TestDeps.NewReportGenerator();
            var html = await gen.GenerateHtmlReport(data);

            Assert.Contains("max-width: 520px", html);
        }

        [Fact]
        public async Task Report_SixFilters_GroupedByFilterType() {
            SettingsManager.Instance.Current.FilterClassifications = "";

            var data = TestDataFactory.MakeReportData(targets: new[] { "Lagoon" });
            data.LiveStackImages = new List<LiveStackImage> {
                MakeLiveStackImage("Lagoon", "R", stackCount: 2),
                MakeLiveStackImage("Lagoon", "G", stackCount: 2),
                MakeLiveStackImage("Lagoon", "B", stackCount: 1),
                MakeLiveStackImage("Lagoon", "H", stackCount: 4),
                MakeLiveStackImage("Lagoon", "S", stackCount: 3),
                MakeLiveStackImage("Lagoon", "O", stackCount: 2)
            };

            var gen = TestDeps.NewReportGenerator();
            var html = await gen.GenerateHtmlReport(data);

            // Should have two livestack-row divs (broadband + narrowband), not one with 4+2
            var rowCount = System.Text.RegularExpressions.Regex.Matches(html, "<div class='ts-livestack-row'").Count;
            Assert.Equal(2, rowCount);
        }

        [Fact]
        public async Task Report_FourOrFewerFilters_SingleRow() {
            var data = TestDataFactory.MakeReportData(targets: new[] { "M42" });
            data.LiveStackImages = new List<LiveStackImage> {
                MakeLiveStackImage("M42", "H", stackCount: 5),
                MakeLiveStackImage("M42", "S", stackCount: 3),
                MakeLiveStackImage("M42", "O", stackCount: 2)
            };

            var gen = TestDeps.NewReportGenerator();
            var html = await gen.GenerateHtmlReport(data);

            var rowCount = System.Text.RegularExpressions.Regex.Matches(html, "<div class='ts-livestack-row'").Count;
            Assert.Equal(1, rowCount);
        }

        [Fact]
        public async Task Report_LiveStackLabels_IncludeIntegrationTime() {
            var sessionId = Guid.NewGuid().ToString();
            var data = new ReportData {
                Session = TestDataFactory.MakeSession(sessionId),
                Images = new List<ImageRecord> {
                    new ImageRecord { SessionId = sessionId, TargetName = "M42", Filter = "H",
                        ExposureDuration = 600, HFR = 2.5, FWHM = 3.2, StarCount = 100,
                        Accepted = true, Timestamp = DateTime.Now, ImageType = "LIGHT" },
                    new ImageRecord { SessionId = sessionId, TargetName = "M42", Filter = "H",
                        ExposureDuration = 600, HFR = 2.5, FWHM = 3.2, StarCount = 100,
                        Accepted = true, Timestamp = DateTime.Now, ImageType = "LIGHT" }
                },
                Events = new List<SessionEvent>(),
                TsData = new List<TsTargetData>(),
                CumulativeIntegrationSeconds = new Dictionary<string, double>(),
                SessionHistory = new Dictionary<string, List<TargetSessionHistory>>(),
                LiveStackImages = new List<LiveStackImage> {
                    MakeLiveStackImage("M42", "H", stackCount: 2)
                },
                ObserverLatitude = 40.7,
                ObserverLongitude = -74.0,
                ActiveProfileId = "test"
            };

            var gen = TestDeps.NewReportGenerator();
            var html = await gen.GenerateHtmlReport(data);

            // 2 x 600s = 1200s = 20m
            Assert.Contains("20m", html);
        }

        [Fact]
        public async Task Report_SingleLiveStackImage_CappedAt400px() {
            var data = TestDataFactory.MakeReportData(targets: new[] { "M42" });
            data.LiveStackImages = new List<LiveStackImage> {
                MakeLiveStackImage("M42", "H", stackCount: 5)
            };

            var gen = TestDeps.NewReportGenerator();
            var html = await gen.GenerateHtmlReport(data);

            Assert.Contains("width:400px", html);
        }
    }
}
