using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using SmartSentinelEye.EventIngestion.Application.Commands;
using SmartSentinelEye.EventIngestion.Application.Commands.Handlers;
using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Domain.DeadLetter;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Ingress;

/// <summary>
/// Drains the bounded ingest channel and dispatches each envelope through
/// <see cref="IngestEventCommandHandler"/>. Single reader, so per-source FIFO
/// is preserved: the channel is FIFO and nothing fans out inside the loop.
///
/// <para>
/// Since spec 020 the loop takes a <b>batch</b>, stores it, and only then
/// acknowledges exactly the deliveries in that batch. Nothing is acknowledged
/// before it is stored, so a failure leaves the sender holding its copy and a
/// crash leaves the whole batch unacknowledged — which is why the in-memory
/// channel is no longer a place events can be lost.
/// </para>
///
/// <para>
/// A batch that cannot be stored is retried with backoff rather than dropped.
/// The bound below is what stops that becoming the defect spec 018 fixed, where
/// one unpersistable envelope wedged ingestion for every fab.
/// </para>
/// </summary>
public sealed class PersistenceLoopHostedService(
    IIngestChannel channel,
    IServiceScopeFactory scopeFactory,
    IOptions<IngestRetryOptions> retry,
    IClock clock,
    ILogger<PersistenceLoopHostedService> logger) : BackgroundService
{
    /// <summary>
    /// How many deliveries are committed together. Large enough that the
    /// database round trip is amortised at the rate this path was sized for
    /// (spec 006: 5 000 events/s), small enough to stay far inside the broker's
    /// in-flight window.
    /// </summary>
    public const int BatchSize = 200;

    /// <summary>
    /// When each delivery was first seen failing, so the bound can be a
    /// duration. Bounded at all because QoS 1 redelivers forever: without a
    /// stopping rule, one permanently unstorable delivery blocks everything
    /// behind it.
    /// </summary>
    private readonly Dictionary<EventIdentifier, DateTimeOffset> failingSince = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.PersistenceLoopStarted();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                IReadOnlyList<IngestDelivery> batch =
                    await channel.ReadBatchAsync(BatchSize, stoppingToken);
                if (batch.Count == 0)
                {
                    return;
                }

                await StoreBatchAsync(batch, stoppingToken);
            }
        }
        catch (OperationCanceledException ex) when (stoppingToken.IsCancellationRequested)
        {
            // Whatever was in flight is unacknowledged, so the broker still has
            // it. Stopping here costs a redelivery, not an event.
            logger.PersistenceLoopStopping(ex);
        }
    }

    /// <summary>
    /// Stores a batch, retrying while the failure looks transient, and
    /// acknowledges only what was stored.
    ///
    /// <para>
    /// Deliveries are stored one at a time inside the batch rather than as a
    /// single transaction. That sounds like it gives up the batching, and does
    /// not: the win being sought is that acknowledgement is amortised across
    /// the batch, while isolating each delivery is what stops one bad row
    /// costing the other 199 (FR-009). A shared transaction would roll all of
    /// them back for one poisoned envelope.
    /// </para>
    /// </summary>
    private async Task StoreBatchAsync(
        IReadOnlyList<IngestDelivery> batch, CancellationToken cancellationToken)
    {
        IngestRetryOptions options = retry.Value;
        List<IngestDelivery> outstanding = [.. batch];
        TimeSpan backoff = options.InitialBackoff;
        bool interrupted = false;

        while (outstanding.Count > 0 && !cancellationToken.IsCancellationRequested)
        {
            List<IngestDelivery> failed = [];

            foreach (IngestDelivery delivery in outstanding)
            {
                if (await TryStoreAsync(delivery, cancellationToken))
                {
                    failingSince.Remove(delivery.Envelope.Identifier);
                    await delivery.Completion.StoredAsync(cancellationToken);
                    continue;
                }

                if (Exhausted(delivery.Envelope.Identifier, options.MaximumRetryWindow))
                {
                    // Recorded, then released. In that order — releasing an
                    // unrecorded delivery is the original defect with a bound
                    // on it.
                    await AbandonAsync(delivery, cancellationToken);
                    continue;
                }

                failed.Add(delivery);
            }

            if (failed.Count == 0)
            {
                break;
            }

            if (!interrupted)
            {
                interrupted = true;
                logger.IngestInterrupted(failed.Count);
            }

            outstanding = failed;
            await Task.Delay(backoff, cancellationToken);
            backoff = TimeSpan.FromTicks(
                Math.Min(backoff.Ticks * 2, options.MaximumBackoff.Ticks));
        }

        if (interrupted)
        {
            // FR-006. A recovery nobody can see afterwards is indistinguishable
            // from a loss, so the count is part of the requirement rather than
            // a nicety.
            logger.IngestRecovered(batch.Count);
        }
    }

    private async Task<bool> TryStoreAsync(IngestDelivery delivery, CancellationToken cancellationToken)
    {
        try
        {
            await DispatchAsync(delivery.Envelope, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            NoteFailure(delivery.Envelope.Identifier);
            if (IsMissingPartition(ex))
            {
                logger.NoStorageForFab(delivery.Envelope.Identifier, delivery.Envelope.Fab, ex);
            }
            else
            {
                logger.IngestDispatchFaulted(delivery.Envelope.Identifier, delivery.Envelope.Fab, ex);
            }

            return false;
        }
    }

    private async Task AbandonAsync(IngestDelivery delivery, CancellationToken cancellationToken)
    {
        EventEnvelope envelope = delivery.Envelope;
        TimeSpan window = retry.Value.MaximumRetryWindow;
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IDeadLetterRepository deadLetters =
                scope.ServiceProvider.GetRequiredService<IDeadLetterRepository>();

            deadLetters.Add(Domain.DeadLetter.DeadLetter.Capture(
                $"event/{envelope.Fab.Value}/{envelope.Source.Value}/{envelope.Device.Value}",
                envelope.Fab,
                envelope.Payload.Value,
                $"not storable after {window} of retrying",
                clock));
            await deadLetters.SaveAsync(cancellationToken);

            failingSince.Remove(envelope.Identifier);
            logger.IngestAbandoned(envelope.Identifier, envelope.Fab, window);
            await delivery.Completion.AbandonedAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The honest hole, and it is left open on purpose: when the database
            // is away, this write fails for the same reason as the event write.
            // The delivery stays unacknowledged and keeps being retried, which
            // during an outage is exactly right — the escape is for a bad row
            // against a healthy database, not for an outage.
            logger.IngestAbandonFailed(envelope.Identifier, ex);
        }
    }

    /// <summary>
    /// Notes when this event first started failing. In memory and reset on
    /// restart, deliberately: persisting it would mean a durable write per
    /// failed attempt, on the path that is failing because writes are failing.
    /// A restart costs the delivery a fresh window, which during an outage is
    /// the right answer anyway.
    /// </summary>
    private void NoteFailure(EventIdentifier identifier)
    {
        if (!failingSince.ContainsKey(identifier))
        {
            failingSince[identifier] = clock.UtcNow;
        }
    }

    private bool Exhausted(EventIdentifier identifier, TimeSpan window) =>
        failingSince.TryGetValue(identifier, out DateTimeOffset since)
        && clock.UtcNow - since >= window;

    /// <summary>
    /// Whether this is Postgres refusing the row because no partition covers its
    /// fab (spec 019 FR-008).
    ///
    /// <para>
    /// Unwrapped rather than matched directly: the insert goes through EF, which
    /// wraps every provider exception in <see cref="DbUpdateException"/>. A
    /// <c>catch (PostgresException)</c> never fires — it was written that way
    /// first, and the envelope got the same "something faulted" line this exists
    /// to replace.
    /// </para>
    /// </summary>
    private static bool IsMissingPartition(Exception exception) => exception switch
    {
        PostgresException postgres => postgres.SqlState == PostgresErrorCodes.CheckViolation,
        DbUpdateException { InnerException: PostgresException inner } =>
            inner.SqlState == PostgresErrorCodes.CheckViolation,
        _ => false,
    };

    private async Task DispatchAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IngestEventCommandHandler handler =
            scope.ServiceProvider.GetRequiredService<IngestEventCommandHandler>();

        Result<EventIdentifier, IngestEventError> result = await handler
            .HandleAsync(new IngestEventCommand(envelope), cancellationToken);

        if (!result.IsSuccess)
        {
            logger.IngestFailed(envelope.Identifier, envelope.Source, envelope.Device, result.Error.Code);
        }
    }
}
