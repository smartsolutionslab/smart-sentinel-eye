using Microsoft.Extensions.Logging;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using SmartSentinelEye.Shared.Kernel;
using AuditEventEntity = SmartSentinelEye.AuditObservability.Domain.AuditEvent.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Application.EventHandlers;

/// <summary>
/// Audit write-path: takes the bus-fed
/// <see cref="V1Envelope"/> + the runtime CLR type of the V1
/// payload, looks the type up in
/// <see cref="V1ResourceMap"/>, and writes one row through
/// <see cref="IAuditEventRepository"/>.
///
/// <para>
/// Idempotency is handled at the repository / EF Core layer
/// (<c>INSERT … ON CONFLICT (event_identifier) DO NOTHING</c>);
/// the handler does not need to check for prior rows.
/// </para>
///
/// <para>
/// The Infrastructure project wires this handler into Wolverine
/// by building one bus subscriber per concrete
/// <c>IIntegrationEvent</c> in <c>Shared.Contracts</c>; each
/// subscriber translates the raw payload + Wolverine envelope
/// into a <see cref="V1Envelope"/> before calling
/// <see cref="HandleAsync"/>.
/// </para>
/// </summary>
public sealed class AuditingMessageHandler(
    IAuditEventRepository repository,
    V1ResourceMap resourceMap,
    IClock clock,
    ILogger<AuditingMessageHandler> logger)
{
    public async Task HandleAsync(
        Type payloadType,
        object payload,
        V1Envelope envelope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(payloadType);
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(envelope);

        V1Mapping mapping = resourceMap.Lookup(payloadType, payload);
        AuditEventEntity row = AuditEventEntity.From(envelope, mapping, clock);

        repository.Add(row);
        await repository.SaveAsync(cancellationToken).ConfigureAwait(false);

        logger.LogDebug(
            "Audited {EventKind} {EventIdentifier} (resource: {ResourceKind}/{ResourceIdentifier}).",
            envelope.EventTypeName,
            envelope.EventIdentifier,
            mapping.Kind.HasValue ? mapping.Kind.Value.Value : "<none>",
            mapping.ResourceIdentifier.HasValue ? mapping.ResourceIdentifier.Value.Value : "<none>");
    }
}
