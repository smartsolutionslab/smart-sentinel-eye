using Microsoft.Extensions.DependencyInjection;
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
    IServiceScopeFactory scopeFactory,
    IClock clock,
    TimeProvider timeProvider,
    IOptions<AuditRetentionOptions> options,
    IJourneyOrigin journeys,
    ILogger<AuditRetentionHostedService> logger)
    : BackgroundService
{
    /// <summary>
    /// What the journey is called wherever someone goes looking for it.
    /// </summary>
    private const string OriginName = "archive audit chunk";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        AuditRetentionOptions opts = options.Value;
        logger.RetentionWorkerStarted(opts.RetentionWindow, opts.TickInterval);

        using PeriodicTimer timer = new(opts.TickInterval, timeProvider);
        try
        {
            // Run once at startup so a restart catches up immediately.
            await RunOnceAsync(stoppingToken);

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunOnceAsync(stoppingToken);
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

        // The inventory, archiver, and event bus are scoped (they own a
        // DbContext / outbox session); this hosted service is a singleton,
        // so resolve them inside a per-sweep scope rather than injecting
        // them into the constructor.
        using IServiceScope scope = scopeFactory.CreateScope();
        RetentionDependencies deps = new(
            scope.ServiceProvider.GetRequiredService<IAuditChunkInventory>(),
            scope.ServiceProvider.GetRequiredService<IAuditChunkArchiver>(),
            scope.ServiceProvider.GetRequiredService<IEventBus>(),
            scope.ServiceProvider.GetRequiredService<ITransactionalCommit>());

        IReadOnlyList<AuditChunk> chunks = await deps.Inventory.ListChunksOlderThanAsync(boundary, cancellationToken);
        if (chunks.Count == 0)
        {
            logger.RetentionSweepNoChunks(boundary);
            return;
        }

        logger.RetentionSweepChunksToArchive(boundary, chunks.Count);

        foreach (AuditChunk chunk in chunks)
        {
            await ArchiveAndDropAsync(deps, chunk, cancellationToken);
        }
    }

    private async Task ArchiveAndDropAsync(
        RetentionDependencies deps, AuditChunk chunk, CancellationToken cancellationToken)
    {
        // Spec 027. This sweep publishes from a background loop, where nothing
        // is in progress, so without a journey the announcement is an orphan and
        // nothing downstream can be traced back to the run that archived it.
        //
        // Inside the loop, which is the OPPOSITE placement to the stream-health
        // site — and the same rule. One journey per announcement: there an
        // iteration is usually no announcement, so the journey belongs in the
        // domain event handler; here an iteration is exactly one, so it belongs
        // here (FR-003).
        //
        // Worth stating because there is no domain event handler on this path.
        // Anyone arriving from spec 026 looks for one, does not find it, and
        // moves on — which is how this call site stayed an orphan while the
        // mechanism to fix it was already registered for this context.
        using IJourney journey = journeys.Begin(OriginName);

        try
        {
            ChunkArchiveResult result = await deps.Archiver.ArchiveChunkAsync(chunk, cancellationToken);

            AuditChunkArchivedV1 @event = new(
                chunk.ChunkIdentifier,
                FabId: null,
                result.RowCount,
                chunk.OccurredFrom,
                chunk.OccurredUntil,
                clock.UtcNow,
                result.MinioObjectKey,
                result.ContentMd5,
                Metadata: new EventMetadata(Guid.CreateVersion7(), clock.UtcNow, null, null));
            await deps.Events.PublishAsync(@event, cancellationToken);

            // Spec 021. Publishing captures the message into the outbox; this
            // releases it. Nothing else here writes through EF, so this commit
            // saves no rows — it exists because a publish with no accompanying
            // write has no commit to ride on, and without it the announcement
            // sits in the outbox for ever.
            //
            // The atomicity the rest of the feature buys is vacuous here: there
            // is no row for the message to share a fate with. What it does buy
            // is that the send survives a crash, which the previous
            // straight-to-broker publish did not.
            await deps.Commit.CommitAsync(cancellationToken);

            await deps.Inventory.DropChunkAsync(chunk, cancellationToken);

            logger.ArchivedChunk(chunk.ChunkIdentifier, result.RowCount, result.AlreadyArchived, result.MinioObjectKey);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Leave the chunk in place; next sweep retries. NFR-004
            // accepts up to a 5-minute audit lag during outages.
            journey.Failed(ex);
            logger.ArchiveChunkFailed(ex, chunk.ChunkIdentifier);
        }
    }

    /// <summary>The per-sweep scoped collaborators (own a DbContext / outbox session).</summary>
    private sealed record RetentionDependencies(
        IAuditChunkInventory Inventory,
        IAuditChunkArchiver Archiver,
        IEventBus Events,
        ITransactionalCommit Commit);
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
