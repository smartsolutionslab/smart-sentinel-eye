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
/// acknowledges exactly what was stored. Nothing is acknowledged before it is
/// stored, so a failure leaves the sender holding its copy and a crash leaves
/// the batch unacknowledged — which is why the in-memory channel is no longer a
/// place events can be lost.
/// </para>
///
/// <para>
/// A delivery that cannot be stored is <b>carried</b> into the next cycle
/// rather than dropped or blocked on. That distinction is the whole of FR-009:
/// retrying must not become the defect spec 018 fixed, where one unpersistable
/// envelope wedged ingestion for every fab. The loop therefore spends its
/// backoff on a timer and retries the carried deliveries alongside whatever
/// arrived meanwhile, instead of sitting on one failure until the retry window
/// runs out.
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
    /// How many deliveries are in the loop at once. Large enough that the
    /// database round trip is amortised at the rate this path was sized for
    /// (spec 006: 5 000 events/s), small enough to stay far inside the broker's
    /// in-flight window.
    /// </summary>
    public const int BatchSize = 200;

    /// <summary>
    /// When each delivery was first seen failing, so the bound can be a
    /// duration. Bounded at all because QoS 1 redelivers forever: without a
    /// stopping rule, one permanently unstorable delivery is retried for ever.
    /// </summary>
    private readonly Dictionary<EventIdentifier, DateTimeOffset> failingSince = [];

    /// <summary>Deliveries still failing; retried on the next cycle.</summary>
    private readonly List<IngestDelivery> carried = [];

    private TimeSpan backoff;
    private int affected;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.PersistenceLoopStarted();
        backoff = retry.Value.InitialBackoff;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                List<IngestDelivery> attempt = await NextAttemptAsync(stoppingToken);
                if (attempt.Count > 0)
                {
                    await StoreAsync(attempt, stoppingToken);
                }
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
    /// What to try this cycle: everything still failing, plus room for new
    /// arrivals.
    ///
    /// <para>
    /// With nothing carried the loop simply waits for work. With something
    /// carried it waits <i>on a timer</i> instead and then takes whatever
    /// happens to be queued — which is what lets events keep flowing past a
    /// delivery that is failing rather than queueing behind it (FR-009).
    /// </para>
    /// </summary>
    private async Task<List<IngestDelivery>> NextAttemptAsync(CancellationToken cancellationToken)
    {
        List<IngestDelivery> attempt = [.. carried];
        carried.Clear();

        if (attempt.Count == 0)
        {
            attempt.AddRange(await channel.ReadBatchAsync(BatchSize, cancellationToken));
            return attempt;
        }

        await Task.Delay(backoff, cancellationToken);
        attempt.AddRange(channel.TakeAvailable(Math.Max(0, BatchSize - attempt.Count)));
        return attempt;
    }

    /// <summary>
    /// Stores what it can, acknowledges exactly that, and carries the rest.
    ///
    /// <para>
    /// Deliveries are stored one at a time rather than as a single transaction.
    /// That sounds like it gives up the batching, and does not: the win being
    /// sought is that acknowledgement is amortised across the batch, while
    /// isolating each delivery is what stops one bad row costing the other 199.
    /// A shared transaction would roll all of them back for one poisoned
    /// envelope.
    /// </para>
    /// </summary>
    private async Task StoreAsync(List<IngestDelivery> attempt, CancellationToken cancellationToken)
    {
        TimeSpan window = retry.Value.MaximumRetryWindow;
        int stored = 0;

        foreach (IngestDelivery delivery in attempt)
        {
            if (await TryStoreAsync(delivery, cancellationToken))
            {
                stored++;
                failingSince.Remove(delivery.Envelope.Identifier);
                await delivery.Completion.StoredAsync(cancellationToken);
            }
            else if (Exhausted(delivery.Envelope.Identifier, window))
            {
                // Recorded, then released. In that order — releasing an
                // unrecorded delivery is the original defect with a bound on it.
                await AbandonAsync(delivery, window, cancellationToken);
            }
            else
            {
                carried.Add(delivery);
            }
        }

        NoteProgress(stored, attempt.Count - stored);
    }

    /// <summary>
    /// FR-006. An interruption and its recovery are both recorded with how many
    /// events they covered, because a recovery nobody can see afterwards is
    /// indistinguishable from a loss.
    ///
    /// <para>
    /// The backoff grows only while <b>nothing at all</b> is landing. A cycle
    /// that stored something is evidence that storage works and the failure is
    /// specific to particular deliveries, so waiting longer would punish the
    /// healthy traffic for the sake of one bad row.
    /// </para>
    /// </summary>
    private void NoteProgress(int stored, int failed)
    {
        IngestRetryOptions options = retry.Value;

        if (failed > 0 && affected == 0)
        {
            affected = failed;
            logger.IngestInterrupted(failed);
        }

        if (stored > 0)
        {
            backoff = options.InitialBackoff;
        }
        else if (failed > 0)
        {
            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, options.MaximumBackoff.Ticks));
        }

        if (carried.Count == 0 && affected > 0)
        {
            logger.IngestRecovered(affected);
            affected = 0;
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

    private async Task AbandonAsync(
        IngestDelivery delivery, TimeSpan window, CancellationToken cancellationToken)
    {
        EventEnvelope envelope = delivery.Envelope;
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
            carried.Add(delivery);
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
