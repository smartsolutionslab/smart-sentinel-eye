using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.ServiceDefaults;

namespace SmartSentinelEye.MigrationRunner;

internal sealed class RealmDefaultPrivilegeMigrator(
    IKeycloakAdminClient keycloak,
    ILogger<RealmDefaultPrivilegeMigrator> logger) : IMigrator
{
    public string ContextName => "Identity realm default privileges";

    public Task RunAsync(CancellationToken cancellationToken)
    {
        _ = (keycloak, logger);
        throw new NotImplementedException();
    }
}
