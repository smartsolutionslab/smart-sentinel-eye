using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.Resolution;

namespace SmartSentinelEye.SystemVariables.Application.EventHandlers;

/// <summary>
/// Wolverine subscriber that updates the in-memory reverse-index when
/// an overlay revision is Published. Re-parses every label's text
/// (spec 150, #2345 — the revision now carries an ordered set of 1..8)
/// and replaces the overlay's entries with the union of their
/// placeholders — names removed from every label disappear from the
/// index, names added to any label appear. Cached label texts are
/// updated for subsequent resolution.
///
/// Idempotent: re-delivery of the same V2 is safe; the upsert is
/// purely state-overwriting.
/// </summary>
public sealed class OverlayRevisionPublishedV2Handler(IReverseIndex reverseIndex, ILogger<OverlayRevisionPublishedV2Handler> logger)
{
    public Task Handle(OverlayRevisionPublishedV2 message, CancellationToken cancellationToken = default)
    {
        Ensure.That(message).IsNotNull();

        var (overlay, revisionNumber, _, labels, _, _, _) = message;

        IReadOnlyList<string> labelTexts = [.. labels.Select(label => label.Text)];
        reverseIndex.UpsertOverlayReferences(overlay, labelTexts);
        logger.ReverseIndexUpserted(overlay, revisionNumber, labelTexts.Count);
        return Task.CompletedTask;
    }
}
