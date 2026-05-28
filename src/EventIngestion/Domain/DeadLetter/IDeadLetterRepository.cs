namespace SmartSentinelEye.EventIngestion.Domain.DeadLetter;

public interface IDeadLetterRepository
{
    void Add(DeadLetter deadLetter);

    Task SaveAsync(CancellationToken cancellationToken);
}
