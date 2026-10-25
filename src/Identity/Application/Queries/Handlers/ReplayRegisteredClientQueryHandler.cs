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
/// <b>What a replay promises.</b> An unbound replay (devices, kiosks) returns the
/// client's present credentials, which equal the original answer whenever nothing
/// intervened. A replay bound to the version its answer carried (spec 454) is
/// refused instead once the client has changed at all: the original secret was
/// invalidated by whatever replaced it, and handing back the replacement would
/// give the retry a credential it never earned. The version is checked before
/// the Keycloak read, so a superseded key reads no secret, and again after it,
/// because a rotation commits its version bump before it rolls the secret — a
/// secret newer than the answer implies a bump the second check can see.
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

        var (client, answeredAt) = query;

        var found = await clients.GetByIdentifierAsync(client, cancellationToken);

        if (!found.HasValue)
        {
            return Result<ReplayedClientDto, ReplayRegistrationError>.Failure(
                new ReplayedRegistrationMissing(client.Value));
        }

        var registered = found.Value;

        if (answeredAt.HasValue && registered.Version != answeredAt.Value)
        {
            return Superseded(client);
        }

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

        if (answeredAt.HasValue)
        {
            // No tracking: the entity read above would hand back its stale version
            // through identity resolution. The secret read earlier is dropped here.
            var current = await clients.GetVersionAsync(client, cancellationToken);

            if (!current.HasValue || current.Value != answeredAt.Value)
            {
                return Superseded(client);
            }
        }

        return Result<ReplayedClientDto, ReplayRegistrationError>.Success(
            new ReplayedClientDto(
                registered.Id.Value,
                registered.Version,
                registered.ClientId.Value,
                registered.Fab.Value,
                credentials.ClientSecret));
    }

    private static Result<ReplayedClientDto, ReplayRegistrationError> Superseded(
        RegisteredClientIdentifier client) =>
        Result<ReplayedClientDto, ReplayRegistrationError>.Failure(new ReplayedKeySuperseded(client.Value));
}
