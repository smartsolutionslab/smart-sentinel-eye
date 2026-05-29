namespace SmartSentinelEye.Identity.Application.DTOs;

public sealed record DeviceCredentialsDto(
    Guid RegisteredClientIdentifier,
    string ClientId,
    string DeviceType,
    string DeviceIdentifier,
    string Fab,
    string ClientSecret);
