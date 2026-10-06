namespace SmartSentinelEye.AuditObservability.Domain.AuditEvent;

/// <summary>
/// States what a null <see cref="AuditEvent.Fab"/> on a given row means
/// (spec 306 / #2540). Set once, in <see cref="AuditEvent.From"/>, from the
/// envelope's <see cref="FabScope"/> and whether a fab was actually carried.
///
/// <para>
/// <b>Invariant:</b> <see cref="Resolved"/> if and only if
/// <see cref="AuditEvent.Fab"/> is not null. A fab is evidence and is never
/// discarded or relabelled because the event type is registered
/// <see cref="FabScope.Neutral"/> (SC-5).
/// </para>
/// </summary>
public enum FabAttribution
{
    /// <summary>The envelope carried a fab.</summary>
    Resolved = 0,

    /// <summary>No fab, and the event type is fab-owned — the subject has a
    /// fab that nobody resolved.</summary>
    Unresolved = 1,

    /// <summary>No fab, and the event type is registered fab-neutral — the
    /// subject genuinely has no fab dimension.</summary>
    NotApplicable = 2,
}
