using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.AuditObservability.Application.Retention;
using SmartSentinelEye.AuditObservability.Application.Tests.Fakes;
using SmartSentinelEye.Shared.Contracts.AuditObservability;
using SmartSentinelEye.Shared.CQRS;

namespace SmartSentinelEye.AuditObservability.Application.Tests.Retention;

public class AuditRetentionHostedServiceTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-29T02:00:00Z", CultureInfo.InvariantCulture);

    private static AuditChunk StaleChunk(int daysOld = 91) =>
        new(
            ChunkIdentifier: Guid.CreateVersion7(),
            OccurredFrom: Now.AddDays(-daysOld - 30),
            OccurredUntil: Now.AddDays(-daysOld));

    private static AuditChunk FreshChunk(int daysOld = 30) =>
        new(
            ChunkIdentifier: Guid.CreateVersion7(),
            OccurredFrom: Now.AddDays(-daysOld - 30),
            OccurredUntil: Now.AddDays(-daysOld));

    private static AuditRetentionHostedService Build(
        IEnumerable<AuditChunk> chunks,
        FakeAuditChunkArchiver archiver,
        IEventBus bus,
        FakeAuditChunkInventory? inventory = null,
        RecordingJourneyOrigin? journeys = null)
    {
        FakeAuditChunkInventory chunkInventory = inventory ?? new FakeAuditChunkInventory(chunks);

        // Mirror production: the worker is a singleton that resolves its
        // scoped collaborators from a scope factory, so feed the fakes
        // through a real (scoped) service provider.
        ServiceCollection services = new();
        services.AddScoped<IAuditChunkInventory>(_ => chunkInventory);
        services.AddScoped<IAuditChunkArchiver>(_ => archiver);
        services.AddScoped<IEventBus>(_ => bus);

        // Spec 021. The retention sweep publishes without writing through EF, so
        // it flushes the outbox itself — there is no commit for the announcement
        // to ride on. Without this the sweep resolves nothing and throws.
        services.AddScoped<ITransactionalCommit>(_ => new RecordingCommit());
        ServiceProvider provider = services.BuildServiceProvider();

        return new AuditRetentionHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FakeClock(Now),
            TimeProvider.System,
            Options.Create(new AuditRetentionOptions()),
            journeys ?? new RecordingJourneyOrigin(),
            NullLogger<AuditRetentionHostedService>.Instance);
    }

    [Fact]
    public async Task Archives_then_drops_each_stale_chunk_and_publishes_V1()
    {
        AuditChunk stale1 = StaleChunk();
        AuditChunk stale2 = StaleChunk(120);
        AuditChunk fresh = FreshChunk();
        FakeAuditChunkInventory inventory = new([stale1, stale2, fresh]);
        FakeAuditChunkArchiver archiver = new();
        FakeBus bus = new();

        AuditRetentionHostedService worker = Build(
            chunks: [], archiver: archiver, bus: bus, inventory: inventory);

        await worker.RunOnceAsync(default);

        archiver.ArchivedChunks.Count.ShouldBe(2);
        inventory.Dropped.Select(c => c.ChunkIdentifier)
            .OrderBy(g => g)
            .ShouldBe(new[] { stale1.ChunkIdentifier, stale2.ChunkIdentifier }.OrderBy(g => g));
        bus.Published.OfType<AuditChunkArchivedV1>().Count().ShouldBe(2);
    }

    [Fact]
    public async Task Re_run_after_a_successful_pass_is_a_no_op()
    {
        AuditChunk stale = StaleChunk();
        FakeAuditChunkInventory inventory = new([stale]);
        FakeAuditChunkArchiver archiver = new();
        FakeBus bus = new();

        AuditRetentionHostedService worker = Build(
            chunks: [], archiver: archiver, bus: bus, inventory: inventory);

        await worker.RunOnceAsync(default);
        await worker.RunOnceAsync(default);

        // First pass drops the chunk; the second pass sees no
        // stale chunks at all.
        archiver.ArchivedChunks.Count.ShouldBe(1);
        inventory.Dropped.Count.ShouldBe(1);
        bus.Published.OfType<AuditChunkArchivedV1>().Count().ShouldBe(1);
    }

    [Fact]
    public async Task Archiver_failure_leaves_the_chunk_in_place_for_next_sweep()
    {
        AuditChunk stale = StaleChunk();
        FakeAuditChunkInventory inventory = new([stale]);
        FakeAuditChunkArchiver archiver = new()
        {
            FailNextCall = new InvalidOperationException("minio down"),
        };
        FakeBus bus = new();

        AuditRetentionHostedService worker = Build(
            chunks: [], archiver: archiver, bus: bus, inventory: inventory);

        await worker.RunOnceAsync(default);

        // The failure was swallowed (logged + moved on).
        inventory.Dropped.ShouldBeEmpty();
        bus.Published.OfType<AuditChunkArchivedV1>().ShouldBeEmpty();

        // Next sweep retries successfully.
        await worker.RunOnceAsync(default);
        archiver.ArchivedChunks.Count.ShouldBe(1);
        inventory.Dropped.Count.ShouldBe(1);
    }

    /// <summary>
    /// FR-001, and the ordering a call count cannot check. A journey begun
    /// *around* the publish is what the announcement inherits; one begun and
    /// closed beforehand leaves it exactly as orphaned as it was, while
    /// `Begun.Count` reports both as done.
    ///
    /// <para>
    /// This site has no domain event handler and publishes inline, which
    /// research.md calls the one most likely to be got wrong. Without this the
    /// journey could be moved before the archive and every other test here
    /// would stay green.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_publish_happens_inside_the_journey_it_begins()
    {
        RecordingJourneyOrigin journeys = new();
        OpenJourneyRecordingBus bus = new(journeys);

        AuditRetentionHostedService worker = Build(
            chunks: [StaleChunk()],
            archiver: new FakeAuditChunkArchiver(),
            bus: bus,
            journeys: journeys);

        await worker.RunOnceAsync(default);

        bus.OpenAtPublish.ShouldBe(1, "the publish must be caused by the journey, not merely preceded by one");
    }

    /// <summary>
    /// Spec 027 FR-003 / SC-003. A run archives every chunk past the boundary, so
    /// one journey per run would merge unrelated chunks onto one origin — which
    /// still reads as correct from the downstream end and makes "what did this
    /// archival cause" unanswerable.
    ///
    /// <para>
    /// This is the opposite placement to the stream-health site and the same
    /// rule: there an iteration is usually no announcement, so the journey lives
    /// in the domain event handler; here an iteration is exactly one.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Each_chunk_in_a_run_begins_its_own_journey()
    {
        RecordingJourneyOrigin journeys = new();
        FakeAuditChunkArchiver archiver = new();
        FakeBus bus = new();

        AuditRetentionHostedService worker = Build(
            chunks: [StaleChunk(), StaleChunk(), StaleChunk()],
            archiver: archiver,
            bus: bus,
            journeys: journeys);

        await worker.RunOnceAsync(default);

        bus.Published.OfType<AuditChunkArchivedV1>().Count().ShouldBe(3);
        journeys.Begun.Count.ShouldBe(3, "three chunks are three journeys, not one run");
        journeys.Open.ShouldBe(0, "every journey ends with the chunk that began it");
    }

    /// <summary>
    /// FR-006, SC-005. Nothing to archive is not an archival. Without this the
    /// sink fills with records of work that did not happen — the same failure as
    /// a journey per camera per poll on the other site.
    /// </summary>
    [Fact]
    public async Task A_run_with_nothing_to_archive_begins_no_journey()
    {
        RecordingJourneyOrigin journeys = new();
        FakeBus bus = new();

        AuditRetentionHostedService worker = Build(
            chunks: [FreshChunk(daysOld: 1)],
            archiver: new FakeAuditChunkArchiver(),
            bus: bus,
            journeys: journeys);

        await worker.RunOnceAsync(default);

        bus.Published.ShouldBeEmpty();
        journeys.Begun.ShouldBeEmpty();
    }

    /// <summary>
    /// FR-004, SC-004. A journey that failed otherwise looks identical to one
    /// that succeeded and caused nothing: same name, no children, no error.
    /// Shipped once in spec 026 and caught in code review.
    /// </summary>
    [Fact]
    public async Task A_failed_archival_marks_its_journey()
    {
        RecordingJourneyOrigin journeys = new();
        InvalidOperationException refused = new("minio down");
        FakeAuditChunkArchiver archiver = new() { FailNextCall = refused };

        AuditRetentionHostedService worker = Build(
            chunks: [StaleChunk()],
            archiver: archiver,
            bus: new FakeBus(),
            journeys: journeys);

        await worker.RunOnceAsync(default);

        journeys.Failure.ShouldBeSameAs(refused);
        journeys.Open.ShouldBe(0, "a failed journey still ends");
    }

    /// <summary>
    /// SC-004's other half. A status that is always set carries no information.
    /// </summary>
    [Fact]
    public async Task A_successful_archival_leaves_its_journey_unmarked()
    {
        RecordingJourneyOrigin journeys = new();

        AuditRetentionHostedService worker = Build(
            chunks: [StaleChunk()],
            archiver: new FakeAuditChunkArchiver(),
            bus: new FakeBus(),
            journeys: journeys);

        await worker.RunOnceAsync(default);

        journeys.Failure.ShouldBeNull();
    }

    /// <summary>
    /// Notes how many journeys were open at the moment of the publish, which is
    /// the question `Begun.Count` cannot answer.
    /// </summary>
    private sealed class OpenJourneyRecordingBus(RecordingJourneyOrigin journeys) : IEventBus
    {
        public int OpenAtPublish { get; private set; }

        public Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken cancellationToken = default)
            where TEvent : notnull
        {
            OpenAtPublish = journeys.Open;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Stands in for the outbox flush. These tests are about which chunks are
    /// archived and dropped, not about delivery — the flush is exercised against
    /// a real outbox in the integration suite.
    /// </summary>
    private sealed class RecordingCommit : ITransactionalCommit
    {
        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
