using SmartSentinelEye.AuditObservability.Domain.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Domain.Tests.AuditEvent;

public class PayloadSizeBytesTests
{
    [Fact]
    public void Accepts_a_measured_byte_count()
    {
        PayloadSizeBytes size = PayloadSizeBytes.From(42);
        size.Value.ShouldBe(42);
    }

    [Fact]
    public void Accepts_zero()
    {
        PayloadSizeBytes.From(0).Value.ShouldBe(0);
    }

    [Fact]
    public void Rejects_a_negative_count()
    {
        Should.Throw<ArgumentException>(() => PayloadSizeBytes.From(-1));
    }

    [Fact]
    public void Two_instances_with_the_same_count_are_equal()
    {
        PayloadSizeBytes.From(7).ShouldBe(PayloadSizeBytes.From(7));
    }

    [Fact]
    public void Of_measures_utf8_bytes_not_chars()
    {
        PayloadSizeBytes.Of("{\"note\":\"Größenänderung\"}")
            .Value.ShouldBe(System.Text.Encoding.UTF8.GetByteCount("{\"note\":\"Größenänderung\"}"));
    }
}
