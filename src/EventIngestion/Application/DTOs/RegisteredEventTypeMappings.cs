using SmartSentinelEye.EventIngestion.Domain.RegisteredEventType;

namespace SmartSentinelEye.EventIngestion.Application.DTOs;

internal static class RegisteredEventTypeMappings
{
    public static RegisteredEventTypeDto ToDto(this RegisteredEventType eventType) =>
        new(
            eventType.Id.Value,
            eventType.Fab.Value,
            eventType.Kind.Value,
            eventType.State.Value,
            eventType.Registration.RegisteredAt,
            eventType.Registration.RegisteredBy,
            eventType.Version);
}
