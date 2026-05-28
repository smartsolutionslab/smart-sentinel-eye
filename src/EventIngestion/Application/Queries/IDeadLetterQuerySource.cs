using SmartSentinelEye.EventIngestion.Domain.DeadLetter;

namespace SmartSentinelEye.EventIngestion.Application.Queries;

public interface IDeadLetterQuerySource
{
    IQueryable<DeadLetter> DeadLetters { get; }
}
