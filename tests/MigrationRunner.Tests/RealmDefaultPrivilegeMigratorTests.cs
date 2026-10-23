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

    /// <summary>
    /// ADR-0186 note 5 (FR-004a): a refused narrowing offers the checks that destroy
    /// nothing first, and names the volume drop last and only for the local dev stack.
    /// Wrapped and rethrown, never swallowed.
    /// </summary>
    [Fact]
    public async Task A_forbidden_narrowing_is_wrapped_and_names_the_missing_manage_realm_grant()
    {
        HttpRequestException refusal = new("forbidden", null, HttpStatusCode.Forbidden);
        RecordingKeycloakAdminClient keycloak = new() { Failure = refusal };

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => MigratorOver(keycloak).RunAsync(CancellationToken.None));

        thrown.InnerException.ShouldBeSameAs(refusal);
        thrown.Message.ShouldContain("manage-realm");
        thrown.Message.ShouldContain("migration-runner");
    }

    [Fact]
    public async Task A_forbidden_narrowing_offers_the_non_destructive_checks_before_the_volume_drop()
    {
        RecordingKeycloakAdminClient keycloak = new()
        {
            Failure = new HttpRequestException("forbidden", null, HttpStatusCode.Forbidden),
        };

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => MigratorOver(keycloak).RunAsync(CancellationToken.None));

        var check = thrown.Message.IndexOf("role mapping", StringComparison.OrdinalIgnoreCase);
        var drop = thrown.Message.IndexOf("keycloak-data", StringComparison.Ordinal);
        check.ShouldBeGreaterThanOrEqualTo(0, "the verdict should tell the reader to check the service account's role mapping.");
        drop.ShouldBeGreaterThan(check, "the destructive remedy must not lead the message.");
        thrown.Message.ShouldContain("dev stack", Case.Insensitive);
    }

    /// <summary>
    /// ADR-0186 Decision 4 / FR-004b: on this migrator's call path a 400 or 401 can only be
    /// the token mint refusing the signed assertion, so the verdict names the credential.
    /// </summary>
    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task A_refused_mint_is_wrapped_and_names_the_key_pair_and_the_decision(HttpStatusCode status)
    {
        HttpRequestException refusal = new("invalid_client", null, status);
        RecordingKeycloakAdminClient keycloak = new() { Failure = refusal };

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => MigratorOver(keycloak).RunAsync(CancellationToken.None));

        thrown.InnerException.ShouldBeSameAs(refusal);
        thrown.Message.ShouldContain("ADR-0186");
        thrown.Message.ShouldContain("public key", Case.Insensitive);
        thrown.Message.ShouldContain("private key", Case.Insensitive);
    }

    [Fact]
    public async Task A_refused_mint_names_the_volume_drop_last_and_only_for_the_dev_stack()
    {
        RecordingKeycloakAdminClient keycloak = new()
        {
            Failure = new HttpRequestException("invalid_client", null, HttpStatusCode.Unauthorized),
        };

        var thrown = await Should.ThrowAsync<InvalidOperationException>(
            () => MigratorOver(keycloak).RunAsync(CancellationToken.None));

        var credential = thrown.Message.IndexOf("public key", StringComparison.OrdinalIgnoreCase);
        var drop = thrown.Message.IndexOf("keycloak-data", StringComparison.Ordinal);
        credential.ShouldBeGreaterThanOrEqualTo(0);
        drop.ShouldBeGreaterThan(credential);
        thrown.Message.ShouldContain("dev stack", Case.Insensitive);
    }

    [Fact]
    public async Task A_refused_mint_fails_the_run_and_names_the_realm_privilege_step()
    {
        RecordingKeycloakAdminClient keycloak = new()
        {
            Failure = new HttpRequestException("invalid_client", null, HttpStatusCode.Unauthorized),
        };
        var migrator = MigratorOver(keycloak);
        CapturingLogger<Program> logger = new();
        using var services = Services(migrator);

        var exitCode = await MigrationRun.ExecuteAsync(services, logger, CancellationToken.None);

        exitCode.ShouldBe(MigrationRun.Failed);
        logger.Entries.ShouldContain(entry =>
            entry.Level >= LogLevel.Error
            && entry.Message.Contains(migrator.ContextName, StringComparison.Ordinal));
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
