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
/// Reacts to a variable-value change: publishes the V1 integration
/// event (Wolverine outbox), then for every overlay referencing the
/// variable, resolves every label's text using one shared variable
/// snapshot (spec 150, #2345 — a revision carries a set, not a scalar)
/// and publishes a single <see cref="ResolvedOverlayTextChangedV3"/>
/// per overlay carrying every resolved text under one version bump.
/// LayoutComposition subscribes to that and pushes the SignalR frame on
/// the hub it owns — the resolution stays here, the broadcast stays
/// with the hub (no cross-context dependency).
/// </summary>
public sealed class VariableValueChangedDomainEventHandler(
    IEventBus events,
    IReverseIndex reverseIndex,
    IOverlayTextVersions overlayTextVersions,
    IVariableRepository variables,
    IResolver resolver,
    ILogger<VariableValueChangedDomainEventHandler> logger)
    : IDomainEventHandler<VariableValueChangedDomainEvent>
{
    public async Task Handle(VariableValueChangedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        Ensure.That(domainEvent).IsNotNull();

        var (variable, fab, name, type, value, changedAt, changedBy, _, rootIngestedAt) = domainEvent;

        SystemVariableValueChangedV1 systemVariableValueChangedEvent = new(
            Variable: variable.Value,
            Name: name.Value,
            Type: type.Value,
            Value: value.ToWireString(),
            ChangedAt: changedAt,
            ChangedBy: changedBy.Value,
            Metadata: new EventMetadata(Guid.CreateVersion7(), changedAt, fab.Value, changedBy.Value));
        await events.PublishAsync(systemVariableValueChangedEvent, cancellationToken);

        // The near end of the `event → overlay state` leg, handed on rather than
        // re-stamped. LayoutComposition closes the leg when it pushes the frame
        // and cannot otherwise know when the journey began (ADR-0102, #2173).
        DateTimeOffset? accepted = rootIngestedAt.Match<DateTimeOffset?>(moment => moment, () => null);

        IReadOnlyCollection<Guid> affectedOverlays = reverseIndex.LookupOverlays(name.Value);
        if (affectedOverlays.Count == 0)
        {
            logger.NoOverlaysReferenceVariable(name);
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

            // One snapshot over the union of every label's placeholders, not
            // one per label (spec 150 plan.md "SystemVariables") — the added
            // cost on the `event → overlay state` leg must stay constant in
            // label-set width, the same constraint #2426 already applies to
            // the version advance above.
            IReadOnlyDictionary<string, VariableSnapshotEntry> snapshot =
                await BuildSnapshotAsync(labelTexts, domainEvent, cancellationToken);

            IReadOnlyList<ResolvedOverlayTextV3> texts =
                [.. ResolvedTextPairs.Build(labelTexts, resolver, snapshot)
                    .Select(pair => new ResolvedOverlayTextV3(pair.Template, pair.Resolved))];
            long version = versionsByOverlay[overlayId];

            ResolvedOverlayTextChangedV3 @event = new(
                Overlay: overlayId,
                Texts: texts,
                Version: version,
                Metadata: new(
                    Guid.CreateVersion7(),
                    changedAt,
                    fab.Value,
                    changedBy.Value,
                    accepted));
            await events.PublishAsync(@event, cancellationToken);
        }

        logger.PushedResolvedTextAfterChange(affectedOverlays.Count, name);
    }

    /// <summary>
    /// Builds a snapshot of every variable referenced by any label in the
    /// set — the union of their placeholders (spec 150), not one snapshot
    /// per label. The just-changed variable is taken from the domain
    /// event; every other referenced variable is fetched from the
    /// repository. Unset / archived / missing variables are absent from
    /// the snapshot — the resolver leaves their placeholders literal.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, VariableSnapshotEntry>> BuildSnapshotAsync(
        IReadOnlyList<string> labelTexts,
        VariableValueChangedDomainEvent changed,
        CancellationToken cancellationToken)
    {
        Dictionary<string, VariableSnapshotEntry> snapshot = new(StringComparer.Ordinal);

        foreach (string name in labelTexts.SelectMany(PlaceholderParser.ExtractNames).Distinct(StringComparer.Ordinal))
        {
            if (string.Equals(name, changed.Name.Value, StringComparison.Ordinal))
            {
                if (changed.Value is not VariableValue.Unset)
                {
                    snapshot[name] = new VariableSnapshotEntry(changed.Value, changed.BooleanLabels);
                }
                continue;
            }

            VariableName parsed;
            try
            {
                parsed = VariableName.From(name);
            }
            catch (ArgumentException)
            {
                continue;
            }

            // Siblings resolve in the fab that changed, not globally: the
            // overlay is a fab-neutral template and this render belongs to one
            // plant (ADR-0115).
            Option<Variable> other = await variables.GetByNameAsync(changed.Fab, parsed, cancellationToken);
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

        return snapshot;
    }
}
