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
/// Drains the bounded ingest channel and stores what it finds. Single reader,
/// so per-source FIFO is preserved: the channel is FIFO and nothing fans out.
///
/// <para>
/// Since spec 020 nothing is acknowledged before it is stored, so a failure
/// leaves the sender holding its copy and a crash leaves the batch
/// unacknowledged — which is why the in-memory channel is no longer a place
/// events can be lost.
/// </para>
///
/// <para>
/// Every delivery ends in exactly one of three states, and keeping them apart
/// is most of what this class does. <b>Stored</b> is acknowledged.
/// <b>Rejected</b> — a domain rule refuses it and always will — is recorded as
/// a dead letter and then acknowledged, because redelivering it forever helps
/// nobody and dropping it silently is the defect this feature exists to close.
/// <b>Failed</b> is transient: it is carried into the next cycle, and only
/// after the retry window expires does it become Rejected.
/// </para>
///
/// <para>
/// Arrivals and retries are kept in separate passes on purpose. Arrivals go
/// through the batch, which is all-or-nothing; retries go one at a time,
/// because they are the deliveries already known to fail and mixing them into
/// the batch would fail it every cycle and put every healthy event on the slow
/// path for the whole retry window (FR-009, FR-010 — one bad row must not cost
/// the throughput of everything behind it).
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
    /// How many arrivals are committed together. Large enough that the database
    /// round trip is amortised at the rate this path was sized for (spec 006:
    /// 5 000 events/s), small enough to stay far inside the broker's in-flight
    /// window.
    /// </summary>
    public const int BatchSize = 200;

    /// <summary>
    /// How many failing deliveries may be held before arrivals stop being
    /// taken. Real backpressure rather than a limit nobody reaches: past this
    /// the channel fills, the broker's in-flight window fills, and the plant is
    /// slowed — which is the answer FR-013 asks for when the system cannot keep
    /// up. A fifth of the channel, so the channel still has room to absorb the
    /// burst the retry is failing on.
    /// </summary>
    public const int MaximumCarried = 1_000;

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
    private int recovered;

    private enum Outcome
    {
        /// <summary>It is in the database. Acknowledge it.</summary>
        Stored,

        /// <summary>No rule will ever accept it. Record it, then acknowledge.</summary>
        Rejected,

        /// <summary>Something transient. Keep it, and keep the sender's copy.</summary>
        Failed,
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.PersistenceLoopStarted();
        backoff = retry.Value.InitialBackoff;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!await RunCycleAsync(stoppingToken))
                {
                    return;
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
    /// One pass: retry what is failing, store what has arrived. Returns false
    /// when the channel is closed and there is nothing left to do — without
    /// that the loop would spin at full tilt on a completed channel.
    /// </summary>
    private async Task<bool> RunCycleAsync(CancellationToken cancellationToken)
    {
        List<IngestDelivery> retrying = [.. carried];
        carried.Clear();

        IReadOnlyList<IngestDelivery> arrived;
        if (retrying.Count == 0)
        {
            arrived = await channel.ReadBatchAsync(BatchSize, cancellationToken);
            if (arrived.Count == 0)
            {
                return false;
            }
        }
        else
        {
            // The backoff is spent here rather than blocked in a read, so
            // arrivals keep flowing past whatever is failing (FR-009).
            await Task.Delay(backoff, cancellationToken);
            arrived = retrying.Count < MaximumCarried
                ? channel.TakeAvailable(BatchSize)
                : [];
        }

        int stored = 0;
        if (arrived.Count > 0)
        {
            stored += await StoreArrivalsAsync(arrived, cancellationToken);
        }

        if (retrying.Count > 0)
        {
            stored += await RetryAsync(retrying, cancellationToken);
        }

        NoteProgress(stored, carried.Count);
        return true;
    }

    /// <summary>
    /// Stores newly arrived deliveries, as a batch if it will go and one at a
    /// time if it will not.
    ///
    /// <para>
    /// The batch is one existence query and one insert for the lot, because
    /// FR-010 forbids ingest becoming a round trip per event. It is
    /// all-or-nothing, so one row the database refuses fails it — and then the
    /// deliveries are stored individually, which is the only way to find out
    /// which row it was without costing the other 199.
    /// </para>
    /// </summary>
    private async Task<int> StoreArrivalsAsync(
        IReadOnlyList<IngestDelivery> arrived, CancellationToken cancellationToken)
    {
        Option<IngestEventBatchResult> result = await TryStoreBatchAsync(arrived, cancellationToken);
        if (!result.HasValue)
        {
            return await RetryAsync(arrived, cancellationToken);
        }

        // A refused envelope is one no rule will ever accept, so it gets the
        // dead letter it is owed rather than an acknowledgement into silence.
        HashSet<EventIdentifier> refused =
            [.. result.Value.Refused.Select(envelope => envelope.Identifier)];

        foreach (IngestDelivery delivery in arrived)
        {
            await CompleteAsync(
                delivery,
                refused.Contains(delivery.Envelope.Identifier) ? Outcome.Rejected : Outcome.Stored,
                cancellationToken);
        }

        return arrived.Count - refused.Count;
    }

    /// <summary>
    /// Stores deliveries one at a time and gives each its own ending.
    /// </summary>
    private async Task<int> RetryAsync(
        IReadOnlyList<IngestDelivery> deliveries, CancellationToken cancellationToken)
    {
        TimeSpan window = retry.Value.MaximumRetryWindow;
        int stored = 0;

        foreach (IngestDelivery delivery in deliveries)
        {
            Outcome outcome = await StoreOneAsync(delivery, cancellationToken);
            if (outcome == Outcome.Failed && Exhausted(delivery.Envelope.Identifier, window))
            {
                outcome = Outcome.Rejected;
            }

            if (outcome == Outcome.Stored)
            {
                stored++;
            }

            await CompleteAsync(delivery, outcome, cancellationToken);
        }

        return stored;
    }

    /// <summary>
    /// Ends a delivery: acknowledged, recorded then acknowledged, or kept.
    ///
    /// <para>
    /// Every acknowledgement in this class goes through here, and it is guarded.
    /// Acknowledging an MQTT delivery puts a PUBACK on the wire, which throws
    /// on a dropped connection — and an exception escaping the loop faults the
    /// <see cref="BackgroundService"/>, whose default behaviour stops the host.
    /// That is the defect spec 018 fixed, and a broker blip is exactly the
    /// scenario this feature is built for.
    /// </para>
    /// </summary>
    private async Task CompleteAsync(
        IngestDelivery delivery, Outcome outcome, CancellationToken cancellationToken)
    {
        EventIdentifier identifier = delivery.Envelope.Identifier;

        if (outcome == Outcome.Failed)
        {
            carried.Add(delivery);
            return;
        }

        if (outcome == Outcome.Rejected && !await RecordRejectionAsync(delivery, cancellationToken))
        {
            // Not recorded, so not released. During an outage this write fails
            // for the same reason the event write did, and releasing an
            // unrecorded delivery is the original defect with a bound on it.
            carried.Add(delivery);
            return;
        }

        try
        {
            if (outcome == Outcome.Stored)
            {
                NoteRecovery(identifier);
                await delivery.Completion.StoredAsync(cancellationToken);
            }
            else
            {
                await delivery.Completion.AbandonedAsync(cancellationToken);
                failingSince.Remove(identifier);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The sender was not told. It still holds its copy, so the event is
            // safe; what is at risk is a duplicate on redelivery, which the
            // idempotency check absorbs.
            logger.AcknowledgementFailed(identifier, ex);
        }
    }

    /// <summary>
    /// Records a delivery nothing will ever store, so it is never merely gone
    /// (FR-008). Returns whether it is now on the record.
    /// </summary>
    private async Task<bool> RecordRejectionAsync(
        IngestDelivery delivery, CancellationToken cancellationToken)
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

            logger.IngestAbandoned(envelope.Identifier, envelope.Fab, window);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The honest hole, left open on purpose: when the database is away
            // this write fails for the same reason as the event write. The
            // delivery stays unacknowledged and keeps being retried, which
            // during an outage is exactly right — the escape is for a bad row
            // against a healthy database, not for an outage.
            logger.IngestAbandonFailed(envelope.Identifier, ex);
            return false;
        }
    }

    private async Task<Option<IngestEventBatchResult>> TryStoreBatchAsync(
        IReadOnlyList<IngestDelivery> arrived, CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IngestEventBatchCommandHandler handler =
                scope.ServiceProvider.GetRequiredService<IngestEventBatchCommandHandler>();

            return Option<IngestEventBatchResult>.Some(await handler.HandleAsync(
                new IngestEventBatchCommand([.. arrived.Select(delivery => delivery.Envelope)]),
                cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.BatchFellBackToSingles(arrived.Count, ex);
            return Option<IngestEventBatchResult>.None;
        }
    }

    private async Task<Outcome> StoreOneAsync(
        IngestDelivery delivery, CancellationToken cancellationToken)
    {
        EventEnvelope envelope = delivery.Envelope;
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            IngestEventCommandHandler handler =
                scope.ServiceProvider.GetRequiredService<IngestEventCommandHandler>();

            Result<EventIdentifier, IngestEventError> result = await handler
                .HandleAsync(new IngestEventCommand(envelope), cancellationToken);

            if (result.IsSuccess)
            {
                return Outcome.Stored;
            }

            logger.IngestFailed(envelope.Identifier, envelope.Source, envelope.Device, result.Error.Code);

            // Already ingested means it IS stored — the redelivery is the
            // idempotency rule working. Anything else is a rule that refused
            // the envelope and will refuse it identically next time, so it is
            // recorded rather than acknowledged into silence.
            return result.Error is IngestEventError.EventAlreadyIngested
                ? Outcome.Stored
                : Outcome.Rejected;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            NoteFailure(envelope.Identifier);
            if (IsMissingPartition(ex))
            {
                logger.NoStorageForFab(envelope.Identifier, envelope.Fab, ex);
            }
            else
            {
                logger.IngestDispatchFaulted(envelope.Identifier, envelope.Fab, ex);
            }

            return Outcome.Failed;
        }
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
            // What actually came back, not what was affected. An interruption
            // can also end by the last delivery being abandoned, and calling
            // that a recovery would report the loss this feature exists to
            // stop as a success. Abandonment has its own line.
            if (recovered > 0)
            {
                logger.IngestRecovered(recovered);
            }

            affected = 0;
            recovered = 0;
        }
    }

    /// <summary>
    /// This delivery stored. If it had been failing, that is a recovery and is
    /// counted as one — separately from the interruption's own total, because a
    /// delivery can also leave the interruption by being abandoned, and
    /// reporting those as recovered would be the loss this feature exists to
    /// stop, reported as a success.
    /// </summary>
    private void NoteRecovery(EventIdentifier identifier)
    {
        if (failingSince.Remove(identifier))
        {
            recovered++;
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
}
