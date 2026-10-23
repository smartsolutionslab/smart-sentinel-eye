namespace SmartSentinelEye.Identity.Infrastructure.KeycloakAdmin;

public sealed partial class HttpKeycloakAdminClient
{
    private sealed record ClientRow(string Id, string ClientId, bool Enabled, Dictionary<string, string>? Attributes);

    private sealed record RealmRoleRow(string Id, string Name);

    private sealed record ServiceAccountUser(string Id, long? CreatedTimestamp);

    // Name and SubGroups are new for spec 019's sub-group read; the
    // group-by-path lookup in AssignServiceAccountToGroupAsync uses Id alone and is unaffected by the extra
    // members, which simply stay null when Keycloak does not send them.
    private sealed record GroupRow(string Id, string Name, string Path, GroupRow[]? SubGroups);

    private sealed record RealmRow(DefaultRoleRow? DefaultRole);

    private sealed record DefaultRoleRow(string? Name);

    private sealed record ClientCredentialPayload(string Type, string Value);
}
