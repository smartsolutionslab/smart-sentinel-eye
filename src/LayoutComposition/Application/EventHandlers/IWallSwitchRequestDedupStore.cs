using SmartSentinelEye.LayoutComposition.Domain.Wall;

namespace SmartSentinelEye.LayoutComposition.Application.EventHandlers;

/// <summary>
/// Dedup store for <see cref="WallSceneSwitchRequestedV1Handler"/> (spec 296
/// FR-012, spec 007 FR-018 precedent). Wolverine's at-least-once outbox can
/// redeliver the same envelope, and an upstream re-run mints a fresh
/// <c>WallSceneSwitchRequestedV1</c> with a new
/// <c>Metadata.EventIdentifier</c> for the same decision — the durable inbox
/// catches the first case but not the second, so the
/// <c>(rule, causingEvent)</c> pair is the idempotency key. The rule is part
/// of it so that two different rules firing on one causing event both apply
/// (the #2214 lesson, restated for walls).
/// </summary>
public interface IWallSwitchRequestDedupStore
{
    /// <summary>
    /// Atomically reserves <paramref name="rule"/> + <paramref name="causingEvent"/>.
    /// Returns <see langword="true"/> the first time this pair is seen
    /// (proceed), <see langword="false"/> on a repeat (no-op).
    /// </summary>
    Task<bool> TryReserveAsync(
        RuleIdentifier rule, CausingEventIdentifier causingEvent, WallIdentifier wall,
        CancellationToken cancellationToken);
}
