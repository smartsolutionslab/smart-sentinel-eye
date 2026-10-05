using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.Resolution;

namespace SmartSentinelEye.SystemVariables.Application.EventHandlers;

/// <summary>
/// Wolverine subscriber that updates the in-memory reverse-index when
/// an overlay revision is Published (spec 300, #2349, ADR-0165 — renamed
/// from <c>OverlayRevisionPublishedV2Handler</c>). Re-parses every
/// element's text (spec 150, #2345 — the revision carries an ordered set
/// of 1..8) and replaces the overlay's entries with the union of their
/// placeholders — names removed from every element disappear from the
/// index, names added to any element appear. A non-text element
/// (<c>Box</c>/<c>Ellipse</c>) contributes <c>""</c> at its position, so
/// the cached list stays index-aligned with the published set. Cached
/// texts are updated for subsequent resolution.
///
/// Idempotent: re-delivery of the same V3 is safe; the upsert is
/// purely state-overwriting.
/// </summary>
public sealed class OverlayRevisionPublishedV3Handler(IReverseIndex reverseIndex, ILogger<OverlayRevisionPublishedV3Handler> logger)
{
    public Task Handle(OverlayRevisionPublishedV3 message, CancellationToken cancellationToken = default)
    {
        Ensure.That(message).IsNotNull();

        var (overlay, revisionNumber, _, elements, _, _, _) = message;

        IReadOnlyList<string> elementTexts = [.. elements.Select(element => element.Text ?? string.Empty)];
        reverseIndex.UpsertOverlayReferences(overlay, elementTexts);
        logger.ReverseIndexUpserted(overlay, revisionNumber, elementTexts.Count);
        return Task.CompletedTask;
    }
}
