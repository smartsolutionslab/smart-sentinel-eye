using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.LayoutComposition.Domain.Wall;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application;

[ExcludeFromCodeCoverage]
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Archived layout {Layout} revision {Revision} by {Operator}.")]
    public static partial void ArchivedRevision(this ILogger logger, LayoutIdentifier layout, LayoutRevisionNumber revision, OperatorIdentifier @operator);

    [LoggerMessage(Level = LogLevel.Information, Message = "Branched draft revision {Revision} on layout {Layout} by {Operator}.")]
    public static partial void BranchedDraftRevision(this ILogger logger, LayoutRevisionNumber revision, LayoutIdentifier layout, OperatorIdentifier @operator);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created layout {Layout} '{Name}' (Draft) by {Operator}.")]
    public static partial void CreatedLayout(this ILogger logger, LayoutIdentifier layout, LayoutName name, OperatorIdentifier @operator);

    [LoggerMessage(Level = LogLevel.Information, Message = "Published layout {Layout} revision {Revision} by {Operator}.")]
    public static partial void PublishedRevision(this ILogger logger, LayoutIdentifier layout, LayoutRevisionNumber revision, OperatorIdentifier @operator);

    [LoggerMessage(Level = LogLevel.Information, Message = "Edited draft revision {Revision} on layout {Layout}.")]
    public static partial void EditedDraftRevision(this ILogger logger, LayoutRevisionNumber revision, LayoutIdentifier layout);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reverted revision {Revision} on layout {Layout} to Draft by {Operator}.")]
    public static partial void RevertedRevision(this ILogger logger, LayoutRevisionNumber revision, LayoutIdentifier layout, OperatorIdentifier @operator);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Broadcast OverlayHighlightChanged for overlay {Overlay} ({Duration} ms; caused by {CausingEvent}).")]
    public static partial void BroadcastOverlayHighlightChanged(this ILogger logger, Guid overlay, int duration, Guid causingEvent);

    [LoggerMessage(Level = LogLevel.Warning, Message = "OverlayHighlightRequested for overlay {Overlay} carries no fab; not broadcast (caused by {CausingEvent}).")]
    public static partial void OverlayHighlightWithoutFab(this ILogger logger, Guid overlay, Guid causingEvent);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Broadcast ResolvedOverlayTextChanged for overlay {Overlay} (version {Version}).")]
    public static partial void BroadcastResolvedOverlayTextChanged(this ILogger logger, Guid overlay, long version);

    // Distinct from a successful broadcast, and at Warning: a frame with no fab
    // cannot be delivered to anyone (spec 014 FR-015), and a kiosk that never
    // updates looks identical to one nothing was sent to.
    [LoggerMessage(Level = LogLevel.Warning, Message = "ResolvedOverlayTextChanged for overlay {Overlay} v{Version} carries no fab; not broadcast.")]
    public static partial void ResolvedOverlayTextChangedWithoutFab(this ILogger logger, Guid overlay, long version);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Broadcast OverlayArchived for overlay {Overlay} revision {Revision}.")]
    public static partial void BroadcastOverlayArchived(this ILogger logger, Guid overlay, int revision);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Broadcast OverlayPublished for overlay {Overlay} revision {Revision}.")]
    public static partial void BroadcastOverlayPublished(this ILogger logger, Guid overlay, int revision);
    [LoggerMessage(Level = LogLevel.Warning, Message = "Refused {Count} tile(s) naming a camera outside fab {Fab} (spec 017 FR-014).")]
    public static partial void RefusedCrossFabTiles(this ILogger logger, FabIdentifier fab, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created wall {Wall} '{Name}' by {Operator}.")]
    public static partial void CreatedWall(this ILogger logger, WallIdentifier wall, WallName name, OperatorIdentifier @operator);

    [LoggerMessage(Level = LogLevel.Information, Message = "Edited scenes on wall {Wall} by {Operator}.")]
    public static partial void EditedWallScenes(this ILogger logger, WallIdentifier wall, OperatorIdentifier @operator);

    [LoggerMessage(Level = LogLevel.Information, Message = "Switched wall {Wall} scene by {Operator}.")]
    public static partial void SwitchedWallScene(this ILogger logger, WallIdentifier wall, OperatorIdentifier @operator);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Switch on wall {Wall} by {Operator} was a no-op; scene unchanged.")]
    public static partial void WallSceneSwitchWasNoOp(this ILogger logger, WallIdentifier wall, OperatorIdentifier @operator);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Broadcast WallSceneChanged for wall {Wall} (scene version {SceneVersion}).")]
    public static partial void BroadcastWallSceneChanged(this ILogger logger, Guid wall, long sceneVersion);

    [LoggerMessage(Level = LogLevel.Warning, Message = "WallSceneChanged for wall {Wall} v{SceneVersion} carries no fab; not broadcast.")]
    public static partial void WallSceneChangedWithoutFab(this ILogger logger, Guid wall, long sceneVersion);

    // ---- Spec 296 FR-010/FR-012: WallSceneSwitchRequestedV1Handler. Each
    // carries wall, rule and causing event, since the log is the only
    // observable trace of a drop on this path (no caller to answer). ----

    [LoggerMessage(Level = LogLevel.Warning, Message = "WallSceneSwitchRequested for wall {Wall} (rule {Rule}, event {CausingEvent}) carries no usable fab; dropping.")]
    public static partial void WallSwitchRequestWithoutFab(this ILogger logger, Guid wall, Guid rule, Guid causingEvent);

    [LoggerMessage(Level = LogLevel.Warning, Message = "WallSceneSwitchRequested for wall {Wall} (rule {Rule}, event {CausingEvent}) names fab '{RequestFab}', but the wall belongs to fab '{ActualFab}'; dropping.")]
    public static partial void WallSwitchRequestForWallInAnotherFab(this ILogger logger, Guid wall, Guid rule, Guid causingEvent, FabIdentifier requestFab, FabIdentifier actualFab);

    [LoggerMessage(Level = LogLevel.Warning, Message = "WallSceneSwitchRequested names wall {Wall} (rule {Rule}, event {CausingEvent}), which exists in no fab; dropping.")]
    public static partial void WallSwitchRequestForUnknownWall(this ILogger logger, Guid wall, Guid rule, Guid causingEvent);

    [LoggerMessage(Level = LogLevel.Warning, Message = "WallSceneSwitchRequested for wall {Wall} (rule {Rule}, event {CausingEvent}) carries a malformed target '{Target}'; dropping.")]
    public static partial void WallSwitchRequestMalformedTarget(this ILogger logger, Guid wall, Guid rule, Guid causingEvent, string target);

    [LoggerMessage(Level = LogLevel.Warning, Message = "WallSceneSwitchRequested for wall {Wall} (rule {Rule}, event {CausingEvent}) names layout {Layout}, which is not one of the wall's scenes; dropping.")]
    public static partial void WallSwitchRequestSceneNotInSet(this ILogger logger, Guid wall, Guid rule, Guid causingEvent, LayoutIdentifier layout);

    [LoggerMessage(Level = LogLevel.Warning, Message = "WallSceneSwitchRequested for wall {Wall} (rule {Rule}, event {CausingEvent}) names layout {Layout}, which is not Published; dropping.")]
    public static partial void WallSwitchRequestSceneNotPublished(this ILogger logger, Guid wall, Guid rule, Guid causingEvent, LayoutIdentifier layout);

    [LoggerMessage(Level = LogLevel.Information, Message = "WallSceneSwitchRequested for wall {Wall} (rule {Rule}, event {CausingEvent}) is a dedup hit; no-op.")]
    public static partial void WallSwitchRequestDuplicate(this ILogger logger, Guid wall, Guid rule, Guid causingEvent);

    [LoggerMessage(Level = LogLevel.Information, Message = "Switched wall {Wall} scene by rule {Rule} (event {CausingEvent}).")]
    public static partial void RuleSwitchedWallScene(this ILogger logger, Guid wall, Guid rule, Guid causingEvent);

    [LoggerMessage(Level = LogLevel.Information, Message = "Switch request for wall {Wall} by rule {Rule} (event {CausingEvent}) was a no-op; scene unchanged.")]
    public static partial void RuleWallSceneSwitchWasNoOp(this ILogger logger, Guid wall, Guid rule, Guid causingEvent);
}
