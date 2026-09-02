using SmartSentinelEye.EventIngestion.Domain.DeadLetter;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;

namespace SmartSentinelEye.EventIngestion.Domain.Tests.DeadLetter;

/// <summary>
/// The three text concepts a dead letter carries, plus the webhook's Keycloak
/// client identifier.
///
/// <para>
/// <c>RawPayload</c> is the one that changes behaviour rather than only moving
/// it: <c>DeadLetter.Capture</c> guarded it with <c>IsNotNull()</c> alone, so an
/// empty payload was capturable. An empty dead letter records that something was
/// rejected while discarding the only evidence of what — the exact thing the
/// aggregate exists to preserve.
/// </para>
/// </summary>
public class DeadLetterValueObjectTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_delivery_topic_that_is_empty_or_whitespace_is_refused(string input)
    {
        Action act = () => DeliveryTopic.From(input);

        act.ShouldThrow<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_rejection_reason_that_is_empty_or_whitespace_is_refused(string input)
    {
        Action act = () => RejectionReason.From(input);

        act.ShouldThrow<ArgumentException>();
    }

    /// <summary>
    /// The one property in this feature that deliberately keeps accepting empty
    /// input. A zero-length MQTT delivery reaches
    /// <c>MqttSubscriberHostedService</c> as <c>""</c> and is exactly the kind of
    /// malformed message that gets rejected — refusing it would throw inside the
    /// capture path and lose the dead letter for one of the most likely rejection
    /// causes.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_raw_payload_that_is_empty_is_still_captured(string input)
    {
        RawPayload.From(input).Value.ShouldBe(input);
    }

    [Fact]
    public void A_raw_payload_that_is_null_is_refused()
    {
        Action act = () => RawPayload.From(null);

        act.ShouldThrow<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_keycloak_client_identifier_that_is_empty_or_whitespace_is_refused(string input)
    {
        Action act = () => KeycloakClientIdentifier.From(input);

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void A_delivery_topic_longer_than_the_column_is_refused_before_the_database_sees_it()
    {
        Action act = () => DeliveryTopic.From(new string('x', DeliveryTopic.MaximumLength + 1));

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void A_rejection_reason_longer_than_the_column_is_refused_before_the_database_sees_it()
    {
        Action act = () => RejectionReason.From(new string('x', RejectionReason.MaximumLength + 1));

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void A_keycloak_client_identifier_longer_than_the_column_is_refused()
    {
        Action act = () => KeycloakClientIdentifier.From(
            new string('x', KeycloakClientIdentifier.MaximumLength + 1));

        act.ShouldThrow<ArgumentException>();
    }

    /// <summary>
    /// <c>raw_payload</c> is a <c>text</c> column and deliberately unbounded. A
    /// rejected delivery is captured so an operator can post-mortem it without a
    /// redeploy; truncating it at some invented ceiling would destroy the
    /// evidence in exactly the cases most worth keeping — the oversized message
    /// that caused the rejection.
    /// </summary>
    [Fact]
    public void A_raw_payload_has_no_upper_bound_because_its_column_has_none()
    {
        string enormous = new('x', 5_000_000);

        RawPayload.From(enormous).Value.Length.ShouldBe(5_000_000);
    }

    /// <summary>
    /// Captured verbatim — not trimmed, not normalised. Leading and trailing
    /// whitespace can be the defect being investigated.
    /// </summary>
    [Fact]
    public void A_raw_payload_is_captured_exactly_as_it_arrived()
    {
        const string awkward = "  {\"a\":1}\n\t";

        RawPayload.From(awkward).Value.ShouldBe(awkward);
    }

    [Fact]
    public void The_bounds_match_the_columns_they_protect()
    {
        DeliveryTopic.MaximumLength.ShouldBe(256);
        RejectionReason.MaximumLength.ShouldBe(512);
        KeycloakClientIdentifier.MaximumLength.ShouldBe(255);
    }
}
