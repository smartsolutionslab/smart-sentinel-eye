using Microsoft.Extensions.Logging;
using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.CQRS;
using SmartSentinelEye.Shared.Kernel;
using RegisteredClientAggregate = SmartSentinelEye.Identity.Domain.RegisteredClient.RegisteredClient;

namespace SmartSentinelEye.Identity.Application.Commands.Handlers;

public sealed class EnrollKioskCommandHandler(
    IRegisteredClientRepository clients,
    IKeycloakAdminClient keycloak,
    IClock clock,
    ILogger<EnrollKioskCommandHandler> logger)
    : ICommandHandler<EnrollKioskCommand, Result<KioskCredentialsDto, EnrollKioskError>>
{
    public async Task<Result<KioskCredentialsDto, EnrollKioskError>> HandleAsync(
        EnrollKioskCommand command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();
        (var name, var fab, var enrolledBy) = command;

        var clientIdResult = ToClientId(name, fab);
        if (clientIdResult.IsFailure)
        {
            return Failure(clientIdResult.Error);
        }

        var clientId = clientIdResult.Value;

        var existing = await clients
            .GetWithinFabAsync(fab, clientId, cancellationToken);
        if (existing.HasValue)
        {
            return Failure(EnrollKioskFailures.KioskAlreadyEnrolled(clientId.Value));
        }

        var created = await CreateInKeycloakAsync(KioskRepresentation(clientId, fab), fab, cancellationToken);
        if (created.IsFailure)
        {
            return Failure(created.Error);
        }

        var registered = RegisteredClientAggregate.Register(
            clientId, ClientKind.Kiosk, fab, enrolledBy, clock);
        clients.Add(registered);
        await clients.SaveAsync(cancellationToken);

        logger.EnrolledKiosk(registered.Id, clientId, fab);

        return Success(
            new KioskCredentialsDto(
                registered.Id.Value,
                clientId.Value,
                fab.Value,
                created.Value.ClientSecret));
    }

    private static Result<ClientId, EnrollKioskError> ToClientId(string name, FabIdentifier fab)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Failure(EnrollKioskFailures.InvalidName("must not be empty."));
        }

        if (!IsHyphenSeparatedAlphanumeric(name))
        {
            return Failure(EnrollKioskFailures.InvalidName(
                "each hyphen-separated segment must be one or more letters or digits."));
        }

        try
        {
            return Success(ClientId.From($"kiosk.{fab.Value}.{name}"));
        }
        catch (ArgumentException ex)
        {
            return Failure(EnrollKioskFailures.InvalidName(ex.Message));
        }
    }

    private static KeycloakClientRepresentation KioskRepresentation(ClientId clientId, FabIdentifier fab) =>
        new(
            ClientId: clientId.Value,
            Name: $"Kiosk {clientId.Value}",
            ServiceAccountsEnabled: true,
            StandardFlowEnabled: false,
            DirectAccessGrantsEnabled: false,
            PublicClient: false,
            DefaultClientScopes:
                [.. KeycloakScopeBundles.Kiosk, KeycloakScopeBundles.AudienceScope, KeycloakScopeBundles.GroupsScope],
            OptionalClientScopes: Array.Empty<string>(),
            Attributes: new Dictionary<string, string>
            {
                ["sse.kind"] = "kiosk",
                ["sse.fab"] = fab.Value,
            });

    private async Task<Result<KeycloakClientCredentials, EnrollKioskError>> CreateInKeycloakAsync(
        KeycloakClientRepresentation representation,
        FabIdentifier fab,
        CancellationToken cancellationToken)
    {
        try
        {
            return Success(await keycloak.CreateClientAsync(
                representation,
                fabGroupPath: $"/fabs/{fab.Value}",
                cancellationToken));
        }
        catch (KeycloakClientAlreadyExistsException ex)
        {
            return Failure(EnrollKioskFailures.KioskAlreadyEnrolled(ex.ClientId));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failure(EnrollKioskFailures.KeycloakUnavailable(ex.Message));
        }
    }

    private static bool IsHyphenSeparatedAlphanumeric(string name) =>
        name.Split('-').All(segment => segment.Length > 0 && segment.All(char.IsLetterOrDigit));
}
