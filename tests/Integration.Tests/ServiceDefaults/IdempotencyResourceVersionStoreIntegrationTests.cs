using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.CameraCatalog.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.Integration.Tests.ServiceDefaults;

/// <summary>
/// Spec 454 (#3096), plan.md §1 — <c>IdempotencyStore&lt;TDbContext&gt;</c> stores and returns the
/// version an answer carried, against the real stack (ADR-0103). I1: the version is written in
/// the same fenced <c>UPDATE</c> as <c>resource_identifier</c>, so an overtaken attempt's late
/// completion cannot write one either. The store applies no policy (I2): it hands the version back
/// and nothing here compares it.
///
/// <para>
/// Driven against <c>CameraCatalogDbContext</c>, as the sibling store tests are: the SQL is shared
/// by every context, so one context proves the statement and the other six migrations are proved
/// by each context's own idempotency integration tests.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class IdempotencyResourceVersionStoreIntegrationTests(AspireFixture aspire)
{
    private const string Endpoint = "TEST /idempotency-resource-version";
    private const string Caller = "resource-version-test@fab";

    private static readonly Guid ResourceA = Guid.Parse("0198f454-0000-7000-8000-00000000000a");
    private static readonly Guid ResourceB = Guid.Parse("0198f454-0000-7000-8000-00000000000b");

    [Fact]
    public async Task A_key_completed_with_a_version_replays_that_version()
    {
        var scope = NewScope();
        await using var db = await aspire.CreateCameraCatalogDbContextAsync();
        IdempotencyStore<CameraCatalogDbContext> store = new(db);

        var reserved = await store.BeginAsync(scope, CancellationToken.None);
        await store.CompleteAsync(reserved.Claim.Value, ResourceA, AggregateVersion.From(7), CancellationToken.None);

        var replay = await store.BeginAsync(scope, CancellationToken.None);

        replay.Outcome.ShouldBe(IdempotencyOutcome.Completed);
        replay.ResourceIdentifier.ShouldBe(Option<Guid>.Some(ResourceA));
        replay.ResourceVersion.ShouldBe(Option<AggregateVersion>.Some(AggregateVersion.From(7)));
    }

    [Fact]
    public async Task A_version_of_zero_is_stored_as_zero_and_not_as_absent()
    {
        var scope = NewScope();
        await using var db = await aspire.CreateCameraCatalogDbContextAsync();
        IdempotencyStore<CameraCatalogDbContext> store = new(db);

        var reserved = await store.BeginAsync(scope, CancellationToken.None);
        await store.CompleteAsync(reserved.Claim.Value, ResourceA, AggregateVersion.From(0), CancellationToken.None);

        var replay = await store.BeginAsync(scope, CancellationToken.None);

        replay.ResourceVersion.ShouldBe(Option<AggregateVersion>.Some(AggregateVersion.From(0)));
    }

    [Fact]
    public async Task A_key_completed_without_a_version_replays_none_not_zero()
    {
        var scope = NewScope();
        await using var db = await aspire.CreateCameraCatalogDbContextAsync();
        IdempotencyStore<CameraCatalogDbContext> store = new(db);

        var reserved = await store.BeginAsync(scope, CancellationToken.None);
        await store.CompleteAsync(reserved.Claim.Value, ResourceA, CancellationToken.None);

        var replay = await store.BeginAsync(scope, CancellationToken.None);

        replay.Outcome.ShouldBe(IdempotencyOutcome.Completed);
        replay.ResourceVersion.ShouldBe(Option<AggregateVersion>.None);
    }

    /// <summary>
    /// The fence of #2491 carries the version with it: an overtaken attempt's late completion
    /// matches no row, so it writes neither the identifier nor a version onto the new holder's
    /// reservation.
    /// </summary>
    [Fact]
    public async Task A_late_completion_with_a_version_from_an_overtaken_attempt_writes_nothing()
    {
        var scope = NewScope();
        await using var dbA = await aspire.CreateCameraCatalogDbContextAsync();
        IdempotencyStore<CameraCatalogDbContext> storeA = new(dbA);
        var claimA = (await storeA.BeginAsync(scope, CancellationToken.None)).Claim.Value;
        await AgeAsync(scope, TimeSpan.FromMinutes(30));

        await using var dbB = await aspire.CreateCameraCatalogDbContextAsync();
        IdempotencyStore<CameraCatalogDbContext> storeB = new(dbB);
        var reclaimed = await storeB.BeginAsync(scope, CancellationToken.None);
        reclaimed.Outcome.ShouldBe(IdempotencyOutcome.Reserved, "B must have taken over the stale row.");

        await storeA.CompleteAsync(claimA, ResourceA, AggregateVersion.From(9), CancellationToken.None);

        (await StoredVersionsAsync(scope)).ShouldBeEmpty("the overtaken attempt's version must not be written.");

        await storeB.CompleteAsync(reclaimed.Claim.Value, ResourceB, AggregateVersion.From(2), CancellationToken.None);
        var replay = await storeB.BeginAsync(scope, CancellationToken.None);
        replay.ResourceIdentifier.ShouldBe(Option<Guid>.Some(ResourceB));
        replay.ResourceVersion.ShouldBe(Option<AggregateVersion>.Some(AggregateVersion.From(2)));
    }

    private static IdempotencyScope NewScope() =>
        IdempotencyScope.For(
            IdempotencyKey.From($"resource-version-{Guid.CreateVersion7():N}"),
            Endpoint,
            Caller,
            Option<string>.Some("munich"),
            IdempotencyFingerprint.Of(new ProbeRequest("alpha")));

    private async Task AgeAsync(IdempotencyScope scope, TimeSpan age)
    {
        await using var db = await aspire.CreateCameraCatalogDbContextAsync();

        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE idempotency_key
               SET reserved_at = NOW() - {2}
             WHERE key = {0} AND endpoint = {1};
            """,
            scope.Key.Value, scope.Endpoint, age);
    }

    private async Task<List<int>> StoredVersionsAsync(IdempotencyScope scope)
    {
        await using var db = await aspire.CreateCameraCatalogDbContextAsync();

        return await db.Database
            .SqlQueryRaw<int>(
                """
                SELECT resource_version AS "Value" FROM idempotency_key
                 WHERE key = {0} AND endpoint = {1} AND resource_version IS NOT NULL;
                """,
                scope.Key.Value, scope.Endpoint)
            .ToListAsync();
    }

    private sealed record ProbeRequest(string Value);
}
