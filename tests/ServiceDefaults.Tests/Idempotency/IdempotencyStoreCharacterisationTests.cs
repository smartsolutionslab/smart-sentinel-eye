using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using NpgsqlTypes;
using SmartSentinelEye.ServiceDefaults.Idempotency;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.ServiceDefaults.Tests.Idempotency;

/// <summary>
/// Characterisation (spec 368, ADR-0144), captured green on unmodified code before
/// <c>IdempotencyStore&lt;T&gt;.BeginAsync</c> is split. <c>BeginAsync</c> is the at-most-once
/// claim of ADR-0142: the SQL text, the parameter list and the classification order are the
/// security property, so each is pinned here. Until now they were observed only by Docker
/// integration tests.
///
/// <para>
/// <b>No database.</b> The real store runs over an EF context whose connection open is suppressed
/// and whose readers are canned <see cref="DataTable"/>s; every command (text and parameters) is
/// recorded. The expected SQL below is written out by hand, with only CRLF normalised, and is
/// never read from the production constants. Concurrent reclaim cannot be observed here; the
/// integration tests remain the observer for row locking.
/// </para>
///
/// <para>
/// Counterfactuals seen to fail: deleting <c>SET reserved_at = NOW(),</c> from the claim, typing
/// the <c>fab</c> parameter as Text, comparing with <c>&gt; 0</c> instead of <c>== 1</c>, and
/// checking completion before the mismatch each turn a test below red.
/// </para>
/// </summary>
public class IdempotencyStoreCharacterisationTests
{
    private const string ExpectedClaimSql =
        """
        INSERT INTO idempotency_key (key, endpoint, caller, fab, request_fingerprint, reserved_at)
        VALUES (@p0, @p1, @p2, @fab, @p3, NOW())
        ON CONFLICT (key, endpoint, caller) DO UPDATE
           SET reserved_at = NOW(),
               fab = EXCLUDED.fab,
               request_fingerprint = EXCLUDED.request_fingerprint
         WHERE idempotency_key.resource_identifier IS NULL
           AND idempotency_key.reserved_at < NOW() - @p4
        RETURNING reserved_at AS "Value";

        """;

    private const string ExpectedReadSql =
        """
        SELECT resource_identifier AS "ResourceIdentifier",
               fab AS "Fab",
               request_fingerprint AS "RequestFingerprint",
               resource_version AS "ResourceVersion"
        FROM idempotency_key
        WHERE key = @p0 AND endpoint = @p1 AND caller = @p2;

        """;

    private static readonly DateTime Reserved = new(2026, 10, 15, 8, 30, 0, DateTimeKind.Utc);

    private static readonly Guid Completed = Guid.Parse("0198f1c0-0000-7000-8000-000000000042");

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

    private sealed record RecordedParameter(string Name, object? Value, NpgsqlDbType Type);

    private sealed record RecordedCommand(string Text, IReadOnlyList<RecordedParameter> Parameters);

    private sealed class RecordingReaderInterceptor(Queue<Func<DataTable>> readers) : DbCommandInterceptor
    {
        public List<RecordedCommand> Commands { get; } = [];

        public List<RecordedCommand> NonQueries { get; } = [];

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            NonQueries.Add(new RecordedCommand(
                command.CommandText.Replace("\r\n", "\n", StringComparison.Ordinal),
                [.. command.Parameters.Cast<NpgsqlParameter>().Select(parameter =>
                    new RecordedParameter(parameter.ParameterName, parameter.Value, parameter.NpgsqlDbType))]));

            return new(InterceptionResult<int>.SuppressWithResult(1));
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(new RecordedCommand(
                command.CommandText.Replace("\r\n", "\n", StringComparison.Ordinal),
                [.. command.Parameters.Cast<NpgsqlParameter>().Select(parameter =>
                    new RecordedParameter(parameter.ParameterName, parameter.Value, parameter.NpgsqlDbType))]));

            return new(InterceptionResult<DbDataReader>.SuppressWithResult(readers.Dequeue()().CreateDataReader()));
        }
    }

    private sealed record ProbeRequest(string Value);

    private sealed record Harness(IdempotencyStore<ProbeDbContext> Store, RecordingReaderInterceptor Recorder);

    private static Harness Build(params Func<DataTable>[] readers)
    {
        RecordingReaderInterceptor recorder = new(new Queue<Func<DataTable>>(readers));
        var options = new DbContextOptionsBuilder<ProbeDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .AddInterceptors(new SuppressOpenInterceptor(), recorder)
            .Options;

        return new Harness(new IdempotencyStore<ProbeDbContext>(new ProbeDbContext(options)), recorder);
    }

    private static DataTable ClaimTable(params DateTime[] rows)
    {
        DataTable table = new();
        table.Columns.Add("Value", typeof(DateTime)).DateTimeMode = DataSetDateTime.Utc;
        foreach (var row in rows)
        {
            table.Rows.Add(row);
        }

        return table;
    }

    private static DataTable BindingTable(Guid? resource, string? fab, string? fingerprint, int? version = null)
    {
        var table = EmptyBindingTable();
        table.Rows.Add(
            resource is { } value ? value : DBNull.Value,
            fab is null ? DBNull.Value : fab,
            fingerprint is null ? DBNull.Value : fingerprint,
            version is { } stored ? stored : DBNull.Value);

        return table;
    }

    private static DataTable EmptyBindingTable()
    {
        DataTable table = new();
        table.Columns.Add("ResourceIdentifier", typeof(Guid));
        table.Columns.Add("Fab", typeof(string));
        table.Columns.Add("RequestFingerprint", typeof(string));
        table.Columns.Add("ResourceVersion", typeof(int));

        return table;
    }

    private static IdempotencyScope ScopeFor(Option<string> fab) =>
        IdempotencyScope.For(
            IdempotencyKey.From("key-1"),
            "POST /devices/register",
            "admin@fab",
            fab,
            IdempotencyFingerprint.Of(new ProbeRequest("fixed")));

    private static IdempotencyScope Munich => ScopeFor(Option<string>.Some("munich"));

    private static async Task<IdempotencyReservation> BeginAfterAClaimThatLost(
        IdempotencyScope scope, Func<DataTable> storedRow) =>
        await Build(() => ClaimTable(), storedRow).Store.BeginAsync(scope, CancellationToken.None);

    [Fact]
    public async Task A_claim_returning_one_row_reserves_the_key_as_that_row_without_reading_the_binding()
    {
        var harness = Build(() => ClaimTable(Reserved));
        var scope = Munich;

        var reservation = await harness.Store.BeginAsync(scope, CancellationToken.None);

        reservation.ShouldBe(IdempotencyReservation.ReservedAs(new IdempotencyClaim(scope, Reserved)));
        reservation.Claim.Value.ReservedAt.Kind.ShouldBe(DateTimeKind.Utc);
        harness.Recorder.Commands.Count.ShouldBe(1);
    }

    [Fact]
    public async Task The_claim_command_carries_the_exact_sql_and_the_five_typed_parameters_in_order()
    {
        var harness = Build(() => ClaimTable(Reserved));
        var scope = Munich;

        await harness.Store.BeginAsync(scope, CancellationToken.None);

        var claim = harness.Recorder.Commands[0];
        claim.Text.ShouldBe(Lf(ExpectedClaimSql));
        claim.Parameters.ShouldBe(
            [
                new RecordedParameter("p0", "key-1", NpgsqlDbType.Text),
                new RecordedParameter("p1", "POST /devices/register", NpgsqlDbType.Text),
                new RecordedParameter("p2", "admin@fab", NpgsqlDbType.Text),
                new RecordedParameter("fab", "munich", NpgsqlDbType.Varchar),
                new RecordedParameter("p3", scope.Fingerprint.Value, NpgsqlDbType.Text),
                new RecordedParameter("p4", TimeSpan.FromMinutes(10), NpgsqlDbType.Interval),
            ]);
    }

    [Fact]
    public async Task A_fab_less_scope_sends_dbnull_for_the_fab_and_keeps_the_parameter_typed_varchar()
    {
        var harness = Build(() => ClaimTable(Reserved));

        await harness.Store.BeginAsync(ScopeFor(Option<string>.None), CancellationToken.None);

        var fab = harness.Recorder.Commands[0].Parameters.Single(parameter => parameter.Name == "fab");
        fab.Value.ShouldBe(DBNull.Value);
        fab.Type.ShouldBe(NpgsqlDbType.Varchar);
        harness.Recorder.Commands[0].Text.ShouldBe(Lf(ExpectedClaimSql));
    }

    [Fact]
    public async Task The_binding_read_carries_the_exact_sql_and_the_three_identity_parameters()
    {
        var harness = Build(() => ClaimTable(), () => EmptyBindingTable());

        await harness.Store.BeginAsync(Munich, CancellationToken.None);

        harness.Recorder.Commands.Count.ShouldBe(2);
        var read = harness.Recorder.Commands[1];
        read.Text.ShouldBe(Lf(ExpectedReadSql));
        read.Parameters.ShouldBe(
            [
                new RecordedParameter("p0", "key-1", NpgsqlDbType.Text),
                new RecordedParameter("p1", "POST /devices/register", NpgsqlDbType.Text),
                new RecordedParameter("p2", "admin@fab", NpgsqlDbType.Text),
            ]);
    }

    [Fact]
    public async Task A_lost_claim_whose_row_vanished_before_the_read_is_in_progress()
    {
        var harness = Build(() => ClaimTable(), () => EmptyBindingTable());

        var reservation = await harness.Store.BeginAsync(Munich, CancellationToken.None);

        reservation.ShouldBe(IdempotencyReservation.InProgress);
        harness.Recorder.Commands.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_row_stored_under_a_different_fab_is_mismatched()
    {
        var fingerprint = Munich.Fingerprint.Value;

        var reservation = await BeginAfterAClaimThatLost(
            Munich, () => BindingTable(null, "berlin", fingerprint));

        reservation.ShouldBe(IdempotencyReservation.Mismatched);
    }

    [Fact]
    public async Task A_row_with_no_stored_fab_is_mismatched_for_a_scope_that_has_one()
    {
        var fingerprint = Munich.Fingerprint.Value;

        var reservation = await BeginAfterAClaimThatLost(
            Munich, () => BindingTable(null, null, fingerprint));

        reservation.ShouldBe(IdempotencyReservation.Mismatched);
    }

    [Fact]
    public async Task A_row_with_a_stored_fab_is_mismatched_for_a_fab_less_scope()
    {
        var scope = ScopeFor(Option<string>.None);

        var reservation = await BeginAfterAClaimThatLost(
            scope, () => BindingTable(null, "munich", scope.Fingerprint.Value));

        reservation.ShouldBe(IdempotencyReservation.Mismatched);
    }

    [Fact]
    public async Task A_row_stored_under_a_different_fingerprint_is_mismatched()
    {
        var reservation = await BeginAfterAClaimThatLost(
            Munich, () => BindingTable(null, "munich", "not-the-same-digest"));

        reservation.ShouldBe(IdempotencyReservation.Mismatched);
    }

    [Fact]
    public async Task A_row_with_no_stored_fingerprint_fails_closed_as_mismatched()
    {
        var reservation = await BeginAfterAClaimThatLost(
            Munich, () => BindingTable(null, "munich", null));

        reservation.ShouldBe(IdempotencyReservation.Mismatched);
    }

    [Fact]
    public async Task A_mismatched_row_is_refused_even_when_it_is_already_completed()
    {
        var fingerprint = Munich.Fingerprint.Value;

        var reservation = await BeginAfterAClaimThatLost(
            Munich, () => BindingTable(Completed, "berlin", fingerprint));

        reservation.ShouldBe(IdempotencyReservation.Mismatched);
    }

    [Fact]
    public async Task A_fab_that_differs_only_by_case_is_mismatched()
    {
        var fingerprint = Munich.Fingerprint.Value;

        var reservation = await BeginAfterAClaimThatLost(
            Munich, () => BindingTable(null, "MUNICH", fingerprint));

        reservation.ShouldBe(IdempotencyReservation.Mismatched);
    }

    [Fact]
    public async Task A_matching_row_with_no_resource_yet_is_in_progress()
    {
        var fingerprint = Munich.Fingerprint.Value;

        var reservation = await BeginAfterAClaimThatLost(
            Munich, () => BindingTable(null, "munich", fingerprint));

        reservation.ShouldBe(IdempotencyReservation.InProgress);
    }

    [Fact]
    public async Task A_matching_completed_row_replays_its_resource()
    {
        var fingerprint = Munich.Fingerprint.Value;

        var reservation = await BeginAfterAClaimThatLost(
            Munich, () => BindingTable(Completed, "munich", fingerprint));

        reservation.ShouldBe(IdempotencyReservation.CompletedWith(Completed));
    }

    [Fact]
    public async Task A_matching_completed_row_replays_the_version_its_answer_carried()
    {
        var fingerprint = Munich.Fingerprint.Value;

        var reservation = await BeginAfterAClaimThatLost(
            Munich, () => BindingTable(Completed, "munich", fingerprint, version: 5));

        reservation.ResourceIdentifier.ShouldBe(Option<Guid>.Some(Completed));
        reservation.ResourceVersion.ShouldBe(Option<AggregateVersion>.Some(AggregateVersion.From(5)));
    }

    [Fact]
    public async Task A_matching_completed_row_written_before_versions_were_stored_replays_with_none()
    {
        var fingerprint = Munich.Fingerprint.Value;

        var reservation = await BeginAfterAClaimThatLost(
            Munich, () => BindingTable(Completed, "munich", fingerprint, version: null));

        reservation.Outcome.ShouldBe(IdempotencyOutcome.Completed);
        reservation.ResourceVersion.ShouldBe(Option<AggregateVersion>.None);
    }

    [Fact]
    public async Task A_mismatched_row_is_refused_before_its_stored_version_is_considered()
    {
        var reservation = await BeginAfterAClaimThatLost(
            Munich, () => BindingTable(Completed, "berlin", Munich.Fingerprint.Value, version: 5));

        reservation.ShouldBe(IdempotencyReservation.Mismatched);
        reservation.ResourceVersion.ShouldBe(Option<AggregateVersion>.None);
    }

    [Fact]
    public async Task Completing_with_a_version_writes_it_as_a_typed_integer_in_the_fenced_update()
    {
        var harness = Build();
        IdempotencyClaim claim = new(Munich, Reserved);

        await harness.Store.CompleteAsync(claim, Completed, AggregateVersion.From(5), CancellationToken.None);

        var update = harness.Recorder.NonQueries.ShouldHaveSingleItem();
        update.Text.ShouldContain("resource_version");
        update.Text.ShouldContain("resource_identifier IS NULL");
        update.Text.ShouldContain("reserved_at = ");
        update.Parameters.ShouldContain(
            parameter => parameter.Type == NpgsqlDbType.Integer && Equals(parameter.Value, 5));
    }

    [Fact]
    public async Task Completing_without_a_version_writes_no_version_value()
    {
        var harness = Build();
        IdempotencyClaim claim = new(Munich, Reserved);

        await harness.Store.CompleteAsync(claim, Completed, CancellationToken.None);

        var update = harness.Recorder.NonQueries.ShouldHaveSingleItem();
        update.Text.ShouldContain("resource_version = ");
        update.Parameters
            .Where(parameter => parameter.Type == NpgsqlDbType.Integer)
            .ShouldHaveSingleItem()
            .Value.ShouldBe(DBNull.Value);
    }

    [Fact]
    public async Task A_matching_completed_row_replays_for_a_fab_less_scope_with_no_stored_fab()
    {
        var scope = ScopeFor(Option<string>.None);

        var reservation = await BeginAfterAClaimThatLost(
            scope, () => BindingTable(Completed, null, scope.Fingerprint.Value));

        reservation.ShouldBe(IdempotencyReservation.CompletedWith(Completed));
    }

    [Fact]
    public async Task A_claim_returning_two_rows_is_not_a_reservation()
    {
        var fingerprint = Munich.Fingerprint.Value;
        var harness = Build(
            () => ClaimTable(Reserved, Reserved.AddSeconds(1)),
            () => BindingTable(null, "munich", fingerprint));

        var reservation = await harness.Store.BeginAsync(Munich, CancellationToken.None);

        reservation.ShouldBe(IdempotencyReservation.InProgress);
        harness.Recorder.Commands.Count.ShouldBe(2);
    }

    [Fact]
    public async Task A_null_scope_is_refused_before_any_command_is_issued()
    {
        var harness = Build();

        await Should.ThrowAsync<ArgumentNullException>(
            () => harness.Store.BeginAsync(null!, CancellationToken.None));

        harness.Recorder.Commands.ShouldBeEmpty();
    }
}
