using Microsoft.Extensions.Logging;
using SmartSentinelEye.LayoutComposition.Domain.Layout;
using SmartSentinelEye.Shared.Contracts.SystemVariables;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Application.Resolution;
using SmartSentinelEye.SystemVariables.Domain.Variable;
using SmartSentinelEye.SystemVariables.Domain.Variable.Events;

namespace SmartSentinelEye.SystemVariables.Application.EventHandlers;

/// <summary>
/// Reacts to a variable-value change: publishes the V1 integration
/// event (Wolverine outbox), then for every overlay in the
/// reverse-index entry for this variable, resolves the label text
/// using the current variable snapshot and broadcasts the new
/// resolved text via the cross-context broadcaster bridge.
/// </summary>
public sealed class VariableValueChangedDomainEventHandler(
    IEventBus events,
    IReverseIndex reverseIndex,
    IVariableRepository variables,
    IResolver resolver,
    ILayoutLifecycleBroadcaster broadcaster,
    ILogger<VariableValueChangedDomainEventHandler> log)
    : IDomainEventHandler<VariableValueChangedDomainEvent>
{
    public async Task Handle(VariableValueChangedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        await events.PublishAsync(
            new SystemVariableValueChangedV1(
                Variable: domainEvent.Variable.Value,
                Name: domainEvent.Name.Value,
                Type: domainEvent.Type.Value,
                Value: domainEvent.Value.ToWireString(),
                ChangedAt: domainEvent.ChangedAt,
                ChangedBy: domainEvent.ChangedBy.Value),
            cancellationToken).ConfigureAwait(false);

        IReadOnlyCollection<Guid> affectedOverlays =
            reverseIndex.LookupOverlays(domainEvent.Name.Value);
        if (affectedOverlays.Count == 0)
        {
            log.LogDebug("No overlays reference variable '{Name}'; skipping push fan-out.",
                domainEvent.Name);
            return;
        }

        foreach (Guid overlayId in affectedOverlays)
        {
            string? labelText = reverseIndex.LookupLabelText(overlayId);
            if (labelText is null) continue;

            IReadOnlyDictionary<string, VariableSnapshotEntry> snapshot =
                await BuildSnapshotAsync(labelText, domainEvent, cancellationToken).ConfigureAwait(false);

            string resolvedText = resolver.Resolve(labelText, snapshot);
            long version = reverseIndex.NextVersionFor(overlayId);

            await broadcaster.ResolvedOverlayTextChangedAsync(
                new ResolvedOverlayTextChangedNotification(overlayId, resolvedText, version),
                cancellationToken).ConfigureAwait(false);
        }

        log.LogInformation(
            "Pushed ResolvedOverlayTextChanged to {Count} overlays after '{Name}' changed.",
            affectedOverlays.Count, domainEvent.Name);
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

            Option<Variable> other = await variables
                .GetByNameAsync(parsed, cancellationToken)
                .ConfigureAwait(false);
            if (!other.HasValue) continue;

            Variable v = other.Value;
            if (v.State == VariableState.Archived) continue;
            if (v.Value is VariableValue.Unset) continue;

            snapshot[name] = new VariableSnapshotEntry(v.Value, v.BooleanLabels);
        }

        return snapshot;
    }
}
