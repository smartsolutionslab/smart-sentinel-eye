using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
/// Spec 318 §6.3 — the retire/provision race against real Postgres and real
/// MediaMTX, and the facts whose job is to make a wrong-but-plausible
/// implementation of the version assertion fail.
///
/// <para>
/// The race is staged deterministically: the retire runs from inside the
/// provision's <c>AddPathAsync</c>, which is the window by construction. The
/// one bounded wait (IT-4) bounds a negative and never orders events.
/// </para>
///
/// <para>
/// <b>The live StreamDistribution service sweeps these rows.</b> Its
/// <c>StreamHealthWatcher</c> reads every non-retired stream in this database
/// every two seconds and saves a health report, which bumps the version. A
/// seeded <c>Provisioning</c> row is moved to <c>Degraded</c> on the first
/// sweep, and that bump landing between a load and a save here would fail the
/// fact for a reason that has nothing to do with it. Seeded rows are therefore
/// written already <c>Degraded</c> with the exact error the watcher would
/// report, so its report changes no column and issues no <c>UPDATE</c>.
/// </para>
///
/// </summary>
[Collection(AspireCollection.Name)]
public class ProvisionRetireRaceIntegrationTests(AspireFixture aspire) : IAsyncLifetime
{
    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");
    private static readonly OperatorIdentifier AnAdmin = OperatorIdentifier.From(Guid.CreateVersion7());
    private static readonly TimeSpan HasNotCompletedWithin = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan CompletesWithin = TimeSpan.FromSeconds(30);

    private const string Source = "rtsp://10.0.5.1/h264";

    // What MediaMtxRtspGateway.GetPathHealthAsync reports for a path MediaMTX
    // does not have, and for one it has that never became ready.
    private const string PathNotRegistered = "path not registered";
    private const string PathNotReady = "not ready";

    public async Task InitializeAsync()
    {
        await aspire.ResetMediaMtxAsync();
        await aspire.ResetStreamDistributionAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// IT-3. The assertion must never make a concurrent retire lose (spec 318
    /// §1.3 item 2). Both contexts load the same version; the provision's check
    /// runs and matches; the retire, which loaded before the check, then saves.
    /// A check implemented as a tracked, version-bumping save would move the
    /// token under the retire and hand it a <see cref="DbUpdateConcurrencyException"/>
    /// — a dead-lettered retire that leaves the path reachable for good.
    /// </summary>
    [Fact]
    public async Task A_retire_that_read_the_row_before_the_assertion_still_commits()
    {
        StreamIdentifier streamIdentifier = await SeedAsync(CameraIdentifier.From(Guid.CreateVersion7()), PathNotRegistered);

        await using StreamDistributionDbContext contextA = await VersionedContextAsync();
        await using StreamDistributionDbContext contextB = await VersionedContextAsync();

        StreamRepository repositoryA = NewRepository(contextA);
        StreamRepository repositoryB = NewRepository(contextB);

        StreamAggregate loadedByA = (await repositoryA.GetByIdentifierAsync(streamIdentifier, CancellationToken.None)).Value;
        StreamAggregate loadedByB = (await repositoryB.GetByIdentifierAsync(streamIdentifier, CancellationToken.None)).Value;

        loadedByB.Version.ShouldBe(loadedByA.Version, "the race needs both sides to have read the same version");

        (await repositoryA.IsUnchangedSinceLoadAsync(loadedByA, CancellationToken.None))
            .ShouldBeTrue("nothing has written since A loaded");

        loadedByB.Retire(new SystemClock());

        await Should.NotThrowAsync(
            () => repositoryB.SaveAsync(CancellationToken.None),
            "the provision's check must not move the token a retire that read before it is predicated on");

        (await CommittedStateAsync(streamIdentifier)).ShouldBe(StreamState.Retired);
    }

    /// <summary>
    /// IT-4. The check must serialise behind a retire whose <c>UPDATE</c> has
    /// run but whose transaction has not committed (spec 318 §1.3 item 3, §9
    /// A1). A plain read would not wait: it would see the old committed version,
    /// answer "unchanged" at once, and the provision would keep a path the
    /// retire is about to commit away.
    ///
    /// <para>
    /// The bounded wait only bounds a negative — "has not completed within one
    /// second" — and orders nothing. Its sole false-pass mode is the machine
    /// stalling for the whole interval so that even a non-waiting check had not
    /// yet returned; it cannot produce a false failure for a correct check,
    /// because a correct check cannot complete while the row lock is held.
    /// </para>
    /// </summary>
    [Fact]
    public async Task The_assertion_waits_for_an_uncommitted_retire_and_then_does_not_match()
    {
        StreamIdentifier streamIdentifier = await SeedAsync(CameraIdentifier.From(Guid.CreateVersion7()), PathNotRegistered);

        await using StreamDistributionDbContext contextA = await VersionedContextAsync();
        await using StreamDistributionDbContext contextB = await VersionedContextAsync();

        StreamRepository repositoryA = NewRepository(contextA);
        StreamRepository repositoryB = NewRepository(contextB);

        StreamAggregate loadedByA = (await repositoryA.GetByIdentifierAsync(streamIdentifier, CancellationToken.None)).Value;
        StreamAggregate loadedByB = (await repositoryB.GetByIdentifierAsync(streamIdentifier, CancellationToken.None)).Value;

        await using IDbContextTransaction retireTransaction = await contextB.Database.BeginTransactionAsync();

        loadedByB.Retire(new SystemClock());
        await repositoryB.SaveAsync(CancellationToken.None);

        Task<bool> assertion = repositoryA.IsUnchangedSinceLoadAsync(loadedByA, CancellationToken.None);

        await Task.WhenAny(assertion, Task.Delay(HasNotCompletedWithin));

        assertion.IsCompleted.ShouldBeFalse(
            "the check answered while the retire's UPDATE was uncommitted — it did not wait on the row lock");

        await retireTransaction.CommitAsync();

        (await assertion.WaitAsync(CompletesWithin))
            .ShouldBeFalse("once the retire commits, the version A loaded is no longer the row's");

        (await repositoryA.ReadCommittedStateAsync(streamIdentifier, CancellationToken.None)).ShouldBe(StreamState.Retired);
    }

    /// <summary>
    /// IT-5, the issue's interleaving on a redelivery: the row exists and its
    /// path is registered; the provision reads the row as live; a retire then
    /// commits and removes the path; the provision re-adds it. The provision
    /// must notice and take the path away again.
    ///
    /// <para>
    /// This is the hole the Application fake cannot see. The fake shares one
    /// <see cref="StreamAggregate"/> instance between handlers, so a provision
    /// that decided on its own tracked <c>stream.State</c> would see the
    /// retire's <c>Retired</c> there and pass. Here the two handlers have
    /// separate contexts: context A still holds the state it loaded, and only
    /// the committed read says <c>Retired</c>.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_retire_inside_the_provision_window_leaves_MediaMTX_without_the_path()
    {
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        MediaMtxPath path = MediaMtxPath.For(camera);

        // The first delivery's work, done: path registered, then the row the
        // watcher would leave behind for a registered path that is not ready.
        await NewGateway().AddPathAsync(path, Source, CancellationToken.None);
        StreamIdentifier seeded = await SeedAsync(camera, PathNotReady);

        await using StreamDistributionDbContext contextA = await VersionedContextAsync();
        await using StreamDistributionDbContext contextB = await VersionedContextAsync();

        WindowedGateway gateway = new(NewGateway(), RetireOn(contextB, camera, seeded), RetireRunsAt.BeforeTheAdd);
        ProvisionStreamCommandHandler provision = NewProvisionHandler(contextA, gateway);

        Result<StreamIdentifier, ProvisionStreamError> result = await provision.HandleAsync(
            new ProvisionStreamCommand(Munich, camera, Source, AnAdmin),
            CancellationToken.None);

        gateway.RetireRuns.ShouldBe(1, "the retire must have run inside the provision's window, or nothing was raced");
        Describe(result).ShouldBe(Describe(seeded), "a provision that learns late that the row is retired reaches P2's outcome");
        (await MediaMtxConfigStatusAsync(path)).ShouldBe(HttpStatusCode.NotFound, "a retired camera's path must not survive the race");
        (await CommittedStateAsync(seeded)).ShouldBe(StreamState.Retired);
    }

    /// <summary>
    /// IT-6, the same race on the first delivery (spec 318 §1.1 P3′): no row
    /// yet; the provision inserts it and commits; a retire lands right behind
    /// the insert and removes the path before the provision registers it.
    /// </summary>
    [Fact]
    public async Task A_retire_between_the_insert_and_the_path_registration_leaves_MediaMTX_without_the_path()
    {
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        MediaMtxPath path = MediaMtxPath.For(camera);

        await using StreamDistributionDbContext contextA = await VersionedContextAsync();
        await using StreamDistributionDbContext contextB = await VersionedContextAsync();

        WindowedGateway gateway = new(NewGateway(), RetireOn(contextB, camera, expected: null), RetireRunsAt.BeforeTheAdd);
        ProvisionStreamCommandHandler provision = NewProvisionHandler(contextA, gateway);

        Result<StreamIdentifier, ProvisionStreamError> result = await provision.HandleAsync(
            new ProvisionStreamCommand(Munich, camera, Source, AnAdmin),
            CancellationToken.None);

        StreamIdentifier inserted = await StreamOfAsync(camera);

        gateway.RetireRuns.ShouldBe(1, "the retire must have run inside the provision's window, or nothing was raced");
        Describe(result).ShouldBe(Describe(inserted));
        (await MediaMtxConfigStatusAsync(path)).ShouldBe(HttpStatusCode.NotFound, "a retired camera's path must not survive the race");
        (await CommittedStateAsync(inserted)).ShouldBe(StreamState.Retired);
    }

    /// <summary>
    /// Adversarial: the retire commits <em>after</em> MediaMTX applied the add
    /// but before the provision's check. The retire's own removal already took
    /// the path, so the provision's compensating removal is a second DELETE of a
    /// path MediaMTX no longer has.
    ///
    /// <para>
    /// The design leans on that DELETE being harmless (spec 318 §1.4 cites
    /// <c>MediaMtxRtspGateway</c>'s 404 tolerance), and only the real MediaMTX
    /// can say what it answers for a missing path. If it answered anything the
    /// gateway does not tolerate, the compensation would throw, the provision
    /// would return <c>RtspGatewayUnavailable</c>, and the outbox would
    /// redeliver a camera-registered event for a camera that is already
    /// correctly torn down — once per redelivery, for as long as it is retried.
    /// The Application fake's gateway removes whatever it is asked to and
    /// cannot show this.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_retire_after_the_add_makes_the_compensation_a_second_removal_that_still_succeeds()
    {
        CameraIdentifier camera = CameraIdentifier.From(Guid.CreateVersion7());
        MediaMtxPath path = MediaMtxPath.For(camera);

        await NewGateway().AddPathAsync(path, Source, CancellationToken.None);
        StreamIdentifier seeded = await SeedAsync(camera, PathNotReady);

        await using StreamDistributionDbContext contextA = await VersionedContextAsync();
        await using StreamDistributionDbContext contextB = await VersionedContextAsync();

        WindowedGateway gateway = new(NewGateway(), RetireOn(contextB, camera, seeded), RetireRunsAt.AfterTheAdd);
        ProvisionStreamCommandHandler provision = NewProvisionHandler(contextA, gateway);

        Result<StreamIdentifier, ProvisionStreamError> result = await provision.HandleAsync(
            new ProvisionStreamCommand(Munich, camera, Source, AnAdmin),
            CancellationToken.None);

        gateway.RetireRuns.ShouldBe(1, "the retire must have run inside the provision's window, or nothing was raced");
        Describe(result).ShouldBe(Describe(seeded), "removing a path the retire already removed is not a gateway failure");
        (await MediaMtxConfigStatusAsync(path)).ShouldBe(HttpStatusCode.NotFound);
        (await CommittedStateAsync(seeded)).ShouldBe(StreamState.Retired);
    }

    /// <summary>
    /// Runs a real <see cref="RetireStreamCommandHandler"/> on its own context,
    /// repository and gateway — a second handler instance, as a concurrent
    /// Wolverine delivery would be. Asserts the retire itself succeeded, so a
    /// fact cannot pass because the staged retire silently did nothing.
    /// </summary>
    private Func<Task> RetireOn(StreamDistributionDbContext context, CameraIdentifier camera, StreamIdentifier? expected) =>
        async () =>
        {
            RetireStreamCommandHandler retire = new(
                NewRepository(context),
                NewGateway(),
                new SystemClock(),
                NullLogger<RetireStreamCommandHandler>.Instance);

            Result<Option<StreamIdentifier>, RetireStreamError> retired =
                await retire.HandleAsync(new RetireStreamCommand(camera), CancellationToken.None);

            retired.IsSuccess.ShouldBeTrue("the staged retire must itself succeed");
            retired.Value.HasValue.ShouldBeTrue("the staged retire must have found the row the provision is working on");

            if (expected is not null)
            {
                retired.Value.Value.ShouldBe(expected.Value);
            }
        };

    private static ProvisionStreamCommandHandler NewProvisionHandler(StreamDistributionDbContext context, IRtspGateway gateway) =>
        new(NewRepository(context), gateway, new SystemClock(), NullLogger<ProvisionStreamCommandHandler>.Instance);

    private static string Describe(Result<StreamIdentifier, ProvisionStreamError> result) =>
        result.Match(identifier => Describe(identifier), error => $"Failure({error})");

    private static string Describe(StreamIdentifier identifier) => $"Success({identifier.Value})";

    private async Task<StreamIdentifier> SeedAsync(CameraIdentifier camera, string watcherError)
    {
        await using StreamDistributionDbContext context = await VersionedContextAsync();
        StreamRepository repository = NewRepository(context);

        SystemClock clock = new();
        StreamAggregate stream = StreamAggregate.Provision(Munich, camera, StreamSourceUrl.From(Source), AnAdmin, clock);
        stream.ReportDegraded(StreamError.Truncating(watcherError), clock);

        repository.Add(stream);
        await repository.SaveAsync(CancellationToken.None);

        return stream.Id;
    }

    private async Task<StreamState> CommittedStateAsync(StreamIdentifier stream)
    {
        await using StreamDistributionDbContext reader = await VersionedContextAsync();

        return await reader.Streams
            .AsNoTracking()
            .Where(candidate => candidate.Id == stream)
            .Select(candidate => candidate.State)
            .SingleAsync();
    }

    private async Task<StreamIdentifier> StreamOfAsync(CameraIdentifier camera)
    {
        await using StreamDistributionDbContext reader = await VersionedContextAsync();

        return await reader.Streams
            .AsNoTracking()
            .Where(candidate => candidate.Camera == camera)
            .Select(candidate => candidate.Id)
            .SingleAsync();
    }

    private async Task<HttpStatusCode> MediaMtxConfigStatusAsync(MediaMtxPath path)
    {
        using HttpClient mediaMtx = aspire.App.CreateHttpClient("mediamtx", "api");
        using HttpResponseMessage response = await mediaMtx.GetAsync($"/v3/config/paths/get/{path.Value}");

        return response.StatusCode;
    }

    private MediaMtxRtspGateway NewGateway() =>
        new(aspire.App.CreateHttpClient("mediamtx", "api"), NullLogger<MediaMtxRtspGateway>.Instance);

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

    private enum RetireRunsAt
    {
        BeforeTheAdd,
        AfterTheAdd,
    }

    /// <summary>
    /// The real gateway, with a retire run once inside the provision's
    /// <c>AddPathAsync</c> — before MediaMTX applies the add (the issue's
    /// window) or after it (the adversarial fact). Only the first add stages
    /// the retire, so an implementation that adds twice is not handed a
    /// second retire to hide behind.
    /// </summary>
    private sealed class WindowedGateway(IRtspGateway inner, Func<Task> retire, RetireRunsAt when) : IRtspGateway
    {
        public int RetireRuns { get; private set; }

        public async Task AddPathAsync(MediaMtxPath path, string rtspSourceUrl, CancellationToken cancellationToken)
        {
            bool stage = RetireRuns == 0;

            if (stage && when == RetireRunsAt.BeforeTheAdd)
            {
                RetireRuns++;
                await retire();
            }

            await inner.AddPathAsync(path, rtspSourceUrl, cancellationToken);

            if (stage && when == RetireRunsAt.AfterTheAdd)
            {
                RetireRuns++;
                await retire();
            }
        }

        public Task RemovePathAsync(MediaMtxPath path, CancellationToken cancellationToken) =>
            inner.RemovePathAsync(path, cancellationToken);

        public Task RepointPathAsync(MediaMtxPath path, string rtspSourceUrl, CancellationToken cancellationToken) =>
            inner.RepointPathAsync(path, rtspSourceUrl, cancellationToken);

        public Task<RtspPathHealth> GetPathHealthAsync(MediaMtxPath path, CancellationToken cancellationToken) =>
            inner.GetPathHealthAsync(path, cancellationToken);

        public Task<IReadOnlyList<MediaMtxPath>> ListConfiguredPathsAsync(CancellationToken cancellationToken) =>
            inner.ListConfiguredPathsAsync(cancellationToken);
    }

    /// <summary>
    /// Stands in for the outbox commit, as <c>EventRepositoryOutboxTests</c>
    /// does: <c>OutboxTransactionalCommit</c> commits its own transaction at
    /// <c>SaveAsync</c> too, so a plain <c>SaveChangesAsync</c> has the same
    /// commit point the race depends on <b>on first delivery</b>. On the
    /// redelivery branch this is not the whole shape: Wolverine's own
    /// transaction is still open while the version-check <c>UPDATE</c> runs,
    /// so a match holds the row lock until the handler returns, which these
    /// tests never model (none opens a surrounding transaction). Correctness
    /// under that real shape was verified separately, not by this fixture —
    /// see the PR for the run.
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
