using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.MigrationRunner.Tests.Fakes;
using SmartSentinelEye.ServiceDefaults;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.MigrationRunner.Tests;

/// <summary>
/// Spec 440 T-B (ADR-0181 §3) — the boot step that takes <c>offline_access</c> out of
/// the realm's default composite. It runs on every boot, so it must be repeatable, and
/// a refused narrowing must fail the run rather than let a realm that still hands out
/// the never-expiring credential start the system.
/// </summary>
public sealed class RealmDefaultPrivilegeMigratorTests
{
    [Fact]
    public async Task Running_the_migrator_removes_offline_access_from_the_default_composite()
    {
        RecordingKeycloakAdminClient keycloak = new();

        await MigratorOver(keycloak).RunAsync(CancellationToken.None);

        keycloak.RemovedPrivileges.ShouldBe(["offline_access"]);
    }

    [Fact]
    public async Task Running_the_migrator_on_two_boots_succeeds_both_times()
    {
        RecordingKeycloakAdminClient keycloak = new();
        var migrator = MigratorOver(keycloak);

        await migrator.RunAsync(CancellationToken.None);
        await migrator.RunAsync(CancellationToken.None);

        keycloak.RemovedPrivileges.ShouldBe(["offline_access", "offline_access"]);
    }

    [Fact]
    public async Task A_refused_narrowing_fails_the_run_and_names_the_realm_privilege_step()
    {
        RecordingKeycloakAdminClient keycloak = new()
        {
            Failure = new HttpRequestException("forbidden", null, HttpStatusCode.Forbidden),
        };
        var migrator = MigratorOver(keycloak);
        CapturingLogger<Program> logger = new();
        using var services = Services(migrator);

        var exitCode = await MigrationRun.ExecuteAsync(services, logger, CancellationToken.None);

        exitCode.ShouldBe(MigrationRun.Failed);
        keycloak.RemovedPrivileges.ShouldBe(["offline_access"], "the failure must come from the refused narrowing, not from elsewhere");
        logger.Entries.ShouldContain(entry =>
            entry.Level >= LogLevel.Error
            && entry.Message.Contains(migrator.ContextName, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_forbidden_narrowing_throws_and_says_the_realm_may_predate_the_manage_realm_grant()
    {
        RecordingKeycloakAdminClient keycloak = new()
        {
            Failure = new HttpRequestException("forbidden", null, HttpStatusCode.Forbidden),
        };

        var thrown = await Should.ThrowAsync<Exception>(
            () => MigratorOver(keycloak).RunAsync(CancellationToken.None));

        thrown.Message.ShouldContain("manage-realm");
    }

    [Fact]
    public async Task A_server_error_from_the_provider_also_fails_the_run()
    {
        RecordingKeycloakAdminClient keycloak = new()
        {
            Failure = new HttpRequestException("boom", null, HttpStatusCode.InternalServerError),
        };

        await Should.ThrowAsync<HttpRequestException>(
            () => MigratorOver(keycloak).RunAsync(CancellationToken.None));
    }

    private static RealmDefaultPrivilegeMigrator MigratorOver(IKeycloakAdminClient keycloak) =>
        new(keycloak, NullLogger<RealmDefaultPrivilegeMigrator>.Instance);

    private static ServiceProvider Services(params IMigrator[] migrators)
    {
        ServiceCollection services = new();
        foreach (var migrator in migrators)
        {
            services.AddScoped(_ => migrator);
        }

        return services.BuildServiceProvider(validateScopes: true);
    }

    private sealed class RecordingKeycloakAdminClient : IKeycloakAdminClient
    {
        public List<string> RemovedPrivileges { get; } = [];

        public Exception? Failure { get; init; }

        public Task RemoveRealmDefaultPrivilegeAsync(
            string privilege, CancellationToken cancellationToken)
        {
            RemovedPrivileges.Add(privilege);
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }

        public Task<Option<IReadOnlyList<string>>> GetSubGroupNamesAsync(
            string parentPath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<KeycloakClientCredentials> CreateClientAsync(
            KeycloakClientRepresentation representation, string fabGroupPath, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<KeycloakClientCredentials> ReadClientSecretAsync(
            string clientId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> GetEnrolledKioskClientIdsAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> StripInheritedRealmRolesAsync(
            string clientId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<KeycloakClientCredentials> RotateClientSecretAsync(
            string clientId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task DisableClientAsync(string clientId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<StampedClient>> GetStampedClientsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Option<DateTimeOffset>> GetServiceAccountCreatedAtAsync(
            string clientId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
