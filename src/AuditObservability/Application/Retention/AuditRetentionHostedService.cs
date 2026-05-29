using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSentinelEye.Shared.Contracts;
using SmartSentinelEye.Shared.Contracts.AuditObservability;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.AuditObservability.Application.Retention;

/// <summary>
/// Daily retention worker (spec 009 FR-013). Lists chunks past
/// the configured boundary, hands each to
/// <see cref="IAuditChunkArchiver"/>, then drops the chunk via
/// <see cref="IAuditChunkInventory.DropChunkAsync"/> and
/// publishes <see cref="AuditChunkArchivedV1"/>.
///
/// <para>
/// The hot loop calls <see cref="RunOnceAsync"/> which tests can
/// also invoke directly to bypass the timer.
/// </para>
/// </summary>
public sealed class AuditRetentionHostedService(
    IAuditChunkInventory inventory,
    IAuditChunkArchiver archiver,
    IEventBus events,
    IClock clock,
    TimeProvider timeProvider,
    IOptions<AuditRetentionOptions> options,
    ILogger<AuditRetentionHostedService> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        AuditRetentionOptions opts = options.Value;
        log.LogInformation(
            "Audit retention worker started with window {Window} and tick interval {Interval}.",
            opts.RetentionWindow, opts.TickInterval);

        using PeriodicTimer timer = new(opts.TickInterval, timeProvider);
        try
        {
            // Run once at startup so a restart catches up immediately.
            await RunOnceAsync(stoppingToken).ConfigureAwait(false);

            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
    }

    /// <summary>
    /// Process every chunk past the retention boundary exactly
    /// once. Public so integration + retention tests can drive
    /// the worker without spinning the timer.
    /// </summary>
    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        AuditRetentionOptions opts = options.Value;
        DateTimeOffset boundary = clock.UtcNow.Subtract(opts.RetentionWindow);

        IReadOnlyList<AuditChunk> chunks = await inventory
            .ListChunksOlderThanAsync(boundary, cancellationToken).ConfigureAwait(false);
        if (chunks.Count == 0)
        {
            log.LogDebug("Retention sweep at {Boundary}: no chunks past the boundary.", boundary);
            return;
        }

        log.LogInformation(
            "Retention sweep at {Boundary}: {ChunkCount} chunk(s) to archive.",
            boundary, chunks.Count);

        foreach (AuditChunk chunk in chunks)
        {
            await ArchiveAndDropAsync(chunk, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ArchiveAndDropAsync(AuditChunk chunk, CancellationToken cancellationToken)
    {
        try
        {
            ChunkArchiveResult result = await archiver
                .ArchiveChunkAsync(chunk, cancellationToken).ConfigureAwait(false);

            await events.PublishAsync(
                new AuditChunkArchivedV1(
                    chunk.ChunkIdentifier,
                    FabId: null,
                    result.RowCount,
                    chunk.OccurredFrom,
                    chunk.OccurredUntil,
                    clock.UtcNow,
                    result.MinioObjectKey,
                    result.ContentMd5,
                    Metadata: new EventMetadata(Guid.CreateVersion7(), clock.UtcNow, null, null)),
                cancellationToken).ConfigureAwait(false);

            await inventory.DropChunkAsync(chunk, cancellationToken).ConfigureAwait(false);

            log.LogInformation(
                "Archived chunk {ChunkIdentifier} ({RowCount} rows, already-archived={AlreadyArchived}) to {ObjectKey}.",
                chunk.ChunkIdentifier, result.RowCount, result.AlreadyArchived, result.MinioObjectKey);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Leave the chunk in place; next sweep retries. NFR-004
            // accepts up to a 5-minute audit lag during outages.
            log.LogError(ex,
                "Failed to archive chunk {ChunkIdentifier}; leaving it in place for the next sweep.",
                chunk.ChunkIdentifier);
        }
    }
}

/// <summary>
/// Configuration for <see cref="AuditRetentionHostedService"/>.
/// </summary>
public sealed class AuditRetentionOptions
{
    public const string SectionName = "AuditObservability:Retention";

    /// <summary>How old a chunk has to be before it's archived + dropped. Default 90 days (spec 009 FR-013).</summary>
    public TimeSpan RetentionWindow { get; set; } = TimeSpan.FromDays(90);

    /// <summary>How often the worker wakes up to sweep. Default daily.</summary>
    public TimeSpan TickInterval { get; set; } = TimeSpan.FromHours(24);
}
