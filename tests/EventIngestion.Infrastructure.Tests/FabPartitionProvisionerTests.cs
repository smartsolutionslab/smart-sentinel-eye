using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Tests;

/// <summary>
/// Spec 019 T009 and T021. Asserts the statement provisioning issues, not the
/// state it leaves behind — an outcome test passes trivially for a fab that had
/// nothing to lose, which is precisely the case FR-006 is about.
/// </summary>
public class FabPartitionProvisionerTests
{
    [Fact]
    public void Creates_the_partition_shape_the_rollover_expects()
    {
        string ddl = FabPartitionProvisioner.BuildPartitionDdl(FabIdentifier.From("berlin"));

        // The name is a contract with EventPartitionRolloverMigrator, which
        // discovers fab partitions from the catalog and appends _yyyyMM to what
        // it finds. A different shape here and the two halves stop meeting.
        ddl.ShouldContain("events_berlin");
        ddl.ShouldContain("PARTITION OF events");
        ddl.ShouldContain("FOR VALUES IN ('berlin')");
        ddl.ShouldContain("PARTITION BY RANGE (ingested_at)");
    }

    /// <summary>
    /// The grammar is kebab-style, and every fab in the realm today happens not
    /// to use a hyphen — which is why nothing caught this. Unquoted,
    /// <c>events_munich-north</c> is <c>syntax error at or near "-"</c>, and
    /// since provisioning does not catch, one such fab would fail the whole run
    /// and leave every fab without storage.
    ///
    /// <para>
    /// The names are safe to interpolate and were not valid to execute. Those
    /// are two different claims, and the allow-list only ever established the
    /// first.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("munich-north")]
    [InlineData("fab-2")]
    [InlineData("a-b-c-d")]
    public void Quotes_the_identifier_so_a_kebab_style_fab_is_valid_sql(string fab)
    {
        string ddl = FabPartitionProvisioner.BuildPartitionDdl(FabIdentifier.From(fab));

        ddl.ShouldContain($"\"events_{fab}\"");
        ddl.ShouldContain($"FOR VALUES IN ('{fab}')");
    }

    [Fact]
    public void Is_idempotent_so_a_second_run_changes_nothing()
    {
        string ddl = FabPartitionProvisioner.BuildPartitionDdl(FabIdentifier.From("munich"));

        ddl.ShouldContain("IF NOT EXISTS");
    }

    /// <summary>
    /// FR-006 — the requirement whose failure cannot be undone. Provisioning is
    /// additive, forever: a fab removed from the realm keeps every event it
    /// recorded, because nothing here can drop, detach or empty a partition.
    /// </summary>
    [Theory]
    [InlineData("DROP")]
    [InlineData("DETACH")]
    [InlineData("TRUNCATE")]
    [InlineData("DELETE")]
    public void Never_issues_a_destructive_statement(string forbidden)
    {
        foreach (string fab in new[] { "munich", "dresden", "berlin", "hamburg" })
        {
            string ddl = FabPartitionProvisioner.BuildPartitionDdl(FabIdentifier.From(fab));

            ddl.ShouldNotContain(forbidden, Case.Insensitive,
                $"provisioning must never destroy storage — '{fab}' would lose every event it has");
        }
    }

    /// <summary>
    /// The grammar is what makes the interpolation safe (research §R3), so this
    /// records that nothing which could change the statement's meaning can even
    /// be constructed.
    /// </summary>
    [Theory]
    [InlineData("munich'; DROP TABLE events; --")]
    [InlineData("MUNICH")]
    [InlineData("fab events")]
    [InlineData("m")]
    public void A_name_that_could_change_the_statement_is_not_a_fab_at_all(string hostile)
    {
        Should.Throw<ArgumentException>(() => FabIdentifier.From(hostile));
    }
}
