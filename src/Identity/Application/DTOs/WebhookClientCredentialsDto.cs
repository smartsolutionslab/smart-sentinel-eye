namespace SmartSentinelEye.Identity.Application.DTOs;

public sealed record WebhookClientCredentialsDto(
    Guid RegisteredClientIdentifier,
    string ClientId,
    string IntegrationName,
    string Fab,
    string ClientSecret);
