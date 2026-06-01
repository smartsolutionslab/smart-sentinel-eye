using SmartSentinelEye.EventIngestion.Domain.DeadLetter;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence;

public sealed class DeadLetterRepository(EventIngestionDbContext dbContext) : IDeadLetterRepository
{
    public void Add(DeadLetter deadLetter)
    {
        Ensure.That(deadLetter).IsNotNull();
        dbContext.DeadLetters.Add(deadLetter);
    }

    public Task SaveAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
