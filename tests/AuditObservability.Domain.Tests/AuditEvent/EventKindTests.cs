using SmartSentinelEye.AuditObservability.Domain.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Domain.Tests.AuditEvent;

public class EventKindTests
{
    [Theory]
    [InlineData("CameraRegisteredV1")]
    [InlineData("RuleArchivedV1")]
    [InlineData("AuditChunkArchivedV1")]
    public void Accepts_valid_kinds(string input)
    {
        EventKind kind = EventKind.From(input);
        kind.Value.ShouldBe(input);
    }

    [Fact]
    public void Rejects_empty()
    {
        Should.Throw<ArgumentException>(() => EventKind.From(""));
    }

    [Fact]
    public void Rejects_whitespace()
    {
        Should.Throw<ArgumentException>(() => EventKind.From("   "));
    }

    [Fact]
    public void Rejects_a_string_longer_than_the_maximum()
    {
        Should.Throw<ArgumentException>(() =>
            EventKind.From(new string('A', EventKind.MaximumLength + 1)));
    }

    [Theory]
    [InlineData("1starts-with-digit")]
    [InlineData("contains space")]
    [InlineData("contains-dash")]
    [InlineData("contains.dot")]
    public void Rejects_strings_that_do_not_match_the_C_sharp_identifier_shape(string bad)
    {
        Should.Throw<ArgumentException>(() => EventKind.From(bad));
    }

    [Fact]
    public void Two_instances_with_the_same_string_are_equal()
    {
        EventKind.From("CameraRegisteredV1").ShouldBe(EventKind.From("CameraRegisteredV1"));
    }
}
