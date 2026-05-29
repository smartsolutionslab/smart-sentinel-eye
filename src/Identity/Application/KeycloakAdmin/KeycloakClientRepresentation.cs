namespace SmartSentinelEye.Identity.Application.KeycloakAdmin;

/// <summary>
/// Subset of Keycloak's Admin API <c>ClientRepresentation</c>
/// shape that Identity actually exchanges. JSON property names
/// match Keycloak's wire format (<c>System.Text.Json</c> uses
/// the property names directly; the
/// <c>HttpKeycloakAdminClient</c> serialisation context handles
/// camelCase as needed via <c>JsonSerializerOptions</c>).
/// </summary>
public sealed record KeycloakClientRepresentation(
    string ClientId,
    string Name,
    bool ServiceAccountsEnabled,
    bool StandardFlowEnabled,
    bool DirectAccessGrantsEnabled,
    bool PublicClient,
    IReadOnlyList<string> DefaultClientScopes,
    IReadOnlyList<string> OptionalClientScopes,
    IReadOnlyDictionary<string, string> Attributes);

/// <summary>
/// Group representation Identity reads to discover the
/// <c>/fabs/&lt;fabId&gt;</c> group id needed when attaching a
/// service-account user to the fab group.
/// </summary>
public sealed record KeycloakGroupRepresentation(
    string Id,
    string Name,
    string Path);

/// <summary>
/// Outcome of a successful <c>CreateClientAsync</c> /
/// <c>RotateClientSecretAsync</c> call: the just-issued
/// plaintext client secret.
/// </summary>
public sealed record KeycloakClientCredentials(string ClientSecret);
