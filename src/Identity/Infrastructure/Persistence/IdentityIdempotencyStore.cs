using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Infrastructure.Persistence;

/// <summary>
/// Postgres-backed <see cref="IIdempotencyStore"/> for Identity (ADR-0142), in
/// Identity's own schema for the same reason the Wolverine outbox is — there is
/// no shared database, and inventing one for this would be a larger deviation
/// than the problem warrants.
///
/// <para>
/// Raw SQL rather than the change tracker, following
/// <c>VariableValueRequestDedupStore</c>. The claim has to be atomic against
/// concurrent retries of the same key, and <c>INSERT ... ON CONFLICT</c> is the
/// database doing that in one statement; an EF read-then-write would race
/// exactly the request pair this mechanism exists for.
/// </para>
/// </summary>
public sealed class IdentityIdempotencyStore(IdentityDbContext dbContext) : IIdempotencyStore
{
    public async Task<IdempotencyReservation> BeginAsync(
        IdempotencyScope scope, CancellationToken cancellationToken)
    {
        Ensure.That(scope).IsNotNull();

        const string claim =
            """
            INSERT INTO idempotency_key (key, endpoint, caller, reserved_at)
            VALUES ({0}, {1}, {2}, NOW())
            ON CONFLICT (key, endpoint, caller) DO NOTHING;
            """;

        int inserted = await dbContext.Database.ExecuteSqlRawAsync(
            claim, [scope.Key.Value, scope.Endpoint, scope.Caller], cancellationToken);

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
