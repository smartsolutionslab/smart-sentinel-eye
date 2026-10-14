using System.Data.Common;
using System.Globalization;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using SmartSentinelEye.AuditObservability.Infrastructure.Persistence;
using SmartSentinelEye.Shared.Kernel;
using AuditEventEntity = SmartSentinelEye.AuditObservability.Domain.AuditEvent.AuditEvent;

namespace SmartSentinelEye.Architecture.Tests.Persistence;

/// <summary>
/// Characterisation (spec 352, ADR-0144), captured green on unmodified <c>develop</c> before the
/// per-row insert in <c>AuditEventRepository.SaveAsync</c> is extracted. This is the per-event
/// ingest write and the only thing between an integration event and the audit table, so the
/// statement and every value bound into it are pinned as literals: the SQL text, the order and
/// name of the parameters, and each parameter's CLR type and value, nulls included.
///
/// <para>
/// <b>No database.</b> The real repository runs over a real
/// <see cref="AuditObservabilityDbContext"/> on Npgsql. A connection interceptor suppresses the
/// open and a command interceptor records the command and suppresses its execution with a result
/// of 1, so what is asserted is what EF would have sent. There is no <c>*.Infrastructure.Tests</c>
/// project for this context (spec 352 J3); <c>Architecture.Tests</c> already references the
/// Infrastructure assembly.
/// </para>
///
/// <para>
/// The SQL literal is compared after normalising line endings: the raw string literal in the
/// repository carries the source file's newlines, which are CRLF on a Windows checkout and LF on
/// Linux CI. Only the actual value is normalised; the expected text is a plain literal.
/// </para>
///
/// <para>
/// Pinned as it is: a failed insert leaves every pending row pending, so the next
/// <c>SaveAsync</c> re-issues the whole batch (the <c>ON CONFLICT</c> clause absorbs it).
/// </para>
///
/// <para>
/// Counterfactuals seen to fail: transposing <c>{resourceKind}</c> and <c>{resourceIdentifier}</c> in
/// the VALUES list changes the first row's parameters; moving <c>pending.Clear()</c> before the
/// loop turns <see cref="A_failed_insert_leaves_every_row_pending_for_the_next_save"/> red.
/// </para>
/// </summary>
public class AuditEventInsertCharacterisationTests
{
    private const string ExpectedStatement = """
        INSERT INTO audit_events (
            audit_id, occurred_at, received_at, fab_id, fab_attribution,
            event_kind, resource_kind, resource_identifier,
            actor_identifier, actor_username, event_identifier,
            payload, payload_size_bytes, schema_version,
            handler_entered_at, written_at)
        VALUES (
            @p0, @p1, @p2,
            @p3,
            @p4,
            @p5,
            @p6,
            @p7,
            @p8, @p9,
            @p10,
            @p11::jsonb, @p12,
            @p13,
            @p14,
            CASE WHEN @p15::timestamptz IS NULL
                 THEN NULL ELSE clock_timestamp() END)
        ON CONFLICT (event_identifier, occurred_at) DO NOTHING
        """;


    private static readonly string[] FirstRowParameters =
    [
        "@p0=Guid:a0a0a0a0-0000-4000-8000-000000000003",
        "@p1=DateTimeOffset:2026-03-10T12:00:00.0000000+00:00",
        "@p2=DateTimeOffset:2026-03-10T12:00:01.5000000+00:00",
        "@p3=String:munich",
        "@p4=String:Resolved",
        "@p5=String:CameraRegisteredV1",
        "@p6=String:camera",
        "@p7=String:cam-7",
        "@p8=Guid:a1a1a1a1-0000-4000-8000-000000000001",
        "@p9=String:alice@munich.test",
        "@p10=Guid:e1e1e1e1-0000-4000-8000-000000000002",
        "@p11=String:{\"camera\":\"cam-7\",\"note\":\"ü\"}",
        "@p12=Int32:30",
        "@p13=Int16:1",
        "@p14=DateTimeOffset:2026-03-10T12:00:01.2500000+00:00",
        "@p15=DateTimeOffset:2026-03-10T12:00:01.2500000+00:00",
    ];

    private static readonly string[] SecondRowParameters =
    [
        "@p0=Guid:a0a0a0a0-0000-4000-8000-000000000005",
        "@p1=DateTimeOffset:2026-03-10T12:05:00.0000000+00:00",
        "@p2=DateTimeOffset:2026-03-10T12:05:02.0000000+00:00",
        "@p3=DBNull",
        "@p4=String:Unresolved",
        "@p5=String:OverlayPublishedV1",
        "@p6=DBNull",
        "@p7=DBNull",
        "@p8=Guid:00000000-0000-0000-0000-000000000000",
        "@p9=DBNull",
        "@p10=Guid:e2e2e2e2-0000-4000-8000-000000000004",
        "@p11=String:{}",
        "@p12=Int32:2",
        "@p13=Int16:1",
        "@p14=DBNull",
        "@p15=DBNull",
    ];

    private static readonly PropertyInfo IdentifierProperty =
        typeof(AuditEventEntity).GetProperty(nameof(AuditEventEntity.Id))!;

    private sealed class ClockAt(string instant) : IClock
    {
        public DateTimeOffset UtcNow { get; } = DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture);
    }

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

    private sealed class RecordingInsertInterceptor : DbCommandInterceptor
    {
        public List<string> Statements { get; } = [];

        public List<string[]> Parameters { get; } = [];

        public Exception? FailWith { get; set; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Statements.Add(command.CommandText.Replace("\r\n", "\n", StringComparison.Ordinal));
            Parameters.Add([.. command.Parameters.Cast<DbParameter>().Select(Render)]);

            return FailWith is null
                ? new(InterceptionResult<int>.SuppressWithResult(1))
                : throw FailWith;
        }

        private static string Render(DbParameter parameter) =>
            $"{parameter.ParameterName}="
            + parameter.Value switch
            {
                null => "null",
                DBNull => "DBNull",
                DateTimeOffset moment => $"DateTimeOffset:{moment.ToString("O", CultureInfo.InvariantCulture)}",
                var other => $"{other.GetType().Name}:{Convert.ToString(other, CultureInfo.InvariantCulture)}",
            };
    }

    private static (AuditEventRepository Repository, RecordingInsertInterceptor Commands) Build()
    {
        RecordingInsertInterceptor commands = new();
        var options = new DbContextOptionsBuilder<AuditObservabilityDbContext>()
            .UseNpgsql("Host=unused;Database=unused;Username=unused;Password=unused")
            .AddInterceptors(new SuppressOpenInterceptor(), commands)
            .Options;
        return (new AuditEventRepository(new AuditObservabilityDbContext(options)), commands);
    }

    private static AuditEventEntity WithIdentifier(AuditEventEntity auditEvent, string identifier)
    {
        IdentifierProperty.SetValue(auditEvent, AuditEventIdentifier.From(Guid.Parse(identifier)));
        return auditEvent;
    }

    private static AuditEventEntity FullRow() =>
        WithIdentifier(
            AuditEventEntity.From(
                new V1Envelope(
                    EventTypeName: "CameraRegisteredV1",
                    OccurredAt: DateTimeOffset.Parse("2026-03-10T12:00:00Z", CultureInfo.InvariantCulture),
                    Fab: Option<FabIdentifier>.Some(FabIdentifier.From("munich")),
                    Actor: ActorIdentifier.From(Guid.Parse("a1a1a1a1-0000-4000-8000-000000000001")),
                    ActorUsername: Option<string>.Some("alice@munich.test"),
                    EventIdentifier: EventIdentifier.From(Guid.Parse("e1e1e1e1-0000-4000-8000-000000000002")),
                    Payload: """{"camera":"cam-7","note":"ü"}"""),
                new V1Mapping(
                    Option<ResourceKind>.Some(ResourceKind.Camera),
                    Option<ResourceIdentifier>.Some(ResourceIdentifier.From("cam-7"))),
                new ClockAt("2026-03-10T12:00:01.5Z"),
                DateTimeOffset.Parse("2026-03-10T12:00:01.25Z", CultureInfo.InvariantCulture)),
            "a0a0a0a0-0000-4000-8000-000000000003");

    private static AuditEventEntity EmptyRow() =>
        WithIdentifier(
            AuditEventEntity.From(
                new V1Envelope(
                    EventTypeName: "OverlayPublishedV1",
                    OccurredAt: DateTimeOffset.Parse("2026-03-10T12:05:00Z", CultureInfo.InvariantCulture),
                    Fab: Option<FabIdentifier>.None,
                    Actor: ActorIdentifier.System,
                    ActorUsername: Option<string>.None,
                    EventIdentifier: EventIdentifier.From(Guid.Parse("e2e2e2e2-0000-4000-8000-000000000004")),
                    Payload: "{}"),
                V1Mapping.Unmapped,
                new ClockAt("2026-03-10T12:05:02Z")),
            "a0a0a0a0-0000-4000-8000-000000000005");

    [Fact]
    public async Task Two_pending_rows_are_inserted_in_add_order_with_this_statement_and_these_values()
    {
        var (repository, commands) = Build();
        repository.Add(FullRow());
        repository.Add(EmptyRow());

        await repository.SaveAsync(default);

        var expected = ExpectedStatement.Replace("\r\n", "\n", StringComparison.Ordinal);
        commands.Statements.ShouldBe([expected, expected]);
        commands.Parameters.Count.ShouldBe(2);
        commands.Parameters[0].ShouldBe(FirstRowParameters);
        commands.Parameters[1].ShouldBe(SecondRowParameters);
    }

    [Fact]
    public async Task A_save_with_nothing_pending_issues_no_command()
    {
        var (repository, commands) = Build();

        await repository.SaveAsync(default);

        commands.Statements.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_second_save_after_a_successful_one_issues_nothing_more()
    {
        var (repository, commands) = Build();
        repository.Add(FullRow());
        await repository.SaveAsync(default);

        await repository.SaveAsync(default);

        commands.Statements.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_failed_insert_propagates_and_leaves_every_row_pending_for_the_next_save()
    {
        var (repository, commands) = Build();
        repository.Add(FullRow());
        repository.Add(EmptyRow());
        commands.FailWith = new InvalidOperationException("insert refused");

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => repository.SaveAsync(default));

        thrown.Message.ShouldBe("insert refused");
        commands.Statements.Count.ShouldBe(1);

        commands.FailWith = null;
        await repository.SaveAsync(default);

        commands.Statements.Count.ShouldBe(3);
        commands.Parameters[1].ShouldBe(FirstRowParameters);
        commands.Parameters[2].ShouldBe(SecondRowParameters);
    }

    [Fact]
    public async Task A_fab_neutral_row_is_written_with_the_NotApplicable_attribution()
    {
        var (repository, commands) = Build();
        repository.Add(
            AuditEventEntity.From(
                new V1Envelope(
                    EventTypeName: "AuditChunkArchivedV1",
                    OccurredAt: DateTimeOffset.Parse("2026-03-10T13:00:00Z", CultureInfo.InvariantCulture),
                    Fab: Option<FabIdentifier>.None,
                    Actor: ActorIdentifier.System,
                    ActorUsername: Option<string>.None,
                    EventIdentifier: EventIdentifier.From(Guid.Parse("e3e3e3e3-0000-4000-8000-000000000006")),
                    Payload: "{}",
                    FabScope: FabScope.Neutral),
                V1Mapping.Unmapped,
                new ClockAt("2026-03-10T13:00:01Z")));

        await repository.SaveAsync(default);

        commands.Parameters.Single().Single(parameter => parameter.StartsWith("@p4=", StringComparison.Ordinal))
            .ShouldBe("@p4=String:NotApplicable");
    }
}
