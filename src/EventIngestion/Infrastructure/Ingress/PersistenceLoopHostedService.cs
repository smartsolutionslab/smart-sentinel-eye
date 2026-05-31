using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
        logger.LogInformation("Persistence loop started.");
        try
        {
            await foreach (EventEnvelope envelope in channel.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await DispatchAsync(envelope, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException ex) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation(ex, "Persistence loop stopping (cancellation).");
        }
    }

    private async Task DispatchAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        IngestEventCommandHandler handler =
            scope.ServiceProvider.GetRequiredService<IngestEventCommandHandler>();

        Result<EventIdentifier, IngestEventError> result = await handler
            .HandleAsync(new IngestEventCommand(envelope), cancellationToken)
            .ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            logger.LogWarning(
                "Ingest failed for {Identifier} ({Source}/{Device}): {Code}",
                envelope.Identifier, envelope.Source, envelope.Device, result.Error.Code);
        }
    }
}
