using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;
using SmartSentinelEye.SystemVariables.Application.Resolution;

namespace SmartSentinelEye.SystemVariables.Application.EventHandlers;

/// <summary>
/// Wolverine subscriber that drops the overlay from the reverse-index
/// when its revision is Archived. Subsequent variable changes will
/// not fan out to this overlay until a new <c>OverlayRevisionPublishedV1</c>
/// re-establishes its references.
/// </summary>
public sealed class OverlayRevisionArchivedV1Handler(
    IReverseIndex reverseIndex,
    ILogger<OverlayRevisionArchivedV1Handler> logger)
{
    public Task Handle(OverlayRevisionArchivedV1 message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        reverseIndex.RemoveOverlay(message.Overlay);
        Log.ReverseIndexDroppedOverlay(logger, message.Overlay);
        return Task.CompletedTask;
    }
}
