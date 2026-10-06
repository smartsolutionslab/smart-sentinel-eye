using SmartSentinelEye.AuditObservability.Application.EventHandlers;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using SmartSentinelEye.Shared.Contracts.AuditObservability;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;
using SmartSentinelEye.Shared.Contracts.StreamDistribution;

namespace SmartSentinelEye.AuditObservability.Application.Tests.EventHandlers;

/// <summary>
/// Spec 306 (#2540), plan.md §3/§8.2 — pins the register's exact contents.
/// Adding a neutral type should be a visible test edit, not a silent
/// addition; the fail-safe default (<see cref="FabScope.Owned"/>) bounds the
/// cost if a new neutral publisher is forgotten here.
/// </summary>
public class FabNeutralEventsTests
{
    [Fact]
    public void The_register_names_exactly_the_three_publishers_that_cannot_carry_a_fab()
    {
        FabNeutralEvents.NeutralTypes.Count.ShouldBe(3);
        FabNeutralEvents.NeutralTypes.ShouldContain(typeof(OverlayRevisionPublishedV3));
        FabNeutralEvents.NeutralTypes.ShouldContain(typeof(OverlayRevisionArchivedV1));
        FabNeutralEvents.NeutralTypes.ShouldContain(typeof(AuditChunkArchivedV1));
    }

    [Fact]
    public void Stream_health_is_fab_owned()
    {
        FabNeutralEvents.ScopeOf(typeof(StreamHealthChangedV1)).ShouldBe(FabScope.Owned);
    }

    [Fact]
    public void An_unregistered_type_is_fab_owned()
    {
        // Fail-safe direction (SC-6): a type nobody classified over-reports
        // (Unresolved) rather than being silently treated as neutral.
        FabNeutralEvents.ScopeOf(typeof(object)).ShouldBe(FabScope.Owned);
    }
}
