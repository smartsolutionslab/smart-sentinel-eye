using SmartSentinelEye.Identity.Domain.RegisteredClient;

namespace SmartSentinelEye.Identity.Application.EventHandlers;

internal sealed record WebhookClientDisablingLogging(
    Action<Exception, string, FabIdentifier> InvalidName,
    Action<string, FabIdentifier> NothingToDisable,
    Action<string, FabIdentifier, string, string> Failed);
