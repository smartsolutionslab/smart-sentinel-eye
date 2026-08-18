using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using SmartSentinelEye.EventIngestion.Application.Commands;
using SmartSentinelEye.EventIngestion.Application.Commands.Handlers;
using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Ingress;

/// <summary>
/// Drains the bounded ingest channel and dispatches each envelope
/// through <see cref="IngestEventCommandHandler"/>. Single-reader
/// loop so the per-instance throughput is bounded by Postgres write
/// + Wolverine outbox dispatch (NFR-001 budget). Per-source FIFO is
/// preserved because the channel is FIFO and we don't fan out
/// concurrently inside the loop.
/// </summary>
public sealed class PersistenceLoopHostedService(
    IIngestChannel channel,
    IServiceScopeFactory scopeFactory,
    ILogger<PersistenceLoopHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.PersistenceLoopStarted();
        try
        {
            await foreach (EventEnvelope envelope in channel.ReadAllAsync(stoppingToken))
            {
                // A throwing dispatch used to escape ExecuteAsync, and the
                // default BackgroundServiceExceptionBehavior is StopHost — so a
                // single unpersistable envelope took the whole service down and
                // every later request hung against a dead process. One fab's bad
                // row must not stop ingestion for the other fabs (24/7, §IV).
                // Broad by intent: what reaches here is by definition
                // unanticipated, and the loop is the last thing standing between
                // one row and total ingest loss.
                try
                {
                    await DispatchAsync(envelope, stoppingToken);
                }
                catch (Exception ex) when (IsMissingPartition(ex))
                {
                    // Spec 019 FR-008. This is the one cause worth naming: no
                    // partition exists for the fab, so the insert cannot land.
                    // It arrives as a generic check violation, and left in the
                    // catch below it reads as "something went wrong" — which is
                    // exactly how it went unnoticed from spec 006 to spec 018.
                    //
                    // The endpoint refuses this case up front, so reaching here
                    // means the storage disappeared between that check and this
                    // insert, or the delivery came over the broker where there
                    // is nobody to refuse.
                    logger.NoStorageForFab(envelope.Identifier, envelope.Fab, ex);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.IngestDispatchFaulted(envelope.Identifier, envelope.Fab, ex);
                }
            }
        }
        catch (OperationCanceledException ex) when (stoppingToken.IsCancellationRequested)
        {
            logger.PersistenceLoopStopping(ex);
        }
    }

    /// <summary>
    /// Whether this is Postgres refusing the row because no partition covers its
    /// fab (spec 019 FR-008).
    ///
    /// <para>
    /// Unwrapped rather than matched directly: the insert goes through EF, which
    /// wraps every provider exception in <see cref="DbUpdateException"/>. A
    /// <c>catch (PostgresException)</c> here never fires — it was written that
    /// way first, and the envelope fell through to the generic handler and got
    /// the same "something faulted" line this exists to replace. The bare case
    /// is kept for any path that reaches Npgsql without EF in between.
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
