using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Queries.Handlers;

/// <summary>
/// Rebuilds a device registration's answer for an idempotent replay (ADR-0142):
/// the registration from this context, the secret from Keycloak.
///
/// <para>
/// Nothing about the original response was stored, so this is a reconstruction
/// rather than a cache read. That is what keeps a plaintext secret out of our
/// database while still letting a retry receive the credentials its own first
/// attempt earned.
/// </para>
/// </summary>
public sealed class ReplayDeviceRegistrationQueryHandler(
    IRegisteredClientRepository clients,
    IKeycloakAdminClient keycloak)
{
    public async Task<Result<DeviceCredentialsDto, ReplayRegistrationError>> HandleAsync(
        ReplayDeviceRegistrationQuery query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        var (client, deviceType, deviceIdentifier) = query;

        Option<RegisteredClient> found = await clients.GetByIdentifierAsync(client, cancellationToken);

        if (!found.HasValue)
        {
            return Result<DeviceCredentialsDto, ReplayRegistrationError>.Failure(
                new ReplayedRegistrationMissing(client.Value));
        }

        RegisteredClient registered = found.Value;

        KeycloakClientCredentials credentials;
        try
        {
            credentials = await keycloak.ReadClientSecretAsync(registered.ClientId.Value, cancellationToken);
        }
        catch (KeycloakClientNotFoundException)
        {
            // Refused rather than re-created. Registering again here would give
            // the caller a working answer and quietly leave two registrations
            // behind one key.
            return Result<DeviceCredentialsDto, ReplayRegistrationError>.Failure(
                new ReplayedClientMissingInKeycloak(registered.ClientId.Value));
        }

        return Result<DeviceCredentialsDto, ReplayRegistrationError>.Success(
            new DeviceCredentialsDto(
                registered.Id.Value,
                registered.ClientId.Value,
                deviceType,
                deviceIdentifier,
                registered.Fab.Value,
                credentials.ClientSecret));
    }
}
