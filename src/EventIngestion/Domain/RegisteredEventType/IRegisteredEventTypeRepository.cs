using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;

public interface IRegisteredEventTypeRepository
{
    Task<Option<RegisteredEventType>> GetRegisteredAsync(
        FabIdentifier fab, Kind kind, CancellationToken cancellationToken);

    /// <summary>
    /// The plural-fabs overload is what makes a retire against another fab's
    /// row a 404 fall out of the lookup rather than out of a branch (plan.md
    /// §2): a row the caller cannot see is genuinely absent.
    /// </summary>
    Task<Option<RegisteredEventType>> GetRegisteredAsync(
        IReadOnlyList<FabIdentifier> fabs, Kind kind, CancellationToken cancellationToken);

    void Add(RegisteredEventType eventType);

    Task SaveAsync(CancellationToken cancellationToken);
}
