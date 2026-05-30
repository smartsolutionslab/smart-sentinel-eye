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
        FakeBus bus,
        FakeAuditChunkInventory? inventory = null)
    {
        FakeAuditChunkInventory chunkInventory = inventory ?? new FakeAuditChunkInventory(chunks);

        // Mirror production: the worker is a singleton that resolves its
        // scoped collaborators from a scope factory, so feed the fakes
        // through a real (scoped) service provider.
        ServiceCollection services = new();
        services.AddScoped<IAuditChunkInventory>(_ => chunkInventory);
        services.AddScoped<IAuditChunkArchiver>(_ => archiver);
        services.AddScoped<IEventBus>(_ => bus);
        ServiceProvider provider = services.BuildServiceProvider();

        return new AuditRetentionHostedService(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new FakeClock(Now),
            TimeProvider.System,
            Options.Create(new AuditRetentionOptions()),
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
}
