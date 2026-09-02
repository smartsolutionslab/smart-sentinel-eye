using SmartSentinelEye.AuditObservability.Domain.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Domain.Tests.AuditEvent;

/// <summary>
/// The two text concepts on an audit event, and the only pair in this feature
/// that had <b>no</b> validation at all — <c>AuditEvent.Record</c> guarded the
/// envelope, the mapping and the clock, and neither of these.
///
/// <para>
/// An audit row with an empty payload is worse than a missing row: it asserts
/// that something was recorded while carrying nothing to inspect, and it does so
/// in the one context whose entire purpose is answering "what happened".
/// </para>
/// </summary>
public class AuditEventTextValueObjectTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_audit_payload_that_is_empty_or_whitespace_is_refused(string input)
    {
        Action act = () => AuditPayload.From(input);

        act.ShouldThrow<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_actor_username_that_is_empty_or_whitespace_is_refused(string input)
    {
        Action act = () => ActorUsername.From(input);

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void An_actor_username_longer_than_the_column_is_refused_before_the_database_sees_it()
    {
        Action act = () => ActorUsername.From(new string('x', ActorUsername.MaximumLength + 1));

        act.ShouldThrow<ArgumentException>();
    }

    [Fact]
    public void An_actor_username_at_the_exact_column_width_is_accepted()
    {
        string atLimit = new('x', ActorUsername.MaximumLength);

        ActorUsername.From(atLimit).Value.Length.ShouldBe(ActorUsername.MaximumLength);
    }

    /// <summary>
    /// The payload is a <c>jsonb</c> column and unbounded. The type enforces
    /// non-emptiness and nothing else — ADR-0139 exempts captured payloads from
    /// being <em>parsed</em>, so this must not start validating JSON. An audit
    /// row that cannot be written because its payload failed a schema check is
    /// an audit row that does not exist.
    /// </summary>
    [Fact]
    public void An_audit_payload_is_not_parsed_and_not_bounded()
    {
        const string notJson = "this is not json at all";

        AuditPayload.From(notJson).Value.ShouldBe(notJson);
        AuditPayload.From(new string('x', 2_000_000)).Value.Length.ShouldBe(2_000_000);
    }

    [Fact]
    public void An_audit_payload_is_stored_exactly_as_given()
    {
        const string json = "{\"actor\":\"kiosk-01\"}";

        AuditPayload.From(json).Value.ShouldBe(json);
    }

    [Fact]
    public void The_bound_matches_the_column_it_protects()
    {
        ActorUsername.MaximumLength.ShouldBe(255);
    }
}
