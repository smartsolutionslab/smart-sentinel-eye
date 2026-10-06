using System.Collections.Frozen;
using SmartSentinelEye.AuditObservability.Domain.AuditEvent;
using SmartSentinelEye.Shared.Contracts.AuditObservability;
using SmartSentinelEye.Shared.Contracts.OverlayDesigner;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.AuditObservability.Application.EventHandlers;

/// <summary>
/// Spec 306 (#2540), plan.md §3. The single register of <c>*V1</c> types
/// that are fab-neutral by construction — their subject has no fab
/// dimension, so a null fab on their audit row is
/// <see cref="FabAttribution.NotApplicable"/>, not
/// <see cref="FabAttribution.Unresolved"/>.
///
/// <para>
/// Lives beside <see cref="V1ResourceMap"/> — the same kind of per-V1-type
/// knowledge — but is a separate type: resource pivot and fab scope are
/// independent, and folding them would make every <see cref="V1ResourceMap"/>
/// hand-tweak touch fab semantics too.
/// </para>
///
/// <para>
/// <b>Fail-safe default.</b> A type absent from <see cref="NeutralTypes"/>
/// resolves to <see cref="FabScope.Owned"/> via <see cref="ScopeOf"/> — never
/// to <see cref="FabScope.Neutral"/>. Forgetting to register a neutral type
/// over-reports <see cref="FabAttribution.Unresolved"/>; it never hides an
/// unattributed fab-owned row as <see cref="FabAttribution.NotApplicable"/>
/// (SC-6).
/// </para>
/// </summary>
internal static class FabNeutralEvents
{
    /// <summary>
    /// The three publishers that cannot carry a fab:
    /// <see cref="OverlayRevisionPublishedV3"/> and
    /// <see cref="OverlayRevisionArchivedV1"/> (overlays are fab-neutral,
    /// ADR-0115), and <see cref="AuditChunkArchivedV1"/> (spec 217 F1 —
    /// time-only hypertable partitioning).
    /// </summary>
    public static IReadOnlySet<Type> NeutralTypes { get; } = new HashSet<Type>
    {
        typeof(OverlayRevisionPublishedV3),
        typeof(OverlayRevisionArchivedV1),
        typeof(AuditChunkArchivedV1),
    }.ToFrozenSet();

    /// <summary>
    /// The <see cref="FabScope"/> for a given integration event's runtime
    /// type. An unregistered type resolves to <see cref="FabScope.Owned"/>
    /// (the fail-safe direction).
    /// </summary>
    public static FabScope ScopeOf(Type integrationEventType)
    {
        Ensure.That(integrationEventType).IsNotNull();

        return NeutralTypes.Contains(integrationEventType) ? FabScope.Neutral : FabScope.Owned;
    }
}
