using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.Shared.CQRS;

namespace SmartSentinelEye.Identity.Application.Tests.Fakes;

/// <summary>
/// Stands in for <c>OutboxTransactionalCommit</c> sharing the SAME scoped
/// <c>DbContext</c> as <paramref name="repository"/>'s own <c>SaveAsync</c> —
/// unlike <see cref="NoOpTransactionalCommit"/>, which has no relationship to
/// the repository at all and so cannot see a failure that happened there.
///
/// <para>
/// A <see cref="DbUpdateConcurrencyException"/> from an earlier
/// <c>SaveAsync</c> leaves its entity tracked as <c>Modified</c> with its
/// stale, pre-conflict values — EF does not discard changes on failure — so
/// a later <c>SaveChanges</c> against that same context replays the
/// identical failing UPDATE. This fake reproduces exactly that: once
/// <see cref="InMemoryRegisteredClientRepository.HasUnresolvedConcurrencyFailure"/>
/// is set, every <see cref="CommitAsync"/> call re-throws, the same way
/// <c>OutboxTransactionalCommit</c>'s underlying <c>SaveChangesAndFlushMessagesAsync</c>
/// would in production (#2628 BL1).
/// </para>
/// </summary>
public sealed class RepositoryBackedTransactionalCommit(InMemoryRegisteredClientRepository repository)
    : ITransactionalCommit
{
    public int Commits { get; private set; }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Commits++;
        if (repository.HasUnresolvedConcurrencyFailure)
        {
            throw new DbUpdateConcurrencyException(
                "The stale entity from the earlier failed SaveAsync is still tracked as Modified; "
                + "SaveChanges on the same DbContext replays the identical failing UPDATE.");
        }

        return Task.CompletedTask;
    }
}
