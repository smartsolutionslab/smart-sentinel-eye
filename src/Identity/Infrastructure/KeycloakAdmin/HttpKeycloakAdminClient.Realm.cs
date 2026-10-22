using System.Net;
using System.Net.Http.Json;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Identity.Infrastructure.KeycloakAdmin;

public sealed partial class HttpKeycloakAdminClient
{
    /// <summary>
    /// Sub-groups one level under <paramref name="parentPath"/>, resolved
    /// through the same <c>group-by-path</c> lookup the service-account join
    /// already uses.
    ///
    /// <para>
    /// Newer Keycloak versions omit <c>subGroups</c> from the list response and
    /// require a second call per group, so an empty children array is followed
    /// up rather than believed. An unreachable realm throws (spec 019 FR-011) —
    /// it must never be reported as "no groups".
    /// </para>
    ///
    /// <para>
    /// This is the point where the wire's three answers become the contract's
    /// three (#2139). A 404 on <c>group-by-path</c> is the realm saying the
    /// group is not there, and it becomes <c>None</c> rather than an empty
    /// list; a 403 or a 5xx is the realm declining to say, and it keeps
    /// throwing. Translating here is what ADR-0141 exempts Infrastructure to
    /// do — the exemption is a licence to speak the wire's vocabulary on the
    /// way in, not a licence to hand it onward.
    /// </para>
    /// </summary>
    public async Task<Option<IReadOnlyList<string>>> GetSubGroupNamesAsync(
        string parentPath, CancellationToken cancellationToken)
    {
        Ensure.That(parentPath).IsNotNull().IsNotNullOrWhiteSpace();

        var realm = options.Value.Realm;

        using var response = await httpClient.GetAsync(
            $"admin/realms/{realm}/group-by-path/{parentPath.TrimStart('/')}", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            logger.FabGroupParentMissing(parentPath, realm);
            return Option<IReadOnlyList<string>>.None;
        }
        response.EnsureSuccessStatusCode();

        var parent = await response.Content
            .ReadFromJsonAsync<GroupRow>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Keycloak returned an empty body for group path '{parentPath}'.");

        if (parent.SubGroups is { Length: > 0 })
        {
            return Option<IReadOnlyList<string>>.Some(
                [.. parent.SubGroups.Select(child => child.Name)]);
        }

        var names = await ReadAllChildGroupNamesAsync(realm, parent.Id, cancellationToken);

        // Some, even when empty: the group answered, and "it has no children"
        // is that answer rather than the absence of one.
        return Option<IReadOnlyList<string>>.Some(names);
    }

    public Task RemoveRealmDefaultPrivilegeAsync(
        string privilege, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    private async Task<List<string>> ReadAllChildGroupNamesAsync(
        string realm,
        string parentId,
        CancellationToken cancellationToken)
    {
        // Paged explicitly, and read to exhaustion. Keycloak applies a server-side
        // default page size to this endpoint, so a single unpaged call silently
        // returns a prefix once there are enough sub-groups — and a fab missing
        // from that prefix is indistinguishable from a fab that does not exist.
        // For the caller that means no storage provisioned and no indication why,
        // which is the silent partial this feature exists to end.
        const int pageSize = 100;
        List<string> names = [];
        var lastPageReached = false;
        while (!lastPageReached)
        {
            using var children = await httpClient.GetAsync(
                $"admin/realms/{realm}/groups/{parentId}/children?first={names.Count}&max={pageSize}",
                cancellationToken);
            children.EnsureSuccessStatusCode();

            var rows = await children.Content
                .ReadFromJsonAsync<GroupRow[]>(JsonOptions, cancellationToken) ?? [];

            names.AddRange(rows.Select(child => child.Name));
            lastPageReached = rows.Length < pageSize;
        }

        return names;
    }
}
