using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.OverlayDesigner.Domain.Overlay;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application;

[ExcludeFromCodeCoverage]
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Branched draft revision {Revision} on overlay {Overlay} by {Operator}.")]
    public static partial void BranchedDraftRevision(ILogger logger, OverlayRevisionNumber revision, OverlayIdentifier overlay, OperatorIdentifier @operator);

    [LoggerMessage(Level = LogLevel.Information, Message = "Archived overlay {Overlay} revision {Revision} by {Operator}.")]
    public static partial void ArchivedRevision(ILogger logger, OverlayIdentifier overlay, OverlayRevisionNumber revision, OperatorIdentifier @operator);

    [LoggerMessage(Level = LogLevel.Information, Message = "Edited draft revision {Revision} on overlay {Overlay}.")]
    public static partial void EditedDraftRevision(ILogger logger, OverlayRevisionNumber revision, OverlayIdentifier overlay);

    [LoggerMessage(Level = LogLevel.Information, Message = "Published overlay {Overlay} revision {Revision} by {Operator}.")]
    public static partial void PublishedRevision(ILogger logger, OverlayIdentifier overlay, OverlayRevisionNumber revision, OperatorIdentifier @operator);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created overlay {Overlay} '{Name}' (Draft) by {Operator}.")]
    public static partial void CreatedOverlay(ILogger logger, OverlayIdentifier overlay, OverlayName name, OperatorIdentifier @operator);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reverted revision {Revision} on overlay {Overlay} to Draft by {Operator}.")]
    public static partial void RevertedRevision(ILogger logger, OverlayRevisionNumber revision, OverlayIdentifier overlay, OperatorIdentifier @operator);
}
