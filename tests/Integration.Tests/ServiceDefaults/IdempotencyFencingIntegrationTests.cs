using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.CameraCatalog.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Integration.Tests.ServiceDefaults;

/// <summary>
/// #2491 — a reclaim (or a sweep) hands a stale key to a new holder, but the
/// overtaken attempt is only *slow*, not dead, and can still act on the row
/// after the takeover. Against the real stack (ADR-0103): the fix is two
/// <c>WHERE</c> predicates and one <c>RETURNING</c>, and a fake store that
/// ignores the SQL proves nothing about either (<c>spec.md</c> §*Why no
/// existing test caught it*).
///
/// <para>
/// Every scenario drives <see cref="IdempotentRequest.ExecuteAsync"/>, never
/// the store directly, so a claim that was not the one <c>BeginAsync</c>
/// returned would also fail the guards below — proving the threading, not
/// only the store's SQL (<c>plan.md</c> §*Why an integration suite*).
/// </para>
///
/// <para>
/// The takeover happens from <i>inside</i> A's <c>work()</c>, because that is
/// the only place a slow-but-not-dead attempt can actually be overtaken by
/// another request (<c>plan.md</c> §*The recipe*).
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class IdempotencyFencingIntegrationTests(AspireFixture aspire)
{
    private const string Endpoint = "TEST /idempotency-fencing";

    // A literal caller is correct here, unlike spec 201's endpoint tests: no
    // HTTP request derives the scope for these cases, so there is no second
    // derivation of `caller` that could disagree with this one. Every store
    // call in this class is handed the very same IdempotencyScope instance.
    private const string Caller = "fencing-test@fab";

    private static readonly Guid ResourceA = Guid.Parse("0198f2a0-0000-7000-8000-00000000000a");
    private static readonly Guid ResourceB = Guid.Parse("0198f2a0-0000-7000-8000-00000000000b");

    /// <summary>
    /// T004 — RED. A's late release must not delete the row a reclaimer (B)
    /// has since reserved. Today it does: <c>ReleaseAsync</c> is guarded only
    /// on <c>resource_identifier IS NULL</c>, which B's unfinished row
    /// satisfies just as well as A's own.
    /// </summary>
    [Fact]
    public async Task A_reclaimed_attempt_that_fails_does_not_release_the_new_holders_reservation()
    {
        IdempotencyScope scope = NewScope();
        await using CameraCatalogDbContext dbA = await aspire.CreateCameraCatalogDbContextAsync();
        IdempotencyStore<CameraCatalogDbContext> storeA = new(dbA);

        IdempotencyClaim? claimB = null;

        await Should.ThrowAsync<InvalidOperationException>(() => IdempotentRequest.ExecuteAsync(
            new IdempotentExecution(Option<IdempotencyScope>.Some(scope), storeA, TimeProvider.System),
            async ct =>
            {
                claimB = await ReclaimFromInsideWorkAsync(scope, ct);

                throw new InvalidOperationException("A is actually dead, just slow to notice.");
            },
            NoReplayExpected,
            CancellationToken.None));

        claimB.ShouldNotBeNull("B must have won its reclaim inside A's work(), or this test proves nothing.");

        (await UnfinishedRowExistsAsync(scope)).ShouldBeTrue(
            "B's live reservation must survive A's late release. Today it does not: A's ReleaseAsync "
            + "matches B's row too, because it is guarded only on resource_identifier IS NULL.");
        (await ReservedAtAsync(scope)).ShouldBe(
            claimB.ReservedAt,
            "the surviving row must still be the one B's claim returned, not some third state.");
    }

    /// <summary>
    /// T005 — RED. This is the ADR-0142 "same key, different answer" hazard
    /// the issue did not name: A's late completion writes A's identifier onto
    /// B's still-open row, so B's own, later <c>CompleteAsync</c> then matches
    /// nothing (the existing <c>resource_identifier IS NULL</c> guard has
    /// already been satisfied by A's write) and updates 0 rows. Every replay
    /// of the key then answers with A's resource, though B's caller was told
    /// B's.
    /// </summary>
    [Fact]
    public async Task A_reclaimed_attempt_that_succeeds_does_not_complete_the_new_holders_reservation()
    {
        IdempotencyScope scope = NewScope();
        await using CameraCatalogDbContext dbA = await aspire.CreateCameraCatalogDbContextAsync();
        IdempotencyStore<CameraCatalogDbContext> storeA = new(dbA);

        IdempotencyClaim? claimB = null;

        IResult result = await IdempotentRequest.ExecuteAsync(
            new IdempotentExecution(Option<IdempotencyScope>.Some(scope), storeA, TimeProvider.System),
            async ct =>
            {
                claimB = await ReclaimFromInsideWorkAsync(scope, ct);

                return IdempotentOutcome.Created(ResourceA, TypedResults.Ok());
            },
            NoReplayExpected,
            CancellationToken.None);

        result.ShouldNotBeNull();
        claimB.ShouldNotBeNull("B must have won its reclaim inside A's work(), or this test proves nothing.");

        await using CameraCatalogDbContext dbB = await aspire.CreateCameraCatalogDbContextAsync();
        IdempotencyStore<CameraCatalogDbContext> storeB = new(dbB);
        await storeB.CompleteAsync(claimB, ResourceB, CancellationToken.None);

        (await ResourceIdentifierAsync(scope)).ShouldBe(
            ResourceB,
            "the row must carry B's identifier — B is the caller who is told this answer. Today it "
            + $"carries A's ({ResourceA}) instead, because A's CompleteAsync overwrote B's still-open "
            + "row before B ever got to complete it.");
    }

    /// <summary>
    /// T006 — RED. The same hazard as T004, but the takeover route is the
    /// sweep (<see cref="IdempotencyReservationSweepHostedService{T}"/>)
    /// rather than an inline reclaim — the issue names only the reclaimer,
    /// but a swept-then-reinserted row is a second way a "B" can appear
    /// under A's feet (<c>spec.md</c> §*Problem*).
    /// </summary>
    [Fact]
    public async Task A_swept_attempt_that_fails_does_not_release_the_next_arrivals_reservation()
    {
        IdempotencyScope scope = NewScope();
        await using CameraCatalogDbContext dbA = await aspire.CreateCameraCatalogDbContextAsync();
        IdempotencyStore<CameraCatalogDbContext> storeA = new(dbA);

        IdempotencyClaim? claimC = null;

        await Should.ThrowAsync<InvalidOperationException>(() => IdempotentRequest.ExecuteAsync(
            new IdempotentExecution(Option<IdempotencyScope>.Some(scope), storeA, TimeProvider.System),
            async ct =>
            {
                await AgeAsync(scope, TimeSpan.FromMinutes(30));

                using IdempotencyReservationSweepHostedService<CameraCatalogDbContext> sweep =
                    await CreateSweepAsync();
                await sweep.RunOnceAsync(ct);

                await using CameraCatalogDbContext dbC =
                    await aspire.CreateCameraCatalogDbContextAsync(ct);
                IdempotencyStore<CameraCatalogDbContext> storeC = new(dbC);
                IdempotencyReservation reservationC = await storeC.BeginAsync(scope, ct);
                reservationC.Outcome.ShouldBe(
                    IdempotencyOutcome.Reserved, "the sweep must have removed A's row for C to reserve afresh.");
                claimC = reservationC.Claim.Value;

                throw new InvalidOperationException("A is actually dead, just slow to notice.");
            },
            NoReplayExpected,
            CancellationToken.None));

        claimC.ShouldNotBeNull("C must have won a fresh reservation after the sweep, or this test proves nothing.");

        (await UnfinishedRowExistsAsync(scope)).ShouldBeTrue(
            "C's live reservation must survive A's late release. Today it does not: A's ReleaseAsync "
            + "matches C's row too, because it is guarded only on resource_identifier IS NULL.");
        (await ReservedAtAsync(scope)).ShouldBe(
            claimC.ReservedAt,
            "the surviving row must still be the one C's claim returned, not some third state.");
    }

    /// <summary>
    /// T007 — guard, green before and after. What fails if the fence matches
    /// nothing at all: a mis-threaded claim, or a token that loses precision
    /// on its round trip through Npgsql. Without this, the three red tests
    /// above could pass for the wrong reason — a fence that matches nothing
    /// would also leave an unrelated row alone.
    /// </summary>
    [Fact]
    public async Task An_attempt_nobody_overtook_still_releases_its_own_reservation()
    {
        IdempotencyScope scope = NewScope();
        await using CameraCatalogDbContext db = await aspire.CreateCameraCatalogDbContextAsync();
        IdempotencyStore<CameraCatalogDbContext> store = new(db);

        await Should.ThrowAsync<InvalidOperationException>(() => IdempotentRequest.ExecuteAsync(
            new IdempotentExecution(Option<IdempotencyScope>.Some(scope), store, TimeProvider.System),
            _ => throw new InvalidOperationException("ordinary failure, nobody overtook this attempt."),
            NoReplayExpected,
            CancellationToken.None));

        (await RowExistsAsync(scope)).ShouldBeFalse(
            "an attempt nobody overtook must still be able to release its own reservation.");
    }

    /// <summary>
    /// T008 — guard, green before and after. The completion counterpart of
    /// <see cref="An_attempt_nobody_overtook_still_releases_its_own_reservation"/>.
    /// </summary>
    [Fact]
    public async Task An_attempt_nobody_overtook_still_completes_its_own_reservation()
    {
        IdempotencyScope scope = NewScope();
        await using CameraCatalogDbContext db = await aspire.CreateCameraCatalogDbContextAsync();
        IdempotencyStore<CameraCatalogDbContext> store = new(db);

        await IdempotentRequest.ExecuteAsync(
            new IdempotentExecution(Option<IdempotencyScope>.Some(scope), store, TimeProvider.System),
            _ => Task.FromResult(IdempotentOutcome.Created(ResourceA, TypedResults.Ok())),
            NoReplayExpected,
            CancellationToken.None);

        (await ResourceIdentifierAsync(scope)).ShouldBe(
            ResourceA, "an attempt nobody overtook must still be able to complete its own reservation.");
    }

    /// <summary>
    /// T009 — characterisation of #2290's existing guard, green before and
    /// after with no assertion changed. B completes <i>during</i> A's
    /// work(), before A returns — the ordering the existing
    /// <c>resource_identifier IS NULL</c> guard already covers, unlike
    /// T005's "B has reserved but not yet completed" ordering.
    /// </summary>
    [Fact]
    public async Task A_late_completion_after_the_new_holder_completed_is_still_a_no_op()
    {
        IdempotencyScope scope = NewScope();
        await using CameraCatalogDbContext dbA = await aspire.CreateCameraCatalogDbContextAsync();
        IdempotencyStore<CameraCatalogDbContext> storeA = new(dbA);

        await IdempotentRequest.ExecuteAsync(
            new IdempotentExecution(Option<IdempotencyScope>.Some(scope), storeA, TimeProvider.System),
            async ct =>
            {
                IdempotencyClaim claimB = await ReclaimFromInsideWorkAsync(scope, ct);

                await using CameraCatalogDbContext dbB =
                    await aspire.CreateCameraCatalogDbContextAsync(ct);
                IdempotencyStore<CameraCatalogDbContext> storeB = new(dbB);
                await storeB.CompleteAsync(claimB, ResourceB, ct);

                return IdempotentOutcome.Created(ResourceA, TypedResults.Ok());
            },
            NoReplayExpected,
            CancellationToken.None);

        (await ResourceIdentifierAsync(scope)).ShouldBe(
            ResourceB,
            "B already completed before A's late write arrived, so A's completion must still be the "
            + "no-op #2290's guard already gives it.");
    }

    private static Task<IResult> NoReplayExpected(Guid identifier, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            $"a replay must never run in this suite — every scenario is a first arrival ({identifier}).");

    /// <summary>
    /// The takeover shared by T004 and T006's inline-reclaim variant: age A's
    /// row past the bound, then reserve it as B from a second connection —
    /// two requests, two connections, exactly as two real HTTP callers would
    /// never share one.
    /// </summary>
    private async Task<IdempotencyClaim> ReclaimFromInsideWorkAsync(
        IdempotencyScope scope, CancellationToken cancellationToken)
    {
        await AgeAsync(scope, TimeSpan.FromMinutes(30));

        await using CameraCatalogDbContext dbB =
            await aspire.CreateCameraCatalogDbContextAsync(cancellationToken);
        IdempotencyStore<CameraCatalogDbContext> storeB = new(dbB);
        IdempotencyReservation reservationB = await storeB.BeginAsync(scope, cancellationToken);

        reservationB.Outcome.ShouldBe(
            IdempotencyOutcome.Reserved, "B must win the reclaim of A's now-stale row, or this test proves nothing.");

        return reservationB.Claim.Value;
    }

    private static IdempotencyScope NewScope() =>
        IdempotencyScope.For(
            IdempotencyKey.From($"fence-{Guid.CreateVersion7():N}"),
            Endpoint,
            Caller,
            Option<string>.Some("fence-fab"),
            IdempotencyFingerprint.Of(new FencingProbeRequest("fence")));

    private sealed record FencingProbeRequest(string Value);

    private async Task AgeAsync(IdempotencyScope scope, TimeSpan age)
    {
        await using CameraCatalogDbContext db = await aspire.CreateCameraCatalogDbContextAsync();

        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE idempotency_key
               SET reserved_at = NOW() - {2}
             WHERE key = {0} AND endpoint = {1};
            """,
            scope.Key.Value, scope.Endpoint, age);
    }

    private async Task<bool> RowExistsAsync(IdempotencyScope scope)
    {
        await using CameraCatalogDbContext db = await aspire.CreateCameraCatalogDbContextAsync();

        int[] rows = await db.Database
            .SqlQueryRaw<int>(
                """
                SELECT 1 AS "Value" FROM idempotency_key
                WHERE key = {0} AND endpoint = {1} AND caller = {2};
                """,
                scope.Key.Value, scope.Endpoint, scope.Caller)
            .ToArrayAsync();

        return rows.Length > 0;
    }

    private async Task<bool> UnfinishedRowExistsAsync(IdempotencyScope scope)
    {
        await using CameraCatalogDbContext db = await aspire.CreateCameraCatalogDbContextAsync();

        int[] rows = await db.Database
            .SqlQueryRaw<int>(
                """
                SELECT 1 AS "Value" FROM idempotency_key
                WHERE key = {0} AND endpoint = {1} AND caller = {2} AND resource_identifier IS NULL;
                """,
                scope.Key.Value, scope.Endpoint, scope.Caller)
            .ToArrayAsync();

        return rows.Length > 0;
    }

    private async Task<DateTime?> ReservedAtAsync(IdempotencyScope scope)
    {
        await using CameraCatalogDbContext db = await aspire.CreateCameraCatalogDbContextAsync();

        DateTime[] rows = await db.Database
            .SqlQueryRaw<DateTime>(
                """
                SELECT reserved_at AS "Value" FROM idempotency_key
                WHERE key = {0} AND endpoint = {1} AND caller = {2};
                """,
                scope.Key.Value, scope.Endpoint, scope.Caller)
            .ToArrayAsync();

        return rows.Length == 0 ? null : rows[0];
    }

    private async Task<Guid?> ResourceIdentifierAsync(IdempotencyScope scope)
    {
        await using CameraCatalogDbContext db = await aspire.CreateCameraCatalogDbContextAsync();

        Guid?[] rows = await db.Database
            .SqlQueryRaw<Guid?>(
                """
                SELECT resource_identifier AS "Value" FROM idempotency_key
                WHERE key = {0} AND endpoint = {1} AND caller = {2};
                """,
                scope.Key.Value, scope.Endpoint, scope.Caller)
            .ToArrayAsync();

        return rows.Length == 0 ? null : rows[0];
    }

    /// <summary>
    /// Copied from <c>StaleIdempotencyReservationIntegrationTests.CreateSweepAsync</c>
    /// rather than shared, per <c>plan.md</c> §*The recipe*: making that
    /// file's private helper shared is a cross-file refactor out of scope
    /// here.
    /// </summary>
    private async Task<IdempotencyReservationSweepHostedService<CameraCatalogDbContext>> CreateSweepAsync()
    {
        string? connectionString =
            await aspire.App.GetConnectionStringAsync(AspireFixture.CameraCatalogConnectionName);

        ServiceCollection services = new();
        services.AddDbContext<CameraCatalogDbContext>(options => options.UseNpgsql(connectionString));
        ServiceProvider provider = services.BuildServiceProvider();

        return new IdempotencyReservationSweepHostedService<CameraCatalogDbContext>(
            provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System,
            Options.Create(new IdempotencyReservationSweepOptions()),
            NullLogger<IdempotencyReservationSweepHostedService<CameraCatalogDbContext>>.Instance);
    }
}
