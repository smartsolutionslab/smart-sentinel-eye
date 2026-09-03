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
}

/// <summary>
/// The result of claiming a key. <see cref="ResourceIdentifier"/> is populated
/// only for <see cref="IdempotencyOutcome.Completed"/>.
/// </summary>
public sealed record IdempotencyReservation(IdempotencyOutcome Outcome, Option<Guid> ResourceIdentifier)
{
    public static IdempotencyReservation Reserved { get; } =
        new(IdempotencyOutcome.Reserved, Option<Guid>.None);

    public static IdempotencyReservation InProgress { get; } =
        new(IdempotencyOutcome.InProgress, Option<Guid>.None);

    public static IdempotencyReservation CompletedWith(Guid resourceIdentifier) =>
        new(IdempotencyOutcome.Completed, Option<Guid>.Some(resourceIdentifier));
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
    /// replayable answer.
    /// </summary>
    Task CompleteAsync(IdempotencyScope scope, Guid resourceIdentifier, CancellationToken cancellationToken);

    /// <summary>
    /// Drops an unfinished reservation so a later attempt can retry.
    ///
    /// <para>
    /// Without this a request that failed or was cancelled would wedge its key
    /// as permanently in-progress, and every retry — the thing the mechanism
    /// exists to serve — would be refused for as long as the row survived.
    /// </para>
    /// </summary>
    Task ReleaseAsync(IdempotencyScope scope, CancellationToken cancellationToken);
}
