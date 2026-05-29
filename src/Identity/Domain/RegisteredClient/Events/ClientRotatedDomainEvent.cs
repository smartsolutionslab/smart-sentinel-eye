using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Domain.RegisteredClient.Events;

public sealed record ClientRotatedDomainEvent(
    RegisteredClientIdentifier Client,
    ClientId ClientId,
    DateTimeOffset RotatedAt) : IDomainEvent;
