using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence;

/// <summary>
/// Creates the <c>events_&lt;fab&gt;</c> list-partition for a fab that does not
/// have one (spec 019 FR-002).
///
/// <para>
/// Until this existed, that partition was created by a hand-written migration —
/// once for munich in spec 006, once for dresden in spec 018, and never for
/// whatever fab came next. An event for a fab with no partition raises
/// <c>23514</c> deep inside the persistence loop, long after the caller was
/// told <c>202 Accepted</c>.
/// </para>
///
/// <para>
/// The name shape is a contract, not a convention:
/// <see cref="EventPartitionRolloverMigrator"/> discovers fab partitions from
/// the catalog and appends <c>_yyyyMM</c> to whatever it finds. Produce a
/// different shape here and the two halves stop meeting, silently.
/// </para>
/// </summary>
public sealed class FabPartitionProvisioner(ILogger<FabPartitionProvisioner> logger)
{
    public async Task ProvisionAsync(
        EventIngestionDbContext context,
        IReadOnlyList<FabIdentifier> fabs,
        CancellationToken cancellationToken)
    {
        foreach (FabIdentifier fab in fabs)
        {
#pragma warning disable S2077
            await context.Database.ExecuteSqlRawAsync(BuildPartitionDdl(fab), cancellationToken);
#pragma warning restore S2077

            logger.EnsuredFabPartition(PartitionName(fab), fab);
        }
    }

    /// <summary>
    /// The one statement this class issues, as a value so a test can assert on
    /// it directly. That matters more than it looks: FR-006 says provisioning
    /// must never destroy a fab's events, and an outcome test passes trivially
    /// for a fab that had nothing to lose. Asserting the statement is how the
    /// guarantee is actually checked.
    ///
    /// <para>
    /// S2077 flags the interpolation, and the justification changed with spec
    /// 019 — read this one rather than the older one in
    /// <see cref="EventPartitionRolloverMigrator"/>. It used to be provenance:
    /// names came from <c>pg_class</c>, so they could only name a table that
    /// already existed. They now come from a Keycloak group, which is
    /// administrator-controlled rather than database-derived, and that argument
    /// no longer holds.
    /// </para>
    ///
    /// <para>
    /// What holds instead is validation. <paramref name="fab"/> is a
    /// <see cref="FabIdentifier"/>, so it has already satisfied
    /// <c>^[a-z][a-z0-9-]{1,31}$</c> — an allow-list containing no quote,
    /// semicolon, whitespace, comment marker, backslash or non-ASCII character,
    /// bounded to 32 characters. A group name that fails the grammar never
    /// becomes a <see cref="FabIdentifier"/> at all.
    /// </para>
    ///
    /// <para>
    /// The identifier is nonetheless <b>quoted</b>, because that grammar is
    /// kebab-style and admits a character which breaks the statement without
    /// being an injection: <c>events_munich-north</c> unquoted is
    /// <c>syntax error at or near "-"</c>, and since nothing here catches, one
    /// hyphenated fab would fail the whole run and leave every fab without
    /// storage. Safe to interpolate and valid to execute are two different
    /// claims; the grammar only ever established the first.
    /// </para>
    /// </summary>
    public static string BuildPartitionDdl(FabIdentifier fab)
    {
        Ensure.That(fab).IsNotNull();

        return $"CREATE TABLE IF NOT EXISTS \"{PartitionName(fab)}\" PARTITION OF events "
            + $"FOR VALUES IN ('{fab.Value}') "
            + "PARTITION BY RANGE (ingested_at);";
    }

    /// <summary>
    /// The unquoted name, as it appears in <c>pg_class.relname</c>. Callers
    /// building SQL must quote it — see <see cref="BuildPartitionDdl"/>.
    /// </summary>
    public static string PartitionName(FabIdentifier fab) => $"events_{fab.Value}";
}
