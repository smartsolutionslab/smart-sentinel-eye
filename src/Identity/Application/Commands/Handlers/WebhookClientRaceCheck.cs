using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Commands.Handlers;

internal readonly record struct WebhookClientRaceCheck(
    ClientId ClientId,
    FabIdentifier Fab,
    string IntegrationName,
    bool DisablesOnUnverifiable,
    WebhookClientRaceLogging Logging);
