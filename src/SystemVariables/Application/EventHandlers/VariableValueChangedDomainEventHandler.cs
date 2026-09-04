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
/// variable, resolves the label text using the current variable
/// snapshot and publishes a <see cref="ResolvedOverlayTextChangedV1"/>
/// per overlay. LayoutComposition subscribes to that and pushes the
/// SignalR frame on the hub it owns — the resolution stays here, the
/// broadcast stays with the hub (no cross-context dependency).
/// </summary>
public sealed class VariableValueChangedDomainEventHandler(
    IEventBus events,
    IReverseIndex reverseIndex,
    IVariableRepository variables,
    IResolver resolver,
    ILogger<VariableValueChangedDomainEventHandler> logger)
    : IDomainEventHandler<VariableValueChangedDomainEvent>
{
    public async Task Handle(VariableValueChangedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        Ensure.That(domainEvent).IsNotNull();

        var (variable, fab, name, type, value, changedAt, changedBy, _) = domainEvent;

        SystemVariableValueChangedV1 systemVariableValueChangedEvent = new(
            Variable: variable.Value,
            Name: name.Value,
            Type: type.Value,
            Value: value.ToWireString(),
            ChangedAt: changedAt,
            ChangedBy: changedBy.Value,
            Metadata: new EventMetadata(Guid.CreateVersion7(), changedAt, fab.Value, changedBy.Value));
        await events.PublishAsync(systemVariableValueChangedEvent, cancellationToken);

        IReadOnlyCollection<Guid> affectedOverlays = reverseIndex.LookupOverlays(name.Value);
        if (affectedOverlays.Count == 0)
        {
            logger.NoOverlaysReferenceVariable(name);
            return;
        }

        foreach (Guid overlayId in affectedOverlays)
        {
            string? labelText = reverseIndex.LookupLabelText(overlayId);
            if (labelText is null)
            {
                continue;
            }

            IReadOnlyDictionary<string, VariableSnapshotEntry> snapshot = await BuildSnapshotAsync(labelText, domainEvent, cancellationToken);

            string resolvedText = resolver.Resolve(labelText, snapshot);
            long version = reverseIndex.NextVersionFor(overlayId);

            ResolvedOverlayTextChangedV1 @event = new(
                Overlay: overlayId,
                ResolvedText: resolvedText,
                Version: version,
                Metadata: new(
                    Guid.CreateVersion7(),
                    changedAt,
                    fab.Value,
                    changedBy.Value));
            await events.PublishAsync(@event, cancellationToken);
        }

        logger.PushedResolvedTextAfterChange(affectedOverlays.Count, name);
    }

    /// <summary>
    /// Builds a snapshot of every variable referenced by the label.
    /// The just-changed variable is taken from the domain event;
    /// every other referenced variable is fetched from the repository.
    /// Unset / archived / missing variables are absent from the
    /// snapshot — the resolver leaves their placeholders literal.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, VariableSnapshotEntry>> BuildSnapshotAsync(
        string labelText,
        VariableValueChangedDomainEvent changed,
        CancellationToken cancellationToken)
    {
        Dictionary<string, VariableSnapshotEntry> snapshot = new(StringComparer.Ordinal);

        foreach (string name in PlaceholderParser.ExtractNames(labelText))
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
            if (!other.HasValue)
            {
                continue;
            }

            Variable variable = other.Value;
            if (variable.State == VariableState.Archived)
            {
                continue;
            }

            if (variable.Value is VariableValue.Unset)
            {
                continue;
            }

            snapshot[name] = new VariableSnapshotEntry(variable.Value, variable.BooleanLabels);
        }

        return snapshot;
    }
}
