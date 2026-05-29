using SmartSentinelEye.AuditObservability.Domain.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Domain.Tests.AuditEvent;

public class ResourceIdentifierTests
{
    [Fact]
    public void Accepts_a_short_string()
    {
        ResourceIdentifier id = ResourceIdentifier.From("rule-pilot");
        id.Value.ShouldBe("rule-pilot");
    }

    [Fact]
    public void Accepts_a_string_at_the_maximum_length()
    {
        string maxLength = new('a', ResourceIdentifier.MaximumLength);
        ResourceIdentifier.From(maxLength).Value.ShouldBe(maxLength);
    }

    [Fact]
    public void Rejects_empty()
    {
        Should.Throw<ArgumentException>(() => ResourceIdentifier.From(""));
    }

    [Fact]
    public void Rejects_a_string_longer_than_the_maximum()
    {
        Should.Throw<ArgumentException>(() =>
            ResourceIdentifier.From(new string('a', ResourceIdentifier.MaximumLength + 1)));
    }

    [Fact]
    public void Two_instances_with_the_same_string_are_equal()
    {
        ResourceIdentifier.From("x").ShouldBe(ResourceIdentifier.From("x"));
    }
}
