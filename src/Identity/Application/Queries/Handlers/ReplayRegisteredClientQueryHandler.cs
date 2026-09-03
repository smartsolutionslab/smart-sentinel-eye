using SmartSentinelEye.Identity.Application.DTOs;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Identity.Domain.RegisteredClient;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Application.Queries.Handlers;

/// <summary>
/// Rebuilds the server-held half of a credential answer for an idempotent replay
/// (ADR-0142): the registration from this context, the secret from Keycloak.
///
/// <para>
/// A reconstruction rather than a cache read — nothing about the original
/// response was stored. That is what keeps a plaintext secret out of our
/// database while still letting a retry receive the credentials its own first
/// attempt earned.
/// </para>
///
/// <para>
/// <b>It reads the secret; it never rotates it.</b> Rotating on a replay would
/// hand the retry a different secret and silently invalidate the one the first
/// attempt already delivered — the caller would end up holding a credential the
/// server had just replaced.
/// </para>
///
/// <para>
/// <b>What a replay does not promise.</b> If something else changed this client
/// between the original attempt and the retry, the version and secret returned
/// here are the current ones rather than the originals. That is deliberate: the
/// original secret has been invalidated by whatever replaced it, so reproducing
/// it faithfully would hand back a credential that no longer works. The honest
/// framing is that a replay returns the client's present credentials, and is
/// identical to the original answer in every case where nothing intervened.
/// </para>
/// </summary>
public sealed class ReplayRegisteredClientQueryHandler(
    IRegisteredClientRepository clients,
    IKeycloakAdminClient keycloak)
{
    public async Task<Result<ReplayedClientDto, ReplayRegistrationError>> HandleAsync(
        ReplayRegisteredClientQuery query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        Option<RegisteredClient> found = await clients.GetByIdentifierAsync(query.Client, cancellationToken);

        if (!found.HasValue)
        {
            return Result<ReplayedClientDto, ReplayRegistrationError>.Failure(
                new ReplayedRegistrationMissing(query.Client.Value));
        }

        RegisteredClient registered = found.Value;

        KeycloakClientCredentials credentials;
        try
        {
            credentials = await keycloak.ReadClientSecretAsync(registered.ClientId.Value, cancellationToken);
        }
        catch (KeycloakClientNotFoundException)
        {
            // Refused rather than re-created. Minting again here would give the
            // caller a working answer and quietly leave two registrations behind
            // one key.
            return Result<ReplayedClientDto, ReplayRegistrationError>.Failure(
                new ReplayedClientMissingInKeycloak(registered.ClientId.Value));
        }

        return Result<ReplayedClientDto, ReplayRegistrationError>.Success(
            new ReplayedClientDto(
                registered.Id.Value,
                registered.Version,
                registered.ClientId.Value,
                registered.Fab.Value,
                credentials.ClientSecret));
    }
}
