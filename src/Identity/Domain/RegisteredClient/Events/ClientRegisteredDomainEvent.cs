using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Domain.RegisteredClient.Events;

public sealed record ClientRegisteredDomainEvent(
    RegisteredClientIdentifier Client,
    ClientId ClientId,
    ClientKind Kind,
    FabIdentifier Fab,
    DateTimeOffset RegisteredAt,
    OperatorIdentifier RegisteredBy) : IDomainEvent;
