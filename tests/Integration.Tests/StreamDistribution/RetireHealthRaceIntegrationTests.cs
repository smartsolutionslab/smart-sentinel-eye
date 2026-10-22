using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.ServiceDefaults.Persistence;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Commands;
using SmartSentinelEye.StreamDistribution.Application.Commands.Handlers;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Gateways;
using SmartSentinelEye.StreamDistribution.Infrastructure.Persistence;
using StreamAggregate = SmartSentinelEye.StreamDistribution.Domain.Stream.Stream;

namespace SmartSentinelEye.Integration.Tests.StreamDistribution;

/// <summary>
/// Spec 436 / ADR-0182 Implementation Note 1 (#2783): the race between the retire and the health sweep, on real
/// Postgres and real MediaMTX. A health save on a separate <c>DbContext</c> is interposed between the retire's load and
/// its save, which is what makes the retire's save throw <see cref="DbUpdateConcurrencyException"/>; a second
/// attempt on a fresh scope then loads the new version and retires.
///
/// <para>
/// IT-1 is a premise check and may be green on arrival: it proves the conflict is real, not that Wolverine retries.
/// The retry itself is asserted deterministically in the unit tier (<c>RetireStreamFailurePolicyTests</c>).
/// The interposed write, not the live watcher's timing, forces the conflict; the seed is already Healthy so the
/// watcher's own sweeps are not what the facts depend on.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class RetireHealthRaceIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");
    private static readonly OperatorIdentifier AnAdmin = OperatorIdentifier.From(Guid.CreateVersion7());

    private const string Source = "rtsp://10.0.5.1/h264";

    public async Task InitializeAsync()
    {
        await aspire.ResetMediaMtxAsync();
        await aspire.ResetStreamDistributionAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_health_save_between_the_retires_load_and_save_makes_the_retire_lose_the_race()
    {
        var camera = CameraIdentifier.From(Guid.CreateVersion7());
        var stream = await SeedHealthyAsync(camera);

        await using var context = await VersionedContextAsync();
        var retire = NewRetireHandler(context, camera);

        await Should.ThrowAsync<DbUpdateConcurrencyException>(
            () => retire.HandleAsync(new RetireStreamCommand(camera), CancellationToken.None),
            "the interposed health save moved the version the retire loaded");

        (await CommittedStateAsync(stream)).ShouldNotBe(StreamState.Retired, "the lost retire wrote nothing");
    }

    [Fact]
    public async Task A_second_attempt_on_a_fresh_scope_retires_the_stream_and_removes_its_path()
    {
        var camera = CameraIdentifier.From(Guid.CreateVersion7());
        var path = MediaMtxPath.For(camera);
        var stream = await SeedHealthyAsync(camera);

        await using (var firstScope = await VersionedContextAsync())
        {
            await Should.ThrowAsync<DbUpdateConcurrencyException>(
                () => NewRetireHandler(firstScope, camera)
                    .HandleAsync(new RetireStreamCommand(camera), CancellationToken.None));
        }

        await using var freshScope = await VersionedContextAsync();
        var retried = await NewRetireHandler(freshScope, camera, interpose: false)
            .HandleAsync(new RetireStreamCommand(camera), CancellationToken.None);

        retried.IsSuccess.ShouldBeTrue();
        (await CommittedStateAsync(stream)).ShouldBe(StreamState.Retired);
        (await MediaMtxConfigStatusAsync(path)).ShouldBe(HttpStatusCode.NotFound, "the retired camera's path is gone");
    }

    private RetireStreamCommandHandler NewRetireHandler(
        StreamDistributionDbContext context,
        CameraIdentifier camera,
        bool interpose = true)
    {
        IStreamRepository repository = new StreamRepository(
            context,
            new SaveChangesCommit(context),
            new NoOpDomainEventDispatcher());

        if (interpose)
        {
            repository = new HealthSaveAfterLoad(repository, () => ReportHealthyOnSeparateContextAsync(camera));
        }

        return new RetireStreamCommandHandler(
            repository,
            new MediaMtxRtspGateway(aspire.CreateMediaMtxApiClient(), NullLogger<MediaMtxRtspGateway>.Instance),
            new SystemClock(),
            NullLogger<RetireStreamCommandHandler>.Instance);
    }

    private async Task ReportHealthyOnSeparateContextAsync(CameraIdentifier camera)
    {
        await using var other = await VersionedContextAsync();
        ReportStreamHealthCommandHandler report = new(
            new StreamRepository(other, new SaveChangesCommit(other), new NoOpDomainEventDispatcher()),
            new SystemClock(),
            NullLogger<ReportStreamHealthCommandHandler>.Instance);

        var reported = await report.HandleAsync(
            new ReportStreamHealthCommand(
                camera,
                new RtspPathHealth(
                    IsReady: true,
                    LastError: null,
                    LastFrameAt: null,
                    DetectedMode: TranscodeMode.Passthrough),
                DeclareOffline: false),
            CancellationToken.None);

        reported.IsSuccess.ShouldBeTrue("the interposed health save must itself commit");
    }

    private async Task<StreamIdentifier> SeedHealthyAsync(CameraIdentifier camera)
    {
        using (var gateway = aspire.CreateMediaMtxApiClient())
        {
            await new MediaMtxRtspGateway(gateway, NullLogger<MediaMtxRtspGateway>.Instance)
                .AddPathAsync(MediaMtxPath.For(camera), Source, CancellationToken.None);
        }

        await using var context = await VersionedContextAsync();
        StreamRepository repository = new(context, new SaveChangesCommit(context), new NoOpDomainEventDispatcher());

        SystemClock clock = new();
        var stream = StreamAggregate.Provision(Munich, camera, StreamSourceUrl.From(Source), AnAdmin, clock);
        stream.ReportHealthy(TranscodeMode.Passthrough, clock);

        repository.Add(stream);
        await repository.SaveAsync(CancellationToken.None);

        return stream.Id;
    }

    private async Task<StreamState> CommittedStateAsync(StreamIdentifier stream)
    {
        await using var reader = await VersionedContextAsync();

        return await reader.Streams
            .AsNoTracking()
            .Where(candidate => candidate.Id == stream)
            .Select(candidate => candidate.State)
            .SingleAsync();
    }

    private async Task<HttpStatusCode> MediaMtxConfigStatusAsync(MediaMtxPath path)
    {
        using var mediaMtx = aspire.CreateMediaMtxApiClient();
        using var response = await mediaMtx.GetAsync($"/v3/config/paths/get/{path.Value}");

        return response.StatusCode;
    }

    private async Task<StreamDistributionDbContext> VersionedContextAsync()
    {
        var connectionString = await aspire.App
            .GetConnectionStringAsync(AspireFixture.StreamDistributionConnectionName)
            ?? throw new InvalidOperationException(
                $"Connection string '{AspireFixture.StreamDistributionConnectionName}' was not provisioned by Aspire.");

        DbContextOptionsBuilder<StreamDistributionDbContext> options = new();
        options.UseNpgsql(connectionString);
        options.AddInterceptors(new AggregateVersionInterceptor());

        return new StreamDistributionDbContext(options.Options);
    }

    /// <summary>Runs a health save on another context once, right after the retire's load.</summary>
    private sealed class HealthSaveAfterLoad(IStreamRepository inner, Func<Task> interposed) : IStreamRepository
    {
        private bool interposedOnce;

        public async Task<Option<StreamAggregate>> GetByCameraAsync(
            CameraIdentifier camera,
            CancellationToken cancellationToken)
        {
            var loaded = await inner.GetByCameraAsync(camera, cancellationToken);

            if (!interposedOnce)
            {
                interposedOnce = true;
                await interposed();
            }

            return loaded;
        }

        public Task<Option<StreamAggregate>> GetByIdentifierAsync(
            StreamIdentifier stream,
            CancellationToken cancellationToken) =>
            inner.GetByIdentifierAsync(stream, cancellationToken);

        public Task<Option<StreamAggregate>> GetByPathAsync(MediaMtxPath path, CancellationToken cancellationToken) =>
            inner.GetByPathAsync(path, cancellationToken);

        public void Add(StreamAggregate stream) => inner.Add(stream);

        public Task SaveAsync(CancellationToken cancellationToken) => inner.SaveAsync(cancellationToken);

        public Task<bool> IsUnchangedSinceLoadAsync(StreamAggregate stream, CancellationToken cancellationToken) =>
            inner.IsUnchangedSinceLoadAsync(stream, cancellationToken);

        public Task<StreamState> ReadCommittedStateAsync(StreamIdentifier stream, CancellationToken cancellationToken) =>
            inner.ReadCommittedStateAsync(stream, cancellationToken);
    }

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
