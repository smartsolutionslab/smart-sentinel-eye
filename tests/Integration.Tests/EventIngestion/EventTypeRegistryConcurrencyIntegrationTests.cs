using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;
using static SmartSentinelEye.Integration.Tests.EventIngestion.EventTypeRegistryApi;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// Spec 143 FR-004's <b>second</b> enforcement — the partial unique index, which
/// the application-level lookup cannot stand in for.
///
/// <para>
/// The primary 4a suite registers the same kind twice, in sequence, from one
/// client. That exercises <c>GetRegisteredAsync</c> and nothing else: the second
/// request starts after the first has committed, so the lookup sees the row and
/// the index is never asked. Delete
/// <c>ux_registered_event_types_fab_kind</c> entirely and every one of those
/// assertions stays green.
/// </para>
///
/// <para>
/// <b>Two shapes, because one of them is honest about being probabilistic.</b>
/// <see cref="The_partial_unique_index_exists_on_the_pair_the_lookup_reads"/>
/// asks Postgres what it actually built — deterministic, and the only assertion
/// here that cannot pass against an index that is missing or unfiltered. The
/// racing tests then show the invariant surviving real concurrency; they can in
/// principle be won by the application check alone on a slow day, which is
/// exactly why they are not left to carry the claim by themselves.
/// </para>
///
/// <para>
/// <b>Red on arrival:</b> <c>/event-types</c> is unmapped (404) and
/// <c>registered_event_types</c> does not exist, so <c>pg_indexes</c> returns
/// nothing.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class EventTypeRegistryConcurrencyIntegrationTests(AspireFixture aspire)
{
    /// <summary>How many requests race. Enough to lose the lookup, few enough to stay quick.</summary>
    private const int Racers = 6;

    /// <summary>
    /// Asked of the database, not of the model snapshot or the configuration
    /// file. A guard that reads the design artefact proves the design was
    /// written down, not that it holds.
    /// </summary>
    [Fact]
    public async Task The_partial_unique_index_exists_on_the_pair_the_lookup_reads()
    {
        await using EventIngestionDbContext dbContext = await aspire.CreateEventIngestionDbContextAsync();

        string[] definitions = await dbContext.Database
            .SqlQueryRaw<string>(
                """
                SELECT indexdef AS "Value"
                FROM pg_indexes
                WHERE tablename = 'registered_event_types';
                """)
            .ToArrayAsync();

        string? unique = definitions.FirstOrDefault(definition =>
            definition.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
            && definition.Contains("fab", StringComparison.OrdinalIgnoreCase)
            && definition.Contains("kind", StringComparison.OrdinalIgnoreCase));

        unique.ShouldNotBeNull(
            "FR-004 enforces uniqueness twice, and the application lookup is the half that loses a "
            + $"race. Indexes actually on the table: {string.Join(" | ", definitions)}");

        unique.ShouldContain(
            "WHERE",
            customMessage: "the index must be partial — a total unique index would keep a retired "
            + "kind's name taken forever and FR-004's released name could not be re-registered. "
            + $"Found: {unique}");

        unique.ShouldContain(
            "Retired",
            customMessage: "the filter must exclude retired rows by their stored value, not by some "
            + $"other predicate. Found: {unique}");
    }

    /// <summary>
    /// <see cref="Racers"/> registrations of one kind, dispatched together and
    /// awaited together, so several are inside the handler before any has
    /// committed — the window the application lookup cannot see across.
    /// </summary>
    [Fact]
    public async Task Simultaneous_registrations_of_one_kind_leave_exactly_one_entry()
    {
        string kind = UniqueKind();
        using HttpClient dresden = await ClientFor(DresdenOperator);

        HttpResponseMessage[] answers = await Task.WhenAll(
            Enumerable.Range(0, Racers).Select(_ => RegisterAsync(dresden, kind)));

        string answered = Describe(answers);
        answers.Count(answer => answer.StatusCode == HttpStatusCode.Created)
            .ShouldBe(1, $"exactly one racer may create the row. Answers: {answered}");
        answers.Count(answer => answer.StatusCode == HttpStatusCode.Conflict)
            .ShouldBe(Racers - 1,
                "every loser must be told it conflicted. A 500 here is the race reaching Postgres "
                + "with no UniqueConstraintExceptionHandler in the way — a server fault for asking "
                + $"about a name that was free when it asked. Answers: {answered}");

        JsonElement rows = await ListRowsAsync(dresden, answered);
        CountOf(rows, kind).ShouldBe(1, $"one registered entry per (fab, kind). Answers: {answered}");
    }

    /// <summary>
    /// The control. Uniqueness is per fab (spec.md §4's second conflict block),
    /// so an index built on <c>(kind)</c> alone — or a lock taken too widely —
    /// would pass the test above and fail this one.
    /// </summary>
    [Fact]
    public async Task Simultaneous_registrations_of_one_kind_in_two_fabs_both_succeed()
    {
        string kind = UniqueKind();
        using HttpClient multi = await ClientFor(MultiFabOperator);

        HttpResponseMessage[] answers = await Task.WhenAll(
            RegisterAsync(multi, kind, fabId: "dresden"),
            RegisterAsync(multi, kind, fabId: "munich"));

        string answered = Describe(answers);
        answers.ShouldAllBe(
            answer => answer.StatusCode == HttpStatusCode.Created,
            customMessage: $"a kind is unique within a fab, not across the estate. Answers: {answered}");

        JsonElement rows = await ListRowsAsync(multi, answered);
        CountOf(rows, kind).ShouldBe(2, $"one entry per fab. Answers: {answered}");
    }

    /// <summary>
    /// ADR-0113's second layer, which the sequential stale-version test does not
    /// reach: two retires that both read version 0 and both pass the handler's
    /// <c>Version != expectedVersion</c> gate before either saves. The EF
    /// concurrency token is the only thing between them.
    /// </summary>
    [Fact]
    public async Task Simultaneous_retires_of_one_entry_leave_one_winner()
    {
        string kind = UniqueKind();
        using HttpClient dresden = await ClientFor(DresdenOperator);
        (await RegisterAsync(dresden, kind)).StatusCode.ShouldBe(HttpStatusCode.Created);

        HttpResponseMessage[] answers = await Task.WhenAll(
            RetireAsync(dresden, kind, expectedVersion: 0),
            RetireAsync(dresden, kind, expectedVersion: 0));

        string answered = Describe(answers);
        answers.Count(answer => answer.StatusCode == HttpStatusCode.OK)
            .ShouldBe(1, $"only one retire may apply. Answers: {answered}");
        answers.Count(answer =>
                answer.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.NotFound)
            .ShouldBe(1,
                "the loser is refused, not silently accepted. 409 if it lost on the version token, "
                + "404 if the winner had already left the registered set — either is a correct "
                + $"refusal; a second 200 is a lost update. Answers: {answered}");

        JsonElement rows = await ListRowsAsync(dresden, answered);
        CountOf(rows, kind).ShouldBe(0, $"the entry is retired and the list excludes it. Answers: {answered}");
    }

    private static string Describe(IEnumerable<HttpResponseMessage> answers) =>
        string.Join(", ", answers.Select(answer => ((int)answer.StatusCode).ToString(
            CultureInfo.InvariantCulture)));

    private Task<HttpClient> ClientFor(string username) =>
        aspire.CreateAuthenticatedClientAsync(ResourceName, username, OperatorPassword);
}
