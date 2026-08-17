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
            await handler.HandleAsync(new ListDeadLettersQuery([Munich], 10), CancellationToken.None);

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
            await handler.HandleAsync(
                new ListDeadLettersQuery([Munich], 10_000), CancellationToken.None);

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
            await handler.HandleAsync(
                new ListDeadLettersQuery([Dresden], 10), CancellationToken.None);

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
            await handler.HandleAsync(
                new ListDeadLettersQuery([Munich, Dresden], 10), CancellationToken.None);

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
            await handler.HandleAsync(
                new ListDeadLettersQuery([Munich, Dresden], 10), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(d => d.Topic).ShouldBe(["fab/munich/plc/a"]);
    }

    private static DeadLetter Captured(string topic, FabIdentifier? fab, DateTimeOffset at) =>
        DeadLetter.Capture(topic, fab, "raw", "err", new FakeClock(at));
}
