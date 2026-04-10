using System;
namespace NINA.Plugin.NightSummary.Data {
    /// <summary>
    /// Represents a single captured image and all associated metadata
    /// recorded during a Night Summary session.
    /// </summary>
    public class ImageRecord {
        // Primary key for SQLite
        public int Id { get; set; }

        // Session this image belongs to
        public string SessionId { get; set; }

        // When the image was saved
        public DateTime Timestamp { get; set; }

        // Target and filter info
        public string TargetName { get; set; }
        public string Filter { get; set; }
        public double ExposureDuration { get; set; }

        // Image quality metrics
        public double HFR { get; set; }
        public double FWHM { get; set; }
        public double Eccentricity { get; set; }
        public int StarCount { get; set; }

        // Guiding - stored in arcseconds using NINA's scale factor
        public double GuidingRMSTotal { get; set; }
        public double GuidingScale { get; set; }

        // Whether this image was accepted or rejected by image grader
        public bool Accepted { get; set; }

        // Target coordinates — decimal hours (RA) and decimal degrees (Dec); 0 = unknown
        public double RaHours    { get; set; }
        public double DecDegrees { get; set; }

        // Temperature readings at capture time; null = device not connected or no sensor
        public double? FocuserTemp { get; set; }
        public double? AmbientTemp { get; set; }

        // Camera acquisition parameters; -1 = not reported by camera driver
        public int Gain    { get; set; }
        public int Offset  { get; set; }
        public int Binning { get; set; }  // BinX (assumes BinX == BinY); 0 = unknown

        // Camera thermal; null = cooler not connected or data unavailable
        public double? CameraTemp      { get; set; }  // sensor temperature °C
        public double? CoolerSetpoint  { get; set; }  // cooler target setpoint °C

        // Equipment state at capture time; null = device not connected
        public int?    FocuserPosition { get; set; }  // absolute focuser step position
        public double? RotatorPosition { get; set; }  // mechanical rotator angle in degrees
        public double? PositionAngle   { get; set; }  // sky position angle from plate solve (degrees E of N)

        // Extended weather data; null = weather device not connected
        public double? Humidity { get; set; }   // relative humidity %
        public double? DewPoint { get; set; }   // dew point °C
        public double? WindSpeed { get; set; }  // wind speed m/s
        public double? Pressure  { get; set; }  // atmospheric pressure hPa

        // Target Scheduler grading; -1 = no TS match or TS not installed
        public int    GradingStatus { get; set; }
        public string RejectReason  { get; set; }

        // Frame type — "LIGHT", "DARK", "FLAT", "BIAS", "SNAPSHOT"; empty = unknown (pre-v2.7 data)
        public string ImageType { get; set; }

        // Telescope pointing at capture time; null = mount not connected
        public double? Altitude { get; set; }      // degrees above horizon
        public double? Azimuth  { get; set; }      // degrees
        public double? Airmass  { get; set; }      // atmospheric airmass

        // Pier side; null = unknown or mount not connected
        public string SideOfPier { get; set; }

        // Camera readout mode name; null = not reported
        public string ReadoutMode { get; set; }

        // Sky quality and cloud cover; null = sensor not connected
        public double? SkyQuality { get; set; }    // mag/arcsec² (SQM)
        public double? CloudCover { get; set; }    // percentage

        // ASCOM seeing monitor; null = device not connected
        public double? SeeingFWHM { get; set; }    // star FWHM arcseconds from ASCOM seeing monitor

        // Image statistics from NINA's IImageStatistics; null = not available (pre-v2.10 data)
        public double? StatMedian    { get; set; }  // median ADU value
        public double? StatMean      { get; set; }  // mean ADU value
        public double? StatStDev     { get; set; }  // standard deviation
        public double? StatMAD       { get; set; }  // median absolute deviation
        public int?    StatMin       { get; set; }  // minimum pixel value
        public int?    StatMax       { get; set; }  // maximum pixel value
        public int?    StatBitDepth  { get; set; }  // image bit depth
    }
}