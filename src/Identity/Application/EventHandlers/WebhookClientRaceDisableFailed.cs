namespace SmartSentinelEye.Identity.Application.EventHandlers;

public sealed record WebhookClientRaceDisableFailed(string IntegrationName, string Fab);
