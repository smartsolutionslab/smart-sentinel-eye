using Microsoft.Extensions.Logging;
using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.Kernel;
using EventAggregate = SmartSentinelEye.EventIngestion.Domain.Event.Event;

namespace SmartSentinelEye.EventIngestion.Application.Commands.Handlers;

/// <summary>
/// The batched write behind the broker ingress (spec 020 FR-010). Same three
/// steps as <see cref="IngestEventCommandHandler"/> — is it already here, build
/// it, store it — taken once for the whole batch rather than once per event.
///
/// <para>
/// Deliberately <b>not</b> an <c>ICommandHandler&lt;,&gt;</c>: nothing dispatches
/// it, the persistence loop calls it directly, and giving it the interface would
/// advertise a general batch-ingest capability that the HTTP paths must not use.
/// They store one event and answer for it (FR-001), which is the opposite trade.
/// </para>
///
/// <para>
/// All-or-nothing on purpose. One envelope the database refuses fails the whole
/// insert, and the caller falls back to storing them one at a time — which is
/// where FR-009 is kept. Salvaging the batch here would mean guessing which row
/// Postgres objected to.
/// </para>
/// </summary>
public sealed class IngestEventBatchCommandHandler(
    IEventRepository events,
    IClock clock,
    ILogger<IngestEventBatchCommandHandler> logger)
{
    /// <summary>
    /// Stores what needs storing and returns everything the caller may
    /// acknowledge — which includes the events that were already there and the
    /// ones a domain rule refuses, because neither will ever be storable by
    /// being sent again.
    /// </summary>
    public async Task<IReadOnlyList<EventEnvelope>> HandleAsync(
        IngestEventBatchCommand command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        IReadOnlyList<EventEnvelope> envelopes = command.Envelopes;
        if (envelopes.Count == 0)
        {
            return [];
        }

        // FR-002, once for the batch. Redelivery became the ordinary way an
        // interruption ends with spec 020, so this is the common case now and
        // not an edge one.
        IReadOnlySet<EventIdentifier> already = await events.ExistingAsync(
            [.. envelopes.Select(envelope => (envelope.Fab, envelope.Identifier))],
            cancellationToken);

        foreach (EventEnvelope envelope in envelopes)
        {
            if (already.Contains(envelope.Identifier))
            {
                logger.IdempotentReDelivery(envelope.Identifier, envelope.Fab);
                continue;
            }

            if (TryBuild(envelope, out EventAggregate @event))
            {
                events.Add(@event);
            }
        }

        await events.SaveAsync(cancellationToken);
        return envelopes;
    }

    /// <summary>
    /// Builds the aggregate, or reports that this envelope can never be built.
    /// The future-skew rule (spec 006 FR-014) is the only way this fails, and it
    /// fails the same way every time — so the envelope is left out of the insert
    /// here rather than failing the batch and sending the other 199 down the
    /// slow path once per retry, for ever.
    /// </summary>
    private bool TryBuild(EventEnvelope envelope, out EventAggregate @event)
    {
        try
        {
            @event = EventAggregate.Ingest(
                envelope.Identifier,
                envelope.Fab,
                envelope.Source,
                envelope.Device,
                envelope.Kind,
                envelope.OccurredAt,
                envelope.Payload,
                clock);
            return true;
        }
        catch (ArgumentException)
        {
            logger.BatchEnvelopeRejected(
                envelope.Identifier,
                envelope.Source,
                envelope.Device,
                IngestEventFailures.OccurredAtTooFarInFuture(envelope.OccurredAt.Value).Code);
            @event = null;
            return false;
        }
    }
}
