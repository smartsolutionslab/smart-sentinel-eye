using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.AuditObservability.Domain.Tests.AuditEvent;

public class AuditEventIdentifierTests
{
    [Fact]
    public void New_returns_a_Guid_v7()
    {
        var id = AuditEventIdentifier.New();
        id.Value.Version.ShouldBe(7);
    }

    [Fact]
    public void New_returns_distinct_values()
    {
        // Guid v7 places a millisecond timestamp in the high bits
        // and random data in the low ones — back-to-back New()
        // calls always produce distinct values, but they aren't
        // strictly monotonic within the same millisecond because
        // the random suffix can shift either way. The handler-side
        // cursor pagination doesn't rely on strict ordering across
        // ties; distinctness is the only guarantee the Domain
        // promises.
        var first = AuditEventIdentifier.New();
        var second = AuditEventIdentifier.New();
        first.Value.ShouldNotBe(second.Value);
    }

    [Fact]
    public void From_rejects_Guid_Empty()
    {
        var ex = Should.Throw<ArgumentException>(
            () => AuditEventIdentifier.From(Guid.Empty));
        ex.ParamName.ShouldBe("value");
    }

    [Fact]
    public void From_round_trips_a_valid_Guid()
    {
        var raw = Guid.CreateVersion7();
        var id = AuditEventIdentifier.From(raw);
        id.Value.ShouldBe(raw);
    }

    [Fact]
    public void Implements_IStronglyTypedId_of_Guid()
    {
        var id = AuditEventIdentifier.New();
        id.ShouldBeAssignableTo<IStronglyTypedId<Guid>>();
    }

    [Fact]
    public void ToString_returns_the_underlying_Guid_string()
    {
        var raw = Guid.CreateVersion7();
        AuditEventIdentifier.From(raw).ToString().ShouldBe(raw.ToString());
    }
}
