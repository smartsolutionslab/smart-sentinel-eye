using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;

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
/// </summary>
public class OutboxBacklogHealthCheckTests
{
    /// <summary>
    /// No model: <c>SqlQueryRaw</c> needs a connection, not an entity, so an
    /// empty context is enough to drive the check under test.
    /// </summary>
    private sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options) : DbContext(options);

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
}
