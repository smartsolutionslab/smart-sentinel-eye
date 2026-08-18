using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.EventIngestion.Application.Ingress;
using SmartSentinelEye.ServiceDefaults;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence;

/// <summary>
/// Idempotent monthly-partition creator for the <c>events</c> table
/// (spec 006 T108). Runs as part of the MigrationRunner pipeline
/// (ADR-0067) so the next two months of partitions exist for every
/// known fab before any Api service starts, then re-runs nightly via
/// the same MigrationRunner CronJob in prod.
///
/// <para>
/// Discovery walks <c>information_schema.tables</c> for every list-
/// partition under <c>events</c> (named <c>events_&lt;fab&gt;</c>) and
/// ensures the current month + the next month exist as range
/// partitions beneath each. Idempotent — <c>IF NOT EXISTS</c> on
/// every CREATE.
/// </para>
/// </summary>
public sealed class EventPartitionRolloverMigrator(
    IDbContextFactory<EventIngestionDbContext> dbContextFactory,
    IProvisionedFabSource fabs,
    FabPartitionProvisioner provisioner,
    ILogger<EventPartitionRolloverMigrator> logger) : IMigrator
{
    public string ContextName => "EventIngestion.PartitionRollover";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using EventIngestionDbContext context =
            await dbContextFactory.CreateDbContextAsync(cancellationToken);

        // Spec 019 FR-002/FR-004, and the order is the requirement. Every fab
        // that exists gets its partition BEFORE discovery runs, so the rollover
        // below finds a brand-new fab and gives it this month and next in the
        // same pass. Provisioning after the rollover would leave a new fab with
        // a partition and no month beneath it — which stores exactly as little
        // as no partition at all, and would not fix itself until the next run.
        await provisioner.ProvisionAsync(
            context, await fabs.GetFabsAsync(cancellationToken), cancellationToken);

        string[] fabPartitions = await DiscoverFabPartitionsAsync(context, cancellationToken);
        if (fabPartitions.Length == 0)
        {
            logger.NoFabPartitions();
            return;
        }

        DateTime nowUtc = DateTime.UtcNow;
        foreach (DateTime month in NextTwoMonths(nowUtc))
        {
            foreach (string fabPartition in fabPartitions)
            {
                string monthlyTable = $"{fabPartition}_{month:yyyyMM}";
                string fromBound = month.ToString("yyyy-MM-01", CultureInfo.InvariantCulture);
                DateTime nextMonth = month.AddMonths(1);
                string toBound = nextMonth.ToString("yyyy-MM-01", CultureInfo.InvariantCulture);

                // S2077 flags the interpolated DDL. Suppressed: table and
                // partition names cannot be parameterised in Postgres, and
                // nothing here is attacker-reachable. `fabPartition` comes
                // from `pg_class.relname` via DiscoverFabPartitionsAsync — a
                // constant catalog query, so it can only name a table that
                // already exists; `monthlyTable` derives from it; both bounds
                // are invariant-formatted dates off DateTime.UtcNow.
                //
                // That provenance argument still holds *here*, because this
                // loop reads the catalog. It does not hold for the names the
                // provisioner above uses, which come from the realm — see
                // FabPartitionProvisioner, where validation replaces it.
                // Quoted, because a fab name may be kebab-style: events_munich-north
                // unquoted is a syntax error, and one such fab would fail the run
                // for every fab. The catalog stores the unquoted name, so the
                // quoting belongs here rather than in what discovery returns.
                string ddl =
                    $"CREATE TABLE IF NOT EXISTS \"{monthlyTable}\" PARTITION OF \"{fabPartition}\" " +
                    $"FOR VALUES FROM ('{fromBound}') TO ('{toBound}');";
#pragma warning disable S2077
                await context.Database.ExecuteSqlRawAsync(ddl, cancellationToken);
#pragma warning restore S2077
                logger.EnsuredPartition(monthlyTable, fromBound, toBound);
            }
        }
    }

    private static async Task<string[]> DiscoverFabPartitionsAsync(
        EventIngestionDbContext context, CancellationToken cancellationToken)
    {
        // List child tables of `events` that are themselves partitioned
        // (i.e. per-fab list-partitions). Their child range partitions
        // are the monthly tables we're rolling.
        const string discoverySql = """
            SELECT child.relname
            FROM pg_inherits
            JOIN pg_class parent ON pg_inherits.inhparent = parent.oid
            JOIN pg_class child ON pg_inherits.inhrelid = child.oid
            WHERE parent.relname = 'events'
              AND child.relkind = 'p';
            """;

        await using DbCommand command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = discoverySql;
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
            List<string> names = [];
            while (await reader.ReadAsync(cancellationToken))
            {
                names.Add(reader.GetString(0));
            }
            return names.ToArray();
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    private static IEnumerable<DateTime> NextTwoMonths(DateTime nowUtc)
    {
        DateTime currentMonth = new(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        yield return currentMonth;
        yield return currentMonth.AddMonths(1);
    }
}
