using System.Globalization;
using SmartSentinelEye.EventIngestion.Application.Queries;
using SmartSentinelEye.EventIngestion.Application.Queries.Handlers;
using SmartSentinelEye.EventIngestion.Application.Tests.Fakes;
using SmartSentinelEye.EventIngestion.Domain.DeadLetter;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Queries;

/// <summary>
/// Characterisation (spec 346, behaviour-preserving): pins what
/// <see cref="ListDeadLettersQueryHandler"/> does today that
/// <see cref="ListDeadLettersQueryHandlerTests"/> does not — the default limit
/// and the mapping of every <c>DeadLetterDto</c> field. These must pass
/// unmodified after the handler is split into helpers.
/// </summary>
public class ListDeadLettersMappingCharacterisationTests
{
    private static readonly DateTimeOffset BaseMoment =
        DateTimeOffset.Parse("2026-05-28T08:00:00Z", CultureInfo.InvariantCulture);

    private static readonly FabIdentifier Munich = FabIdentifier.From("munich");

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task A_limit_of_zero_or_below_returns_the_default_hundred_of_one_hundred_fifty_newest_first(
        int limit)
    {
        var seed = Enumerable.Range(0, 150)
            .Select(i => Captured($"fab/munich/plc/{i}", Munich, BaseMoment.AddSeconds(i)))
            .ToArray();
        ListDeadLettersQueryHandler handler = new(new TestDeadLetterQuerySource(seed));

        var result = await handler.HandleAsync(Query([Munich], limit), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(100);
        result.Value[0].Topic.ShouldBe("fab/munich/plc/149");
        result.Value[99].Topic.ShouldBe("fab/munich/plc/50");
    }

    [Fact]
    public async Task A_limit_inside_the_range_returns_exactly_that_many()
    {
        var seed = Enumerable.Range(0, 20)
            .Select(i => Captured($"fab/munich/plc/{i}", Munich, BaseMoment.AddSeconds(i)))
            .ToArray();
        ListDeadLettersQueryHandler handler = new(new TestDeadLetterQuerySource(seed));

        var result = await handler.HandleAsync(Query([Munich], 7), CancellationToken.None);

        result.Value.Count.ShouldBe(7);
        result.Value[0].Topic.ShouldBe("fab/munich/plc/19");
    }

    [Fact]
    public async Task Every_dto_field_carries_its_own_value_for_a_held_row()
    {
        var rejectedAt = BaseMoment.AddMinutes(42);
        var held = DeadLetter.Capture(
            DeliveryTopic.From("event/munich/manual/line-7"),
            Munich,
            RawPayload.From("{\"raw\":\"distinct-payload\"}"),
            RejectionReason.From("EVENT_TYPE_HELD: distinct-error"),
            DeadLetterReason.UnknownEventType,
            Kind.From("DistinctKind"),
            new FakeClock(rejectedAt));
        ListDeadLettersQueryHandler handler = new(new TestDeadLetterQuerySource([held]));

        var result = await handler.HandleAsync(Query([Munich], 10), CancellationToken.None);

        var dto = result.Value.ShouldHaveSingleItem();
        dto.DeadLetterIdentifier.ShouldBe(held.Id.Value);
        dto.Topic.ShouldBe("event/munich/manual/line-7");
        dto.RawPayload.ShouldBe("{\"raw\":\"distinct-payload\"}");
        dto.Error.ShouldBe("EVENT_TYPE_HELD: distinct-error");
        dto.RejectedAt.ShouldBe(rejectedAt);
        dto.Fab.ShouldBe("munich");
        dto.Reason.ShouldBe("UnknownEventType");
        dto.Kind.ShouldBe("DistinctKind");
        dto.State.ShouldBe("Held");
    }

    [Fact]
    public async Task A_parse_failure_row_maps_its_kind_to_null_and_keeps_the_other_fields()
    {
        var rejectedAt = BaseMoment.AddMinutes(3);
        var parseFailure = DeadLetter.Capture(
            DeliveryTopic.From("fab/munich/plc/oven-2"),
            Munich,
            RawPayload.From("not json"),
            RejectionReason.From("envelope parse failed: x"),
            DeadLetterReason.ParseFailure,
            null,
            new FakeClock(rejectedAt));
        ListDeadLettersQueryHandler handler = new(new TestDeadLetterQuerySource([parseFailure]));

        var result = await handler.HandleAsync(Query([Munich], 10), CancellationToken.None);

        var dto = result.Value.ShouldHaveSingleItem();
        dto.Kind.ShouldBeNull();
        dto.Reason.ShouldBe("ParseFailure");
        dto.Topic.ShouldBe("fab/munich/plc/oven-2");
        dto.RawPayload.ShouldBe("not json");
        dto.Error.ShouldBe("envelope parse failed: x");
        dto.RejectedAt.ShouldBe(rejectedAt);
        dto.Fab.ShouldBe("munich");
        dto.State.ShouldBe("Held");
    }

    /// <summary>
    /// A row with no fab satisfies no <c>IN</c> in the database (spec 018
    /// FR-011), so through the real query it never reaches the mapping. A list
    /// that itself holds <c>null</c> is the only offline way to put such a row
    /// in front of the mapping; it pins that the mapping does not throw on it
    /// and carries <c>null</c>.
    /// </summary>
    [Fact]
    public async Task An_unattributed_row_that_reaches_the_mapping_carries_a_null_fab()
    {
        var unattributed = Captured("not-a-fab-topic", null, BaseMoment);
        ListDeadLettersQueryHandler handler = new(new TestDeadLetterQuerySource([unattributed]));

        var result = await handler.HandleAsync(Query([null!], 10), CancellationToken.None);

        var dto = result.Value.ShouldHaveSingleItem();
        dto.Fab.ShouldBeNull();
        dto.Topic.ShouldBe("not-a-fab-topic");
    }

    private static ListDeadLettersQuery Query(IReadOnlyList<FabIdentifier> fabs, int limit) =>
        new(fabs, limit, Option<DeadLetterReason>.None, Option<HoldState>.None);

    private static DeadLetter Captured(string topic, FabIdentifier? fab, DateTimeOffset at) =>
        DeadLetter.Capture(
            DeliveryTopic.From(topic),
            fab,
            RawPayload.From("raw"),
            RejectionReason.From("err"),
            DeadLetterReason.ParseFailure,
            null,
            new FakeClock(at));
}
