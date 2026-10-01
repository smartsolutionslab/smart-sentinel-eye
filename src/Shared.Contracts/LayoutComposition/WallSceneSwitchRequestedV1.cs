namespace SmartSentinelEye.Shared.Contracts.LayoutComposition;

/// <summary>
/// Integration event raised by Automation (spec 296, ADR-0157 §2) when a
/// matched <c>SwitchWallScene</c> rule fires, asking LayoutComposition to
/// switch <paramref name="Wall"/> to <paramref name="Target"/>.
///
/// <para>
/// <paramref name="Target"/> is <c>"Next"</c> or <c>"Layout"</c>;
/// <paramref name="TargetLayout"/> is set if and only if
/// <paramref name="Target"/> is <c>"Layout"</c> — the same flat-shape
/// convention <c>WallSceneChangedV1</c> uses for its own
/// <c>Cause</c>/<c>Rule</c> pair, rather than a polymorphic payload.
/// <paramref name="Rule"/> is the firing rule's identifier
/// (<c>CompiledRule.Identifier</c>); <paramref name="CausingEventIdentifier"/>
/// is the <c>FabEventIngestedV1.EventIdentifier</c> that triggered it. Both
/// are carried for FR-012's dedup key and for audit correlation.
/// <see cref="EventMetadata.Actor"/> is <see langword="null"/> — a rule, not
/// an operator, requested this.
/// </para>
/// </summary>
public sealed record WallSceneSwitchRequestedV1(
    Guid Wall,
    string Target,
    Guid? TargetLayout,
    Guid Rule,
    DateTimeOffset RequestedAt,
    Guid CausingEventIdentifier,
    EventMetadata Metadata) : IIntegrationEvent
{
    /// <summary>Wire literal for <see cref="Target"/> meaning "the next scene in the wall's ordered set".</summary>
    public const string NextTarget = "Next";

    /// <summary>Wire literal for <see cref="Target"/> meaning "the layout named by <see cref="TargetLayout"/>".</summary>
    public const string LayoutTarget = "Layout";
}
