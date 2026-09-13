using SmartSentinelEye.EventIngestion.Domain.Event;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;

public interface IRegisteredEventTypeRepository
{
    Task<Option<RegisteredEventType>> GetRegisteredAsync(
        FabIdentifier fab, Kind kind, CancellationToken cancellationToken);

    void Add(RegisteredEventType eventType);

    Task SaveAsync(CancellationToken cancellationToken);
}
