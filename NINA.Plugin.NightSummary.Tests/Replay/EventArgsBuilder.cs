using NINA.Astrometry;
using NINA.Core.Enum;
using NINA.Core.Model;
using NINA.Equipment.Equipment.MyFocuser;
using NINA.Equipment.Equipment.MySafetyMonitor;
using NINA.Equipment.Interfaces.Mediator;
using NINA.Image.ImageData;
using NINA.Plugin.NightSummary.Tests.Replay.Models;
using NINA.WPF.Base.Interfaces.Mediator;
using System;

namespace NINA.Plugin.NightSummary.Tests.Replay {

    /// <summary>
    /// Reconstructs NINA event argument objects from recording data classes.
    /// Each builder method creates the full object tree that SessionCollector/
    /// SessionEventCollector expects when processing events.
    /// </summary>
    internal static class EventArgsBuilder {

        /// <summary>
        /// Builds a complete ImageSavedEventArgs with all metadata fields
        /// that SessionCollector.OnImageSaved reads.
        /// </summary>
        public static ImageSavedEventArgs BuildImageSavedEventArgs(ImageSavedData data) {
            var metadata = new ImageMetaData();

            // Image parameters
            metadata.Image.ImageType = data.ImageType ?? "LIGHT";
            metadata.Image.ExposureTime = data.ExposureTime;

            // Guiding RMS — requires RMS object with Scale
            if (data.GuidingRmsTotal > 0 || data.GuidingScale != 1.0) {
                var rms = new RMS();
                rms.SetScale(data.GuidingScale);
                // Total is stored as the raw value; SessionCollector multiplies by Scale
                // So we need to store Total/Scale so the multiplication yields the correct value
                if (data.GuidingScale > 0)
                    rms.Total = data.GuidingRmsTotal / data.GuidingScale;
                else
                    rms.Total = data.GuidingRmsTotal;
                metadata.Image.RecordedRMS = rms;
            }

            // Target
            metadata.Target.Name = data.TargetName ?? "Unknown";
            if (data.RaHours != 0 || data.DecDegrees != 0) {
                metadata.Target.Coordinates = new Coordinates(
                    data.RaHours, data.DecDegrees, Epoch.J2000, Coordinates.RAType.Hours);
            }
            if (data.PositionAngle.HasValue)
                metadata.Target.PositionAngle = data.PositionAngle.Value;

            // Filter
            metadata.FilterWheel.Filter = data.Filter ?? "None";

            // Camera
            metadata.Camera.Gain = data.Gain;
            metadata.Camera.Offset = data.Offset;
            metadata.Camera.BinX = data.BinX;
            metadata.Camera.Temperature = data.CameraTemp ?? double.NaN;
            metadata.Camera.SetPoint = data.CoolerSetpoint ?? double.NaN;
            metadata.Camera.ReadoutModeName = data.ReadoutMode ?? "";

            // Focuser
            metadata.Focuser.Temperature = data.FocuserTemp ?? double.NaN;
            metadata.Focuser.Position = data.FocuserPosition;

            // Rotator
            metadata.Rotator.Position = data.RotatorPosition ?? double.NaN;

            // Weather
            metadata.WeatherData.Temperature = data.AmbientTemp ?? double.NaN;
            metadata.WeatherData.Humidity = data.Humidity ?? double.NaN;
            metadata.WeatherData.DewPoint = data.DewPoint ?? double.NaN;
            metadata.WeatherData.WindSpeed = data.WindSpeed ?? double.NaN;
            metadata.WeatherData.Pressure = data.Pressure ?? double.NaN;
            metadata.WeatherData.SkyQuality = data.SkyQuality ?? double.NaN;
            metadata.WeatherData.CloudCover = data.CloudCover ?? double.NaN;
            metadata.WeatherData.StarFWHM = data.SeeingFWHM ?? double.NaN;

            // Telescope pointing
            metadata.Telescope.Altitude = data.Altitude ?? double.NaN;
            metadata.Telescope.Azimuth = data.Azimuth ?? double.NaN;
            metadata.Telescope.Airmass = data.Airmass ?? double.NaN;
            if (!string.IsNullOrEmpty(data.SideOfPier) && Enum.TryParse<PierSide>(data.SideOfPier, out var pier))
                metadata.Telescope.SideOfPier = pier;

            // Star detection analysis
            var starAnalysis = new StarDetectionAnalysis {
                HFR = data.HFR,
                DetectedStars = data.DetectedStars
            };

            return new ImageSavedEventArgs {
                MetaData = metadata,
                StarDetectionAnalysis = starAnalysis
            };
        }

        /// <summary>
        /// Builds AutoFocusInfo for focuser consumer callback.
        /// </summary>
        public static AutoFocusInfo BuildAutoFocusInfo(AutoFocusData data, DateTime timestamp) {
            return new AutoFocusInfo(data.Temperature, data.Position, data.Filter, timestamp);
        }

        /// <summary>
        /// Builds SafetyMonitorInfo for safety consumer callback.
        /// </summary>
        public static SafetyMonitorInfo BuildSafetyMonitorInfo(SafetyStateData data) {
            return new SafetyMonitorInfo {
                IsSafe = data.IsSafe
            };
        }
    }
}
