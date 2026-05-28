using SmartSentinelEye.EventIngestion.Domain.DeadLetter;

namespace SmartSentinelEye.EventIngestion.Infrastructure.Persistence;

public sealed class DeadLetterRepository(EventIngestionDbContext dbContext) : IDeadLetterRepository
{
    public void Add(DeadLetter deadLetter)
    {
        ArgumentNullException.ThrowIfNull(deadLetter);
        dbContext.DeadLetters.Add(deadLetter);
    }

    public Task SaveAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
