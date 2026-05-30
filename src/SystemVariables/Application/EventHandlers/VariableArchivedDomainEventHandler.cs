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
/// re-resolves every affected overlay (the archived variable's
/// placeholder reverts to literal per FR-011) and publishes a
/// <see cref="ResolvedOverlayTextChangedV1"/> per overlay for
/// LayoutComposition to broadcast — same split as the value-changed
/// handler.
/// </summary>
public sealed class VariableArchivedDomainEventHandler(
    IEventBus events,
    IReverseIndex reverseIndex,
    IVariableRepository variables,
    IResolver resolver,
    ILogger<VariableArchivedDomainEventHandler> log)
    : IDomainEventHandler<VariableArchivedDomainEvent>
{
    public async Task Handle(VariableArchivedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        await events.PublishAsync(
            new SystemVariableArchivedV1(
                Variable: domainEvent.Variable.Value,
                Name: domainEvent.Name.Value,
                ArchivedAt: domainEvent.ArchivedAt,
                ArchivedBy: domainEvent.ArchivedBy.Value,
                Metadata: new EventMetadata(Guid.CreateVersion7(), domainEvent.ArchivedAt, null, domainEvent.ArchivedBy.Value)),
            cancellationToken).ConfigureAwait(false);

        IReadOnlyCollection<Guid> affectedOverlays =
            reverseIndex.LookupOverlays(domainEvent.Name.Value);
        if (affectedOverlays.Count == 0) return;

        foreach (Guid overlayId in affectedOverlays)
        {
            string? labelText = reverseIndex.LookupLabelText(overlayId);
            if (labelText is null) continue;

            // Build a snapshot of every OTHER variable in the label
            // (the archived one is intentionally absent so it
            // resolves to its literal placeholder).
            Dictionary<string, VariableSnapshotEntry> snapshot = new(StringComparer.Ordinal);
            foreach (string name in PlaceholderParser.ExtractNames(labelText))
            {
                if (string.Equals(name, domainEvent.Name.Value, StringComparison.Ordinal)) continue;

                VariableName parsed;
                try { parsed = VariableName.From(name); }
                catch (ArgumentException) { continue; }

                Option<Variable> other = await variables
                    .GetByNameAsync(parsed, cancellationToken)
                    .ConfigureAwait(false);
                if (!other.HasValue) continue;

                Variable v = other.Value;
                if (v.State == VariableState.Archived) continue;
                if (v.Value is VariableValue.Unset) continue;

                snapshot[name] = new VariableSnapshotEntry(v.Value, v.BooleanLabels);
            }

            string resolvedText = resolver.Resolve(labelText, snapshot);
            long version = reverseIndex.NextVersionFor(overlayId);

            await events.PublishAsync(
                new ResolvedOverlayTextChangedV1(
                    Overlay: overlayId,
                    ResolvedText: resolvedText,
                    Version: version,
                    Metadata: new EventMetadata(Guid.CreateVersion7(), domainEvent.ArchivedAt, null, domainEvent.ArchivedBy.Value)),
                cancellationToken).ConfigureAwait(false);
        }

        log.LogInformation(
            "Pushed ResolvedOverlayTextChanged to {Count} overlays after '{Name}' was archived.",
            affectedOverlays.Count, domainEvent.Name);
    }
}
