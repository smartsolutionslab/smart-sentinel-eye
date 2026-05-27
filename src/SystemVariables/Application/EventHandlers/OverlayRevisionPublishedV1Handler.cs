using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;
using SmartSentinelEye.SystemVariables.Application.Resolution;

namespace SmartSentinelEye.SystemVariables.Application.EventHandlers;

/// <summary>
/// Wolverine subscriber that updates the in-memory reverse-index when
/// an overlay revision is Published. Re-parses the new label text and
/// replaces the overlay's entries — names removed from the label
/// disappear from the index, names added appear. Cached label text is
/// updated for subsequent resolution.
///
/// Idempotent: re-delivery of the same V1 is safe; the upsert is
/// purely state-overwriting.
/// </summary>
public sealed class OverlayRevisionPublishedV1Handler(
    IReverseIndex reverseIndex,
    ILogger<OverlayRevisionPublishedV1Handler> log)
{
    public Task Handle(OverlayRevisionPublishedV1 message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        reverseIndex.UpsertOverlayReferences(message.Overlay, message.Text);
        log.LogDebug(
            "Reverse-index upserted for overlay {Overlay} v{Revision}; label='{Text}'.",
            message.Overlay, message.RevisionNumber, message.Text);
        return Task.CompletedTask;
    }
}
