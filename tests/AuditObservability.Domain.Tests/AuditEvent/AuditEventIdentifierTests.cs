using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.AuditObservability.Domain.Tests.AuditEvent;

public class AuditEventIdentifierTests
{
    [Fact]
    public void New_returns_a_Guid_v7()
    {
        AuditEventIdentifier id = AuditEventIdentifier.New();
        id.Value.Version.ShouldBe(7);
    }

    [Fact]
    public void New_returns_a_monotonic_pair_within_the_same_tick()
    {
        AuditEventIdentifier first = AuditEventIdentifier.New();
        AuditEventIdentifier second = AuditEventIdentifier.New();
        first.Value.CompareTo(second.Value).ShouldBeLessThan(0);
    }

    [Fact]
    public void From_rejects_Guid_Empty()
    {
        ArgumentException ex = Should.Throw<ArgumentException>(
            () => AuditEventIdentifier.From(Guid.Empty));
        ex.ParamName.ShouldBe("value");
    }

    [Fact]
    public void From_round_trips_a_valid_Guid()
    {
        Guid raw = Guid.CreateVersion7();
        AuditEventIdentifier id = AuditEventIdentifier.From(raw);
        id.Value.ShouldBe(raw);
    }

    [Fact]
    public void Implements_IStronglyTypedId_of_Guid()
    {
        AuditEventIdentifier id = AuditEventIdentifier.New();
        id.ShouldBeAssignableTo<IStronglyTypedId<Guid>>();
    }

    [Fact]
    public void ToString_returns_the_underlying_Guid_string()
    {
        Guid raw = Guid.CreateVersion7();
        AuditEventIdentifier.From(raw).ToString().ShouldBe(raw.ToString());
    }
}
