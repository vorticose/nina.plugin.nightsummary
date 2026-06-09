namespace NINA.Plugin.NightSummary.Session;

/// <summary>
/// A captured live stack image, stored as compressed JPEG bytes.
/// Lives in the Dashboard project so the cross-platform ReportGenerator can
/// embed images without pulling in the WPF-only LiveStackCapture path.
/// Namespace deliberately kept as NINA.Plugin.NightSummary.Session to match
/// existing call-site references.
/// </summary>
public class LiveStackImage {
    public string Target { get; init; } = "";
    public string Filter { get; init; } = "";
    public bool IsMonochrome { get; init; }
    /// <summary>JPEG at report-embed resolution (760px wide, q75).</summary>
    public byte[] JpegData { get; init; } = System.Array.Empty<byte>();
    /// <summary>JPEG master at archive resolution (2000px wide, q90) for persistence.</summary>
    public byte[] MasterJpegData { get; init; } = System.Array.Empty<byte>();
    public int StackCount { get; init; }
    public int? RedStackCount { get; init; }
    public int? GreenStackCount { get; init; }
    public int? BlueStackCount { get; init; }
}
