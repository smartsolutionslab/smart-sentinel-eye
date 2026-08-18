using System.Collections.Frozen;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence;

/// <summary>
/// Answers <see cref="IFabStorageReadiness"/> from the Postgres catalog: a fab
/// can store events exactly when <c>events_&lt;fab&gt;</c> exists as a partition
/// of <c>events</c> (spec 019).
///
/// <para>
/// Cached, because this sits on the write path and the answer changes about as
/// often as a plant is built. The cache is only ever allowed to make the
/// <b>positive</b> answer fast: a negative is re-read from the catalog before it
/// is returned, so a fab provisioned a minute ago is never refused by a stale
/// snapshot. The asymmetry is deliberate — a wrong "yes" costs one dropped
/// envelope that the persistence loop logs, while a wrong "no" refuses writes
/// for a fab that is perfectly capable of storing them.
/// </para>
/// </summary>
public sealed class CatalogFabStorageReadiness : IFabStorageReadiness, IDisposable
{
    private readonly Func<CancellationToken, Task<FrozenSet<string>>> readProvisionedFabs;
    private readonly IClock clock;

    public CatalogFabStorageReadiness(
        IDbContextFactory<EventIngestionDbContext> contexts, IClock clock)
        : this(cancellationToken => ReadProvisionedFabsAsync(contexts, cancellationToken), clock)
    {
    }

    /// <summary>
    /// Test seam. The cache policy — stale positives allowed, negatives always
    /// re-read, failures surfaced — is the part worth asserting, and asserting
    /// it should not require a database.
    /// </summary>
    internal CatalogFabStorageReadiness(
        Func<CancellationToken, Task<FrozenSet<string>>> readProvisionedFabs, IClock clock)
    {
        this.readProvisionedFabs = readProvisionedFabs;
        this.clock = clock;
    }

    /// <summary>
    /// Short enough that a fab provisioned mid-shift becomes writable without a
    /// restart; long enough that the catalog is not queried per request.
    /// </summary>
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private readonly SemaphoreSlim gate = new(1, 1);

    private FrozenSet<string> provisioned = new HashSet<string>(StringComparer.Ordinal).ToFrozenSet(StringComparer.Ordinal);
    private DateTimeOffset refreshedAt = DateTimeOffset.MinValue;

    public void Dispose() => gate.Dispose();

    public async Task<bool> IsReadyAsync(FabIdentifier fab, CancellationToken cancellationToken)
    {
        Ensure.That(fab).IsNotNull();

        if (IsFresh() && provisioned.Contains(fab.Value))
        {
            return true;
        }

        await RefreshAsync(cancellationToken);
        return provisioned.Contains(fab.Value);
    }

    private bool IsFresh() => clock.UtcNow - refreshedAt < Ttl;

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        // Captured before the wait, so the check below asks the right question:
        // "did somebody refresh while I queued?" — not "is the snapshot young?".
        //
        // Asking the second was a bug the tests caught: a miss inside the TTL
        // returned here without reading anything, so a fab provisioned a minute
        // ago stayed refused for the rest of the window. That is exactly the
        // stale negative the contract forbids.
        DateTimeOffset before = refreshedAt;

        await gate.WaitAsync(cancellationToken);
        try
        {
            // A burst of writes for an unprovisioned fab collapses into one
            // catalog read rather than one per request.
            if (refreshedAt != before)
            {
                return;
            }

            // Not wrapped in a try/catch. A database failure must surface, not
            // become "not provisioned" — that would report a gap that does not
            // exist and send someone to look in the wrong place.
            provisioned = await readProvisionedFabs(cancellationToken);
            refreshedAt = clock.UtcNow;
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<FrozenSet<string>> ReadProvisionedFabsAsync(
        IDbContextFactory<EventIngestionDbContext> contexts, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT child.relname
            FROM   pg_inherits
            JOIN   pg_class parent ON pg_inherits.inhparent = parent.oid
            JOIN   pg_class child  ON pg_inherits.inhrelid  = child.oid
            WHERE  parent.relname = 'events'
              AND  child.relkind = 'p';
            """;

        await using EventIngestionDbContext context =
            await contexts.CreateDbContextAsync(cancellationToken);
        await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;

        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            HashSet<string> fabs = new(StringComparer.Ordinal);
            while (await reader.ReadAsync(cancellationToken))
            {
                string relation = reader.GetString(0);
                if (relation.StartsWith("events_", StringComparison.Ordinal))
                {
                    fabs.Add(relation["events_".Length..]);
                }
            }

            return fabs.ToFrozenSet(StringComparer.Ordinal);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }
}
