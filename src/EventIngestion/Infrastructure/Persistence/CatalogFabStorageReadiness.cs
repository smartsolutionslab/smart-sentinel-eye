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
        : this(
            cancellationToken => ReadProvisionedFabsAsync(contexts, clock.UtcNow, cancellationToken),
            clock)
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

    // Ticks in a long rather than a DateTimeOffset, and written through
    // Volatile: both are read outside the gate, and a DateTimeOffset is wider
    // than a word, so a concurrent write can be read torn — yielding a
    // freshness verdict from a moment that never existed.
    private long refreshedAtTicks = DateTimeOffset.MinValue.UtcTicks;

    // Counts catalog reads. A waiter may reuse another thread's result only if a
    // read *started* after the waiter decided it needed one — otherwise it
    // answers from a snapshot older than its own question.
    //
    // A counter rather than a timestamp, deliberately: wall-clock resolution is
    // coarse (tens of milliseconds on Windows), so two requests inside one tick
    // would look simultaneous and the second would reuse a read that predates
    // it. A frozen clock makes that permanent, which is how the test found it.
    private long readGeneration;

    public void Dispose() => gate.Dispose();

    public async Task<bool> IsReadyAsync(FabIdentifier fab, CancellationToken cancellationToken)
    {
        Ensure.That(fab).IsNotNull();

        if (IsFresh() && provisioned.Contains(fab.Value))
        {
            return true;
        }

        // Sampled before the refresh, so "has anyone read since I asked?" is
        // answerable without relying on the clock.
        await RefreshAsync(Volatile.Read(ref readGeneration), cancellationToken);
        return provisioned.Contains(fab.Value);
    }

    private bool IsFresh() =>
        clock.UtcNow.UtcTicks - Volatile.Read(ref refreshedAtTicks) < Ttl.Ticks;

    private async Task RefreshAsync(long generationWhenAsked, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            // A burst of writes for the same unprovisioned fab collapses onto one
            // catalog read — but only onto a read that started *after* this
            // caller decided it needed one. Reusing an earlier one would answer
            // "not provisioned" from a snapshot taken before the question was
            // asked, which is the stale negative this class exists to avoid.
            if (Volatile.Read(ref readGeneration) != generationWhenAsked)
            {
                return;
            }

            Interlocked.Increment(ref readGeneration);

            // Not wrapped in a try/catch. A database failure must surface, not
            // become "not provisioned" — that would report a gap that does not
            // exist and send someone to look in the wrong place.
            provisioned = await readProvisionedFabs(cancellationToken);
            Volatile.Write(ref refreshedAtTicks, clock.UtcNow.UtcTicks);
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<FrozenSet<string>> ReadProvisionedFabsAsync(
        IDbContextFactory<EventIngestionDbContext> contexts,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        // Grandchildren, not children. A fab partition with no monthly range
        // child beneath it accepts nothing — the insert still raises 23514 — so
        // treating the fab partition alone as "ready" answers 202 and then drops
        // the envelope, which is precisely the defect this feature closes.
        //
        // Reachable whenever the rollover has not run since the current month
        // began, and reproduced by this feature's own refusal test when it puts
        // hamburg's partition back.
        const string sql = """
            SELECT fab.relname
            FROM   pg_inherits AS fab_link
            JOIN   pg_class    AS events ON fab_link.inhparent = events.oid
            JOIN   pg_class    AS fab    ON fab_link.inhrelid  = fab.oid
            JOIN   pg_inherits AS month_link ON month_link.inhparent = fab.oid
            JOIN   pg_class    AS month ON month_link.inhrelid = month.oid
            WHERE  events.relname = 'events'
              AND  fab.relkind = 'p'
              AND  month.relname = fab.relname || @monthSuffix;
            """;

        await using EventIngestionDbContext context =
            await contexts.CreateDbContextAsync(cancellationToken);
        await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = sql;

        // A value, not an identifier, so it is parameterised. The suffix is the
        // naming contract FabPartitionProvisioner and the rollover both keep.
        DbParameter monthSuffix = command.CreateParameter();
        monthSuffix.ParameterName = "monthSuffix";
        monthSuffix.Value = $"_{now:yyyyMM}";
        command.Parameters.Add(monthSuffix);

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
