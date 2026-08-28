using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.SystemVariables;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.SystemVariables.Domain.Variable.Events;

namespace SmartSentinelEye.SystemVariables.Application.EventHandlers;

/// <summary>
/// Reacts to a variable being defined: publishes the V1 integration
/// event so subscribers — AuditObservability above all — see the
/// variable's creation, not only its later value changes.
///
/// No overlay fan-out, unlike its two siblings: a definition changes
/// no already-resolved label. A placeholder for a name that did not
/// exist resolved literally and continues to until the first
/// <c>SetValue</c>, which raises its own event.
/// </summary>
public sealed class VariableDefinedDomainEventHandler(IEventBus events)
    : IDomainEventHandler<VariableDefinedDomainEvent>
{
    public async Task Handle(VariableDefinedDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        Ensure.That(domainEvent).IsNotNull();

        var (variable, fab, name, type, definedAt, definedBy) = domainEvent;

        SystemVariableDefinedV1 systemVariableDefinedEvent = new(
            Variable: variable.Value,
            Name: name.Value,
            Type: type.Value,
            DefinedAt: definedAt,
            DefinedBy: definedBy.Value,
            Metadata: new EventMetadata(Guid.CreateVersion7(), definedAt, fab.Value, definedBy.Value));

        await events.PublishAsync(systemVariableDefinedEvent, cancellationToken);
    }
}
