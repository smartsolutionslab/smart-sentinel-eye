using System.Globalization;
using SmartSentinelEye.EventIngestion.Domain.DeadLetter;
using SmartSentinelEye.EventIngestion.Domain.Event;
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
            DeliveryTopic.From("fab/munich/plc/station-4"),
            FabIdentifier.From("munich"),
            RawPayload.From("<not-json>"),
            RejectionReason.From("payload parse failed"),
            new FakeClock(Now));

        deadLetter.Topic.Value.ShouldBe("fab/munich/plc/station-4");
        deadLetter.RawPayload.Value.ShouldBe("<not-json>");
        deadLetter.Error.Value.ShouldBe("payload parse failed");
        deadLetter.RejectedAt.Value.ShouldBe(Now);
        deadLetter.Id.Value.ShouldNotBe(Guid.Empty);
    }

    /// <summary>
    /// Spec 018 FR-008. The common rejection — a well-formed address carrying a
    /// payload that will not parse — does have a plant, and its own operators
    /// must be able to see it.
    /// </summary>
    [Fact]
    public void Capture_records_the_fab_it_is_given()
    {
        Domain.DeadLetter.DeadLetter deadLetter = Domain.DeadLetter.DeadLetter.Capture(
            DeliveryTopic.From("fab/dresden/plc/station-9"),
            FabIdentifier.From("dresden"),
            RawPayload.From("<not-json>"),
            RejectionReason.From("payload parse failed"),
            new FakeClock(Now));

        deadLetter.Fab.ShouldBe(FabIdentifier.From("dresden"));
    }

    /// <summary>
    /// Spec 018 FR-010. A delivery whose address establishes no plant is not
    /// attributed to one — the null is the honest answer, and it is what keeps
    /// the row out of every listing (FR-011).
    /// </summary>
    [Fact]
    public void Capture_leaves_the_fab_unset_when_none_was_established()
    {
        Domain.DeadLetter.DeadLetter deadLetter = Domain.DeadLetter.DeadLetter.Capture(
            DeliveryTopic.From("fab/NOT-A-FAB/plc/station-4"),
            null,
            RawPayload.From("<not-json>"),
            RejectionReason.From("envelope parse failed"),
            new FakeClock(Now));

        deadLetter.Fab.ShouldBeNull();
    }

    [Fact]
    public void Capture_rejects_empty_topic_or_error()
    {
        FakeClock clock = new(Now);
        FabIdentifier fab = FabIdentifier.From("munich");
        Action emptyTopic = () =>
            Domain.DeadLetter.DeadLetter.Capture(DeliveryTopic.From(""), fab, RawPayload.From("raw"), RejectionReason.From("err"), clock);
        Action emptyError = () =>
            Domain.DeadLetter.DeadLetter.Capture(DeliveryTopic.From("fab/m/plc/x"), fab, RawPayload.From("raw"), RejectionReason.From(""), clock);
        emptyTopic.ShouldThrow<ArgumentException>();
        emptyError.ShouldThrow<ArgumentException>();
    }
}
