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
            DeadLetterReason.ParseFailure,
            null,
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
            DeadLetterReason.ParseFailure,
            null,
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
            DeadLetterReason.ParseFailure,
            null,
            new FakeClock(Now));

        deadLetter.Fab.ShouldBeNull();
    }

    [Fact]
    public void Capture_rejects_empty_topic_or_error()
    {
        FakeClock clock = new(Now);
        FabIdentifier fab = FabIdentifier.From("munich");
        Action emptyTopic = () =>
            Domain.DeadLetter.DeadLetter.Capture(
                DeliveryTopic.From(""), fab, RawPayload.From("raw"), RejectionReason.From("err"),
                DeadLetterReason.ParseFailure, null, clock);
        Action emptyError = () =>
            Domain.DeadLetter.DeadLetter.Capture(
                DeliveryTopic.From("fab/m/plc/x"), fab, RawPayload.From("raw"), RejectionReason.From(""),
                DeadLetterReason.ParseFailure, null, clock);
        emptyTopic.ShouldThrow<ArgumentException>();
        emptyError.ShouldThrow<ArgumentException>();
    }

    /// <summary>FR-003: every captured row starts Held, whatever its reason.</summary>
    [Fact]
    public void Capture_sets_the_hold_state_to_Held()
    {
        Domain.DeadLetter.DeadLetter deadLetter = Domain.DeadLetter.DeadLetter.Capture(
            DeliveryTopic.From("fab/munich/plc/station-4"),
            FabIdentifier.From("munich"),
            RawPayload.From("<not-json>"),
            RejectionReason.From("payload parse failed"),
            DeadLetterReason.ParseFailure,
            null,
            new FakeClock(Now));

        deadLetter.State.ShouldBe(HoldState.Held);
    }

    /// <summary>FR-001: the reason code travels with the row independently of the free-text error.</summary>
    [Fact]
    public void Capture_records_the_reason_it_is_given()
    {
        Domain.DeadLetter.DeadLetter deadLetter = Domain.DeadLetter.DeadLetter.Capture(
            DeliveryTopic.From("event/munich/manual/station-4"),
            FabIdentifier.From("munich"),
            RawPayload.From("{}"),
            RejectionReason.From("EVENT_TYPE_NOT_REGISTERED: probe"),
            DeadLetterReason.Refused,
            Kind.From("NobodyDeclaredThis"),
            new FakeClock(Now));

        deadLetter.Reason.ShouldBe(DeadLetterReason.Refused);
    }

    /// <summary>FR-002: set for a Refused or UnknownEventType row, where the envelope parsed.</summary>
    [Fact]
    public void Capture_records_the_kind_when_one_is_given()
    {
        Kind kind = Kind.From("NobodyDeclaredThis");

        Domain.DeadLetter.DeadLetter deadLetter = Domain.DeadLetter.DeadLetter.Capture(
            DeliveryTopic.From("event/munich/manual/station-4"),
            FabIdentifier.From("munich"),
            RawPayload.From("{}"),
            RejectionReason.From("EVENT_TYPE_HELD: probe"),
            DeadLetterReason.UnknownEventType,
            kind,
            new FakeClock(Now));

        deadLetter.Kind.ShouldBe(kind);
    }

    /// <summary>FR-002: a parse failure never reaches a parsed envelope, so it has no kind.</summary>
    [Fact]
    public void Capture_leaves_the_kind_unset_when_none_is_given()
    {
        Domain.DeadLetter.DeadLetter deadLetter = Domain.DeadLetter.DeadLetter.Capture(
            DeliveryTopic.From("fab/munich/plc/station-4"),
            FabIdentifier.From("munich"),
            RawPayload.From("<not-json>"),
            RejectionReason.From("payload parse failed"),
            DeadLetterReason.ParseFailure,
            null,
            new FakeClock(Now));

        deadLetter.Kind.ShouldBeNull();
    }

    /// <summary>
    /// FR-002's invariant: <c>UnknownEventType ⇒ kind is not null</c>. A hold
    /// with no fab has nothing to key a promotion on, either.
    /// </summary>
    [Fact]
    public void Capture_refuses_UnknownEventType_without_a_fab()
    {
        Action act = () => Domain.DeadLetter.DeadLetter.Capture(
            DeliveryTopic.From("event/munich/manual/station-4"),
            null,
            RawPayload.From("{}"),
            RejectionReason.From("EVENT_TYPE_HELD: probe"),
            DeadLetterReason.UnknownEventType,
            Kind.From("NobodyDeclaredThis"),
            new FakeClock(Now));

        act.ShouldThrow<ArgumentException>();
    }

    /// <summary>FR-002's invariant, the other half: no kind, no hold.</summary>
    [Fact]
    public void Capture_refuses_UnknownEventType_without_a_kind()
    {
        Action act = () => Domain.DeadLetter.DeadLetter.Capture(
            DeliveryTopic.From("event/munich/manual/station-4"),
            FabIdentifier.From("munich"),
            RawPayload.From("{}"),
            RejectionReason.From("EVENT_TYPE_HELD: probe"),
            DeadLetterReason.UnknownEventType,
            null,
            new FakeClock(Now));

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void Capture_accepts_UnknownEventType_with_both_fab_and_kind()
    {
        Kind kind = Kind.From("NobodyDeclaredThis");
        FabIdentifier fab = FabIdentifier.From("munich");

        Domain.DeadLetter.DeadLetter deadLetter = Domain.DeadLetter.DeadLetter.Capture(
            DeliveryTopic.From("event/munich/manual/station-4"),
            fab,
            RawPayload.From("{}"),
            RejectionReason.From("EVENT_TYPE_HELD: probe"),
            DeadLetterReason.UnknownEventType,
            kind,
            new FakeClock(Now));

        deadLetter.Reason.ShouldBe(DeadLetterReason.UnknownEventType);
        deadLetter.Fab.ShouldBe(fab);
        deadLetter.Kind.ShouldBe(kind);
    }
}
