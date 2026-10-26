namespace SmartSentinelEye.Identity.Application.DTOs;

public sealed record DeviceCredentialsDto(
    Guid RegisteredClientIdentifier,
    string ClientId,
    string DeviceType,
    string DeviceIdentifier,
    string Fab,
    string ClientSecret)
{
    // The generated ToString prints every member, and ClientSecret is plaintext.
    public override string ToString() => nameof(DeviceCredentialsDto);
}
