using System.Net;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.ServiceDefaults;

namespace SmartSentinelEye.MigrationRunner;

/// <summary>
/// Takes <c>offline_access</c> out of the realm's default composite on every boot
/// (ADR-0181), so no account created by any route inherits the credential that
/// never expires. The realm file cannot do it (declaring the composite makes
/// Keycloak bind a decoy), and a failure here must never be swallowed: a realm that
/// still hands the privilege out must not start the system.
/// </summary>
internal sealed class RealmDefaultPrivilegeMigrator(
    IKeycloakAdminClient keycloak,
    ILogger<RealmDefaultPrivilegeMigrator> logger) : IMigrator
{
    // The wall displays keep this by name (ADR-0131); every other account must not
    // receive it by merely existing.
    private const string OfflineAccess = "offline_access";

    public string ContextName => "Identity realm default privileges";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await keycloak.RemoveRealmDefaultPrivilegeAsync(OfflineAccess, cancellationToken);
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException(
                "Keycloak refused to narrow the realm's default roles (403). The imported realm may predate the "
                + "manage-realm grant on migration-runner; drop the keycloak-data volume so the realm file is "
                + "imported afresh (ADR-0181).",
                exception);
        }

        logger.RealmDefaultPrivilegeRemoved(OfflineAccess);
    }
}
