using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace SmartSentinelEye.ServiceDefaults.Tests;

/// <summary>
/// Spec 172 / ADR-0154. Of the four outcomes <see cref="OutboxBacklogHealthCheck{TDbContext}"/>
/// can produce, this is the only one with no coverage anywhere: an unreachable
/// database is not merely uncovered, it <i>is</i> the decision — readiness on
/// this system is a liveness and routing signal, and a shared dependency being
/// unreachable does not fail this replica's readiness (ADR-0154).
///
/// <para>
/// <b>This test was red on its first run, against the tree exactly as it stood
/// before this spec.</b> A connection failure against
/// <see cref="UnreachableConnectionString"/> does not surface to
/// <c>OutboxBacklogHealthCheck</c> as the <c>DbException</c> its
/// <c>catch (DbException ex) when (IsUnreachable(ex))</c> guard expects: EF
/// Core's Npgsql execution strategy (<c>NpgsqlExecutionStrategy</c>) treats any
/// exception <c>NpgsqlException.IsTransient</c> reports as true — which, per
/// Npgsql's own source, is any connection failure carrying an
/// <c>IOException</c>, <c>SocketException</c> <i>or</i> <c>TimeoutException</c>
/// inner exception, i.e. a refused connection just as much as a timed-out one —
/// and rethrows it wrapped in a plain <c>InvalidOperationException</c>, which is
/// not a <c>DbException</c> at all. The production code was widened to also
/// catch that wrapped shape (see <c>OutboxBacklogHealthCheck.cs</c>'s
/// <c>catch (InvalidOperationException ...)</c> block) before this test could
/// pass. That widening is a real, narrow behaviour fix — completing ADR-0154's
/// decision uniformly rather than only for a shape of failure that turns out
/// not to occur — and this file's own history is therefore the red/green pair:
/// red against the unwidened check, green after. It must stay green,
/// unmodified, from here on; a change that turns it red again is reversing the
/// widening, not merely moving a characterisation.
/// </para>
///
/// <para>
/// <b>Spec 238.</b> The audit behind this spec found two shapes the widening
/// above does not cover, and pins both rather than trusting memory of Npgsql's
/// source: a <see cref="PostgresException"/> that carries a <b>transient</b>
/// SQLSTATE (<c>57P03</c>, "cannot connect now") is still wrapped in an
/// <see cref="InvalidOperationException"/> exactly like the connection
/// failures above, but its inner exception has a non-empty
/// <see cref="PostgresException.SqlState"/> — so <c>IsUnreachable</c> is false
/// and it escapes every <c>catch</c> in the check, an accepted gap (spec 238
/// §3). A <b>non-transient</b> SQLSTATE (<c>42P01</c>, "undefined table") is
/// never wrapped at all — it reaches the check as a bare
/// <see cref="PostgresException"/>, confirming the premise the classifiers at
/// <c>UniqueConstraintExceptionHandler</c> and
/// <c>PersistenceLoopHostedService.IsMissingPartition</c> depend on.
/// </para>
/// </summary>
public class OutboxBacklogHealthCheckTests
{
    /// <summary>
    /// No model: <c>SqlQueryRaw</c> needs a connection, not an entity, so an
    /// empty context is enough to drive the check under test.
    /// </summary>
    private sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options) : DbContext(options);

    /// <summary>
    /// Throws a supplied exception before Npgsql ever dials, so a
    /// <see cref="PostgresException"/> shape can be driven through
    /// <see cref="OutboxBacklogHealthCheck{TDbContext}"/> with no real Postgres
    /// listening anywhere (spec 238 §2, assumption A2). Overrides both the sync
    /// and async connection-opening hooks so neither path can bypass it.
    /// </summary>
    private sealed class ThrowingOnOpenInterceptor(Exception exception) : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
            throw exception;

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            throw exception;
    }

    /// <summary>
    /// <c>127.0.0.1:1</c> is a closed loopback port: port 1 (TCP port service
    /// multiplexer) has no listener on a developer machine or a CI runner.
    /// Whether the connection attempt fails via an immediate refusal or a
    /// bounded wait depends on the host's network stack — verified on this
    /// repository's Windows dev environment, both a refused port and a
    /// different, definitely-unbound high port produced the same
    /// <c>TimeoutException</c>-wrapping outcome, and Npgsql's own source marks
    /// both a <c>SocketException</c> and a <c>TimeoutException</c> inner
    /// exception as transient identically (see the class doc comment above) — so
    /// the check under test cannot observe a difference either way. <c>Timeout=1</c>
    /// bounds whichever shape occurs to one second, so this test cannot hang.
    /// </summary>
    private const string UnreachableConnectionString =
        "Host=127.0.0.1;Port=1;Database=probe;Username=probe;Password=probe;Timeout=1";

    [Fact]
    public async Task A_database_that_cannot_be_reached_is_reported_healthy()
    {
        DbContextOptions<ProbeDbContext> options = new DbContextOptionsBuilder<ProbeDbContext>()
            .UseNpgsql(UnreachableConnectionString)
            .Options;

        await using ProbeDbContext database = new(options);

        OutboxBacklogHealthCheck<ProbeDbContext> check = new(
            database,
            NullLogger<OutboxBacklogHealthCheck<ProbeDbContext>>.Instance,
            "wolverine_probe");

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.ShouldBe(
            HealthStatus.Healthy,
            "ADR-0154: readiness is a liveness and routing signal by choice — a shared "
            + "database being unreachable is not this replica's failure to report, and "
            + "draining every replica for an outage none of them caused helps nobody. "
            + "This is a decision, not a bug; if this assertion needed to change, that "
            + "decision was just reversed.");

        result.Data.ShouldContainKey("error");
        result.Data["error"].ShouldBe(typeof(Npgsql.NpgsqlException).Name);
    }

    [Fact]
    public async Task A_transient_refusal_that_carries_a_sqlstate_escapes_the_check_for_its_registration_to_resolve()
    {
        PostgresException transientRefusal = new(
            "cannot connect now",
            severity: "FATAL",
            invariantSeverity: "FATAL",
            sqlState: PostgresErrorCodes.CannotConnectNow);

        DbContextOptions<ProbeDbContext> options = new DbContextOptionsBuilder<ProbeDbContext>()
            .UseNpgsql(UnreachableConnectionString)
            .AddInterceptors(new ThrowingOnOpenInterceptor(transientRefusal))
            .Options;

        await using ProbeDbContext database = new(options);

        OutboxBacklogHealthCheck<ProbeDbContext> check = new(
            database,
            NullLogger<OutboxBacklogHealthCheck<ProbeDbContext>>.Instance,
            "wolverine_probe");

        InvalidOperationException thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => check.CheckHealthAsync(new HealthCheckContext()));

        thrown.InnerException.ShouldBeOfType<PostgresException>(
            "spec 238 §3 accepted this shape escaping to the registration's "
            + "failureStatus: Degraded (ADR-0154 row 4). Red here means either the check now "
            + "handles it — a reclassification under ADR-0154 that needs a human decision and "
            + "an update to spec 238 §3 — or the provider stopped wrapping it.");

        ((PostgresException)thrown.InnerException).SqlState.ShouldBe(PostgresErrorCodes.CannotConnectNow);
    }

    [Fact]
    public async Task A_non_transient_refusal_is_not_wrapped_and_is_reported_as_an_unreadable_backlog()
    {
        PostgresException nonTransientRefusal = new(
            "relation does not exist",
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: PostgresErrorCodes.UndefinedTable);

        DbContextOptions<ProbeDbContext> options = new DbContextOptionsBuilder<ProbeDbContext>()
            .UseNpgsql(UnreachableConnectionString)
            .AddInterceptors(new ThrowingOnOpenInterceptor(nonTransientRefusal))
            .Options;

        await using ProbeDbContext database = new(options);

        OutboxBacklogHealthCheck<ProbeDbContext> check = new(
            database,
            NullLogger<OutboxBacklogHealthCheck<ProbeDbContext>>.Instance,
            "wolverine_probe");

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.ShouldBe(
            HealthStatus.Degraded,
            "red here means the provider now wraps non-transient SQLSTATE errors, so "
            + "UniqueConstraintExceptionHandler.IsUniqueViolation and "
            + "PersistenceLoopHostedService.IsMissingPartition no longer see them "
            + "(spec 238 §2.1 S2, S3).");

        result.Data.ShouldContainKey("error");
        result.Data["error"].ShouldBe(nameof(PostgresException));
    }
}
