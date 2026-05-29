using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using SmartSentinelEye.Shared.Kernel.Primitives;

namespace SmartSentinelEye.AuditObservability.Domain.Tests.AuditEvent;

public class EventIdentifierTests
{
    [Fact]
    public void From_rejects_Guid_Empty()
    {
        ArgumentException ex = Should.Throw<ArgumentException>(
            () => EventIdentifier.From(Guid.Empty));
        ex.ParamName.ShouldBe("value");
    }

    [Fact]
    public void From_round_trips_a_valid_Guid()
    {
        Guid raw = Guid.CreateVersion7();
        EventIdentifier id = EventIdentifier.From(raw);
        id.Value.ShouldBe(raw);
    }

    [Fact]
    public void Implements_IValueObject_of_Guid()
    {
        EventIdentifier id = EventIdentifier.From(Guid.CreateVersion7());
        id.ShouldBeAssignableTo<IValueObject<Guid>>();
    }

    [Fact]
    public void Two_instances_with_the_same_Guid_are_equal()
    {
        Guid raw = Guid.CreateVersion7();
        EventIdentifier.From(raw).ShouldBe(EventIdentifier.From(raw));
    }
}
