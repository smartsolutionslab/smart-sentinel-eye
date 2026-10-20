using System.Globalization;
using System.Reflection;
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
/// Adversarial (spec 396, #2927). Tries every handler-level route by which a fab-scoped
/// caller could still be handed an <c>Unresolved</c> row: each search filter, a page walk to
/// the last cursor, every timeline resource kind, odd claim spellings on all three paths.
/// Also pins that a caller whose every claim is malformed holds no fab and reads only the
/// <c>NotApplicable</c> row, never an <c>Unresolved</c> one (spec 422, #3035, reversing the
/// residual spec 396 disclosed). Rows are addressed by literal identifiers so a count cannot hide a swap.
/// </summary>
public class AuditReadScopeAdversarialTests
{
    private const string MunichRow = "000000a1-0000-4000-8000-000000000000";
    private const string BerlinRow = "000000b1-0000-4000-8000-000000000000";
    private const string UnresolvedRow = "000000c1-0000-4000-8000-000000000000";
    private const string NotApplicableRow = "000000d1-0000-4000-8000-000000000000";

    private const string Camera = "44444444-4444-4444-4444-444444444444";

    private static readonly Guid UnresolvedActor = Guid.Parse("0199aaaa-0000-7000-8000-000000000001");

    private static readonly DateTimeOffset Start =
        DateTimeOffset.Parse("2026-03-10T12:00:00Z", CultureInfo.InvariantCulture);

    private static readonly PropertyInfo AttributionProperty =
        typeof(AuditEventEntity).GetProperty(nameof(AuditEventEntity.FabAttribution))!;

    // One row of each class, on one resource, at distinct instants (newest first: munich).
    private static AuditEventEntity[] Rows(string resourceKind = "stream") =>
    [
        new AuditEventBuilder().WithOccurredAt(Start.AddMinutes(4)).WithFab("munich")
            .WithEventKind("StreamHealthChangedV1").WithResource(resourceKind, Camera).Build().WithIdentifier(MunichRow),
        new AuditEventBuilder().WithOccurredAt(Start.AddMinutes(3)).WithFab("berlin")
            .WithEventKind("StreamHealthChangedV1").WithResource(resourceKind, Camera).Build().WithIdentifier(BerlinRow),
        new AuditEventBuilder().WithOccurredAt(Start.AddMinutes(2)).WithFab(null)
            .WithEventKind("StreamHealthChangedV1").WithResource(resourceKind, Camera)
            .WithActor(UnresolvedActor, "op-munich@munich.test").Build().WithIdentifier(UnresolvedRow),
        new AuditEventBuilder().WithOccurredAt(Start.AddMinutes(1)).WithFab(null).WithFabScope(FabScope.Neutral)
            .WithEventKind("OverlayRevisionPublishedV3").WithResource(resourceKind, Camera).Build().WithIdentifier(NotApplicableRow),
    ];

    private static SearchAuditQuery Search(
        string[] callerFabs,
        string? fab = null,
        Guid? actor = null,
        string? actorUsername = null,
        string? eventKind = null,
        string? resourceKind = null,
        string? resourceIdentifier = null,
        DateTimeOffset? since = null,
        DateTimeOffset? until = null,
        int pageSize = 50,
        string? cursor = null) =>
        new(fab, callerFabs, actor, actorUsername, eventKind, resourceKind, resourceIdentifier, since, until, pageSize, cursor);

    private static async Task<string[]> SearchIdentifiers(SearchAuditQuery query, params AuditEventEntity[] rows)
    {
        SearchAuditQueryHandler handler = new(new TestAuditEventQuerySource(rows));
        var result = await handler.HandleAsync(query, default);
        result.IsSuccess.ShouldBeTrue();
        return [.. result.Value.Rows.Select(row => row.AuditIdentifier.ToString())];
    }

    private static async Task<Result<AuditRowDto, GetAuditEventError>> ReadSingle(
        AuditEventEntity? stored,
        Guid requested,
        params string[] callerFabs)
    {
        GetAuditEventQueryHandler handler = new(new TestAuditEventQuerySource(stored is null ? [] : [stored]));
        return await handler.HandleAsync(
            new GetAuditEventQuery(AuditEventIdentifier.From(requested), callerFabs),
            default);
    }

    // ----- Claim spellings: one usable claim keeps the caller scoped -----

    public static TheoryData<string[]> ScopedClaimSets() => new()
    {
        { ["munich"] },
        { ["munich", "munich"] },
        { ["munich", "Munich"] },
        { ["munich", "MUNICH"] },
        { ["munich", ""] },
        { ["munich", " "] },
        { ["munich", " munich"] },
        { ["munich", "munich "] },
        { ["munich", "munich/"] },
        { ["munich", "munich/ops"] },
        { ["munich", "/fabs/munich"] },
        { ["munich", "m"] },
        { ["munich", new string('a', 33)] },
        { ["Munich", "munich"] },
    };

    [Theory]
    [MemberData(nameof(ScopedClaimSets))]
    public async Task A_caller_with_one_usable_claim_never_sees_the_unresolved_row_in_a_search(string[] claims)
    {
        var identifiers = await SearchIdentifiers(Search(claims), Rows());

        identifiers.ShouldBe([MunichRow, NotApplicableRow]);
    }

    [Theory]
    [MemberData(nameof(ScopedClaimSets))]
    public async Task A_caller_with_one_usable_claim_is_answered_not_found_for_the_unresolved_row(string[] claims)
    {
        var row = Rows()[2];

        var hidden = await ReadSingle(row, row.Id.Value, claims);
        var unknown = await ReadSingle(null, row.Id.Value, claims);

        hidden.IsFailure.ShouldBeTrue();
        hidden.Error.ShouldBe(unknown.Error);
        hidden.Error.Code.ShouldBe(unknown.Error.Code);
        hidden.Error.Message.ShouldBe(unknown.Error.Message);
        hidden.Error.Status.ShouldBe(unknown.Error.Status);
    }

    [Fact]
    public async Task A_multi_fab_caller_never_sees_the_unresolved_row_even_when_the_claims_cover_every_resolved_row()
    {
        var identifiers = await SearchIdentifiers(Search(["berlin", "munich", "hamburg", "dresden"]), Rows());

        identifiers.ShouldBe([MunichRow, BerlinRow, NotApplicableRow]);
    }

    // ----- Wholly malformed claims hold no fab (spec 422 reverses spec 396's residual) -----
    //
    // "munich/ops" is what Keycloak's full-path group mapper emits for a member of a
    // sub-group of a fab; "/fabs/munich" is a doubled prefix after the strip. A caller
    // holding only such a claim is refused a named fab by the guard and reads only the
    // fab-neutral (NotApplicable) row, never an Unresolved one.

    public static TheoryData<string[]> WhollyMalformedClaimSets() => new()
    {
        { [] },
        { [""] },
        { [" "] },
        { ["Munich"] },
        { ["munich "] },
        { ["munich/"] },
        { ["munich/ops"] },
        { ["/fabs/munich"] },
        { ["m"] },
        { [new string('a', 33)] },
        { ["Munich", "BERLIN", "munich/ops"] },
    };

    [Theory]
    [MemberData(nameof(WhollyMalformedClaimSets))]
    public async Task A_caller_whose_every_claim_is_malformed_searches_only_the_not_applicable_row(string[] claims)
    {
        var identifiers = await SearchIdentifiers(Search(claims), Rows());

        identifiers.ShouldBe([NotApplicableRow]);
    }

    [Theory]
    [MemberData(nameof(WhollyMalformedClaimSets))]
    public async Task A_caller_whose_every_claim_is_malformed_is_answered_not_found_for_the_unresolved_row(string[] claims)
    {
        var row = Rows()[2];

        var hidden = await ReadSingle(row, row.Id.Value, claims);
        var unknown = await ReadSingle(null, row.Id.Value, claims);

        hidden.IsFailure.ShouldBeTrue();
        hidden.Error.ShouldBeOfType<GetAuditEventError.AuditEventNotFound>().AuditIdentifier.ShouldBe(row.Id.Value);
        hidden.Error.ShouldBe(unknown.Error);
        hidden.Error.Code.ShouldBe(unknown.Error.Code);
        hidden.Error.Message.ShouldBe(unknown.Error.Message);
        hidden.Error.Status.ShouldBe(unknown.Error.Status);
    }

    // ----- Every search filter that matches the hidden row -----

    [Fact]
    public async Task Filtering_by_the_unresolved_rows_actor_returns_nothing_to_a_scoped_caller()
    {
        var identifiers = await SearchIdentifiers(Search(["munich"], actor: UnresolvedActor), Rows());

        identifiers.ShouldBeEmpty();
    }

    [Fact]
    public async Task Filtering_by_the_unresolved_rows_actor_username_returns_nothing_to_a_scoped_caller()
    {
        var identifiers = await SearchIdentifiers(Search(["munich"], actorUsername: "op-munich@munich.test"), Rows());

        identifiers.ShouldBeEmpty();
    }

    [Fact]
    public async Task Filtering_by_the_unresolved_rows_kind_and_resource_omits_it_for_a_scoped_caller()
    {
        var identifiers = await SearchIdentifiers(
            Search(["munich"], eventKind: "StreamHealthChangedV1", resourceKind: "stream", resourceIdentifier: Camera),
            Rows());

        identifiers.ShouldBe([MunichRow]);
    }

    [Fact]
    public async Task A_window_enclosing_only_the_unresolved_row_returns_an_empty_page_without_a_cursor()
    {
        SearchAuditQueryHandler handler = new(new TestAuditEventQuerySource(Rows()));

        var result = await handler.HandleAsync(
            Search(["munich"], since: Start.AddMinutes(2), until: Start.AddMinutes(3), pageSize: 1),
            default);

        result.Value.Rows.ShouldBeEmpty();
        result.Value.NextCursor.ShouldBeNull();
    }

    [Fact]
    public async Task Naming_a_fab_hides_the_unresolved_row_whatever_the_caller_claims()
    {
        (await SearchIdentifiers(Search([], fab: "munich"), Rows())).ShouldBe([MunichRow, NotApplicableRow]);
        (await SearchIdentifiers(Search(["Munich"], fab: "munich"), Rows())).ShouldBe([MunichRow, NotApplicableRow]);
        (await SearchIdentifiers(Search(["berlin"], fab: "munich"), Rows())).ShouldBe([MunichRow, NotApplicableRow]);
        (await SearchIdentifiers(Search([], fab: "hamburg"), Rows())).ShouldBe([NotApplicableRow]);
    }

    // ----- Paging: a hidden row must not leave a trace in the cursor -----

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Walking_every_search_page_never_yields_an_unresolved_row_nor_a_cursor_past_the_last_visible_row(int pageSize)
    {
        // Two visible rows on top, then a tail of hidden rows: a cursor computed before the
        // scope was applied would announce a further page that has nothing to show.
        List<AuditEventEntity> rows =
        [
            new AuditEventBuilder().WithOccurredAt(Start.AddMinutes(20)).WithFab("munich").Build().WithIdentifier(MunichRow),
            new AuditEventBuilder().WithOccurredAt(Start.AddMinutes(19)).WithFab(null).WithFabScope(FabScope.Neutral)
                .Build().WithIdentifier(NotApplicableRow),
        ];
        for (var index = 0; index < 5; index++)
        {
            rows.Add(new AuditEventBuilder().WithOccurredAt(Start.AddMinutes(10 - index)).WithFab(null).Build()
                .WithIdentifier($"000000e{index}-0000-4000-8000-000000000000"));
        }

        SearchAuditQueryHandler handler = new(new TestAuditEventQuerySource(rows));
        List<string> seen = [];
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = (await handler.HandleAsync(Search(["munich"], pageSize: pageSize, cursor: cursor), default)).Value;
            seen.AddRange(page.Rows.Select(row => row.AuditIdentifier.ToString()));
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null && pages < 20);

        seen.ShouldBe([MunichRow, NotApplicableRow]);
        pages.ShouldBe(pageSize == 1 ? 2 : 1);
    }

    [Fact]
    public async Task A_cursor_forged_at_the_unresolved_rows_position_returns_only_the_older_visible_rows()
    {
        var unresolved = Rows()[2];
        var forged = AuditCursor.Encode(unresolved.OccurredAt.Value, unresolved.Id.Value);

        var identifiers = await SearchIdentifiers(Search(["munich"], cursor: forged), Rows());

        identifiers.ShouldBe([NotApplicableRow]);
    }

    // ----- Timeline: every resource kind -----

    public static TheoryData<string> EveryResourceKind()
    {
        TheoryData<string> data = [];
        foreach (var kind in ResourceKind.All)
        {
            data.Add(kind.Value);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryResourceKind))]
    public async Task The_timeline_of_every_resource_kind_hides_the_unresolved_row_and_the_foreign_fab(string resourceKind)
    {
        GetResourceTimelineQueryHandler handler = new(new TestAuditEventQuerySource(Rows(resourceKind)));

        var result = await handler.HandleAsync(
            new GetResourceTimelineQuery(resourceKind, ResourceIdentifier.From(Camera), FabIdentifier.From("munich"), null, null, 50, null),
            default);

        result.Value.Rows.Select(row => row.AuditIdentifier.ToString()).ShouldBe([NotApplicableRow, MunichRow]);
    }

    [Fact]
    public async Task Walking_every_timeline_page_one_row_at_a_time_ends_on_the_last_visible_row()
    {
        GetResourceTimelineQueryHandler handler = new(new TestAuditEventQuerySource(Rows()));
        List<string> seen = [];
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = (await handler.HandleAsync(
                new GetResourceTimelineQuery("stream", ResourceIdentifier.From(Camera), FabIdentifier.From("munich"), null, null, 1, cursor),
                default)).Value;
            seen.AddRange(page.Rows.Select(row => row.AuditIdentifier.ToString()));
            cursor = page.NextCursor;
            pages++;
        }
        while (cursor is not null && pages < 20);

        seen.ShouldBe([NotApplicableRow, MunichRow]);
        pages.ShouldBe(2);
    }

    [Fact]
    public async Task A_timeline_naming_a_fab_with_no_rows_returns_only_the_not_applicable_row()
    {
        GetResourceTimelineQueryHandler handler = new(new TestAuditEventQuerySource(Rows()));

        var result = await handler.HandleAsync(
            new GetResourceTimelineQuery("stream", ResourceIdentifier.From(Camera), FabIdentifier.From("hamburg"), null, null, 50, null),
            default);

        result.Value.Rows.Select(row => row.AuditIdentifier.ToString()).ShouldBe([NotApplicableRow]);
    }

    // ----- Attribution that contradicts the fab (unreachable through AuditEvent.From) -----
    //
    // The invariant "Resolved iff Fab is not null" is kept by AuditEvent.From and by the
    // migration's backfill, so these rows cannot be produced by the write path. They are
    // pinned for when the invariant is ever broken (a hand-run UPDATE, a future backfill):
    // the scope fails closed, so a fab-carrying row labelled NotApplicable is not admitted
    // by its label, and a fab-carrying row labelled Unresolved is judged by its fab.

    private static AuditEventEntity Relabelled(AuditEventEntity row, FabAttribution attribution)
    {
        AttributionProperty.SetValue(row, attribution);
        return row;
    }

    [Fact]
    public async Task A_berlin_row_mislabelled_not_applicable_is_not_shown_to_a_munich_search()
    {
        var mislabelled = Relabelled(
            new AuditEventBuilder().WithOccurredAt(Start).WithFab("berlin").Build().WithIdentifier(BerlinRow),
            FabAttribution.NotApplicable);

        var identifiers = await SearchIdentifiers(Search(["munich"]), mislabelled);

        identifiers.ShouldBe([]);
    }

    [Fact]
    public async Task A_munich_row_mislabelled_unresolved_is_found_by_munich_both_by_search_and_by_identifier()
    {
        var mislabelled = Relabelled(
            new AuditEventBuilder().WithOccurredAt(Start).WithFab("munich").Build().WithIdentifier(MunichRow),
            FabAttribution.Unresolved);

        var identifiers = await SearchIdentifiers(Search(["munich"]), mislabelled);
        var single = await ReadSingle(mislabelled, mislabelled.Id.Value, "munich");

        identifiers.ShouldBe([MunichRow]);
        single.IsSuccess.ShouldBeTrue();
        single.Value.AuditIdentifier.ToString().ShouldBe(MunichRow);
    }
}
