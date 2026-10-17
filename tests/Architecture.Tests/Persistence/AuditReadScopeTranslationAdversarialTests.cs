using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SmartSentinelEye.AuditObservability.Application.Queries;
using SmartSentinelEye.AuditObservability.Application.Queries.Handlers;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using SmartSentinelEye.AuditObservability.Infrastructure.Persistence;
using Xunit.Abstractions;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Adversarial (spec 396, #2927). The handler tests run R1 as LINQ-to-objects; this runs the
/// three real handlers through the real <see cref="AuditEventQuerySource"/> and the Npgsql
/// provider, with the connection open suppressed and the command intercepted, so what is
/// asserted is the SQL Postgres would receive. Two ways the predicate could be wrong only in
/// SQL: the enum compared as its ordinal against a <c>varchar</c> column (a runtime 42883 on
/// every scoped read), or a client-evaluation fallback. Neither is visible to a fake.
/// </summary>
public class AuditReadScopeTranslationAdversarialTests(ITestOutputHelper output)
{
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

    private sealed class CaptureInterceptor : DbCommandInterceptor
    {
        public string CommandText { get; private set; } = string.Empty;

        public List<string> Parameters { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            CommandText = command.CommandText.Replace("\r\n", "\n", StringComparison.Ordinal);
            foreach (var parameter in command.Parameters.Cast<DbParameter>())
            {
                var value = parameter.Value is System.Collections.IEnumerable sequence and not string
                    ? "[" + string.Join(",", sequence.Cast<object>().Select(item => Convert.ToString(item, CultureInfo.InvariantCulture))) + "]"
                    : Convert.ToString(parameter.Value, CultureInfo.InvariantCulture);
                Parameters.Add($"{parameter.ParameterName}={value}");
            }

            using DataTable empty = new();
            return new(InterceptionResult<DbDataReader>.SuppressWithResult(empty.CreateDataReader()));
        }
    }

    private static (AuditEventQuerySource Source, CaptureInterceptor Capture, AuditObservabilityDbContext Context) Build()
    {
        CaptureInterceptor capture = new();
        var options = new DbContextOptionsBuilder<AuditObservabilityDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .AddInterceptors(new SuppressOpenInterceptor(), capture)
            .Options;
        AuditObservabilityDbContext context = new(options);
        return (new AuditEventQuerySource(context), capture, context);
    }

    private static string Where(string sql)
    {
        var start = sql.IndexOf("WHERE", StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }

        var end = sql.IndexOf("ORDER BY", start, StringComparison.Ordinal);
        return end < 0 ? sql[start..] : sql[start..end];
    }

    private async Task<(string Where, IReadOnlyList<string> Parameters)> SearchSql(string? fab, params string[] callerFabs)
    {
        var (source, capture, context) = Build();
        await using (context)
        {
            SearchAuditQueryHandler handler = new(source);
            var result = await handler.HandleAsync(
                new SearchAuditQuery(fab, callerFabs, null, null, null, null, null, null, null, 50, null),
                default);
            result.IsSuccess.ShouldBeTrue();
        }

        output.WriteLine(capture.CommandText);
        output.WriteLine(string.Join(" | ", capture.Parameters));
        return (Where(capture.CommandText), capture.Parameters);
    }

    [Fact]
    public async Task A_claims_scoped_search_compares_the_attribution_as_its_string_and_never_admits_a_bare_null_fab()
    {
        var (where, parameters) = await SearchSql(null, "munich", "berlin");

        where.ShouldContain("a.fab_attribution = 'NotApplicable'");
        where.ShouldNotContain("fab_attribution = 2");
        where.ShouldNotContain("fab_attribution = 'Unresolved'");
        where.ShouldNotContain("a.fab_id IS NULL OR");
        parameters.ShouldContain(parameter => parameter.EndsWith("=[munich,berlin]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Later_filters_are_conjoined_with_the_whole_scope_disjunction_not_with_its_last_term()
    {
        // An OR spliced in without parentheses would read "fab-set OR (NotApplicable AND
        // actor AND cursor)", handing every row of the caller's fabs past the cursor and
        // ignoring the actor filter.
        var (source, capture, context) = Build();
        await using (context)
        {
            SearchAuditQueryHandler handler = new(source);
            // AuditCursor is internal to Application; this is its format, written out.
            var cursor = Convert.ToBase64String(Encoding.UTF8.GetBytes("0639087408000000000.000000c1000040008000000000000000"));
            var result = await handler.HandleAsync(
                new SearchAuditQuery(
                    null,
                    ["munich"],
                    Guid.Parse("0199aaaa-0000-7000-8000-000000000001"),
                    null,
                    "StreamHealthChangedV1",
                    null,
                    null,
                    null,
                    null,
                    50,
                    cursor),
                default);
            result.IsSuccess.ShouldBeTrue();
        }

        output.WriteLine(capture.CommandText);
        var where = Where(capture.CommandText);
        where.ShouldStartWith("WHERE ((a.fab_id IS NOT NULL AND a.fab_id = ANY (@allowed)) OR a.fab_attribution = 'NotApplicable') AND ");
    }

    [Fact]
    public async Task A_named_fab_search_restricts_to_that_fab_or_not_applicable()
    {
        var (where, parameters) = await SearchSql("munich", "berlin");

        where.ShouldContain("a.fab_attribution = 'NotApplicable'");
        where.ShouldNotContain("a.fab_id IS NULL OR");
        parameters.ShouldContain(parameter => parameter.Contains("munich", StringComparison.Ordinal));
        parameters.ShouldNotContain(parameter => parameter.Contains("berlin", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unscoped_search_keeps_the_bare_null_fab_predicate_and_never_mentions_attribution()
    {
        var (where, _) = await SearchSql(null, "Munich");

        where.ShouldContain("a.fab_id IS NULL");
        where.ShouldNotContain("fab_attribution");
    }

    [Fact]
    public async Task The_timeline_compares_the_attribution_as_its_string()
    {
        var (source, capture, context) = Build();
        await using (context)
        {
            GetResourceTimelineQueryHandler handler = new(source);
            var result = await handler.HandleAsync(
                new GetResourceTimelineQuery(
                    "stream",
                    ResourceIdentifier.From("44444444-4444-4444-4444-444444444444"),
                    FabIdentifier.From("munich"),
                    null,
                    null,
                    50,
                    null),
                default);
            result.IsSuccess.ShouldBeTrue();
        }

        output.WriteLine(capture.CommandText);
        var where = Where(capture.CommandText);
        where.ShouldContain("a.fab_attribution = 'NotApplicable'");
        where.ShouldNotContain("a.fab_id IS NULL OR");
    }

    [Fact]
    public async Task The_single_row_read_fetches_by_identifier_and_decides_visibility_after_materialising()
    {
        var (source, capture, context) = Build();
        await using (context)
        {
            GetAuditEventQueryHandler handler = new(source);
            var result = await handler.HandleAsync(
                new GetAuditEventQuery(AuditEventIdentifier.From(Guid.Parse("000000c1-0000-4000-8000-000000000000")), ["munich"]),
                default);
            result.IsFailure.ShouldBeTrue();
        }

        output.WriteLine(capture.CommandText);
        var where = Where(capture.CommandText);
        where.ShouldContain("a.audit_id = ");
        where.ShouldNotContain("fab_");
    }
}
