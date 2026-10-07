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

        var (variableIdentifier, fab, variableName, archivedAt, archivedBy) = domainEvent;

        SystemVariableArchivedV1 systemVariableArchivedEvent = new(
            Variable: variableIdentifier.Value,
            Name: variableName.Value,
            ArchivedAt: archivedAt,
            ArchivedBy: archivedBy.Value,
            Metadata: new EventMetadata(Guid.CreateVersion7(), archivedAt, fab.Value, archivedBy.Value));

        await events.PublishAsync(systemVariableArchivedEvent, cancellationToken);

        IReadOnlyCollection<Guid> affectedOverlays = reverseIndex.LookupOverlays(variableName.Value);
        if (affectedOverlays.Count == 0)
        {
            return;
        }

        // Issue #2426, plan.md §2 -- one round trip for the whole fan-out,
        // not one advance per overlay: the added cost on the
        // `event → overlay state` leg must stay constant in fan-out width.
        IReadOnlyDictionary<Guid, long> versionsByOverlay =
            await overlayTextVersions.AdvanceAsync(affectedOverlays, cancellationToken);

        foreach (Guid overlayId in affectedOverlays)
        {
            IReadOnlyList<string>? labelTexts = reverseIndex.LookupLabelTexts(overlayId);
            if (labelTexts is null)
            {
                continue;
            }

            // Build a snapshot of every OTHER variable referenced by any
            // label in the set (the archived one is intentionally absent
            // so it resolves to its literal placeholder) — one snapshot
            // over the union, not one per label (spec 150).
            Dictionary<string, VariableSnapshotEntry> snapshot = new(StringComparer.Ordinal);
            foreach (string name in labelTexts.SelectMany(PlaceholderParser.ExtractNames).Distinct(StringComparer.Ordinal))
            {
                if (string.Equals(name, variableName.Value, StringComparison.Ordinal))
                {
                    continue;
                }

                VariableName parsed;
                try { parsed = VariableName.From(name); }
                catch (ArgumentException) { continue; }

                // Siblings resolve in the fab that changed (ADR-0115).
                Option<Variable> other = await variables.GetByNameAsync(
                    fab, parsed, cancellationToken);
                // None also covers an Archived sibling: GetByNameAsync excludes them (FR-005).
                if (!other.HasValue)
                {
                    continue;
                }

                Variable variable = other.Value;
                if (variable.Value is VariableValue.Unset)
                {
                    continue;
                }

                snapshot[name] = new VariableSnapshotEntry(variable.Value, variable.BooleanLabels);
            }

            IReadOnlyList<ResolvedOverlayTextV3> texts =
                [.. ResolvedTextPairs.Build(labelTexts, resolver, snapshot)
                    .Select(pair => new ResolvedOverlayTextV3(pair.Template, pair.Resolved))];
            long version = versionsByOverlay[overlayId];

            ResolvedOverlayTextChangedV3 resolvedOverlayTextChangedEvent = new(
                Overlay: overlayId,
                Texts: texts,
                Version: version,
                Metadata: new(
                    Guid.CreateVersion7(),
                    archivedAt,
                    fab.Value,
                    archivedBy.Value));
            await events.PublishAsync(resolvedOverlayTextChangedEvent, cancellationToken);
        }

        logger.PushedResolvedTextAfterArchive(affectedOverlays.Count, variableName);
    }
}
