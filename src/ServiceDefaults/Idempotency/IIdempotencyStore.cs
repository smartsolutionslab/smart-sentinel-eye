using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.ServiceDefaults.Idempotency;

/// <summary>What a <see cref="IIdempotencyStore.BeginAsync"/> found.</summary>
public enum IdempotencyOutcome
{
    /// <summary>First arrival — the caller owns the reservation and should do the work.</summary>
    Reserved,

    /// <summary>An earlier attempt holds the key and has not finished yet.</summary>
    InProgress,

    /// <summary>An earlier attempt finished; its result identifier is carried alongside.</summary>
    Completed,

    /// <summary>
    /// Spec 302 (#2424/#2492) — a stored row with this <c>(key, endpoint,
    /// caller)</c> exists, but its fab or fingerprint differs from the
    /// scope's (or the stored fingerprint is <c>NULL</c>, a legacy row that
    /// fails closed). Reported whether the row is completed or still
    /// in-progress — a mismatch is refused immediately, never waited on.
    /// </summary>
    Mismatched,
}

/// <summary>
/// The reservation a <see cref="IIdempotencyStore.BeginAsync"/> claim won —
/// the only thing entitled to complete or release it (#2491).
///
/// <para>
/// <b>Uniqueness rests on two facts elsewhere, not on anything this type
/// enforces itself.</b> Every successful claim writes <c>reserved_at =
/// NOW()</c> — the first insert and a reclaiming conflict action alike — so
/// <see cref="ReservedAt"/> changes on every takeover and cannot collide with
/// the value the previous holder saw. If either stops being true —
/// <c>reserved_at</c> stops being stamped by every claim, or
/// <see cref="IdempotencyReclamation.StaleAfter"/> shrinks below the
/// resolution a reclaim and its predecessor's read can land in — the fence
/// this record carries is unsound and this comment is the place that would
/// need correcting.
/// </para>
/// </summary>
public sealed record IdempotencyClaim(IdempotencyScope Scope, DateTime ReservedAt);

/// <summary>
/// The result of claiming a key. <see cref="ResourceIdentifier"/> is populated
/// only for <see cref="IdempotencyOutcome.Completed"/>; <see cref="Claim"/>
/// only for <see cref="IdempotencyOutcome.Reserved"/>.
/// </summary>
public sealed record IdempotencyReservation(
    IdempotencyOutcome Outcome, Option<Guid> ResourceIdentifier, Option<IdempotencyClaim> Claim)
{
    public static IdempotencyReservation ReservedAs(IdempotencyClaim claim) =>
        new(IdempotencyOutcome.Reserved, Option<Guid>.None, Option<IdempotencyClaim>.Some(claim));

    public static IdempotencyReservation InProgress { get; } =
        new(IdempotencyOutcome.InProgress, Option<Guid>.None, Option<IdempotencyClaim>.None);

    public static IdempotencyReservation CompletedWith(Guid resourceIdentifier) =>
        new(IdempotencyOutcome.Completed, Option<Guid>.Some(resourceIdentifier), Option<IdempotencyClaim>.None);

    /// <summary>
    /// Spec 302 (#2424/#2492) — this caller's key is bound to a different
    /// request. No identifier to replay (the row is not this caller's answer)
    /// and no claim (the row was never this caller's to complete or release).
    /// </summary>
    public static IdempotencyReservation Mismatched { get; } =
        new(IdempotencyOutcome.Mismatched, Option<Guid>.None, Option<IdempotencyClaim>.None);
}

/// <summary>
/// Per-context durable record of which idempotency keys have been claimed
/// (ADR-0142). Implemented against each context's own schema, like the
/// Wolverine outbox and <c>variable_value_request_dedup</c> — there is no shared
/// database to put it in.
///
/// <para>
/// <b>Reserve then complete, rather than insert-if-absent.</b> A single
/// <c>INSERT ... ON CONFLICT DO NOTHING</c> answers "has this key been seen",
/// which is enough for message dedup and not enough here: the retry that
/// motivates the whole mechanism arrives <i>while the first attempt is still
/// running</i>, because being slow is why it was retried. A store that cannot
/// tell in-progress from completed replays nothing in exactly that window.
/// </para>
///
/// <para>
/// Nothing sensitive is stored. The row holds the scope and the created
/// resource's identifier — never a response body, never a secret. A replay
/// rebuilds its answer from the identifier.
/// </para>
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Claims <paramref name="scope"/> atomically, reporting whether this caller
    /// won the claim, is racing an unfinished attempt, or is repeating a
    /// finished one.
    /// </summary>
    Task<IdempotencyReservation> BeginAsync(IdempotencyScope scope, CancellationToken cancellationToken);

    /// <summary>
    /// Records the identifier the work produced, turning a reservation into a
    /// replayable answer. A no-op if <paramref name="claim"/> is no longer the
    /// row's current holder — reclaimed by another caller, or swept — so a late
    /// call from an overtaken attempt cannot overwrite the new holder's answer
    /// (#2491).
    /// </summary>
    Task CompleteAsync(IdempotencyClaim claim, Guid resourceIdentifier, CancellationToken cancellationToken);

    /// <summary>
    /// Drops an unfinished reservation so a later attempt can retry.
    ///
    /// <para>
    /// Without this a request that failed or was cancelled would wedge its key
    /// as permanently in-progress, and every retry — the thing the mechanism
    /// exists to serve — would be refused for as long as the row survived.
    /// </para>
    ///
    /// <para>
    /// A no-op if <paramref name="claim"/> is no longer the row's current
    /// holder, for the same reason as <see cref="CompleteAsync"/>: a late
    /// release from an overtaken attempt must not delete the new holder's live
    /// reservation (#2491).
    /// </para>
    /// </summary>
    Task ReleaseAsync(IdempotencyClaim claim, CancellationToken cancellationToken);
}
