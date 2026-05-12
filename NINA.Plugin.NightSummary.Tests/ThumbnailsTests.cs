using NINA.Plugin.NightSummary.Data;
using NINA.Plugin.NightSummary.Tests.Fixtures;
using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace NINA.Plugin.NightSummary.Tests {

    /// <summary>
    /// Encoder + path-helper tests for <see cref="Thumbnails"/>.
    /// Companion to RAW_THUMBNAILS_DESIGN.md.
    /// </summary>
    public class ThumbnailsTests {

        // Synth image — 400x300, single grey channel. Just enough for the encoder
        // to do real work; output exact size doesn't matter for these tests.
        private static BitmapSource MakeTestImage(int width = 400, int height = 300) {
            int stride = width;
            byte[] pixels = new byte[stride * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = (byte)(i % 256);
            return BitmapSource.Create(width, height, 96, 96, PixelFormats.Gray8, null, pixels, stride);
        }

        [Fact]
        public void Encode_SmallTarget_ReturnsBytesAndCorrectHeight() {
            var src = MakeTestImage();
            var (w, h, data) = Thumbnails.Encode(src, Thumbnails.SmallHeightPx);

            Assert.NotNull(data);
            Assert.True(data.Length > 0);
            Assert.Equal(Thumbnails.SmallHeightPx, h);
            // 400×300 scaled to 192h → 256w
            Assert.Equal(256, w);
        }

        [Fact]
        public void Encode_MediumTarget_ProducesLargerOutputThanSmall() {
            var src = MakeTestImage(1600, 1200);
            var (_, _, smData) = Thumbnails.Encode(src, Thumbnails.SmallHeightPx);
            var (_, _, mdData) = Thumbnails.Encode(src, Thumbnails.MediumHeightPx);

            Assert.NotNull(smData);
            Assert.NotNull(mdData);
            Assert.True(mdData.Length > smData.Length,
                $"Medium ({mdData.Length}B) should be larger than small ({smData.Length}B)");
        }

        [Fact]
        public void Encode_NullSource_ReturnsEmptyTuple() {
            var (w, h, data) = Thumbnails.Encode(null, Thumbnails.SmallHeightPx);
            Assert.Equal(0, w);
            Assert.Equal(0, h);
            Assert.Null(data);
        }

        [Fact]
        public void Encode_ZeroTargetHeight_ReturnsEmptyTuple() {
            var src = MakeTestImage();
            var (w, h, data) = Thumbnails.Encode(src, 0);
            Assert.Equal(0, w);
            Assert.Equal(0, h);
            Assert.Null(data);
        }

        [Fact]
        public void GetThumbnailPath_Small_UsesUnderscoreSm() {
            var p = Thumbnails.GetThumbnailPath("/root", "abc", 42, Thumbnails.VersionSmall);
            Assert.EndsWith("42_sm.jpg", p.Replace('\\', '/'));
            Assert.Contains("abc", p);
        }

        [Fact]
        public void GetThumbnailPath_Medium_UsesUnderscoreMd() {
            var p = Thumbnails.GetThumbnailPath("/root", "abc", 42, Thumbnails.VersionMedium);
            Assert.EndsWith("42_md.jpg", p.Replace('\\', '/'));
        }

        [Fact]
        public void WriteToDisk_CreatesParentDir() {
            var dir = Path.Combine(Path.GetTempPath(), "ns_thumbs_test_" + Guid.NewGuid().ToString("N"));
            try {
                var path = Path.Combine(dir, "42_sm.jpg");
                Assert.False(Directory.Exists(dir));
                bool ok = Thumbnails.WriteToDisk(path, new byte[] { 1, 2, 3 });
                Assert.True(ok);
                Assert.True(File.Exists(path));
            } finally {
                if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public void WriteToDisk_NullData_ReturnsFalse() {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jpg");
            Assert.False(Thumbnails.WriteToDisk(path, null));
            Assert.False(File.Exists(path));
        }

        [Fact]
        public void WriteToDisk_EmptyData_ReturnsFalse() {
            var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jpg");
            Assert.False(Thumbnails.WriteToDisk(path, Array.Empty<byte>()));
        }
    }

    /// <summary>
    /// Round-trip tests for the new <c>ThumbnailVersion</c> + <c>FilePath</c>
    /// columns and the <c>UpdateImageThumbnailVersion</c> setter.
    /// </summary>
    public class SessionDatabaseThumbnailTests : IDisposable {
        private readonly string _dbPath;
        private readonly SessionDatabase _db;

        public SessionDatabaseThumbnailTests() {
            _dbPath = Path.Combine(Path.GetTempPath(), $"ns_thumbs_db_{Guid.NewGuid():N}.sqlite");
            _db = new SessionDatabase(_dbPath);
        }

        public void Dispose() {
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }

        [Fact]
        public void NewImageRow_HasNullThumbnailVersionAndFilePath() {
            var sid = Guid.NewGuid().ToString();
            _db.CreateSession(TestDataFactory.MakeSession(sid));
            _db.SaveImageRecord(TestDataFactory.MakeImage(sid));
            var row = _db.GetImagesForSession(sid)[0];
            Assert.Null(row.ThumbnailVersion);
            Assert.Null(row.FilePath);
        }

        [Fact]
        public void SaveImageRecord_PersistsFilePath() {
            var sid = Guid.NewGuid().ToString();
            _db.CreateSession(TestDataFactory.MakeSession(sid));
            var img = TestDataFactory.MakeImage(sid);
            img.FilePath = @"D:\Lights\m31_001.fits";
            _db.SaveImageRecord(img);
            var row = _db.GetImagesForSession(sid)[0];
            Assert.Equal(@"D:\Lights\m31_001.fits", row.FilePath);
        }

        [Fact]
        public void UpdateImageThumbnailVersion_SetsBitmask() {
            var sid = Guid.NewGuid().ToString();
            _db.CreateSession(TestDataFactory.MakeSession(sid));
            long id = _db.SaveImageRecord(TestDataFactory.MakeImage(sid));

            _db.UpdateImageThumbnailVersion(id, Thumbnails.VersionSmall | Thumbnails.VersionMedium);
            var row = _db.GetImagesForSession(sid)[0];
            Assert.Equal(3, row.ThumbnailVersion);
        }

        [Fact]
        public void UpdateImageThumbnailVersion_Null_ClearsBitmask() {
            var sid = Guid.NewGuid().ToString();
            _db.CreateSession(TestDataFactory.MakeSession(sid));
            long id = _db.SaveImageRecord(TestDataFactory.MakeImage(sid));
            _db.UpdateImageThumbnailVersion(id, Thumbnails.VersionSmall);
            _db.UpdateImageThumbnailVersion(id, null);

            var row = _db.GetImagesForSession(sid)[0];
            Assert.Null(row.ThumbnailVersion);
        }

        [Fact]
        public void SaveImageRecord_ReturnsRowId() {
            var sid = Guid.NewGuid().ToString();
            _db.CreateSession(TestDataFactory.MakeSession(sid));
            long a = _db.SaveImageRecord(TestDataFactory.MakeImage(sid));
            long b = _db.SaveImageRecord(TestDataFactory.MakeImage(sid));
            Assert.True(a > 0);
            Assert.Equal(a + 1, b);
        }
    }
}
