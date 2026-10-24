using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSentinelEye.AuditObservability.Infrastructure.Persistence;
using SmartSentinelEye.AuditObservability.Infrastructure.Redaction;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Spec 449 (#2990), U2, Docker-free, with the interceptor pattern of
/// <c>AuditEventInsertCharacterisationTests</c>: the open is suppressed, the SELECT is answered from a fixed
/// table and every other command is recorded and suppressed with a result of 1. What is asserted is what EF
/// would have sent and what the step would have logged. Markers are synthetic.
///
/// <para>
/// Contract this class pins for the implementation (it may not edit the tests): the SELECT projects the
/// aliases <c>AuditId</c>, <c>OccurredAt</c>, <c>EventIdentifier</c>, <c>EventKind</c>, <c>Url</c> and
/// <c>PreviousUrl</c>; the dry-run and applied log lines carry the structured fields named in plan.md
/// (<c>Examined</c>, <c>RegisteredCount</c>, <c>AddressChangedCount</c>; <c>CountBefore</c>, <c>Rewritten</c>,
/// <c>CountAfter</c>).
/// </para>
/// </summary>
public class AuditUrlRedactionStepTests
{
    private const string RegisteredUrl = "rtsp://USERPWMARK:USERPWMARK@cam-a.test:8554/live/SECRETMARK?token=SECRETMARK";
    private const string ChangedPrevious = "rtsp://cam-b.test/old/SECRETMARK";
    private const string ChangedUrl = "rtsp://USERPWMARK@cam-c.test/new?k=SECRETMARK";
    private static readonly string[] Markers = ["USERPWMARK", "SECRETMARK"];

    private static readonly DateTimeOffset OccurredAt =
        DateTimeOffset.Parse("2026-04-01T08:00:00Z", CultureInfo.InvariantCulture);

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

    private sealed record Issued(string Text, IReadOnlyList<object?> Values);

    private sealed class FakeDatabaseInterceptor : DbCommandInterceptor
    {
        public List<Issued> Selects { get; } = [];

        public List<Issued> NonQueries { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Selects.Add(Capture(command));
            return new(InterceptionResult<DbDataReader>.SuppressWithResult(Rows().CreateDataReader()));
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            NonQueries.Add(Capture(command));
            return new(InterceptionResult<int>.SuppressWithResult(1));
        }

        private static string Normalise(string text) =>
            string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        private static Issued Capture(DbCommand command) =>
            new(
                Normalise(command.CommandText),
                [.. command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value)]);

        private static DataTable Rows()
        {
            DataTable table = new();
            table.Columns.Add("AuditId", typeof(Guid));
            table.Columns.Add("OccurredAt", typeof(DateTimeOffset));
            table.Columns.Add("EventIdentifier", typeof(Guid));
            table.Columns.Add("EventKind", typeof(string));
            table.Columns.Add("Url", typeof(string));
            table.Columns.Add("PreviousUrl", typeof(string));
            table.Rows.Add(Guid.CreateVersion7(), OccurredAt, Guid.CreateVersion7(), "CameraRegisteredV1", RegisteredUrl, DBNull.Value);
            table.Rows.Add(Guid.CreateVersion7(), OccurredAt, Guid.CreateVersion7(), "CameraAddressChangedV1", ChangedUrl, ChangedPrevious);
            table.Rows.Add(Guid.CreateVersion7(), OccurredAt, Guid.CreateVersion7(), "CameraAddressChangedV1", "rtsp://cam-c.test", "rtsp://cam-b.test");
            return table;
        }
    }

    private sealed class FixedFactory(DbContextOptions<AuditObservabilityDbContext> options)
        : IDbContextFactory<AuditObservabilityDbContext>
    {
        public AuditObservabilityDbContext CreateDbContext() => new(options);
    }

    private sealed record Entry(LogLevel Level, string Message, IReadOnlyDictionary<string, object?> State, Exception? Exception);

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<Entry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var fields = (state as IEnumerable<KeyValuePair<string, object?>> ?? [])
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            Entries.Add(new(logLevel, formatter(state, exception), fields, exception));
        }
    }

    private static async Task<(FakeDatabaseInterceptor Database, CapturingLogger<AuditUrlRedactionStep> Logger)> RunAsync(
        AuditUrlRedactionOptions options)
    {
        FakeDatabaseInterceptor database = new();
        var contextOptions = new DbContextOptionsBuilder<AuditObservabilityDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .AddInterceptors(new SuppressOpenInterceptor(), database)
            .Options;
        CapturingLogger<AuditUrlRedactionStep> logger = new();

        await new AuditUrlRedactionStep(new FixedFactory(contextOptions), Options.Create(options), logger)
            .RunAsync(default);

        return (database, logger);
    }

    private static void ShouldCarryNoMarker(string text, string where)
    {
        foreach (var marker in Markers)
        {
            text.ShouldNotContain(marker, Case.Sensitive, $"{where} must not carry a URL");
        }
    }

    [Fact]
    public void Apply_defaults_to_false_and_is_bound_from_its_section()
    {
        new AuditUrlRedactionOptions().Apply.ShouldBeFalse();
        AuditUrlRedactionOptions.SectionName.ShouldBe("AuditObservability:UrlRedaction");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AuditObservability:UrlRedaction:Apply"] = "true" })
            .Build();
        AuditUrlRedactionOptions bound = new();
        configuration.GetSection(AuditUrlRedactionOptions.SectionName).Bind(bound);

        bound.Apply.ShouldBeTrue();
        new ConfigurationBuilder().Build().GetSection(AuditUrlRedactionOptions.SectionName)
            .Get<AuditUrlRedactionOptions>().ShouldBeNull("absent configuration binds nothing, so the default (a dry run) stands");
    }

    [Fact]
    public async Task A_dry_run_reads_the_rows_and_reports_counts_but_issues_no_update()
    {
        var (database, logger) = await RunAsync(new AuditUrlRedactionOptions { Apply = false });

        database.Selects.ShouldNotBeEmpty("the dry run must still look at the rows to count them");
        database.NonQueries.ShouldBeEmpty("a dry run issues no DML");

        var report = logger.Entries.Where(entry => entry.State.ContainsKey("Examined")).ShouldHaveSingleItem();
        report.Level.ShouldBe(LogLevel.Information);
        Convert.ToInt32(report.State["Examined"], CultureInfo.InvariantCulture).ShouldBe(3);
        Convert.ToInt32(report.State["RegisteredCount"], CultureInfo.InvariantCulture).ShouldBe(1);
        Convert.ToInt32(report.State["AddressChangedCount"], CultureInfo.InvariantCulture).ShouldBe(1);
    }

    [Fact]
    public async Task Nothing_the_step_logs_or_throws_carries_a_url_in_either_mode()
    {
        foreach (var apply in new[] { false, true })
        {
            var (_, logger) = await RunAsync(new AuditUrlRedactionOptions { Apply = apply });

            logger.Entries.ShouldNotBeEmpty($"Apply = {apply} must say what it did");

            foreach (var entry in logger.Entries)
            {
                ShouldCarryNoMarker(entry.Message, $"the log message (Apply = {apply})");
                ShouldCarryNoMarker(entry.Exception?.ToString() ?? string.Empty, "the logged exception");

                foreach (var (key, value) in entry.State)
                {
                    ShouldCarryNoMarker(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, $"log field {key}");
                }
            }
        }
    }

    [Fact]
    public async Task An_applied_run_issues_one_guarded_update_per_row_that_is_not_its_origin()
    {
        var (database, logger) = await RunAsync(new AuditUrlRedactionOptions { Apply = true });

        database.NonQueries.Count.ShouldBe(2, "one UPDATE per unredacted row, each in its own statement");

        foreach (var update in database.NonQueries)
        {
            update.Text.ShouldStartWith("UPDATE audit_events SET", Case.Sensitive);
            update.Text.ShouldContain("jsonb_set");
            update.Text.ShouldContain("payload_size_bytes = payload_size_bytes -");
            update.Text.ShouldContain("schema_version = 2");
            update.Text.ShouldContain("audit_id =");
            update.Text.ShouldContain("occurred_at =");
            update.Text.ShouldContain("schema_version = 1", Case.Sensitive);
            update.Text.ShouldContain("payload->>'Url' =");
            ShouldCarryNoMarker(update.Text, "the UPDATE text (values travel as parameters)");
        }

        database.NonQueries.Count(update => update.Text.Contains("payload->>'PreviousUrl' =", StringComparison.Ordinal))
            .ShouldBe(1, "only the address change guards its previous address");

        var parameters = database.NonQueries.SelectMany(update => update.Values).OfType<string>().ToList();
        parameters.ShouldContain(RegisteredUrl, "the old value is a guard parameter");
        parameters.ShouldContain("rtsp://cam-a.test:8554", "the new value is the origin, as a parameter");
        parameters.ShouldContain(ChangedUrl);
        parameters.ShouldContain(ChangedPrevious);

        var applied = logger.Entries.Where(entry => entry.State.ContainsKey("Rewritten")).ShouldHaveSingleItem();
        Convert.ToInt32(applied.State["Rewritten"], CultureInfo.InvariantCulture).ShouldBe(2);
        applied.State.ShouldContainKey("CountBefore");
        applied.State.ShouldContainKey("CountAfter");
    }
}
