using Microsoft.Extensions.Logging;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.SystemVariables;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.Resolution;
using SmartSentinelEye.SystemVariables.Domain.Variable;
using SmartSentinelEye.SystemVariables.Domain.Variable.Events;

namespace SmartSentinelEye.SystemVariables.Application.EventHandlers;

/// <summary>
/// Reacts to a variable being archived: publishes the V1 event, then
/// re-resolves every affected overlay's whole label set (spec 150,
/// #2345 — the archived variable's placeholder reverts to literal in
/// every label that carries it, per FR-011) and publishes a single
/// <see cref="ResolvedOverlayTextChangedV3"/> per overlay for
/// LayoutComposition to broadcast — same split as the value-changed
/// handler.
/// </summary>
public sealed class VariableArchivedDomainEventHandler(
    IEventBus events,
    IReverseIndex reverseIndex,
    IOverlayTextVersions overlayTextVersions,
    IVariableRepository variables,
    IResolver resolver,
    ILogger<VariableArchivedDomainEventHandler> logger)
    : IDomainEventHandler<VariableArchivedDomainEvent>
{
    public async Task Handle(VariableArchivedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        Ensure.That(domainEvent).IsNotNull();

        var (_, fab, variableName, archivedAt, archivedBy) = domainEvent;

        var archivedMetadata = new EventMetadata(Guid.CreateVersion7(), archivedAt, fab.Value, archivedBy.Value);
        await events.PublishAsync(ToArchivedV1(domainEvent, archivedMetadata), cancellationToken);

        var affectedOverlays = reverseIndex.LookupOverlays(variableName.Value);
        if (affectedOverlays.Count == 0)
        {
            return;
        }

        // Issue #2426, plan.md §2 -- one round trip for the whole fan-out,
        // not one advance per overlay: the added cost on the
        // `event → overlay state` leg must stay constant in fan-out width.
        var versionsByOverlay =
            await overlayTextVersions.AdvanceAsync(affectedOverlays, cancellationToken);

        foreach (var overlayId in affectedOverlays)
        {
            var labelTexts = reverseIndex.LookupLabelTexts(overlayId);
            if (labelTexts is null)
            {
                continue;
            }

            var snapshot = await BuildSnapshotAsync(labelTexts, fab, variableName, cancellationToken);

            var texts = ResolveTexts(labelTexts, snapshot);
            var metadata = new EventMetadata(Guid.CreateVersion7(), archivedAt, fab.Value, archivedBy.Value);

            ResolvedOverlayTextChangedV3 @event = new(Overlay: overlayId, Texts: texts, Version: versionsByOverlay[overlayId], Metadata: metadata);
            await events.PublishAsync(@event, cancellationToken);
        }

        logger.PushedResolvedTextAfterArchive(affectedOverlays.Count, variableName);
    }

    private static SystemVariableArchivedV1 ToArchivedV1(
        VariableArchivedDomainEvent archived,
        EventMetadata metadata)
    {
        return new SystemVariableArchivedV1(
            Variable: archived.Variable.Value,
            Name: archived.Name.Value,
            ArchivedAt: archived.ArchivedAt,
            ArchivedBy: archived.ArchivedBy.Value,
            Metadata: metadata);
    }

    // Build a snapshot of every OTHER variable referenced by any
    // label in the set (the archived one is intentionally absent
    // so it resolves to its literal placeholder) — one snapshot
    // over the union, not one per label (spec 150).
    private async Task<IReadOnlyDictionary<string, VariableSnapshotEntry>> BuildSnapshotAsync(
        IReadOnlyList<string> labelTexts,
        FabIdentifier fab,
        VariableName archivedName,
        CancellationToken cancellationToken)
    {
        Dictionary<string, VariableSnapshotEntry> snapshot = new(StringComparer.Ordinal);
        foreach (var name in labelTexts.SelectMany(PlaceholderParser.ExtractNames).Distinct(StringComparer.Ordinal))
        {
            if (string.Equals(name, archivedName.Value, StringComparison.Ordinal))
            {
                continue;
            }

            VariableName parsed;
            try
            { parsed = VariableName.From(name); }
            catch (ArgumentException) { continue; }

            // Siblings resolve in the fab that changed (ADR-0115).
            var other = await variables.GetByNameAsync(
                fab, parsed, cancellationToken);
            // None also covers an Archived sibling: GetByNameAsync excludes them (FR-005).
            if (!other.HasValue)
            {
                continue;
            }

            var variable = other.Value;
            if (variable.Value is VariableValue.Unset)
            {
                continue;
            }

            snapshot[name] = new VariableSnapshotEntry(variable.Value, variable.BooleanLabels);
        }

        return snapshot;
    }

    private IReadOnlyList<ResolvedOverlayTextV3> ResolveTexts(
        IReadOnlyList<string> labelTexts,
        IReadOnlyDictionary<string, VariableSnapshotEntry> snapshot)
    {
        return [.. ResolvedTextPairs.Build(labelTexts, resolver, snapshot)
            .Select(pair => new ResolvedOverlayTextV3(pair.Template, pair.Resolved))];
    }
}
