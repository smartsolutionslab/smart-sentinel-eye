using SmartSentinelEye.EventIngestion.Domain.DeadLetter;
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
}
