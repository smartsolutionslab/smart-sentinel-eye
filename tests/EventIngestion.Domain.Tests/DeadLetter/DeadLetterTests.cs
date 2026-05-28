using System.Globalization;
using SmartSentinelEye.EventIngestion.Domain.DeadLetter;
using SmartSentinelEye.EventIngestion.Domain.Tests.Event.Fakes;

namespace SmartSentinelEye.EventIngestion.Domain.Tests.DeadLetter;

public class DeadLetterTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-05-28T08:14:33Z", CultureInfo.InvariantCulture);

    [Fact]
    public void Capture_stores_the_topic_payload_and_error_with_the_clock_moment()
    {
        Domain.DeadLetter.DeadLetter deadLetter = Domain.DeadLetter.DeadLetter.Capture(
            "fab/munich/plc/station-4",
            "<not-json>",
            "payload parse failed",
            new FakeClock(Now));

        deadLetter.Topic.ShouldBe("fab/munich/plc/station-4");
        deadLetter.RawPayload.ShouldBe("<not-json>");
        deadLetter.Error.ShouldBe("payload parse failed");
        deadLetter.RejectedAt.ShouldBe(Now);
        deadLetter.Id.Value.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void Capture_rejects_empty_topic_or_error()
    {
        FakeClock clock = new(Now);
        Action emptyTopic = () =>
            Domain.DeadLetter.DeadLetter.Capture("", "raw", "err", clock);
        Action emptyError = () =>
            Domain.DeadLetter.DeadLetter.Capture("fab/m/plc/x", "raw", "", clock);
        emptyTopic.ShouldThrow<ArgumentException>();
        emptyError.ShouldThrow<ArgumentException>();
    }
}
