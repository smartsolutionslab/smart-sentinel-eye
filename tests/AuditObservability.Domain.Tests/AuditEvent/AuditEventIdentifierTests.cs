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
    public void New_returns_distinct_monotonically_non_decreasing_values()
    {
        // Guid v7 is monotonic at millisecond resolution per the
        // BCL contract. Back-to-back New() calls can land in the
        // same millisecond (then strictly increasing) or in
        // different ones (then also strictly increasing thanks
        // to the timestamp); the assertion accepts both.
        AuditEventIdentifier first = AuditEventIdentifier.New();
        AuditEventIdentifier second = AuditEventIdentifier.New();
        first.Value.ShouldNotBe(second.Value);
        first.Value.CompareTo(second.Value).ShouldBeLessThanOrEqualTo(0);
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
