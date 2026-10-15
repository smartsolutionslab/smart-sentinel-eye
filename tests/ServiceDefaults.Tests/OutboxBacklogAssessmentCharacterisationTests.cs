using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.ServiceDefaults.Tests.Fakes;

namespace SmartSentinelEye.ServiceDefaults.Tests;

/// <summary>
/// Characterisation (spec 368, ADR-0144), captured green on unmodified code before
/// <c>OutboxBacklogHealthCheck.CheckHealthAsync</c> is split. <see cref="OutboxBacklogHealthCheckTests"/>
/// covers the unreachable and unreadable outcomes; this pins the four read outcomes (empty,
/// healthy backlog, degraded on attempts, degraded on volume): the status, the description, the
/// data dictionary (keys in order, value types), the commands issued and their order, and the one
/// warning log record. Expected texts are hand-written literals.
///
/// <para>
/// <b>No database.</b> The real check runs over an EF context whose connection open is suppressed
/// and whose scalar readers are canned one-row <see cref="DataTable"/>s.
/// </para>
///
/// <para>
/// Counterfactual seen to fail: <c>&lt;=</c> in place of <c>&lt;</c> on the attempts threshold
/// turns <see cref="Four_attempts_and_a_small_backlog_is_healthy_and_logs_nothing"/> or
/// <see cref="Five_attempts_on_a_single_message_is_degraded_and_logs_a_warning"/> red.
/// </para>
/// </summary>
public class OutboxBacklogAssessmentCharacterisationTests
{
    private const string Schema = "wolverine_probe";

    private const string ExpectedCountSql =
        """
        SELECT s."Value"
        FROM (
            SELECT count(*) AS "Value" FROM wolverine_probe.wolverine_outgoing_envelopes
        ) AS s
        LIMIT 2
        """;

    private const string ExpectedAttemptsSql =
        """
        SELECT s."Value"
        FROM (
            SELECT COALESCE(max("attempts"), 0) AS "Value" FROM wolverine_probe.wolverine_outgoing_envelopes
        ) AS s
        LIMIT 2
        """;

    private static string Lf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options) : DbContext(options);

    private sealed class SuppressOpenInterceptor : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result) => InterceptionResult.Suppress();

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default) => new(InterceptionResult.Suppress());
    }

    private sealed class ScalarsInterceptor(Queue<DataTable> tables) : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText.Replace("\r\n", "\n", StringComparison.Ordinal));

            return new(InterceptionResult<DbDataReader>.SuppressWithResult(tables.Dequeue().CreateDataReader()));
        }
    }

    private sealed record Run(
        HealthCheckResult Result,
        IReadOnlyList<string> Commands,
        IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Log);

    private static DataTable Scalar<T>(T value)
    {
        DataTable table = new();
        table.Columns.Add("Value", typeof(T));
        object?[] row = [value];
        table.Rows.Add(row);

        return table;
    }

    private static async Task<Run> CheckWith(long pending, int? attempts)
    {
        Queue<DataTable> tables = new();
        tables.Enqueue(Scalar(pending));
        if (attempts is { } maximum)
        {
            tables.Enqueue(Scalar(maximum));
        }

        ScalarsInterceptor scalars = new(tables);
        var options = new DbContextOptionsBuilder<ProbeDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .AddInterceptors(new SuppressOpenInterceptor(), scalars)
            .Options;

        await using ProbeDbContext database = new(options);
        CapturingLogger<OutboxBacklogHealthCheck<ProbeDbContext>> logger = new();
        OutboxBacklogHealthCheck<ProbeDbContext> check = new(database, logger, Schema);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        return new Run(result, scalars.Commands, logger.Entries);
    }

    private static void ShouldCarry(HealthCheckResult result, long pending, int attempts)
    {
        result.Data.Keys.ShouldBe(["pending", "maxAttempts", "schema"]);
        result.Data["pending"].ShouldBeOfType<long>().ShouldBe(pending);
        result.Data["maxAttempts"].ShouldBeOfType<int>().ShouldBe(attempts);
        result.Data["schema"].ShouldBeOfType<string>().ShouldBe(Schema);
        result.Exception.ShouldBeNull();
    }

    [Fact]
    public async Task An_empty_outbox_is_healthy_after_a_single_query_and_logs_nothing()
    {
        var run = await CheckWith(0, null);

        run.Result.Status.ShouldBe(HealthStatus.Healthy);
        run.Result.Description.ShouldBe("No announcements are waiting.");
        ShouldCarry(run.Result, 0, 0);
        run.Commands.ShouldBe([Lf(ExpectedCountSql)]);
        run.Log.ShouldBeEmpty();
    }

    [Fact]
    public async Task Four_attempts_and_a_small_backlog_is_healthy_and_logs_nothing()
    {
        var run = await CheckWith(4_999, 4);

        run.Result.Status.ShouldBe(HealthStatus.Healthy);
        run.Result.Description.ShouldBe("4999 announcement(s) waiting; most-retried has failed 4 time(s).");
        ShouldCarry(run.Result, 4_999, 4);
        run.Commands.ShouldBe([Lf(ExpectedCountSql), Lf(ExpectedAttemptsSql)]);
        run.Log.ShouldBeEmpty();
    }

    [Fact]
    public async Task Five_attempts_on_a_single_message_is_degraded_and_logs_a_warning()
    {
        var run = await CheckWith(1, 5);

        run.Result.Status.ShouldBe(HealthStatus.Degraded);
        run.Result.Description.ShouldBe("1 announcement(s) waiting; most-retried has failed 5 time(s).");
        ShouldCarry(run.Result, 1, 5);
        run.Commands.ShouldBe([Lf(ExpectedCountSql), Lf(ExpectedAttemptsSql)]);
        run.Log.Count.ShouldBe(1);
        run.Log[0].Level.ShouldBe(LogLevel.Warning);
        run.Log[0].Message.ShouldBe(
            "Outbox wolverine_probe is not draining: 1 announcement(s) waiting, most-retried has failed 5 time(s).");
        run.Log[0].Exception.ShouldBeNull();
    }

    [Fact]
    public async Task Five_thousand_waiting_announcements_with_no_attempts_is_degraded_and_logs_a_warning()
    {
        var run = await CheckWith(5_000, 0);

        run.Result.Status.ShouldBe(HealthStatus.Degraded);
        run.Result.Description.ShouldBe("5000 announcement(s) waiting; most-retried has failed 0 time(s).");
        ShouldCarry(run.Result, 5_000, 0);
        run.Commands.ShouldBe([Lf(ExpectedCountSql), Lf(ExpectedAttemptsSql)]);
        run.Log.Count.ShouldBe(1);
        run.Log[0].Level.ShouldBe(LogLevel.Warning);
        run.Log[0].Message.ShouldBe(
            "Outbox wolverine_probe is not draining: 5000 announcement(s) waiting, most-retried has failed 0 time(s).");
    }
}
