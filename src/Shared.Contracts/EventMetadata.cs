namespace SmartSentinelEye.Shared.Contracts;

/// <summary>
/// Common metadata carried by every <see cref="IIntegrationEvent"/>
/// (ADR-0102). Read uniformly by the AuditObservability subscriber to
/// record one audit row per event; useful for tracing + replay beyond
/// audit.
///
/// <para>
/// Primitive types are used at the wire boundary per ADR-0040;
/// value-object types stay inside their owning context.
/// </para>
/// </summary>
/// <param name="EventIdentifier">Guid v7, stable per logical event — the audit idempotency key.</param>
/// <param name="OccurredAt">When the originating domain action happened.</param>
/// <param name="Fab">Owning fab when the event is fab-scoped; otherwise <see langword="null"/>.</param>
/// <param name="Actor">Acting principal when known; otherwise <see langword="null"/>.</param>
/// <param name="RootIngestedAt">
/// When the plant-floor event at the root of this causal chain was accepted by
/// EventIngestion; <see langword="null"/> when this event has no such root, which
/// is most of them.
///
/// <para>
/// <b>Why this is not <see cref="OccurredAt"/>.</b> That field means "when the
/// thing this event describes happened", and on a downstream event that is
/// correctly the moment the downstream decision was made. Overloading it would
/// make one field mean two things depending on which message you were holding,
/// which is how a measurement quietly becomes wrong (spec 025 FR-002).
/// </para>
///
/// <para>
/// <b>Why it exists.</b> The constitution budgets <c>event → overlay state</c>
/// at 200 ms (§IV) and §VII requires an implemented leg to be measured. That leg
/// runs from acceptance to effect, and no single service sees both ends: the
/// acceptance moment is known at ingestion and was previously discarded when
/// Automation published downstream. Carrying it is what makes the leg
/// measurable at the point the effect lands.
/// </para>
///
/// <para>
/// <b>Optional on purpose, and additive both ways.</b> Every existing
/// construction site compiles untouched, a message serialised before this field
/// existed deserialises with it null, and a consumer that predates it ignores an
/// unknown property. So it is not a breaking change under ADR-0073 and needs no
/// <c>V2</c> — established by inspection rather than assumed (spec 025 FR-011).
/// A null here means "not measurable", never "instant": see FR-005.
/// </para>
/// </param>
public sealed record EventMetadata(
    Guid EventIdentifier,
    DateTimeOffset OccurredAt,
    string? Fab,
    Guid? Actor,
    DateTimeOffset? RootIngestedAt = null);
