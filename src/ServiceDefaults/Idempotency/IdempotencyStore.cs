using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults.Idempotency;

/// <summary>
/// Postgres-backed <see cref="IIdempotencyStore"/> against a context's own
/// schema (ADR-0142), for the same reason the Wolverine outbox lives there —
/// there is no shared database, and inventing one for this would be a larger
/// deviation than the problem warrants.
///
/// <para>
/// Generic over the <see cref="DbContext"/> because the SQL is the only thing
/// this class contains and none of it is context-specific. Identity wrote the
/// first copy; six more were about to be written by hand before it was clear
/// they would differ in nothing but a type parameter. Register it as
/// <c>AddScoped&lt;IIdempotencyStore, IdempotencyStore&lt;XDbContext&gt;&gt;()</c>
/// and add <see cref="IdempotencyKeyTable"/>'s DDL in a migration.
/// </para>
///
/// <para>
/// Raw SQL rather than the change tracker, following
/// <c>VariableValueRequestDedupStore</c>. The claim has to be atomic against
/// concurrent retries of the same key, and <c>INSERT ... ON CONFLICT</c> is the
/// database doing that in one statement; an EF read-then-write would race
/// exactly the request pair this mechanism exists for.
/// </para>
/// </summary>
public sealed class IdempotencyStore<TDbContext>(TDbContext dbContext) : IIdempotencyStore
    where TDbContext : DbContext
{
    public async Task<IdempotencyReservation> BeginAsync(
        IdempotencyScope scope, CancellationToken cancellationToken)
    {
        Ensure.That(scope).IsNotNull();

        // #2290 US1. On conflict, a plain DO NOTHING left a dead attempt's
        // reservation wedged forever: nothing ever cleared resource_identifier
        // IS NULL for a process that crashed between reserve and complete. The
        // DO UPDATE below reclaims such a row for the caller attempting it now
        // — guarded to a stale, still-unfinished row only, never a completed
        // one (ADR-0142 §Consequences; the same resource_identifier IS NULL
        // guard ReleaseAsync already carries below).
        //
        // SET reserved_at = NOW() is not bookkeeping — it is what keeps the
        // reclaim atomic against a second concurrent reclaimer. Postgres
        // serializes the two attempts on the row's lock; the winner's UPDATE
        // commits first and bumps reserved_at to NOW(), so the loser's own
        // WHERE re-evaluates against that just-bumped timestamp, no longer
        // satisfies "< NOW() - StaleAfter", and it gets 0 rows back — falling
        // through to the InProgress path below exactly as it would for any
        // other live attempt. Without this SET, both requests would match and
        // both would run the work, which is the exact double-application
        // ADR-0142 exists to prevent.
        const string claim =
            """
            INSERT INTO idempotency_key (key, endpoint, caller, reserved_at)
            VALUES ({0}, {1}, {2}, NOW())
            ON CONFLICT (key, endpoint, caller) DO UPDATE
               SET reserved_at = NOW()
             WHERE idempotency_key.resource_identifier IS NULL
               AND idempotency_key.reserved_at < NOW() - {3};
            """;

        int inserted = await dbContext.Database.ExecuteSqlRawAsync(
            claim,
            [scope.Key.Value, scope.Endpoint, scope.Caller, IdempotencyReclamation.StaleAfter],
            cancellationToken);

        if (inserted == 1)
        {
            return IdempotencyReservation.Reserved;
        }

        // Someone else holds the key. Whether they finished is the whole
        // question: a completed row replays, an unfinished one is the retry
        // racing the attempt that provoked it.
        Guid?[] existing = await dbContext.Database
            .SqlQueryRaw<Guid?>(
                """
                SELECT resource_identifier AS "Value"
                FROM idempotency_key
                WHERE key = {0} AND endpoint = {1} AND caller = {2};
                """,
                scope.Key.Value, scope.Endpoint, scope.Caller)
            .ToArrayAsync(cancellationToken);

        // Empty means the holder released it between our insert and this read.
        // Reporting in-progress rather than reserved keeps this caller honest:
        // it does not own the key, so it must claim it again rather than assume.
        if (existing.Length == 0 || existing[0] is not { } identifier)
        {
            return IdempotencyReservation.InProgress;
        }

        return IdempotencyReservation.CompletedWith(identifier);
    }

    public async Task CompleteAsync(
        IdempotencyScope scope, Guid resourceIdentifier, CancellationToken cancellationToken)
    {
        Ensure.That(scope).IsNotNull();

        const string sql =
            """
            UPDATE idempotency_key
            SET resource_identifier = {3}, completed_at = NOW()
            WHERE key = {0} AND endpoint = {1} AND caller = {2};
            """;

        await dbContext.Database.ExecuteSqlRawAsync(
            sql, [scope.Key.Value, scope.Endpoint, scope.Caller, resourceIdentifier], cancellationToken);
    }

    public async Task ReleaseAsync(IdempotencyScope scope, CancellationToken cancellationToken)
    {
        Ensure.That(scope).IsNotNull();

        // Guarded on resource_identifier IS NULL so a release arriving late
        // cannot delete a reservation that has since completed — which would
        // turn a replayable answer back into a fresh registration.
        const string sql =
            """
            DELETE FROM idempotency_key
            WHERE key = {0} AND endpoint = {1} AND caller = {2} AND resource_identifier IS NULL;
            """;

        await dbContext.Database.ExecuteSqlRawAsync(
            sql, [scope.Key.Value, scope.Endpoint, scope.Caller], cancellationToken);
    }
}
