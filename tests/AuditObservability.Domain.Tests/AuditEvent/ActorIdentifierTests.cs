using SmartSentinelEye.AuditObservability.Domain.AuditEvent;

namespace SmartSentinelEye.AuditObservability.Domain.Tests.AuditEvent;

public class ActorIdentifierTests
{
    [Fact]
    public void From_accepts_any_non_empty_Guid()
    {
        Guid raw = Guid.CreateVersion7();
        ActorIdentifier id = ActorIdentifier.From(raw);
        id.Value.ShouldBe(raw);
        id.IsSystem.ShouldBeFalse();
    }

    [Fact]
    public void From_rejects_Guid_Empty_for_a_human_actor()
    {
        Should.Throw<ArgumentException>(() => ActorIdentifier.From(Guid.Empty));
    }

    [Fact]
    public void System_singleton_wraps_Guid_Empty_and_reports_IsSystem()
    {
        ActorIdentifier.System.Value.ShouldBe(Guid.Empty);
        ActorIdentifier.System.IsSystem.ShouldBeTrue();
    }

    [Fact]
    public void Equality_treats_the_underlying_Guid_as_the_identity()
    {
        Guid raw = Guid.CreateVersion7();
        ActorIdentifier.From(raw).ShouldBe(ActorIdentifier.From(raw));
    }

    [Fact]
    public void System_compares_equal_to_System()
    {
        ActorIdentifier.System.ShouldBe(ActorIdentifier.System);
    }
}
