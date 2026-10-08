using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.ServiceDefaults.Persistence;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;
using StreamAggregate = SmartSentinelEye.StreamDistribution.Domain.Stream.Stream;

namespace SmartSentinelEye.Integration.Tests.StreamDistribution;

/// <summary>
/// Spec 318 §1.3/§6.2 — the non-bumping version assertion the retire/provision
/// race is closed by, proved against real Postgres and the real
/// <see cref="StreamRepository"/> rather than the in-memory Application fake,
/// which shares instances across handlers and so cannot distinguish "the
/// assertion matched" from "nothing else is watching".
///
/// <para>
/// <see cref="IStreamRepository.IsUnchangedSinceLoadAsync"/> and
/// <see cref="IStreamRepository.ReadCommittedStateAsync"/> do not exist on the
/// interface yet (spec 318 §3) — this class fails to compile until phase 4b
/// adds them. That compile failure is the expected red for this file (spec
/// 302 precedent).
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class StreamRepositoryVersionAssertionIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");
    private static readonly OperatorIdentifier AnAdmin = OperatorIdentifier.From(Guid.CreateVersion7());

    public async Task InitializeAsync() => await aspire.ResetStreamDistributionAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// IT-1. A second context retires the row; the first context's assertion,
    /// loaded before that, must no longer match, and the committed read must
    /// show what actually landed.
    /// </summary>
    [Fact]
    public async Task The_assertion_does_not_match_after_another_context_retires_the_row()
    {
        StreamIdentifier streamIdentifier = await SeedAsync();

        await using StreamDistributionDbContext contextA = await VersionedContextAsync();
        await using StreamDistributionDbContext contextB = await VersionedContextAsync();

        StreamRepository repositoryA = NewRepository(contextA);
        StreamRepository repositoryB = NewRepository(contextB);

        StreamAggregate loadedByA = (await repositoryA.GetByIdentifierAsync(streamIdentifier, CancellationToken.None)).Value;
        StreamAggregate loadedByB = (await repositoryB.GetByIdentifierAsync(streamIdentifier, CancellationToken.None)).Value;

        loadedByB.Retire(new SystemClock());
        await repositoryB.SaveAsync(CancellationToken.None);

        (await repositoryA.IsUnchangedSinceLoadAsync(loadedByA, CancellationToken.None)).ShouldBeFalse();
        (await repositoryA.ReadCommittedStateAsync(streamIdentifier, CancellationToken.None)).ShouldBe(StreamState.Retired);
    }

    /// <summary>
    /// IT-2. Nothing else writes: the assertion matches, and — because it is
    /// a non-bumping check — the row's version is exactly what it was loaded
    /// at (spec 318 §1.3 item 3: it must never make a concurrent writer lose).
    /// </summary>
    [Fact]
    public async Task The_assertion_matches_and_leaves_the_version_unchanged_when_nothing_else_wrote()
    {
        StreamIdentifier streamIdentifier = await SeedAsync();

        await using StreamDistributionDbContext contextA = await VersionedContextAsync();
        StreamRepository repositoryA = NewRepository(contextA);

        StreamAggregate loadedByA = (await repositoryA.GetByIdentifierAsync(streamIdentifier, CancellationToken.None)).Value;
        int loadedVersion = loadedByA.Version;

        (await repositoryA.IsUnchangedSinceLoadAsync(loadedByA, CancellationToken.None)).ShouldBeTrue();

        await using StreamDistributionDbContext reader = await VersionedContextAsync();
        StreamAggregate reread = await reader.Streams.AsNoTracking().SingleAsync(stream => stream.Id == streamIdentifier);

        reread.Version.Value.ShouldBe(loadedVersion, "a non-bumping assertion must not move the token it checks");
    }

    private async Task<StreamIdentifier> SeedAsync()
    {
        await using StreamDistributionDbContext context = await VersionedContextAsync();
        StreamRepository repository = NewRepository(context);

        StreamAggregate stream = StreamAggregate.Provision(
            Munich,
            CameraIdentifier.From(Guid.CreateVersion7()),
            StreamSourceUrl.From("rtsp://10.0.5.1/h264"),
            AnAdmin,
            new SystemClock());

        // Seeded already Degraded with the exact message the live
        // StreamHealthWatcher reports for a path this test never registers
        // with MediaMTX (MediaMtxRtspGateway.GetPathHealthAsync answers 404
        // -> "path not registered"). A background sweep landing mid-test
        // then reports the identical state and error, so EF sees no actual
        // change and the interceptor never bumps the version out from under
        // this fact — otherwise this class is intermittently flaky against
        // the live watcher running in the same Aspire stack.
        stream.ReportDegraded(StreamError.Truncating("path not registered"), new SystemClock());

        repository.Add(stream);
        await repository.SaveAsync(CancellationToken.None);

        return stream.Id;
    }

    private static StreamRepository NewRepository(StreamDistributionDbContext context) =>
        new(context, new SaveChangesCommit(context), new NoOpDomainEventDispatcher());

    private async Task<StreamDistributionDbContext> VersionedContextAsync()
    {
        string connectionString = await aspire.App
            .GetConnectionStringAsync(AspireFixture.StreamDistributionConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{AspireFixture.StreamDistributionConnectionName}' was not provisioned by Aspire.");

        DbContextOptionsBuilder<StreamDistributionDbContext> options = new();
        options.UseNpgsql(connectionString);
        options.AddInterceptors(new AggregateVersionInterceptor());

        return new StreamDistributionDbContext(options.Options);
    }

    /// <summary>
    /// Stands in for the outbox commit (as <c>EventRepositoryOutboxTests</c>
    /// does) — the two members under test are plain EF reads/updates and do
    /// not go through it, so a plain <c>SaveChangesAsync</c> is enough for the
    /// seeding and retiring this class also needs.
    /// </summary>
    private sealed class SaveChangesCommit(StreamDistributionDbContext dbContext) : ITransactionalCommit
    {
        public Task CommitAsync(CancellationToken cancellationToken) =>
            dbContext.SaveChangesAsync(cancellationToken);
    }

    private sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
    {
        public Task DispatchAsync(IEnumerable<IDomainEvent> domainEvents, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
