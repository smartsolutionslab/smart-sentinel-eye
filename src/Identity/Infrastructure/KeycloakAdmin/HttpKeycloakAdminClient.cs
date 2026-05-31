using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;

namespace SmartSentinelEye.Identity.Infrastructure.KeycloakAdmin;

/// <summary>
/// Hand-rolled <see cref="IKeycloakAdminClient"/> implementation
/// against Keycloak's REST Admin API. No external SDK
/// dependency; uses a single <see cref="HttpClient"/> wired
/// through DI + the cached <see cref="KeycloakAdminTokenProvider"/>.
///
/// <para>
/// Idempotency notes:
/// <list type="bullet">
/// <item><c>CreateClientAsync</c> probes for an existing client
/// with the same <c>clientId</c> and throws
/// <see cref="KeycloakClientAlreadyExistsException"/> on hit, so
/// the handler can surface a typed 409 instead of an opaque 4xx.</item>
/// <item><c>DisableClientAsync</c> on an unknown client is a
/// silent no-op (we cannot un-create what was never created).</item>
/// </list>
/// </para>
/// </summary>
public sealed class HttpKeycloakAdminClient(
    HttpClient httpClient,
    KeycloakAdminTokenProvider tokenProvider,
    IOptions<KeycloakAdminOptions> options,
    ILogger<HttpKeycloakAdminClient> logger) : IKeycloakAdminClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    public async Task<KeycloakClientCredentials> CreateClientAsync(
        KeycloakClientRepresentation representation,
        string fabGroupPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(representation);

        string realm = options.Value.Realm;
        await AuthorizeAsync(cancellationToken).ConfigureAwait(false);

        // Existence probe — Keycloak's create endpoint returns 409
        // on duplicate, but we want a typed exception either way.
        string? existing = await TryGetClientUuidAsync(realm, representation.ClientId, cancellationToken)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            throw new KeycloakClientAlreadyExistsException(representation.ClientId);
        }

        HttpRequestMessage create = new(HttpMethod.Post, $"admin/realms/{realm}/clients")
        {
            Content = JsonContent.Create(representation, options: JsonOptions),
        };
        HttpResponseMessage createResponse = await httpClient.SendAsync(create, cancellationToken)
            .ConfigureAwait(false);
        if (createResponse.StatusCode == HttpStatusCode.Conflict)
        {
            throw new KeycloakClientAlreadyExistsException(representation.ClientId);
        }
        createResponse.EnsureSuccessStatusCode();

        string? clientUuid = await TryGetClientUuidAsync(realm, representation.ClientId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Keycloak accepted POST /clients but no client with clientId='{representation.ClientId}' is visible.");

        // Attach the service-account user to the fab group so the
        // `groups` claim carries `/fabs/<fabId>` (FR-003).
        await AssignServiceAccountToGroupAsync(
            realm, clientUuid, fabGroupPath, cancellationToken).ConfigureAwait(false);

        // Read the just-minted secret.
        return await ReadClientSecretAsync(realm, clientUuid, cancellationToken).ConfigureAwait(false);
    }

    public async Task<KeycloakClientCredentials> RotateClientSecretAsync(
        string clientId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        string realm = options.Value.Realm;
        await AuthorizeAsync(cancellationToken).ConfigureAwait(false);

        string clientUuid = await TryGetClientUuidAsync(realm, clientId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new KeycloakClientNotFoundException(clientId);

        HttpResponseMessage response = await httpClient
            .PostAsync($"admin/realms/{realm}/clients/{clientUuid}/client-secret",
                content: null, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        ClientCredentialPayload payload = await response.Content
            .ReadFromJsonAsync<ClientCredentialPayload>(JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Keycloak returned an empty rotate-secret response for clientId='{clientId}'.");
        return new KeycloakClientCredentials(payload.Value);
    }

    public async Task DisableClientAsync(string clientId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        string realm = options.Value.Realm;
        await AuthorizeAsync(cancellationToken).ConfigureAwait(false);

        string? clientUuid = await TryGetClientUuidAsync(realm, clientId, cancellationToken)
            .ConfigureAwait(false);
        if (clientUuid is null)
        {
            Log.DisableClientNoOp(logger, clientId);
            return;
        }

        HttpRequestMessage update = new(HttpMethod.Put, $"admin/realms/{realm}/clients/{clientUuid}")
        {
            Content = JsonContent.Create(new { enabled = false }, options: JsonOptions),
        };
        HttpResponseMessage response = await httpClient.SendAsync(update, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    private async Task AuthorizeAsync(CancellationToken cancellationToken)
    {
        string token = await tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task<string?> TryGetClientUuidAsync(
        string realm, string clientId, CancellationToken cancellationToken)
    {
        string url = $"admin/realms/{realm}/clients?clientId={Uri.EscapeDataString(clientId)}";
        HttpResponseMessage response = await httpClient.GetAsync(url, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        ClientRow[] rows = await response.Content
            .ReadFromJsonAsync<ClientRow[]>(JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? Array.Empty<ClientRow>();
        return rows.Length == 0 ? null : rows[0].Id;
    }

    private async Task AssignServiceAccountToGroupAsync(
        string realm, string clientUuid, string groupPath, CancellationToken cancellationToken)
    {
        // Fetch the service-account user behind the client.
        HttpResponseMessage saResponse = await httpClient
            .GetAsync($"admin/realms/{realm}/clients/{clientUuid}/service-account-user", cancellationToken)
            .ConfigureAwait(false);
        saResponse.EnsureSuccessStatusCode();
        ServiceAccountUser? user = await saResponse.Content
            .ReadFromJsonAsync<ServiceAccountUser>(JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"No service-account-user for Keycloak client {clientUuid}.");

        // Resolve the group id by path.
        string lookupUrl = $"admin/realms/{realm}/group-by-path/{groupPath.TrimStart('/')}";
        HttpResponseMessage groupResponse = await httpClient.GetAsync(lookupUrl, cancellationToken)
            .ConfigureAwait(false);
        groupResponse.EnsureSuccessStatusCode();
        GroupRow? group = await groupResponse.Content
            .ReadFromJsonAsync<GroupRow>(JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                $"Keycloak group '{groupPath}' not found; create it before registering clients in this fab.");

        HttpResponseMessage joinResponse = await httpClient.PutAsync(
            $"admin/realms/{realm}/users/{user.Id}/groups/{group.Id}",
            content: null, cancellationToken).ConfigureAwait(false);
        joinResponse.EnsureSuccessStatusCode();
    }

    private async Task<KeycloakClientCredentials> ReadClientSecretAsync(
        string realm, string clientUuid, CancellationToken cancellationToken)
    {
        HttpResponseMessage response = await httpClient
            .GetAsync($"admin/realms/{realm}/clients/{clientUuid}/client-secret", cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        ClientCredentialPayload payload = await response.Content
            .ReadFromJsonAsync<ClientCredentialPayload>(JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "Keycloak returned an empty client-secret response.");
        return new KeycloakClientCredentials(payload.Value);
    }

    private sealed record ClientRow(string Id, string ClientId);

    private sealed record ServiceAccountUser(string Id);

    private sealed record GroupRow(string Id, string Path);

    private sealed record ClientCredentialPayload(string Type, string Value);
}
