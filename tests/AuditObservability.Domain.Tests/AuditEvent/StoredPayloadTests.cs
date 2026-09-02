using System.Text;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Domain.Tests.AuditEvent;

public class StoredPayloadTests
{
    private const string Multibyte = "{\"note\":\"Größenänderung\"}";

    [Fact]
    public void Derives_its_size_from_its_content()
    {
        StoredPayload payload = StoredPayload.From(Multibyte);

        payload.Content.Value.ShouldBe(Multibyte);
        payload.Size.Value.ShouldBe(Encoding.UTF8.GetByteCount(Multibyte));
    }

    [Fact]
    public void Counts_utf8_bytes_not_characters()
    {
        StoredPayload.From(Multibyte).Size.Value.ShouldBeGreaterThan(Multibyte.Length);
    }

    [Fact]
    public void Offers_no_public_way_to_supply_a_size()
    {
        // The invariant is structural, not asserted at runtime: if a public
        // constructor or factory taking a size ever appears, this fails.
        typeof(StoredPayload)
            .GetConstructors()
            .ShouldBeEmpty();

        typeof(StoredPayload)
            .GetMethods()
            .Where(method => method.IsStatic && method.Name == nameof(StoredPayload.From))
            .SelectMany(method => method.GetParameters())
            .ShouldAllBe(parameter => parameter.ParameterType == typeof(string));
    }

    [Fact]
    public void Refuses_a_null_content()
    {
        Should.Throw<ArgumentException>(() => StoredPayload.From(null!));
    }

    [Fact]
    public void Two_payloads_with_the_same_content_are_equal()
    {
        StoredPayload.From(Multibyte).ShouldBe(StoredPayload.From(Multibyte));
    }
}
