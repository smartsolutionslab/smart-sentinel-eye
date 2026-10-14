using System.Net;
using System.Net.Http.Json;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Infrastructure.KeycloakAdmin;

public sealed partial class HttpKeycloakAdminClient
{
    public async Task<IReadOnlyList<string>> GetEnrolledKioskClientIdsAsync(
        CancellationToken cancellationToken)
    {
        var realm = options.Value.Realm;

        using var response = await httpClient
            .GetAsync($"admin/realms/{realm}/clients", cancellationToken);
        response.EnsureSuccessStatusCode();

        var rows = await response.Content
            .ReadFromJsonAsync<ClientRow[]>(JsonOptions, cancellationToken) ?? [];

        // The same attribute enrolment stamps, read back. Deriving the set from
        // what enrolment writes is the point: a second naming convention would
        // drift, and a sweep that quietly matches less than it claims is worse
        // than no sweep.
        return rows
            .Where(row => row.Attributes is not null
                && row.Attributes.TryGetValue("sse.kind", out var kind)
                && kind == "kiosk")
            .Select(row => row.ClientId)
            .ToArray();
    }

    public async Task<bool> StripInheritedRealmRolesAsync(
        string clientId, CancellationToken cancellationToken)
    {
        Ensure.That(clientId).IsNotNull().IsNotNullOrWhiteSpace();
        var realm = options.Value.Realm;

        var clientUuid = await TryGetClientUuidAsync(realm, clientId, cancellationToken)
            ?? throw new KeycloakClientNotFoundException(clientId);

        return await StripInheritedRealmRolesAsync(realm, clientUuid, cancellationToken);
    }

    /// <summary>
    /// Spec 320 (#2181) plan §3 — one <c>GET /clients</c>, the same request
    /// <see cref="GetEnrolledKioskClientIdsAsync"/> makes. Every row carrying
    /// <c>sse.kind</c> is reported, whatever its value or its <c>enabled</c>
    /// flag — <see cref="OrphanedClientSweep"/> is the one that filters to
    /// the kinds and the state it acts on.
    /// </summary>
    public async Task<IReadOnlyList<StampedClient>> GetStampedClientsAsync(
        CancellationToken cancellationToken)
    {
        var realm = options.Value.Realm;

        using var response = await httpClient
            .GetAsync($"admin/realms/{realm}/clients", cancellationToken);
        response.EnsureSuccessStatusCode();

        var rows = await response.Content
            .ReadFromJsonAsync<ClientRow[]>(JsonOptions, cancellationToken) ?? [];

        return rows
            .Where(row => row.Attributes is not null
                && row.Attributes.TryGetValue("sse.kind", out var _))
            .Select(row => new StampedClient(row.ClientId, row.Attributes!["sse.kind"], row.Enabled))
            .ToArray();
    }

    /// <summary>
    /// Spec 320 (#2181) plan §3 — <c>createdTimestamp</c> on the client's
    /// service-account <b>user</b>, the only creation time Keycloak records
    /// for a client at all. <c>None</c> for a client Keycloak no longer has,
    /// a 404 on its service-account user, or a <c>createdTimestamp</c> that
    /// is absent — never treated as old by default. Any other failure
    /// throws.
    /// </summary>
    public async Task<Option<DateTimeOffset>> GetServiceAccountCreatedAtAsync(
        string clientId, CancellationToken cancellationToken)
    {
        Ensure.That(clientId).IsNotNull().IsNotNullOrWhiteSpace();
        var realm = options.Value.Realm;

        var clientUuid = await TryGetClientUuidAsync(realm, clientId, cancellationToken);
        if (clientUuid is null)
        {
            return Option<DateTimeOffset>.None;
        }

        using var response = await httpClient
            .GetAsync($"admin/realms/{realm}/clients/{clientUuid}/service-account-user", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return Option<DateTimeOffset>.None;
        }
        response.EnsureSuccessStatusCode();

        var user = await response.Content
            .ReadFromJsonAsync<ServiceAccountUser>(JsonOptions, cancellationToken);
        return user?.CreatedTimestamp is { } createdTimestamp
            ? Option<DateTimeOffset>.Some(DateTimeOffset.FromUnixTimeMilliseconds(createdTimestamp))
            : Option<DateTimeOffset>.None;
    }

    /// <summary>
    /// Removes the realm privileges an account inherited simply by being
    /// created, leaving it with only what it was given deliberately.
    ///
    /// <para>
    /// <b>The shape matters and is not obvious.</b> The assignment must be read
    /// back before it is removed: the delete only recognises role objects the
    /// realm reports as <i>directly</i> mapped to this account. A role fetched
    /// from the realm's own role list looks identical and produces a
    /// <c>404</c> — which reads exactly like a permissions problem and is not.
    /// </para>
    ///
    /// <para>
    /// Returns whether anything was removed. This method is the only place in the
    /// system that knows; spec 132 (#2169) stopped discarding it.
    /// </para>
    /// </summary>
    private async Task<bool> StripInheritedRealmRolesAsync(
        string realm, string clientUuid, CancellationToken cancellationToken)
    {
        using var saResponse = await httpClient
            .GetAsync($"admin/realms/{realm}/clients/{clientUuid}/service-account-user", cancellationToken);
        saResponse.EnsureSuccessStatusCode();
        var user = await saResponse.Content
            .ReadFromJsonAsync<ServiceAccountUser>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException(
                $"No service-account-user for Keycloak client {clientUuid}.");

        using var assignedResponse = await httpClient
            .GetAsync($"admin/realms/{realm}/users/{user.Id}/role-mappings/realm", cancellationToken);
        assignedResponse.EnsureSuccessStatusCode();
        var assigned = await assignedResponse.Content
            .ReadFromJsonAsync<RealmRoleRow[]>(JsonOptions, cancellationToken) ?? [];

        // Already stripped. Returning rather than sending an empty delete is
        // what makes a sweep safe to run on every startup.
        if (assigned.Length == 0)
        {
            return false;
        }

        using HttpRequestMessage remove = new(
            HttpMethod.Delete,
            $"admin/realms/{realm}/users/{user.Id}/role-mappings/realm")
        {
            Content = JsonContent.Create(assigned, options: JsonOptions),
        };
        using var removeResponse = await httpClient.SendAsync(remove, cancellationToken);
        removeResponse.EnsureSuccessStatusCode();
        return true;
    }
}
