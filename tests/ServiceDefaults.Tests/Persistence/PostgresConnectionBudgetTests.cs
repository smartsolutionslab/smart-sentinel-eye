using Npgsql;
using SmartSentinelEye.ServiceDefaults.Persistence;

namespace SmartSentinelEye.ServiceDefaults.Tests.Persistence;

/// <summary>
/// Issue 1962 / ADR-0125. The arithmetic that keeps nine contexts' pools inside
/// one Postgres, asserted rather than believed.
///
/// <para>
/// The failure these guard against is not a wrong number — it is a tenth
/// context landing and nobody redoing the sum. When the budget runs out the
/// service that fails is whichever one asks next, so the shortfall arrives
/// disguised as an unrelated context's bug.
/// </para>
/// </summary>
public class PostgresConnectionBudgetTests
{
    /// <summary>
    /// The whole point of the type. If every long-running service saturated
    /// every pool at once, the server must still have slots left.
    /// </summary>
    /// <summary>
    /// The whole point of the type, and the reserve is part of it: fitting
    /// exactly is not fitting, because the moment the ceiling is reached is
    /// exactly when someone needs a connection to find out why.
    /// </summary>
    [Fact]
    public void Every_service_saturating_every_pool_still_leaves_the_reserve_intact()
    {
        int required = PostgresConnectionBudget.ServiceCeiling
            + PostgresConnectionBudget.ReservedForToolingAndOperators;

        required.ShouldBeLessThanOrEqualTo(
            PostgresConnectionBudget.ServerMaxConnections,
            $"{PostgresConnectionBudget.Services} services x "
            + $"(({PostgresConnectionBudget.PoolsPerService} pools x {PostgresConnectionBudget.MaxPoolSize}) "
            + $"+ {PostgresConnectionBudget.FixedConnectionsPerDatabase} unpooled) = "
            + $"{PostgresConnectionBudget.ServiceCeiling}, plus "
            + $"{PostgresConnectionBudget.ReservedForToolingAndOperators} reserved = {required}, "
            + $"against a server that allows {PostgresConnectionBudget.ServerMaxConnections}");
    }

    /// <summary>
    /// The unpooled connections are the ones a budget derived from reading the
    /// code misses, and no cap restrains them — so if the count is ever set to
    /// zero the ceiling silently understates the demand by nine slots per
    /// connection.
    /// </summary>
    [Fact]
    public void The_ceiling_counts_the_connections_no_pool_governs()
    {
        PostgresConnectionBudget.FixedConnectionsPerDatabase.ShouldBeGreaterThan(
            0,
            "Wolverine holds an advisory-lock connection and TimescaleDB runs a background "
            + "worker per database; both occupy a max_connections slot outside any pool");

        PostgresConnectionBudget.ServiceCeiling.ShouldBeGreaterThan(
            PostgresConnectionBudget.MaxPoolSize * PostgresConnectionBudget.PoolsPerService
            * PostgresConnectionBudget.Services,
            "the ceiling must exceed the pooled total, or the unpooled connections are not in it");
    }

    /// <summary>
    /// Sized against what was observed rather than what felt safe:
    /// AuditObservability's four listeners plus HTTP peaked at about 11
    /// connections per pool at 100 ev/s. A cap below that would throttle the
    /// heaviest consumer, which is the failure mode a *too small* bound
    /// produces — and it looks like slow ingest, not like a cap.
    /// </summary>
    [Fact]
    public void The_cap_leaves_room_above_the_heaviest_observed_pool()
    {
        const int ObservedPeakPerPool = 11;

        PostgresConnectionBudget.MaxPoolSize.ShouldBeGreaterThan(
            ObservedPeakPerPool + (ObservedPeakPerPool / 2),
            "the cap must clear the heaviest measured pool by a margin, or it throttles instead of protecting");
    }

    [Fact]
    public void A_connection_string_gets_the_cap_applied()
    {
        string bounded = PostgresConnectionBudget.Bounded("Host=localhost;Database=audit-db;Username=postgres");

        new NpgsqlConnectionStringBuilder(bounded).MaxPoolSize
            .ShouldBe(PostgresConnectionBudget.MaxPoolSize);
    }

    /// <summary>
    /// Everything else in the string has to survive. Rebuilding a connection
    /// string is a quiet way to drop a parameter, and the one that goes missing
    /// is discovered in production.
    /// </summary>
    [Fact]
    public void Applying_the_cap_keeps_the_rest_of_the_connection_string()
    {
        string bounded = PostgresConnectionBudget.Bounded(
            "Host=db.internal;Port=6432;Database=audit-db;Username=sse;Password=secret;SSL Mode=Require");

        NpgsqlConnectionStringBuilder parsed = new(bounded);
        parsed.Host.ShouldBe("db.internal");
        parsed.Port.ShouldBe(6432);
        parsed.Database.ShouldBe("audit-db");
        parsed.Username.ShouldBe("sse");
        parsed.Password.ShouldBe("secret");
        parsed.SslMode.ShouldBe(SslMode.Require);
    }

    /// <summary>
    /// The escape hatch, and it is deliberate: a deployment that needs a
    /// different cap says so in its connection string rather than needing a new
    /// knob here. Silently overriding it would make that configuration a lie.
    /// </summary>
    [Fact]
    public void An_explicit_pool_size_in_the_connection_string_wins()
    {
        string bounded = PostgresConnectionBudget.Bounded(
            "Host=localhost;Database=audit-db;Username=postgres;Maximum Pool Size=7");

        new NpgsqlConnectionStringBuilder(bounded).MaxPoolSize.ShouldBe(7);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_connection_string_is_refused(string connectionString) =>
        Should.Throw<ArgumentException>(() => PostgresConnectionBudget.Bounded(connectionString));

    // Its own fact rather than a third InlineData: xUnit1012 rejects a null
    // literal for a non-nullable parameter and fails the Release build.
    [Fact]
    public void A_null_connection_string_is_refused() =>
        Should.Throw<ArgumentException>(() => PostgresConnectionBudget.Bounded(null));
}
