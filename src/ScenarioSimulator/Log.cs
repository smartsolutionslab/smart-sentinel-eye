using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace SmartSentinelEye.ScenarioSimulator;

/// <summary>
/// Source-generated log methods for the Scenario Simulator (ADR-0050).
/// </summary>
[ExcludeFromCodeCoverage] // source-generated logging glue, not business logic
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Minted scenario-simulator token (expires in {ExpiresIn}s).")]
    public static partial void MintedSimulatorToken(this ILogger logger, int expiresIn);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeding scenario '{Scenario}' with {AssetCount} asset(s).")]
    public static partial void SeedingScenario(this ILogger logger, string scenario, int assetCount);

    [LoggerMessage(Level = LogLevel.Information, Message = "Scenario '{Scenario}' seeded.")]
    public static partial void ScenarioSeeded(this ILogger logger, string scenario);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Active scenario '{Scenario}' not found in configuration; nothing to seed.")]
    public static partial void ScenarioNotFound(this ILogger logger, string scenario);

    [LoggerMessage(Level = LogLevel.Information, Message = "Registered camera '{Name}' -> {RtspUrl}.")]
    public static partial void CameraRegistered(this ILogger logger, string name, string rtspUrl);

    [LoggerMessage(Level = LogLevel.Information, Message = "Camera '{Name}' already registered; skipping (idempotent).")]
    public static partial void CameraAlreadyRegistered(this ILogger logger, string name);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not read back camera '{Name}' ({Reason}); it will not be correlated to a wall tile.")]
    public static partial void CameraReadBackFailed(this ILogger logger, string name, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Provisioned camera-sim loop path '{Path}' playing '{Clip}'.")]
    public static partial void CameraSimPathProvisioned(this ILogger logger, string path, string clip);

    [LoggerMessage(Level = LogLevel.Information, Message = "Replaced camera-sim path '{Path}'; it now plays '{Clip}'.")]
    public static partial void CameraSimPathReplaced(this ILogger logger, string path, string clip);

    [LoggerMessage(Level = LogLevel.Error, Message = "Asset '{Asset}' names clip '{Clip}', which is not in the clips directory. Add it (scripts/generate-sim-clips.sh) or correct the scenario file.")]
    public static partial void ClipMissing(this ILogger logger, string asset, string clip);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reconciled camera-sim loop paths for {ReconciledCount} of {AssetCount} asset(s).")]
    public static partial void CameraSimReconciled(this ILogger logger, int reconciledCount, int assetCount);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not reconcile camera-sim loop path '{Path}': {Error}.")]
    public static partial void CameraSimReconcileFailed(this ILogger logger, string path, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Camera URL {Url} has no path component; not a simulated camera, skipping.")]
    public static partial void SkippedNonSimulatedCamera(this ILogger logger, string url);

    // --- M2 seeding (Phase A overlays / Phase B rules / Phase D wall) ---

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded overlay '{Name}' -> {Overlay}.")]
    public static partial void OverlaySeeded(this ILogger logger, string name, Guid overlay);

    [LoggerMessage(Level = LogLevel.Information, Message = "Overlay '{Name}' already exists and needs no publishing; reusing {Overlay} (idempotent).")]
    public static partial void OverlayAlreadyExists(this ILogger logger, string name, Guid overlay);

    [LoggerMessage(Level = LogLevel.Information, Message = "Overlay '{Name}' ({Overlay}) was left in Draft; published it.")]
    public static partial void OverlayDraftPublished(this ILogger logger, string name, Guid overlay);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Overlay '{Name}' ({Overlay}) came back with no revisions; cannot tell whether it still needs publishing.")]
    public static partial void OverlayRevisionsMissing(this ILogger logger, string name, Guid overlay);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded rule '{Name}' -> overlay {Overlay}.")]
    public static partial void RuleSeeded(this ILogger logger, string name, Guid overlay);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rule '{Name}' already exists; skipping (idempotent).")]
    public static partial void RuleAlreadyExists(this ILogger logger, string name);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded wall layout '{Name}' ({Rows}x{Cols}) -> {Layout}.")]
    public static partial void WallSeeded(this ILogger logger, string name, int rows, int cols, Guid layout);

    [LoggerMessage(Level = LogLevel.Information, Message = "Wall layout '{Name}' already exists and needs no publishing; skipping (idempotent).")]
    public static partial void WallAlreadyExists(this ILogger logger, string name);

    [LoggerMessage(Level = LogLevel.Information, Message = "Wall layout '{Name}' ({Layout}) was left in Draft; published it.")]
    public static partial void WallDraftPublished(this ILogger logger, string name, Guid layout);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Wall layout '{Name}' ({Layout}) came back with no revisions; cannot tell whether it still needs publishing.")]
    public static partial void WallRevisionsMissing(this ILogger logger, string name, Guid layout);

    [LoggerMessage(Level = LogLevel.Information, Message = "Recorded camera {Camera} for asset '{Asset}'.")]
    public static partial void AssetCameraRecorded(this ILogger logger, string asset, Guid camera);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Wall not yet complete: {Ready}/{Total} asset(s) have both overlay and camera.")]
    public static partial void WallNotYetComplete(this ILogger logger, int ready, int total);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Asset '{Asset}' is missing its {Field}; cannot seed it.")]
    public static partial void AssetMissingField(this ILogger logger, string asset, string field);

    // --- M2 billet timeline + MQTT publisher ---

    [LoggerMessage(Level = LogLevel.Information, Message = "Billet run started: {Stations} station(s), dwell {DwellMs}ms, tick {TickMs}ms.")]
    public static partial void BilletRunStarted(this ILogger logger, int stations, int dwellMs, int tickMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Billet entered station '{Asset}' (device '{Device}').")]
    public static partial void BilletEnteredStation(this ILogger logger, string asset, string device);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Emitted {Topic} = {Value} {Unit} (kind '{Kind}').")]
    public static partial void BilletSampleEmitted(this ILogger logger, string topic, double value, string unit, string kind);

    [LoggerMessage(Level = LogLevel.Information, Message = "Billet run complete; looping after {LoopGapMs}ms.")]
    public static partial void BilletRunComplete(this ILogger logger, int loopGapMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "MQTT publisher connected to '{Host}' as '{Username}'.")]
    public static partial void MqttPublisherConnected(this ILogger logger, string host, string username);

    [LoggerMessage(Level = LogLevel.Warning, Message = "MQTT publisher disconnected from '{Host}'; reconnecting.")]
    public static partial void MqttPublisherDisconnected(this ILogger logger, string host);

    [LoggerMessage(Level = LogLevel.Error, Message = "MQTT publish to '{Topic}' failed: {Reason}.")]
    public static partial void MqttPublishFailed(this ILogger logger, string topic, string reason);
}
