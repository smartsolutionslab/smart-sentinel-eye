using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Domain.RegisteredClient.Events;

public sealed record ClientDisabledDomainEvent(
    RegisteredClientIdentifier Client,
    ClientId ClientId,
    DateTimeOffset DisabledAt) : IDomainEvent;
