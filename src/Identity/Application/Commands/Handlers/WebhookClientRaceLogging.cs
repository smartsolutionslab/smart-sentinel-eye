using SmartSentinelEye.Identity.Domain.RegisteredClient;

namespace SmartSentinelEye.Identity.Application.Commands.Handlers;

internal sealed record WebhookClientRaceLogging(
    Action<string, ClientId> Disabled,
    Action<string, ClientId> AlreadyDisabled,
    Action<Exception, string, ClientId> CouldNotDisable);
