using System.Linq.Expressions;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Identity.Application.DTOs;

internal static class RegisteredClientMappings
{
    // An expression, not a method: EF translates it into the SELECT, so the lambda must stay a lambda.
    public static Expression<Func<RegisteredClientAggregate, RegisteredClientSummaryDto>> ToSummaryDtoExpression() =>
        client => new RegisteredClientSummaryDto(
            client.Id.Value,
            client.Version,
            client.ClientId.Value,
            client.Kind.Value,
            client.Fab.Value,
            client.Registration.At,
            client.Registration.By.Value,
            client.DisabledAt == null ? null : (DateTimeOffset?)client.DisabledAt.Value,
            client.LastRotatedAt == null ? null : (DateTimeOffset?)client.LastRotatedAt.Value);

    public static DeviceCredentialsDto ToDeviceCredentialsDto(
        this RegisteredClientAggregate registered,
        string deviceType,
        string deviceIdentifier,
        string clientSecret) =>
        new(
            registered.Id.Value,
            registered.ClientId.Value,
            deviceType,
            deviceIdentifier,
            registered.Fab.Value,
            clientSecret);

    public static KioskCredentialsDto ToKioskCredentialsDto(
        this RegisteredClientAggregate registered,
        string clientSecret) =>
        new(
            registered.Id.Value,
            registered.ClientId.Value,
            registered.Fab.Value,
            clientSecret);

    public static WebhookClientCredentialsDto ToWebhookClientCredentialsDto(
        this RegisteredClientAggregate registered,
        ClientId clientId,
        string integrationName,
        FabIdentifier fab,
        string clientSecret) =>
        new(
            registered.Id.Value,
            registered.Version,
            clientId.Value,
            integrationName,
            fab.Value,
            clientSecret);

    public static ReplayedClientDto ToReplayedClientDto(
        this RegisteredClientAggregate registered,
        string clientSecret) =>
        new(
            registered.Id.Value,
            registered.Version,
            registered.ClientId.Value,
            registered.Fab.Value,
            clientSecret);
}
