using System;
using System.Collections.Generic;

namespace NINA.Plugin.NightSummary.Dashboard.Dtos;

// NOTE: DTO field shapes will be expanded to mirror current /api JSON output exactly during the
// DashboardServer port. The skeleton fields here are placeholders to make the interfaces compile.
// As each endpoint is ported, the corresponding DTO is fleshed out by reading the current
// JSON-write code and turning each property into a record field with the same name + type.

public record SessionDto(
    string SessionId,
    DateTime StartLocal,
    DateTime? EndLocal,
    string? TargetName,
    string? ProjectName,
    int ImageCount,
    double IntegrationSeconds);

public record ImageDto(
    int Id,
    string SessionId,
    string? TargetName,
    string? Filter,
    double ExposureSeconds,
    DateTime Timestamp,
    double? Hfr,
    double? Snr,
    double? PositionAngle);

public record EventDto(
    int Id,
    string SessionId,
    DateTime Timestamp,
    string EventType,
    string? Details);

public record TimingEventDto(
    int Id,
    string SessionId,
    DateTime Timestamp,
    string Phase,
    double DurationSeconds,
    string? Details);

public record TargetDetailDto(
    string TargetName,
    int SessionCount,
    DateTime FirstSession,
    DateTime LastSession,
    int TotalImages,
    double TotalIntegrationSeconds);

public record TSProjectDto(
    string Guid,
    string Name,
    int State,
    bool IsMosaic,
    IReadOnlyList<TSTargetDto> Targets);

public record TSTargetDto(
    string Guid,
    string Name,
    double RaDegrees,
    double DecDegrees,
    double? RotationDegrees,
    IReadOnlyList<TSExposurePlanDto> ExposurePlans);

public record TSExposurePlanDto(
    string Guid,
    string FilterName,
    double ExposureSeconds,
    int Desired,
    int Acquired,
    int Accepted);

public record TSApiSettingsDto(
    bool Enabled,
    int Port);

public record DashboardSettingsDto(
    // Mirrors plugin SettingsManager surface used by dashboard endpoints.
    // Filled in during port.
    IReadOnlyDictionary<string, string?> Values);

public record SettingsOverridesDto(
    // Per-session sidecar overrides applied during regen.
    // Mirrors current ApplyOverrides() input.
    IReadOnlyDictionary<string, string?> Values);

public record ReportRegenerationResultDto(
    bool Succeeded,
    string? Message,
    string? ReportPath);
