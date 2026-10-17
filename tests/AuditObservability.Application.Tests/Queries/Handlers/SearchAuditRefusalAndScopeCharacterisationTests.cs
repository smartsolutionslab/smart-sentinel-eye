using System.Globalization;
using System.Net;
using SmartSentinelEye.AuditObservability.Application.DTOs;
using SmartSentinelEye.AuditObservability.Application.Queries;
using SmartSentinelEye.AuditObservability.Application.Queries.Handlers;
using SmartSentinelEye.AuditObservability.Application.Tests.Fakes;
using SmartSentinelEye.AuditObservability.Application.Tests.TestData;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using SmartSentinelEye.Shared.Kernel;
using AuditEventEntity = SmartSentinelEye.AuditObservability.Domain.AuditEvent.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Application.Tests.Queries.Handlers;

/// <summary>
/// Characterisation (spec 352, ADR-0144), captured green on unmodified <c>develop</c> before
/// <c>SearchAuditQueryHandler.HandleAsync</c> is split into steps. Pins the order the handler
/// refuses a request in, the order it throws in, and exactly which fab-scope a caller gets.
/// Expected values are hand-written literals; every row is addressed by a literal audit
/// identifier so a transposed filter cannot hide behind a count.
///
/// <para>
/// The oddities below are pinned as they are, not as they arguably should be: this handler
/// checks the page size before the resource kind (the timeline does the reverse); it parses
/// the fab, actor, event kind and resource identifier without a guard, so an unparseable
/// value throws <see cref="ArgumentException"/>; a named fab makes it ignore the caller's
/// claimed fabs entirely; and a fab-less row is visible to a fab-scoped caller only when its
/// attribution is <c>NotApplicable</c> (spec 396, #2927) — an <c>Unresolved</c> row is
/// visible only to a caller with no usable fab claim.
/// </para>
///
/// <para>
/// Counterfactual seen to fail: swapping the page-size and resource-kind checks turns
/// <see cref="A_search_with_every_input_invalid_is_refused_for_its_page_size_first"/> red.
/// </para>
/// </summary>
public class SearchAuditRefusalAndScopeCharacterisationTests
{
    private const string MunichRow = "0000000a-0000-4000-8000-000000000000";
    private const string BerlinRow = "0000000b-0000-4000-8000-000000000000";
    private const string UnresolvedRow = "0000000c-0000-4000-8000-000000000000";
    private const string NotApplicableRow = "0000000d-0000-4000-8000-000000000000";

    private static readonly DateTimeOffset Start =
        DateTimeOffset.Parse("2026-03-10T12:00:00Z", CultureInfo.InvariantCulture);

    private static SearchAuditQuery Query(
        string? fab = null,
        string[]? callerFabs = null,
        Guid? actor = null,
        string? actorUsername = null,
        string? eventKind = null,
        string? resourceKind = null,
        string? resourceIdentifier = null,
        int pageSize = 50,
        string? cursor = null) =>
        new(fab, callerFabs ?? [], actor, actorUsername, eventKind, resourceKind, resourceIdentifier, null, null, pageSize, cursor);

    private static SearchAuditQueryHandler HandlerOver(params AuditEventEntity[] rows) =>
        new(new TestAuditEventQuerySource(rows));

    private static AuditEventEntity[] ScopeRows() =>
    [
        new AuditEventBuilder().WithOccurredAt(Start.AddMinutes(4)).WithFab("munich").Build().WithIdentifier(MunichRow),
        new AuditEventBuilder().WithOccurredAt(Start.AddMinutes(3)).WithFab("berlin").Build().WithIdentifier(BerlinRow),
        new AuditEventBuilder().WithOccurredAt(Start.AddMinutes(2)).WithFab(null).Build().WithIdentifier(UnresolvedRow),
        new AuditEventBuilder().WithOccurredAt(Start.AddMinutes(1)).WithFab(null).WithFabScope(FabScope.Neutral).Build()
            .WithIdentifier(NotApplicableRow),
    ];

    private static async Task ShouldReturnAsync(
        SearchAuditQueryHandler handler,
        SearchAuditQuery query,
        params string[] expected)
    {
        var result = await handler.HandleAsync(query, default);
        result.Value.Rows.Select(row => row.AuditIdentifier.ToString()).ShouldBe(expected);
    }

    private static void ShouldBeRefusal<TError>(
        Result<AuditPageDto, SearchAuditError> result,
        string code,
        string message)
        where TError : SearchAuditError
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBeOfType<TError>();
        result.Error.Code.ShouldBe(code);
        result.Error.Message.ShouldBe(message);
        result.Error.Status.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_search_with_every_input_invalid_is_refused_for_its_page_size_first()
    {
        var result = await HandlerOver().HandleAsync(
            Query(resourceKind: "bogus", cursor: "not-a-cursor!", pageSize: 201), default);

        ShouldBeRefusal<SearchAuditError.PageSizeOutOfRange>(
            result,
            "AUDIT_SEARCH_PAGE_SIZE_OUT_OF_RANGE",
            "pageSize 201 must be between 1 and 200.");
    }

    [Fact]
    public async Task A_search_with_a_valid_page_size_but_an_unknown_resource_kind_and_a_bad_cursor_is_refused_for_its_resource_kind()
    {
        var result = await HandlerOver().HandleAsync(
            Query(resourceKind: "bogus", cursor: "not-a-cursor!", pageSize: 200), default);

        ShouldBeRefusal<SearchAuditError.InvalidResourceKind>(
            result,
            "AUDIT_SEARCH_INVALID_RESOURCE_KIND",
            "resourceKind 'bogus' is not a known audit resource.");
    }

    [Fact]
    public async Task A_search_with_only_a_bad_cursor_is_refused_for_its_cursor()
    {
        var result = await HandlerOver().HandleAsync(
            Query(resourceKind: "camera", cursor: "not-a-cursor!"), default);

        ShouldBeRefusal<SearchAuditError.InvalidCursor>(
            result,
            "AUDIT_SEARCH_INVALID_CURSOR",
            "Cursor 'not-a-cursor!' is not a valid pagination cursor.");
    }

    [Fact]
    public async Task An_empty_string_cursor_is_refused_as_an_invalid_cursor()
    {
        var result = await HandlerOver().HandleAsync(Query(cursor: string.Empty), default);

        ShouldBeRefusal<SearchAuditError.InvalidCursor>(
            result,
            "AUDIT_SEARCH_INVALID_CURSOR",
            "Cursor '' is not a valid pagination cursor.");
    }

    [Fact]
    public async Task A_cursor_that_decodes_to_the_wrong_shape_is_refused_as_an_invalid_cursor()
    {
        // base64("no-dot-in-here") decodes but has no '.' separator.
        var result = await HandlerOver().HandleAsync(Query(cursor: "bm8tZG90LWluLWhlcmU="), default);

        ShouldBeRefusal<SearchAuditError.InvalidCursor>(
            result,
            "AUDIT_SEARCH_INVALID_CURSOR",
            "Cursor 'bm8tZG90LWluLWhlcmU=' is not a valid pagination cursor.");
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(-1, 50)]
    [InlineData(1, 1)]
    [InlineData(200, 200)]
    public async Task A_page_size_is_defaulted_or_accepted_up_to_the_maximum(int requested, int expectedRows)
    {
        AuditEventEntity[] rows =
        [.. Enumerable.Range(0, 201).Select(minute =>
            new AuditEventBuilder().WithOccurredAt(Start.AddMinutes(minute)).Build())];

        var result = await HandlerOver(rows).HandleAsync(Query(fab: "munich", pageSize: requested), default);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.Count.ShouldBe(expectedRows);
        result.Value.NextCursor.ShouldNotBeNull();
    }

    [Fact]
    public void The_page_size_limits_are_these()
    {
        SearchAuditQueryHandler.DefaultPageSize.ShouldBe(50);
        SearchAuditQueryHandler.MaximumPageSize.ShouldBe(200);
    }

    [Fact]
    public async Task An_unparseable_named_fab_throws_before_an_over_long_username_can_return_an_empty_page()
    {
        var overLongUsername = new string('u', 256);
        var handler = HandlerOver(ScopeRows());

        var thrown = await Should.ThrowAsync<ArgumentException>(
            () => handler.HandleAsync(Query(fab: "Munich", actorUsername: overLongUsername), default));

        thrown.GetType().ShouldBe(typeof(ArgumentException));
        thrown.Message.ShouldBe("value: must be lowercase letters, digits, or '-' and start with a letter (Parameter 'value')");
    }

    [Fact]
    public async Task An_empty_actor_throws_before_an_over_long_username_can_return_an_empty_page()
    {
        var overLongUsername = new string('u', 256);
        var handler = HandlerOver(ScopeRows());

        var thrown = await Should.ThrowAsync<ArgumentException>(
            () => handler.HandleAsync(Query(fab: "munich", actor: Guid.Empty, actorUsername: overLongUsername), default));

        thrown.GetType().ShouldBe(typeof(ArgumentException));
        thrown.Message.ShouldBe("value: cannot be empty for a human caller; use ActorIdentifier.System for system events. (Parameter 'value')");
    }

    [Fact]
    public async Task An_over_long_username_returns_an_empty_page_and_never_parses_the_event_kind()
    {
        var overLongUsername = new string('u', 256);
        var overLongEventKind = new string('k', 101);
        var handler = HandlerOver(ScopeRows());

        var result = await handler.HandleAsync(
            Query(fab: "munich", actorUsername: overLongUsername, eventKind: overLongEventKind), default);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.ShouldBeEmpty();
        result.Value.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task An_over_long_username_alone_returns_an_empty_page_even_where_rows_match_the_fab()
    {
        var handler = HandlerOver(ScopeRows());

        var result = await handler.HandleAsync(Query(fab: "munich", actorUsername: new string('u', 256)), default);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.ShouldBeEmpty();
        result.Value.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task An_unparseable_event_kind_throws_instead_of_returning_an_empty_page()
    {
        var handler = HandlerOver(ScopeRows());

        var thrown = await Should.ThrowAsync<ArgumentException>(
            () => handler.HandleAsync(Query(fab: "munich", eventKind: new string('k', 101)), default));

        thrown.GetType().ShouldBe(typeof(ArgumentException));
        thrown.Message.ShouldBe("value must be no more than 100 character(s). (Parameter 'value')");
    }

    [Fact]
    public async Task A_blank_resource_identifier_throws_instead_of_returning_an_empty_page()
    {
        var handler = HandlerOver(ScopeRows());

        var thrown = await Should.ThrowAsync<ArgumentException>(
            () => handler.HandleAsync(Query(fab: "munich", resourceIdentifier: " "), default));

        thrown.GetType().ShouldBe(typeof(ArgumentException));
        thrown.Message.ShouldBe("value must not be null or whitespace. (Parameter 'value')");
    }

    [Fact]
    public async Task A_named_fab_returns_that_fabs_rows_and_the_not_applicable_row_and_ignores_the_callers_claimed_fabs()
    {
        var handler = HandlerOver(ScopeRows());

        await ShouldReturnAsync(handler, Query(fab: "munich", callerFabs: ["berlin"]), MunichRow, NotApplicableRow);
        await ShouldReturnAsync(handler, Query(fab: "berlin", callerFabs: ["munich"]), BerlinRow, NotApplicableRow);
        await ShouldReturnAsync(handler, Query(fab: "berlin", callerFabs: []), BerlinRow, NotApplicableRow);
    }

    [Fact]
    public async Task A_named_fab_with_no_rows_returns_only_the_not_applicable_row()
    {
        var handler = HandlerOver(ScopeRows());

        await ShouldReturnAsync(handler, Query(fab: "hamburg", callerFabs: ["hamburg", "munich"]), NotApplicableRow);
    }

    [Fact]
    public async Task No_named_fab_returns_the_claimed_fabs_rows_and_only_the_not_applicable_fab_less_row_skipping_a_malformed_claim()
    {
        var handler = HandlerOver(ScopeRows());

        await ShouldReturnAsync(
            handler,
            Query(callerFabs: ["munich", "Munich"]),
            MunichRow,
            NotApplicableRow);
        await ShouldReturnAsync(
            handler,
            Query(callerFabs: ["berlin"]),
            BerlinRow,
            NotApplicableRow);
        await ShouldReturnAsync(
            handler,
            Query(callerFabs: ["berlin", "munich"]),
            MunichRow,
            BerlinRow,
            NotApplicableRow);
    }

    [Fact]
    public async Task No_named_fab_and_no_usable_claim_returns_only_the_fab_less_rows()
    {
        var handler = HandlerOver(ScopeRows());

        await ShouldReturnAsync(handler, Query(callerFabs: []), UnresolvedRow, NotApplicableRow);
        await ShouldReturnAsync(handler, Query(callerFabs: ["Munich"]), UnresolvedRow, NotApplicableRow);
        await ShouldReturnAsync(handler, Query(callerFabs: ["Munich", "1berlin"]), UnresolvedRow, NotApplicableRow);
    }

    [Fact]
    public async Task A_fab_less_row_is_visible_to_a_caller_with_a_claimed_fab_only_when_not_applicable()
    {
        var handler = HandlerOver(ScopeRows());

        var result = await handler.HandleAsync(Query(callerFabs: ["munich"]), default);

        result.Value.Rows.Select(row => row.FabAttribution).ShouldBe(["Resolved", "NotApplicable"]);
    }

    [Fact]
    public async Task A_not_applicable_row_is_visible_to_a_caller_with_a_claimed_fab()
    {
        var handler = HandlerOver(ScopeRows());

        var result = await handler.HandleAsync(Query(callerFabs: ["munich"]), default);

        var identifiers = result.Value.Rows.Select(row => row.AuditIdentifier.ToString()).ToList();
        identifiers.ShouldContain(NotApplicableRow);
        identifiers.ShouldContain(MunichRow);
        identifiers.ShouldNotContain(BerlinRow);
    }

    [Fact]
    public async Task A_resolved_row_of_another_fab_is_never_returned_to_a_caller_without_that_claim()
    {
        var handler = HandlerOver(ScopeRows());

        var result = await handler.HandleAsync(Query(callerFabs: ["munich"]), default);

        result.Value.Rows.Select(row => row.AuditIdentifier.ToString()).ShouldNotContain(BerlinRow);
        result.Value.Rows.Select(row => row.Fab).ShouldNotContain("berlin");
    }

    [Fact]
    public async Task A_named_fab_returns_that_fabs_own_row_and_never_another_fabs()
    {
        var handler = HandlerOver(ScopeRows());

        var result = await handler.HandleAsync(Query(fab: "munich", callerFabs: ["munich"]), default);

        var identifiers = result.Value.Rows.Select(row => row.AuditIdentifier.ToString()).ToList();
        identifiers.ShouldContain(MunichRow);
        identifiers.ShouldNotContain(BerlinRow);
    }
}
