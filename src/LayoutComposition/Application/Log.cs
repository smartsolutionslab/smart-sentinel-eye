using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
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
}
