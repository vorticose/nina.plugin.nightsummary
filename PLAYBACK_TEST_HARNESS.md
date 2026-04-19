# Playback Test Harness — Design Document

**Status:** Research & design phase
**Date:** 2026-03-30
**Goal:** Enable end-to-end testing of Night Summary without a live NINA instance by
recording real imaging session data and replaying it through the plugin's full pipeline.

---

## 1. Problem Statement

Night Summary's test suite covers 67.5% of the logic layer (report generation, calculations,
filtering, database CRUD). However, several critical pipeline stages are completely untested
because they depend on live NINA mediator interfaces:

| Pipeline stage                     | Current coverage |
|------------------------------------|------------------|
| Event arg extraction (OnImageSaved)| None             |
| Event collection (AF, safety, flips)| None            |
| Session lifecycle orchestration    | None             |
| ReportData assembly from DB        | None             |
| Database under realistic load      | Minimal          |
| Report generation with real data   | None (synthetic only) |
| Skipped exposure tracking          | None             |
| TS grading sync                    | None             |

These gaps exist because `SessionCollector`, `SessionEventCollector`, and `SessionService`
require NINA's mediator interfaces (`IImageSaveMediator`, `IFocuserMediator`, etc.) which
cannot be instantiated outside a running NINA application.

---

## 2. Core Concept

Build a two-part system:

1. **Recorder** — A lightweight NINA plugin that subscribes to the same mediator event stream
   Night Summary uses, serializes every event (with timestamp and full payload) to a JSON file
   during a real imaging session.

2. **Replay harness** — A test-time component that deserializes a recording file, constructs
   mock mediator implementations, and fires the recorded events through Night Summary's real
   `SessionCollector` / `SessionEventCollector` / `SessionService` pipeline.

Night Summary sees no difference between a live NINA session and a replayed recording.

### Why record/replay instead of conventional mocking

- **Real-world edge cases** — Recorded data captures sensor quirks, NaN values, unusual target
  names, optional equipment (Hocus Focus, weather stations), and timing patterns that synthetic
  test data would never reproduce.
- **Forward-compatible fixtures** — The recorder captures the full mediator stream, not just what
  Night Summary currently consumes. When new features are added that use previously-ignored fields,
  existing recordings already contain that data. No need to re-record.
- **Zero-maintenance test data** — Recordings are produced as a byproduct of real imaging sessions.
  No synthetic data to maintain or keep realistic.

---

## 3. NINA Dependency Analysis

### 3.1 Interaction classification

| Interface                  | Pattern    | What Night Summary consumes                          |
|----------------------------|------------|------------------------------------------------------|
| `IImageSaveMediator`       | PASSIVE    | `ImageSaved` event — all image metadata (~30 fields) |
| `ITelescopeMediator`       | PASSIVE    | `AfterMeridianFlip` event — success bool             |
| `ISafetyMonitorMediator`   | PASSIVE*   | Consumer callback `UpdateDeviceInfo` — IsSafe bool   |
| `IFocuserMediator`         | PASSIVE*   | Consumer callback `UpdateEndAutoFocusRun` — AF info  |
| `ICameraMediator`          | ACTIVE     | `GetInfo()` called once at session start             |
| `IProfileService`          | ACTIVE     | Property reads: name, focal length, lat/lon, filters |
| `ISequenceMediator`        | HYBRID     | 1s polling for running items + `Initialized` prop    |

*Consumer pattern (NINA pushes via registered callbacks) — functionally passive.

**Key finding:** The data flow is overwhelmingly passive/push-based. The active reads are all
static equipment/profile configuration that doesn't change during a session. This makes
record/replay a natural fit.

### 3.2 Active reads (captured as initial state)

These are read once at session start and don't change during a session:

```
profileService.ActiveProfile.Name              → string
profileService.ActiveProfile.TelescopeSettings.FocalLength → double
profileService.ActiveProfile.AstrometrySettings.Latitude   → double
profileService.ActiveProfile.AstrometrySettings.Longitude  → double
profileService.ActiveProfile.CameraSettings.PixelSize      → double
profileService.ActiveProfile.FilterWheelSettings.FilterWheelFilters → list
profileService.ActiveProfile.Id                → Guid
cameraMediator.GetInfo()                       → CameraInfo (XSize, YSize, PixelSize)
```

### 3.3 Sequence mediator (hybrid, special handling needed)

`SessionCollector` polls `sequenceMediator.GetAdvancedSequencerCurrentRunningItems()` every
1 second to detect skipped/failed exposures. This is the one interaction that doesn't fit the
simple event-push model. Options:

- **Record polling responses** as timestamped snapshots, replay them via mock
- **Defer** — skipped exposure count is a single integer in the report, low-risk to omit initially
- **Simplify** — provide a mock that returns empty collections (tests everything except skip counting)

---

## 4. Event Arg Type Feasibility

### 4.1 Trivially reconstructible (14/17 types)

All have parameterless constructors with fully settable (get/set) properties:

- `ImageSavedEventArgs`
- `ImageMetaData` (auto-initializes all sub-parameter objects)
- `ImageParameter`, `CameraParameter`, `FocuserParameter`, `RotatorParameter`
- `TelescopeParameter`, `FilterWheelParameter`, `TargetParameter`
- `WeatherDataParameter`
- `RMS` (Scale via `SetScale()` method)
- `StarDetectionAnalysis` (implements `IStarDetectionAnalysis`)
- `SafetyMonitorInfo`, `FocuserInfo`

### 4.2 Require parameterized construction (3/17 types)

| Type                          | Constructor required                                    | Difficulty |
|-------------------------------|---------------------------------------------------------|------------|
| `Coordinates`                 | `new Coordinates(Angle.ByHours(ra), Angle.ByDegree(dec), Epoch.J2000)` | Trivial |
| `AutoFocusInfo`               | `new AutoFocusInfo(temp, position, filter, timestamp)`  | Trivial    |
| `AfterMeridianFlipEventArgs`  | `new AfterMeridianFlipEventArgs(success, coordinates)`  | Trivial    |

### 4.3 Reflection-accessed properties (Hocus Focus)

`FWHM` and `Eccentricity` on `StarDetectionAnalysis` are accessed via reflection because they
come from an optional third-party plugin. Night Summary already handles their absence gracefully.
For replay: subclass `StarDetectionAnalysis` and add these properties, or set them on the base
class if the NINA version includes them.

### 4.4 Package availability

All types are in the `NINA.Plugin` NuGet package (v3.2.0.9001) already referenced by the project.
No additional dependencies needed.

---

## 5. Fast Replay Feasibility

### 5.1 Wall-clock dependencies found

Four places in the collection layer use `DateTime.Now` for stored data:

| File                      | Line | Usage                                  |
|---------------------------|------|----------------------------------------|
| `SessionCollector.cs`     | 46   | `SessionStart = DateTime.Now`          |
| `SessionCollector.cs`     | 74   | `FinalizeSession(..., DateTime.Now, ...)`|
| `SessionCollector.cs`     | 156  | `ImageRecord.Timestamp = DateTime.Now` |
| `SessionEventCollector.cs`| 121  | `SessionEvent.Timestamp = DateTime.Now`|

Additionally, the 1-second skip-poll timer (`SessionCollector.cs:56`) runs on wall-clock time.

### 5.2 Impact on fast replay

If events are fired faster than real time, all image/event timestamps collapse to within
milliseconds. Charts, timelines, yield calculations, and session duration all break.

### 5.3 Solution: Clock abstraction

A ~10-line production code change:

```csharp
internal static class Clock {
    internal static Func<DateTime> Now = () => DateTime.Now;
    internal static Func<DateTime> UtcNow = () => DateTime.UtcNow;
}
```

Replace the 4 `DateTime.Now` sites with `Clock.Now`. In production, behavior is identical.
In replay mode, the harness sets `Clock.Now` to the recorded timestamp before firing each event.

### 5.4 Everything downstream is clean

Report generation, chart building, yield calculations, and delivery all operate on data read
from the database, not wall-clock time. The timing concern is isolated to the collection layer.

---

## 6. Recording Format (Conceptual)

```json
{
  "formatVersion": 1,
  "ninaVersion": "3.2.0.9001",
  "nightSummaryVersion": "2.10.0",
  "recordedAt": "2026-03-28T21:00:00-04:00",
  "initialState": {
    "profileName": "Deep Sky",
    "profileId": "guid-here",
    "cameraInfo": { "xSize": 4656, "ySize": 3520, "pixelSize": 3.76 },
    "focalLength": 714,
    "latitude": 40.7128,
    "longitude": -74.0060,
    "filters": ["L", "Ha", "OIII", "SII"]
  },
  "events": [
    {
      "timestamp": "2026-03-28T21:05:00.000-04:00",
      "type": "ImageSaved",
      "data": {
        "imageType": "LIGHT",
        "targetName": "M31",
        "filter": "Ha",
        "exposureTime": 300,
        "hfr": 2.45,
        "fwhm": 3.1,
        "eccentricity": 0.42,
        "starCount": 312,
        "guidingRmsTotal": 0.65,
        "guidingScale": 1.08,
        "raHours": 0.7122,
        "decDegrees": 41.269,
        "gain": 100,
        "offset": 10,
        "binX": 1,
        "focuserTemp": 12.5,
        "focuserPosition": 24500,
        "ambientTemp": 8.0,
        "humidity": 65.0,
        "pressure": 1013.25
      }
    },
    {
      "timestamp": "2026-03-28T21:45:00.000-04:00",
      "type": "AutoFocusComplete",
      "data": { "filter": "Ha", "temperature": 12.3, "position": 24510 }
    },
    {
      "timestamp": "2026-03-28T22:10:00.000-04:00",
      "type": "SafetyStateChanged",
      "data": { "isSafe": false }
    },
    {
      "timestamp": "2026-03-29T01:30:00.000-04:00",
      "type": "MeridianFlip",
      "data": { "success": true, "raHours": 0.7122, "decDegrees": 41.269 }
    }
  ]
}
```

---

## 7. Coverage Impact Summary

| Pipeline stage                  | Current | With playback |
|---------------------------------|---------|---------------|
| Event arg extraction            | None    | **Full**      |
| Event collection (AF/safety/flips)| None  | **Full**      |
| Session lifecycle orchestration | None    | **Full**      |
| ReportData assembly from DB     | None    | **Full**      |
| Database under realistic load   | Minimal | **Full**      |
| Report generation               | Good    | **Full + real data** |
| Skipped exposure tracking       | None    | Partial*      |
| TS grading sync                 | None    | Partial**     |
| Report delivery (network I/O)   | Partial | Same          |
| WPF / Options UI                | None    | None          |
| Plugin lifecycle / MEF          | None    | None          |

\* Sequence mediator polling requires extra mock complexity; can be deferred.
\** Requires recorded TS database snapshot as companion file.

---

## 8. Alternative Considered: Accelerated NINA Fork

During design research we explored a more ambitious idea: fork NINA's open-source codebase
and modify the simulator drivers to run at accelerated speed (exposures complete instantly,
slews are immediate, etc.). This would let a full NINA session that normally takes 6 hours
complete in minutes, with real plugin loading, real mediators, and real sequencer logic.

### Why it's interesting

- **Zero test infrastructure** — plugins load normally via MEF, no mock wiring needed.
- **Full fidelity** — the real sequencer, real mediator pipeline, real plugin lifecycle.
  Every plugin installed "just works."
- **Interactive development** — useful for the inner loop of building new features where
  you want to see your plugin running inside real NINA, just faster.
- **Community value** — a "NINA Plugin Test Runner" would benefit all plugin developers,
  not just Night Summary.

### Preliminary feasibility notes

NINA's sequencer doesn't hardcode wait times — it waits for equipment drivers to report
completion. If the simulated camera reports completion immediately instead of honoring the
requested exposure duration, the sequencer should advance at CPU speed. The change may be
narrow: modify a handful of simulator drivers to skip delays, leaving NINA core untouched.
This needs verification by reading the simulator driver source and sequencer completion logic.

### Why we're deferring it

- **Different problem.** It optimizes interactive development speed, not automated regression
  testing. The playback harness is better for CI and repeatable test suites.
- **Simulated vs. real data.** Accelerated NINA still produces synthetic data from simulator
  drivers. The playback approach uses recorded real-world data, which is more valuable for
  catching edge cases and validating against actual equipment behavior.
- **Fork maintenance.** A NINA fork must be kept in sync with upstream releases. That's ongoing
  cost unrelated to Night Summary.
- **Scope.** This is a standalone dev-tool project for the broader NINA plugin ecosystem, not a
  Night Summary testing improvement.

### Recommendation

Pursue as a separate future project if the plugin development workflow remains a bottleneck
after the playback harness is in place. The two approaches are complementary — playback for
automated regression testing, accelerated NINA for interactive development.

---

## 9. Open Questions

- Where should the recorder plugin live? Separate repo, or a subdirectory of this project?
- Should recordings be committed to the test project as fixtures, or stored externally?
- How should we handle recording file size for very long sessions (500+ images)?
- Should the replay harness live in the existing test project or a new one?
- What's the MVP scope? (Likely: ImageSaved events only, no skip tracking, no TS sync)

---

## 9. Next Steps

1. Design the recorder plugin architecture
2. Define the recording JSON schema formally
3. Implement the Clock abstraction (minimal production code change)
4. Build mock mediator implementations for replay
5. Build the replay harness and wire it to the real NS pipeline
6. Record a first real session and validate replay produces identical reports
