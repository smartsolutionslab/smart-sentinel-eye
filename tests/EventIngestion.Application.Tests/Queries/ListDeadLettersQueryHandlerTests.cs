using System.Globalization;
using SmartSentinelEye.EventIngestion.Application.DTOs;
using SmartSentinelEye.EventIngestion.Application.Queries;
using SmartSentinelEye.EventIngestion.Application.Queries.Handlers;
using SmartSentinelEye.EventIngestion.Application.Tests.Fakes;
using SmartSentinelEye.EventIngestion.Domain.DeadLetter;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Queries;

public class ListDeadLettersQueryHandlerTests
{
    private static readonly DateTimeOffset BaseMoment =
        DateTimeOffset.Parse("2026-05-28T08:00:00Z", CultureInfo.InvariantCulture);

    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");
    private static readonly FabIdentifier Dresden = FabIdentifier.From("dresden");

    [Fact]
    public async Task Returns_dead_letters_ordered_descending_by_rejectedAt()
    {
        DeadLetter[] seed =
        [
            Captured("fab/munich/plc/a", Munich, BaseMoment),
            Captured("fab/munich/plc/b", Munich, BaseMoment.AddMinutes(5)),
            Captured("fab/munich/plc/c", Munich, BaseMoment.AddMinutes(1)),
        ];
        ListDeadLettersQueryHandler handler = new(new TestDeadLetterQuerySource(seed));

        Result<IReadOnlyList<DeadLetterDto>, ListDeadLettersError> result =
            await handler.HandleAsync(Query([Munich], 10), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(d => d.Topic)
            .ShouldBe(["fab/munich/plc/b", "fab/munich/plc/c", "fab/munich/plc/a"]);
    }

    [Fact]
    public async Task Caps_at_MaximumLimit_when_caller_asks_for_more()
    {
        List<DeadLetter> seed = [];
        for (int i = 0; i < 5; i++)
        {
            seed.Add(Captured($"fab/munich/plc/{i}", Munich, BaseMoment.AddSeconds(i)));
        }
        ListDeadLettersQueryHandler handler = new(new TestDeadLetterQuerySource(seed));

        Result<IReadOnlyList<DeadLetterDto>, ListDeadLettersError> result =
            await handler.HandleAsync(Query([Munich], 10_000), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(5);
    }

    /// <summary>Spec 018 FR-009 — every row carries another plant's raw payload.</summary>
    [Fact]
    public async Task Returns_only_the_rejected_deliveries_from_the_callers_fabs()
    {
        DeadLetter[] seed =
        [
            Captured("fab/munich/plc/a", Munich, BaseMoment),
            Captured("fab/dresden/plc/b", Dresden, BaseMoment.AddMinutes(1)),
        ];
        ListDeadLettersQueryHandler handler = new(new TestDeadLetterQuerySource(seed));

        Result<IReadOnlyList<DeadLetterDto>, ListDeadLettersError> result =
            await handler.HandleAsync(Query([Dresden], 10), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(d => d.Topic).ShouldBe(["fab/dresden/plc/b"]);
    }

    [Fact]
    public async Task A_caller_holding_both_fabs_sees_both()
    {
        DeadLetter[] seed =
        [
            Captured("fab/munich/plc/a", Munich, BaseMoment),
            Captured("fab/dresden/plc/b", Dresden, BaseMoment.AddMinutes(1)),
        ];
        ListDeadLettersQueryHandler handler = new(new TestDeadLetterQuerySource(seed));

        Result<IReadOnlyList<DeadLetterDto>, ListDeadLettersError> result =
            await handler.HandleAsync(Query([Munich, Dresden], 10), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
    }

    /// <summary>
    /// Spec 018 FR-011. Not "not shown to a Dresden operator" — shown to
    /// nobody, including the operator who holds every fab there is. Asserted
    /// from that operator's side, because a single-fab assertion would pass
    /// even if the row were quietly attributed to some other plant.
    /// </summary>
    [Fact]
    public async Task A_delivery_with_no_establishable_fab_reaches_nobody()
    {
        DeadLetter[] seed =
        [
            Captured("fab/munich/plc/a", Munich, BaseMoment),
            Captured("not-a-fab-topic", null, BaseMoment.AddMinutes(1)),
        ];
        ListDeadLettersQueryHandler handler = new(new TestDeadLetterQuerySource(seed));

        Result<IReadOnlyList<DeadLetterDto>, ListDeadLettersError> result =
            await handler.HandleAsync(Query([Munich, Dresden], 10), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(d => d.Topic).ShouldBe(["fab/munich/plc/a"]);
    }

    /// <summary>T012 (spec 317, #2325) — FR-008: ?reason= narrows the listing.</summary>
    [Fact]
    public async Task Filtering_by_reason_returns_only_matching_rows()
    {
        DeadLetter[] seed =
        [
            Captured("fab/munich/plc/a", Munich, BaseMoment, DeadLetterReason.ParseFailure),
            Held("event/munich/manual/b", Munich, BaseMoment.AddMinutes(1), Kind.From("NobodyDeclaredThis")),
        ];
        ListDeadLettersQueryHandler handler = new(new TestDeadLetterQuerySource(seed));

        Result<IReadOnlyList<DeadLetterDto>, ListDeadLettersError> result = await handler.HandleAsync(
            Query([Munich], 10, reason: DeadLetterReason.UnknownEventType), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(d => d.Topic).ShouldBe(["event/munich/manual/b"]);
    }

    /// <summary>
    /// T012 — FR-008: <c>?state=</c> is wired into the query and does not
    /// exclude the rows it should include. <c>DeadLetter.Capture</c> always
    /// starts a row <c>Held</c> (FR-003) and the aggregate deliberately has
    /// no <c>Promote</c> method (plan.md §2.1 A7: promotion is a repository-
    /// level set-based update, never a loaded aggregate) — so a unit test
    /// cannot construct a <c>Promoted</c> row to prove exclusion; that half is
    /// <c>HeldEventTypePromotionIntegrationTests</c>' job, against the real
    /// table the repository's <c>UPDATE</c> actually writes to.
    /// </summary>
    [Fact]
    public async Task Filtering_by_state_Held_includes_a_held_row()
    {
        DeadLetter held = Held("event/munich/manual/a", Munich, BaseMoment, Kind.From("StillHeld"));
        ListDeadLettersQueryHandler handler = new(new TestDeadLetterQuerySource([held]));

        Result<IReadOnlyList<DeadLetterDto>, ListDeadLettersError> result = await handler.HandleAsync(
            Query([Munich], 10, state: HoldState.Held), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(d => d.Topic).ShouldBe(["event/munich/manual/a"]);
    }

    /// <summary>
    /// T012 — both filters combine with AND, not OR: a Held row of the wrong
    /// reason must not satisfy a <c>state=Held</c> filter on its own.
    /// </summary>
    [Fact]
    public async Task Filtering_by_both_reason_and_state_combines_them()
    {
        DeadLetter parseFailure = Captured("fab/munich/plc/a", Munich, BaseMoment, DeadLetterReason.ParseFailure);
        DeadLetter held = Held("event/munich/manual/b", Munich, BaseMoment.AddMinutes(1), Kind.From("StillHeld"));
        ListDeadLettersQueryHandler handler = new(new TestDeadLetterQuerySource([parseFailure, held]));

        Result<IReadOnlyList<DeadLetterDto>, ListDeadLettersError> result = await handler.HandleAsync(
            Query([Munich], 10, reason: DeadLetterReason.UnknownEventType, state: HoldState.Held),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(d => d.Topic).ShouldBe(["event/munich/manual/b"]);
    }

    /// <summary>T012 — neither filter present returns every row, unchanged.</summary>
    [Fact]
    public async Task No_filter_returns_every_row_regardless_of_reason_or_state()
    {
        DeadLetter parseFailure = Captured("fab/munich/plc/a", Munich, BaseMoment, DeadLetterReason.ParseFailure);
        DeadLetter held = Held("event/munich/manual/b", Munich, BaseMoment.AddMinutes(1), Kind.From("StillHeld"));
        ListDeadLettersQueryHandler handler = new(new TestDeadLetterQuerySource([parseFailure, held]));

        Result<IReadOnlyList<DeadLetterDto>, ListDeadLettersError> result =
            await handler.HandleAsync(Query([Munich], 10), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(2);
    }

    /// <summary>T012 — FR-008: the DTO carries the new fields.</summary>
    [Fact]
    public async Task The_dto_carries_fab_reason_kind_and_state()
    {
        DeadLetter held = Held("event/munich/manual/a", Munich, BaseMoment, Kind.From("NobodyDeclaredThis"));
        ListDeadLettersQueryHandler handler = new(new TestDeadLetterQuerySource([held]));

        Result<IReadOnlyList<DeadLetterDto>, ListDeadLettersError> result =
            await handler.HandleAsync(Query([Munich], 10), CancellationToken.None);

        DeadLetterDto dto = result.Value.ShouldHaveSingleItem();
        dto.Fab.ShouldBe("munich");
        dto.Reason.ShouldBe("UnknownEventType");
        dto.Kind.ShouldBe("NobodyDeclaredThis");
        dto.State.ShouldBe("Held");
    }

    private static ListDeadLettersQuery Query(
        IReadOnlyList<FabIdentifier> fabs,
        int limit,
        DeadLetterReason? reason = null,
        HoldState? state = null) =>
        new(
            fabs,
            limit,
            reason is null ? Option<DeadLetterReason>.None : Option<DeadLetterReason>.Some(reason),
            state is null ? Option<HoldState>.None : Option<HoldState>.Some(state));

    private static DeadLetter Captured(
        string topic, FabIdentifier? fab, DateTimeOffset at, DeadLetterReason? reason = null) =>
        DeadLetter.Capture(
            DeliveryTopic.From(topic), fab, RawPayload.From("raw"), RejectionReason.From("err"),
            reason ?? DeadLetterReason.ParseFailure, null, new FakeClock(at));

    private static DeadLetter Held(string topic, FabIdentifier fab, DateTimeOffset at, Kind kind) =>
        DeadLetter.Capture(
            DeliveryTopic.From(topic), fab, RawPayload.From("{}"), RejectionReason.From("EVENT_TYPE_HELD: probe"),
            DeadLetterReason.UnknownEventType, kind, new FakeClock(at));
}
