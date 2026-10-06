namespace SmartSentinelEye.AuditObservability.Domain.AuditEvent;

/// <summary>
/// Whether a <c>*V1</c> integration event <b>type</b> carries a fab at all —
/// a property of the type, not of any one row (spec 306 / #2540).
///
/// <para>
/// <see cref="Owned"/> is the default and the fail-safe direction: an event
/// type nobody has classified is treated as fab-owned, so a forgotten
/// registration over-reports <see cref="FabAttribution.Unresolved"/> rather
/// than silently hiding an unattributed row as
/// <see cref="FabAttribution.NotApplicable"/> (SC-6).
/// </para>
/// </summary>
public enum FabScope
{
    /// <summary>The event type's subject has a fab; a null fab on a row of
    /// this type means resolution failed.</summary>
    Owned = 0,

    /// <summary>The event type's subject has no fab by construction (e.g.
    /// ADR-0115's overlay events, or spec 217 F1's retention-chunk
    /// announcement).</summary>
    Neutral = 1,
}
