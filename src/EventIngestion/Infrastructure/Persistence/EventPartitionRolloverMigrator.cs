using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
    ILogger<EventPartitionRolloverMigrator> logger) : IMigrator
{
    public string ContextName => "EventIngestion.PartitionRollover";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await using EventIngestionDbContext context =
            await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

        string[] fabPartitions = await DiscoverFabPartitionsAsync(context, cancellationToken)
            .ConfigureAwait(false);
        if (fabPartitions.Length == 0)
        {
            logger.LogInformation(
                "No per-fab partitions under 'events' yet; skipping rollover. " +
                "Add a fab via 'CREATE TABLE events_<fabId> PARTITION OF events FOR VALUES IN (...)'.");
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

                string ddl =
                    $"CREATE TABLE IF NOT EXISTS {monthlyTable} PARTITION OF {fabPartition} " +
                    $"FOR VALUES FROM ('{fromBound}') TO ('{toBound}');";
                await context.Database.ExecuteSqlRawAsync(ddl, cancellationToken).ConfigureAwait(false);
                logger.LogInformation(
                    "Ensured partition {Partition} (FROM {From} TO {To}).",
                    monthlyTable, fromBound, toBound);
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

        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = discoverySql;
        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            List<string> names = new();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                names.Add(reader.GetString(0));
            }
            return names.ToArray();
        }
        finally
        {
            await context.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    private static IEnumerable<DateTime> NextTwoMonths(DateTime nowUtc)
    {
        DateTime currentMonth = new(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        yield return currentMonth;
        yield return currentMonth.AddMonths(1);
    }
}
