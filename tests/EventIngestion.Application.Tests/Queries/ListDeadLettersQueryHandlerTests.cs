using System.Globalization;
using SmartSentinelEye.EventIngestion.Application.DTOs;
using SmartSentinelEye.EventIngestion.Application.Queries;
using SmartSentinelEye.EventIngestion.Application.Queries.Handlers;
using SmartSentinelEye.EventIngestion.Application.Tests.Fakes;
using SmartSentinelEye.EventIngestion.Domain.DeadLetter;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Application.Tests.Queries;

public class ListDeadLettersQueryHandlerTests
{
    private static readonly DateTimeOffset BaseMoment =
        DateTimeOffset.Parse("2026-05-28T08:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task Returns_dead_letters_ordered_descending_by_rejectedAt()
    {
        DeadLetter[] seed =
        [
            DeadLetter.Capture("fab/m/plc/a", "raw1", "err1", new FakeClock(BaseMoment)),
            DeadLetter.Capture("fab/m/plc/b", "raw2", "err2", new FakeClock(BaseMoment.AddMinutes(5))),
            DeadLetter.Capture("fab/m/plc/c", "raw3", "err3", new FakeClock(BaseMoment.AddMinutes(1))),
        ];
        ListDeadLettersQueryHandler handler = new(new TestDeadLetterQuerySource(seed));

        Result<IReadOnlyList<DeadLetterDto>, ListDeadLettersError> result =
            await handler.HandleAsync(new ListDeadLettersQuery(10), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(d => d.Topic)
            .ShouldBe(["fab/m/plc/b", "fab/m/plc/c", "fab/m/plc/a"]);
    }

    [Fact]
    public async Task Caps_at_MaximumLimit_when_caller_asks_for_more()
    {
        List<DeadLetter> seed = new();
        for (int i = 0; i < 5; i++)
        {
            seed.Add(DeadLetter.Capture(
                $"fab/m/plc/{i}", "raw", "err", new FakeClock(BaseMoment.AddSeconds(i))));
        }
        ListDeadLettersQueryHandler handler = new(new TestDeadLetterQuerySource(seed));

        Result<IReadOnlyList<DeadLetterDto>, ListDeadLettersError> result =
            await handler.HandleAsync(
                new ListDeadLettersQuery(10_000), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Count.ShouldBe(5);
    }
}
