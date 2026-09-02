using SmartSentinelEye.AuditObservability.Domain.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Domain.Tests.AuditEvent;

public class SchemaVersionTests
{
    [Fact]
    public void Accepts_a_stamped_version()
    {
        SchemaVersion version = SchemaVersion.From(3);
        version.Value.ShouldBe((short)3);
    }

    [Fact]
    public void Rejects_a_negative_version()
    {
        Should.Throw<ArgumentException>(() => SchemaVersion.From(-1));
    }

    [Fact]
    public void Current_is_the_version_this_build_stamps()
    {
        SchemaVersion.Current.Value.ShouldBe((short)1);
    }

    [Fact]
    public void Two_instances_with_the_same_version_are_equal()
    {
        SchemaVersion.From(2).ShouldBe(SchemaVersion.From(2));
    }
}
