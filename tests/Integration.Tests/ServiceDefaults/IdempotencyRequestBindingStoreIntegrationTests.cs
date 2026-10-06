using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.CameraCatalog.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Integration.Tests.ServiceDefaults;

/// <summary>
/// Spec 302 (#2424/#2492), plan.md §4 — <c>IdempotencyStore&lt;TDbContext&gt;.BeginAsync</c>'s
/// request-binding comparison, against the real stack (ADR-0103): I2 (a
/// non-claiming arrival is answered from the row only if its fab and
/// fingerprint match), I3 (a NULL stored fingerprint never matches) and the
/// claim-ordering rule that a mismatch is caught <i>before</i> the
/// completed/in-progress branches, so a mismatched in-flight row is refused
/// at once rather than polled.
///
/// <para>
/// <b>Red as a compile error, not an assertion failure.</b> The 5-arg
/// <c>IdempotencyScope.For</c>, <c>IdempotencyFingerprint</c> and
/// <c>IdempotencyOutcome.Mismatched</c> do not exist yet — this is the
/// architect's deliberate second kind of red (plan.md §Testing strategy): the
/// engineer's commit is what makes this file build.
/// </para>
///
/// <para>
/// Shaped after <c>IdempotencyFencingIntegrationTests</c>: every scenario
/// drives the store directly against <c>CameraCatalogDbContext</c>, because
/// the claim is atomic SQL and a fake store would prove nothing about it.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class IdempotencyRequestBindingStoreIntegrationTests(AspireFixture aspire)
{
    private const string Endpoint = "TEST /idempotency-request-binding";
    private const string Caller = "binding-test@fab";

    private static readonly Guid ResourceA = Guid.Parse("0198f3b0-0000-7000-8000-00000000000a");
    private static readonly Guid ResourceB = Guid.Parse("0198f3b0-0000-7000-8000-00000000000b");

    /// <summary>
    /// I2 — a completed row whose stored fingerprint differs from the scope's
    /// is mismatched, not replayed.
    /// </summary>
    [Fact]
    public async Task A_completed_key_presented_with_a_different_fingerprint_is_mismatched()
    {
        IdempotencyKey key = NewKey();
        IdempotencyScope original = Scope(key, Option<string>.Some("munich"), Fingerprint("alpha"));
        await using CameraCatalogDbContext db = await aspire.CreateCameraCatalogDbContextAsync();
        IdempotencyStore<CameraCatalogDbContext> store = new(db);

        IdempotencyReservation reserved = await store.BeginAsync(original, CancellationToken.None);
        reserved.Outcome.ShouldBe(IdempotencyOutcome.Reserved);
        await store.CompleteAsync(reserved.Claim.Value, ResourceA, CancellationToken.None);

        IdempotencyScope differentFingerprint = Scope(key, Option<string>.Some("munich"), Fingerprint("beta"));
        IdempotencyReservation mismatch = await store.BeginAsync(differentFingerprint, CancellationToken.None);

        mismatch.Outcome.ShouldBe(IdempotencyOutcome.Mismatched);
    }

    /// <summary>I2 — same, for the fab half of the binding.</summary>
    [Fact]
    public async Task A_completed_key_presented_with_a_different_fab_is_mismatched()
    {
        IdempotencyKey key = NewKey();
        IdempotencyFingerprint fingerprint = Fingerprint("alpha");
        IdempotencyScope original = Scope(key, Option<string>.Some("munich"), fingerprint);
        await using CameraCatalogDbContext db = await aspire.CreateCameraCatalogDbContextAsync();
        IdempotencyStore<CameraCatalogDbContext> store = new(db);

        IdempotencyReservation reserved = await store.BeginAsync(original, CancellationToken.None);
        await store.CompleteAsync(reserved.Claim.Value, ResourceA, CancellationToken.None);

        IdempotencyScope differentFab = Scope(key, Option<string>.Some("dresden"), fingerprint);
        IdempotencyReservation mismatch = await store.BeginAsync(differentFab, CancellationToken.None);

        mismatch.Outcome.ShouldBe(IdempotencyOutcome.Mismatched);
    }

    /// <summary>
    /// The ordering rule, plan.md §4 step 2 before step 3: a mismatch against
    /// a row that is still unfinished must answer immediately, not report
    /// in-progress and leave the caller to poll.
    /// </summary>
    [Fact]
    public async Task An_unfinished_key_presented_with_a_different_fingerprint_is_mismatched_not_in_progress()
    {
        IdempotencyKey key = NewKey();
        IdempotencyScope original = Scope(key, Option<string>.Some("munich"), Fingerprint("alpha"));
        await using CameraCatalogDbContext db = await aspire.CreateCameraCatalogDbContextAsync();
        IdempotencyStore<CameraCatalogDbContext> store = new(db);

        IdempotencyReservation reserved = await store.BeginAsync(original, CancellationToken.None);
        reserved.Outcome.ShouldBe(IdempotencyOutcome.Reserved, "the row must still be unfinished for this case to mean anything.");

        IdempotencyScope differentFingerprint = Scope(key, Option<string>.Some("munich"), Fingerprint("beta"));
        IdempotencyReservation mismatch = await store.BeginAsync(differentFingerprint, CancellationToken.None);

        mismatch.Outcome.ShouldBe(
            IdempotencyOutcome.Mismatched,
            "an in-flight row with a different binding must be refused at once, not reported in-progress.");
    }

    /// <summary>The positive control: the same fab and fingerprint still replays.</summary>
    [Fact]
    public async Task A_completed_key_presented_with_the_same_binding_still_replays()
    {
        IdempotencyKey key = NewKey();
        IdempotencyFingerprint fingerprint = Fingerprint("alpha");
        IdempotencyScope scope = Scope(key, Option<string>.Some("munich"), fingerprint);
        await using CameraCatalogDbContext db = await aspire.CreateCameraCatalogDbContextAsync();
        IdempotencyStore<CameraCatalogDbContext> store = new(db);

        IdempotencyReservation reserved = await store.BeginAsync(scope, CancellationToken.None);
        await store.CompleteAsync(reserved.Claim.Value, ResourceA, CancellationToken.None);

        IdempotencyReservation replay = await store.BeginAsync(scope, CancellationToken.None);

        replay.Outcome.ShouldBe(IdempotencyOutcome.Completed);
        replay.ResourceIdentifier.Value.ShouldBe(ResourceA);
    }

    /// <summary>
    /// OverlayDesigner's no-fab path: a <c>None</c> scope does not match a row
    /// written with a fab, and vice versa — <c>None</c> is a value to compare,
    /// not a wildcard.
    /// </summary>
    [Fact]
    public async Task A_fabless_scope_does_not_match_a_row_written_with_a_fab()
    {
        IdempotencyKey key = NewKey();
        IdempotencyFingerprint fingerprint = Fingerprint("alpha");
        IdempotencyScope withFab = Scope(key, Option<string>.Some("munich"), fingerprint);
        await using CameraCatalogDbContext db = await aspire.CreateCameraCatalogDbContextAsync();
        IdempotencyStore<CameraCatalogDbContext> store = new(db);

        IdempotencyReservation reserved = await store.BeginAsync(withFab, CancellationToken.None);
        await store.CompleteAsync(reserved.Claim.Value, ResourceA, CancellationToken.None);

        IdempotencyScope withoutFab = Scope(key, Option<string>.None, fingerprint);
        IdempotencyReservation mismatch = await store.BeginAsync(withoutFab, CancellationToken.None);

        mismatch.Outcome.ShouldBe(IdempotencyOutcome.Mismatched);
    }

    /// <summary>The converse of the case above: a fab-bearing scope does not match a fabless row.</summary>
    [Fact]
    public async Task A_fab_bearing_scope_does_not_match_a_row_written_without_a_fab()
    {
        IdempotencyKey key = NewKey();
        IdempotencyFingerprint fingerprint = Fingerprint("alpha");
        IdempotencyScope withoutFab = Scope(key, Option<string>.None, fingerprint);
        await using CameraCatalogDbContext db = await aspire.CreateCameraCatalogDbContextAsync();
        IdempotencyStore<CameraCatalogDbContext> store = new(db);

        IdempotencyReservation reserved = await store.BeginAsync(withoutFab, CancellationToken.None);
        await store.CompleteAsync(reserved.Claim.Value, ResourceA, CancellationToken.None);

        IdempotencyScope withFab = Scope(key, Option<string>.Some("munich"), fingerprint);
        IdempotencyReservation mismatch = await store.BeginAsync(withFab, CancellationToken.None);

        mismatch.Outcome.ShouldBe(IdempotencyOutcome.Mismatched);
    }

    /// <summary>
    /// I3 — a row written before this migration has <c>request_fingerprint IS
    /// NULL</c>; it cannot prove which request it answers, so it fails
    /// closed. Damages a row the system itself wrote (spec 201 precedent:
    /// never hand-insert one), simulating a legacy row rather than fabricating
    /// one no code path could have produced.
    /// </summary>
    [Fact]
    public async Task A_row_written_before_the_binding_existed_is_mismatched()
    {
        IdempotencyKey key = NewKey();
        IdempotencyScope scope = Scope(key, Option<string>.Some("munich"), Fingerprint("alpha"));
        await using CameraCatalogDbContext db = await aspire.CreateCameraCatalogDbContextAsync();
        IdempotencyStore<CameraCatalogDbContext> store = new(db);

        IdempotencyReservation reserved = await store.BeginAsync(scope, CancellationToken.None);
        await store.CompleteAsync(reserved.Claim.Value, ResourceA, CancellationToken.None);
        await NullOutFingerprintAsync(scope);

        IdempotencyReservation replay = await store.BeginAsync(scope, CancellationToken.None);

        replay.Outcome.ShouldBe(
            IdempotencyOutcome.Mismatched,
            "a NULL stored fingerprint must never equal anything, including the scope that originally wrote the row.");
    }

    /// <summary>
    /// A stale reclaim (I1) takes over the binding, not merely the
    /// reservation: the new holder's fab and fingerprint, not the dead
    /// attempt's, are what a later arrival is compared against.
    /// </summary>
    [Fact]
    public async Task A_stale_reservation_reclaimed_by_a_different_request_takes_over_its_binding()
    {
        IdempotencyKey key = NewKey();
        IdempotencyScope deadAttempt = Scope(key, Option<string>.Some("munich"), Fingerprint("alpha"));
        await using CameraCatalogDbContext dbA = await aspire.CreateCameraCatalogDbContextAsync();
        IdempotencyStore<CameraCatalogDbContext> storeA = new(dbA);
        await storeA.BeginAsync(deadAttempt, CancellationToken.None);
        await AgeAsync(deadAttempt, TimeSpan.FromMinutes(30));

        IdempotencyScope reclaimer = Scope(key, Option<string>.Some("dresden"), Fingerprint("beta"));
        await using CameraCatalogDbContext dbB = await aspire.CreateCameraCatalogDbContextAsync();
        IdempotencyStore<CameraCatalogDbContext> storeB = new(dbB);
        IdempotencyReservation reclaimed = await storeB.BeginAsync(reclaimer, CancellationToken.None);
        reclaimed.Outcome.ShouldBe(IdempotencyOutcome.Reserved, "the reclaimer must win the stale row.");
        await storeB.CompleteAsync(reclaimed.Claim.Value, ResourceB, CancellationToken.None);

        IdempotencyReservation replayOfDeadAttempt = await storeA.BeginAsync(deadAttempt, CancellationToken.None);
        replayOfDeadAttempt.Outcome.ShouldBe(
            IdempotencyOutcome.Mismatched,
            "the row's binding now belongs to the reclaimer; the original, dead attempt's own scope must no longer match it.");

        IdempotencyReservation replayOfReclaimer = await storeB.BeginAsync(reclaimer, CancellationToken.None);
        replayOfReclaimer.Outcome.ShouldBe(IdempotencyOutcome.Completed);
        replayOfReclaimer.ResourceIdentifier.Value.ShouldBe(ResourceB);
    }

    private static IdempotencyKey NewKey() => IdempotencyKey.From($"binding-{Guid.CreateVersion7():N}");

    private static IdempotencyFingerprint Fingerprint(string value) =>
        IdempotencyFingerprint.Of(new ProbeRequest(value));

    private static IdempotencyScope Scope(IdempotencyKey key, Option<string> fab, IdempotencyFingerprint fingerprint) =>
        IdempotencyScope.For(key, Endpoint, Caller, fab, fingerprint);

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

    /// <summary>
    /// Damages a row this very test just wrote, rather than hand-inserting a
    /// synthetic legacy one (spec 201 precedent).
    /// </summary>
    private async Task NullOutFingerprintAsync(IdempotencyScope scope)
    {
        await using CameraCatalogDbContext db = await aspire.CreateCameraCatalogDbContextAsync();

        await db.Database.ExecuteSqlRawAsync(
            """
            UPDATE idempotency_key
               SET request_fingerprint = NULL
             WHERE key = {0} AND endpoint = {1} AND caller = {2};
            """,
            scope.Key.Value, scope.Endpoint, scope.Caller);
    }

    private sealed record ProbeRequest(string Value);
}
