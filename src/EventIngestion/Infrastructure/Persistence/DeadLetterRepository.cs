using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Domain.DeadLetter;
using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence;

/// <summary>
/// A dead letter announces nothing — no domain event, no integration event — so
/// spec 021's guarantee has nothing to protect here.
///
/// <para>
/// It commits through the same seam as every other repository anyway. The
/// alternative was an exemption in the architecture rule that keeps the
/// guarantee true (FR-007), and an exemption list is a thing that rots: the
/// next repository added by copying this one would inherit the exemption
/// without inheriting the reason. Flushing an empty outbox costs nothing, and
/// if this aggregate ever does raise an event it is already correct.
/// </para>
/// </summary>
public sealed class DeadLetterRepository(
    EventIngestionDbContext dbContext,
    ITransactionalCommit commit) : IDeadLetterRepository
{
    public void Add(DeadLetter deadLetter)
    {
        Ensure.That(deadLetter).IsNotNull();
        dbContext.DeadLetters.Add(deadLetter);
    }

    public Task SaveAsync(CancellationToken cancellationToken) =>
        commit.CommitAsync(cancellationToken);

    /// <summary>
    /// Promotes every <see cref="HoldState.Held"/>,
    /// <see cref="DeadLetterReason.UnknownEventType"/> row for
    /// <paramref name="fab"/> and <paramref name="kind"/> as one set-based
    /// <c>UPDATE</c> (spec 317, #2325, FR-007, A7) — never by loading rows:
    /// a chatty source can hold millions of rows for one kind in an hour.
    /// The predicate's <c>reason</c> and <c>state</c> terms are the
    /// invariant (plan.md §3.3): a <c>Refused</c> row for the same
    /// <c>(fab, kind)</c>, or a row already <c>Promoted</c>, is untouched.
    /// </summary>
    public Task<int> PromoteHeldAsync(FabIdentifier fab, Kind kind, CancellationToken cancellationToken) =>
        dbContext.DeadLetters
            .Where(deadLetter =>
                deadLetter.Fab == fab
                && deadLetter.Kind == kind
                && deadLetter.Reason == DeadLetterReason.UnknownEventType
                && deadLetter.State == HoldState.Held)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(deadLetter => deadLetter.State, HoldState.Promoted),
                cancellationToken);
}
