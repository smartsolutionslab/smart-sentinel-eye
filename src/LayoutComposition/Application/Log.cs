using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application;

[ExcludeFromCodeCoverage]
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Archived layout {Layout} revision {Revision} by {Operator}.")]
    public static partial void ArchivedRevision(ILogger logger, LayoutIdentifier layout, LayoutRevisionNumber revision, OperatorIdentifier @operator);

    [LoggerMessage(Level = LogLevel.Information, Message = "Branched draft revision {Revision} on layout {Layout} by {Operator}.")]
    public static partial void BranchedDraftRevision(ILogger logger, LayoutRevisionNumber revision, LayoutIdentifier layout, OperatorIdentifier @operator);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created layout {Layout} '{Name}' (Draft) by {Operator}.")]
    public static partial void CreatedLayout(ILogger logger, LayoutIdentifier layout, LayoutName name, OperatorIdentifier @operator);

    [LoggerMessage(Level = LogLevel.Information, Message = "Published layout {Layout} revision {Revision} by {Operator}.")]
    public static partial void PublishedRevision(ILogger logger, LayoutIdentifier layout, LayoutRevisionNumber revision, OperatorIdentifier @operator);

    [LoggerMessage(Level = LogLevel.Information, Message = "Edited draft revision {Revision} on layout {Layout}.")]
    public static partial void EditedDraftRevision(ILogger logger, LayoutRevisionNumber revision, LayoutIdentifier layout);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reverted revision {Revision} on layout {Layout} to Draft by {Operator}.")]
    public static partial void RevertedRevision(ILogger logger, LayoutRevisionNumber revision, LayoutIdentifier layout, OperatorIdentifier @operator);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Broadcast OverlayHighlightChanged for overlay {Overlay} ({Duration} ms; caused by {CausingEvent}).")]
    public static partial void BroadcastOverlayHighlightChanged(ILogger logger, Guid overlay, int duration, Guid causingEvent);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Broadcast ResolvedOverlayTextChanged for overlay {Overlay} (version {Version}).")]
    public static partial void BroadcastResolvedOverlayTextChanged(ILogger logger, Guid overlay, long version);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Broadcast OverlayArchived for overlay {Overlay} revision {Revision}.")]
    public static partial void BroadcastOverlayArchived(ILogger logger, Guid overlay, int revision);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Broadcast OverlayPublished for overlay {Overlay} revision {Revision}.")]
    public static partial void BroadcastOverlayPublished(ILogger logger, Guid overlay, int revision);
}
