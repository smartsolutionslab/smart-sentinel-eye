using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Infrastructure.KeycloakAdmin;

/// <summary>
/// Hand-rolled <see cref="IKeycloakAdminClient"/> implementation
/// against Keycloak's REST Admin API. No external SDK dependency.
/// The bearer token is attached per request by
/// <see cref="KeycloakAdminAuthorizationHandler"/> rather than by this class,
/// so nothing here touches a credential.
///
/// <para>
/// Idempotency notes:
/// <list type="bullet">
/// <item><c>CreateClientAsync</c> probes for an existing client
/// with the same <c>clientId</c>. A hit throws
/// <see cref="KeycloakClientAlreadyExistsException"/> unless the found
/// client is disabled, stamped with the same <c>sse.kind</c> (and
/// <c>sse.fab</c>, where the representation carries one) as the one being
/// created — only that narrow case is a previously-disabled registration
/// being replaced, and it is deleted and recreated instead of refused
/// (#2728).</item>
/// <item><c>DisableClientAsync</c> on an unknown client is a
/// silent no-op (we cannot un-create what was never created).</item>
/// </list>
/// </para>
/// </summary>
public sealed partial class HttpKeycloakAdminClient(
    HttpClient httpClient,
    IOptions<KeycloakAdminOptions> options,
    ILogger<HttpKeycloakAdminClient> logger) : IKeycloakAdminClient
{
    // WhenWritingNull, not WhenWritingDefault. The wider condition dropped every
    // property equal to its type's default — so every `false` — and an omitted
    // flag is not an off flag: Keycloak applies its own default, which for
    // `standardFlowEnabled` is `true`, and which left the disable PUT below
    // sending `{}` and the client enabled (issues #2165, #2207). Nothing this
    // client serialises has a nullable member, so narrowing rather than removing
    // costs nothing today and keeps absent meaning absent if one is added.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task<KeycloakClientCredentials> CreateClientAsync(
        KeycloakClientRepresentation representation,
        string fabGroupPath,
        CancellationToken cancellationToken)
    {
        Ensure.That(representation).IsNotNull();

        var realm = options.Value.Realm;

        await RefuseOrReplaceExistingAsync(realm, representation, cancellationToken);

        using HttpRequestMessage create = new(HttpMethod.Post, $"admin/realms/{realm}/clients")
        {
            Content = JsonContent.Create(representation, options: JsonOptions),
        };
        using var createResponse = await httpClient.SendAsync(create, cancellationToken);
        if (createResponse.StatusCode == HttpStatusCode.Conflict)
        {
            throw new KeycloakClientAlreadyExistsException(representation.ClientId);
        }
        createResponse.EnsureSuccessStatusCode();

        var clientUuid = await TryGetClientUuidAsync(realm, representation.ClientId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Keycloak accepted POST /clients but no client with clientId='{representation.ClientId}' is visible.");

        try
        {
            // Attach the service-account user to the fab group so the
            // `groups` claim carries `/fabs/<fabId>` (FR-003).
            await AssignServiceAccountToGroupAsync(
                realm, clientUuid, fabGroupPath, cancellationToken);

            // **Take back what the realm gave it for free** (spec 052). Keycloak
            // grants every account created after import a default composite that
            // includes the privilege to mint credentials which never expire, so
            // each kiosk is born holding it. Removing it here rather than later
            // is what keeps the window one call wide.
            //
            // The answer is discarded on purpose: an account created moments ago
            // always holds the composite, so it carries no information here. The
            // sweep is where it is the report (spec 132).
            _ = await StripInheritedRealmRolesAsync(realm, clientUuid, cancellationToken);

            // Read the just-minted secret.
            return await ReadClientSecretAsync(realm, clientUuid, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // **The client exists but is not properly set up, so it must not
            // survive.** Leaving it behind fails the caller *and* blocks every
            // retry — the existence probe in RefuseOrReplaceExistingAsync would answer "already enrolled"
            // for a client that was never finished, while its account keeps the
            // privilege this step exists to remove.
            await TryDeleteClientAsync(
                realm, representation.ClientId, clientUuid, cancellationToken);
            throw;
        }
    }

    private async Task RefuseOrReplaceExistingAsync(
        string realm,
        KeycloakClientRepresentation representation,
        CancellationToken cancellationToken)
    {
        // Existence probe — Keycloak's create endpoint returns 409 on
        // duplicate, but we want a typed exception either way.
        // DisableClientAsync never deletes the Keycloak-side client, only
        // flips `enabled`, so a previously disabled registration's clientId
        // is still here on re-registration. That narrow case — disabled, and
        // stamped with the same sse.kind (and sse.fab, where we have one) as
        // the client being created now — is deleted and recreated rather
        // than refused forever (#2728).
        //
        // Everything else conflicts: an active client (including a realm
        // infrastructure client such as `management-web`, which carries no
        // `sse.kind` at all and so never matches) is never deleted, and
        // neither is a disabled client of a different kind or a different
        // fab. A security review of the first cut of this fix found that an
        // unconditional delete-on-hit let any caller holding
        // `sse.identity.kiosks.write` take down an arbitrary realm client by
        // naming it as the clientId of a kiosk enrolment.
        var conflicting = await TryGetClientRowAsync(
            realm, representation.ClientId, cancellationToken);
        if (conflicting is not null)
        {
            if (!IsReplaceableDisabledClient(conflicting, representation))
            {
                throw new KeycloakClientAlreadyExistsException(representation.ClientId);
            }

            logger.ReplacedDisabledClient(representation.ClientId, conflicting.Id);
            await TryDeleteClientAsync(
                realm, representation.ClientId, conflicting.Id, cancellationToken);
        }
    }

    public async Task<KeycloakClientCredentials> RotateClientSecretAsync(
        string clientId, CancellationToken cancellationToken)
    {
        Ensure.That(clientId).IsNotNull().IsNotNullOrWhiteSpace();
        var realm = options.Value.Realm;

        var clientUuid = await TryGetClientUuidAsync(realm, clientId, cancellationToken)
            ?? throw new KeycloakClientNotFoundException(clientId);

        using var response = await httpClient
            .PostAsync($"admin/realms/{realm}/clients/{clientUuid}/client-secret",
                content: null, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content
            .ReadFromJsonAsync<ClientCredentialPayload>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Keycloak returned an empty rotate-secret response for clientId='{clientId}'.");
        return new KeycloakClientCredentials(payload.Value);
    }

    public async Task<KeycloakClientCredentials> ReadClientSecretAsync(
        string clientId, CancellationToken cancellationToken)
    {
        Ensure.That(clientId).IsNotNull().IsNotNullOrWhiteSpace();
        var realm = options.Value.Realm;

        var clientUuid = await TryGetClientUuidAsync(realm, clientId, cancellationToken)
            ?? throw new KeycloakClientNotFoundException(clientId);

        return await ReadClientSecretAsync(realm, clientUuid, cancellationToken);
    }

    public async Task DisableClientAsync(string clientId, CancellationToken cancellationToken)
    {
        Ensure.That(clientId).IsNotNull().IsNotNullOrWhiteSpace();
        var realm = options.Value.Realm;

        var clientUuid = await TryGetClientUuidAsync(realm, clientId, cancellationToken);
        if (clientUuid is null)
        {
            logger.DisableClientNoOp(clientId);
            return;
        }

        using HttpRequestMessage update = new(HttpMethod.Put, $"admin/realms/{realm}/clients/{clientUuid}")
        {
            Content = JsonContent.Create(new { enabled = false }, options: JsonOptions),
        };
        using var response = await httpClient.SendAsync(update, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<string?> TryGetClientUuidAsync(
        string realm, string clientId, CancellationToken cancellationToken) =>
        (await TryGetClientRowAsync(realm, clientId, cancellationToken))?.Id;

    private async Task<ClientRow?> TryGetClientRowAsync(
        string realm, string clientId, CancellationToken cancellationToken)
    {
        var url = $"admin/realms/{realm}/clients?clientId={Uri.EscapeDataString(clientId)}";
        using var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var rows = await response.Content
            .ReadFromJsonAsync<ClientRow[]>(JsonOptions, cancellationToken)
            ?? [];
        return rows.Length == 0 ? null : rows[0];
    }

    /// <summary>
    /// Whether a client Keycloak already has under this clientId is the
    /// narrow, intended replacement case — a previously disabled
    /// registration — rather than a genuine conflict (#2728).
    ///
    /// <para>
    /// All three callers of <c>CreateClientAsync</c>
    /// (<c>RegisterDeviceCommandHandler</c>, <c>EnrollKioskCommandHandler</c>,
    /// <c>RotateWebhookClientCommandHandler</c>) stamp <c>sse.kind</c> on
    /// every client they create, so a found client carrying no <c>sse.kind</c>
    /// at all — any realm infrastructure client, such as
    /// <c>management-web</c> or <c>identity-admin</c> — never matches and is
    /// never a candidate for deletion, however it was named. Matching
    /// <c>sse.fab</c> too (where both sides have one) keeps one fab's
    /// disabled client from being replaced by another fab's registration
    /// that happens to resolve to the same clientId.
    /// </para>
    /// </summary>
    private static bool IsReplaceableDisabledClient(
        ClientRow found, KeycloakClientRepresentation representation)
    {
        if (found.Enabled || found.Attributes is null)
        {
            return false;
        }

        if (!found.Attributes.TryGetValue("sse.kind", out var foundKind)
            || !representation.Attributes.TryGetValue("sse.kind", out var newKind)
            || foundKind != newKind)
        {
            return false;
        }

        var foundHasFab = found.Attributes.TryGetValue("sse.fab", out var foundFab);
        var newHasFab = representation.Attributes.TryGetValue("sse.fab", out var newFab);
        return foundHasFab == newHasFab && foundFab == newFab;
    }

    private async Task AssignServiceAccountToGroupAsync(
        string realm, string clientUuid, string groupPath, CancellationToken cancellationToken)
    {
        // Fetch the service-account user behind the client.
        using var saResponse = await httpClient
            .GetAsync($"admin/realms/{realm}/clients/{clientUuid}/service-account-user", cancellationToken);
        saResponse.EnsureSuccessStatusCode();
        var user = await saResponse.Content
            .ReadFromJsonAsync<ServiceAccountUser>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException(
                $"No service-account-user for Keycloak client {clientUuid}.");

        // Resolve the group id by path.
        var lookupUrl = $"admin/realms/{realm}/group-by-path/{groupPath.TrimStart('/')}";
        using var groupResponse = await httpClient.GetAsync(lookupUrl, cancellationToken);
        groupResponse.EnsureSuccessStatusCode();
        var group = await groupResponse.Content
            .ReadFromJsonAsync<GroupRow>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Keycloak group '{groupPath}' not found; create it before registering clients in this fab.");

        using var joinResponse = await httpClient.PutAsync(
            $"admin/realms/{realm}/users/{user.Id}/groups/{group.Id}",
            content: null, cancellationToken);
        joinResponse.EnsureSuccessStatusCode();
    }

    private async Task<KeycloakClientCredentials> ReadClientSecretAsync(
        string realm, string clientUuid, CancellationToken cancellationToken)
    {
        using var response = await httpClient
            .GetAsync($"admin/realms/{realm}/clients/{clientUuid}/client-secret", cancellationToken);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content
            .ReadFromJsonAsync<ClientCredentialPayload>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException(
                "Keycloak returned an empty client-secret response.");
        return new KeycloakClientCredentials(payload.Value);
    }

    /// <summary>
    /// Removes the client an interrupted enrolment left half-made. Best effort
    /// in that it never throws — the caller is already failing, and its error
    /// is the one that explains why compensation was needed at all — but not
    /// silent: what it could not remove is reported (spec 122, #2166).
    ///
    /// <para>
    /// <b>The kiosk-privilege startup sweep is not the backstop this used to
    /// name.</b> <c>KioskPrivilegeSweep</c> takes a residue's realm roles away
    /// and leaves the client, and its enrolled-kiosk query cannot tell a
    /// residue from a healthy kiosk. So a client that survives here survives
    /// that sweep, and the existence probe in <c>CreateClientAsync</c> keeps
    /// answering already-enrolled for it. <b>A surviving client does not wait
    /// on a human forever any more</b>: once its service account is older than
    /// <c>OrphanedClientSweep.GraceWindow</c> (spec 320, #2181), that sweep
    /// disables it, which hands the next registration or enrolment attempt
    /// for the same clientId #2728's replace path. Until then, a retry
    /// collides.
    /// </para>
    /// </summary>
    private async Task TryDeleteClientAsync(
        string realm, string clientId, string clientUuid, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient
                .DeleteAsync($"admin/realms/{realm}/clients/{clientUuid}", cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return;
            }

            // 404 is Keycloak reporting no such client, which is the outcome
            // this call wanted. Reporting a residue for it would send someone
            // hunting a client that is not there.
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                logger.HalfEnrolledClientWasAlreadyAbsent(clientId, clientUuid);
                return;
            }

            logger.HalfEnrolledClientSurvived(clientId, clientUuid, response.StatusCode);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.CouldNotRemoveHalfEnrolledClient(clientId, clientUuid, exception);
        }
    }
}
