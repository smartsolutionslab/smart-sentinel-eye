namespace SmartSentinelEye.Identity.Application.DTOs;

// Public, unlike its sibling: the Api's replay endpoints re-shape the rebuilt answer into the original wire shape.
public static class ReplayedClientMappings
{
    public static DeviceCredentialsDto ToDeviceCredentialsDto(
        this ReplayedClientDto registered,
        string deviceType,
        string deviceIdentifier) =>
        new(
            registered.RegisteredClientIdentifier,
            registered.ClientId,
            deviceType,
            deviceIdentifier,
            registered.Fab,
            registered.ClientSecret);

    public static KioskCredentialsDto ToKioskCredentialsDto(this ReplayedClientDto registered) =>
        new(
            registered.RegisteredClientIdentifier,
            registered.ClientId,
            registered.Fab,
            registered.ClientSecret);

    public static WebhookClientCredentialsDto ToWebhookClientCredentialsDto(
        this ReplayedClientDto registered,
        string integrationName) =>
        new(
            registered.RegisteredClientIdentifier,
            registered.Version,
            registered.ClientId,
            integrationName,
            registered.Fab,
            registered.ClientSecret);
}
