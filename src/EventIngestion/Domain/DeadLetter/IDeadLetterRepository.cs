using SmartSentinelEye.EventIngestion.Domain.Event;

namespace SmartSentinelEye.EventIngestion.Domain.DeadLetter;

public interface IDeadLetterRepository
{
    void Add(DeadLetter deadLetter);

    Task SaveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Marks every <see cref="HoldState.Held"/>,
    /// <see cref="DeadLetterReason.UnknownEventType"/> row for
    /// <paramref name="fab"/> and <paramref name="kind"/> as
    /// <see cref="HoldState.Promoted"/>, as one set-based update (spec 317,
    /// #2325, FR-007, A7 — never by loading rows). Returns the number of rows
    /// promoted.
    /// </summary>
    Task<int> PromoteHeldAsync(FabIdentifier fab, Kind kind, CancellationToken cancellationToken);
}
