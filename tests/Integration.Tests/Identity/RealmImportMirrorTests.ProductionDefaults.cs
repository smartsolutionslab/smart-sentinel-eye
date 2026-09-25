using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ServiceDefaults;

namespace SmartSentinelEye.Integration.Tests.Identity;

// Architecture.Tests/IntegrationTestSelectionTests.cs scans each source file
// under tests/Integration.Tests independently and requires every file
// declaring a [Fact]/[Theory] to carry its own [Collection(...)] or
// [Trait("Category", ...)] — it has no concept of a partial class spanning
// files. AspireFixture.Auth.cs, D1's cited precedent, never exercises this:
// it declares no [Fact]/[Theory], so the guard's population gate excludes it.
// This file does declare test methods, so it needs its own copy (spec 254 D1).
[Trait("Category", "FixtureLogic")]
public partial class RealmImportMirrorTests
{
    /// <summary>
    /// The five other <c>AppHost.cs</c> secret-parameter defaults (spec 254
    /// §4.1, P1-P5) — the production counterpart of the sixteen test-side
    /// copies in <c>RealmImportMirrorTests.IntegrationTestCopies.cs</c>, same
    /// class as this file's sibling
    /// <see cref="The_AppHost_default_IdentityAdminClientSecret_is_the_secret_the_import_seeds_for_Identitys_client"/>.
    /// Paired by a literal client id in the row rather than through another
    /// default (spec 254 D5): unlike Identity's admin secret, these five
    /// services' presented client ids live in further defaults out of scope
    /// here, and pairing through them would widen scope. A stale row literal
    /// cannot pass silently — <see cref="SecretForClient"/> throws when no
    /// such client exists.
    /// </summary>
    public static TheoryData<string, string> ProductionSecretDefaultRows() => new()
    {
        { "MigrationRunnerClientSecret", "migration-runner" },
        { "ScenarioSimulatorClientSecret", "scenario-simulator" },
        { "EventIngestionMqttClientSecret", "event-ingestion" },
        { "StreamDistributionAttributionClientSecret", "stream-distribution-attribution" },
        { "SystemVariablesSeederClientSecret", "system-variables-seeder" },
    };

    [Theory]
    [MemberData(nameof(ProductionSecretDefaultRows))]
    public async Task An_AppHost_secret_parameter_default_is_the_secret_the_import_seeds_for_its_client(
        string parameterName, string clientId)
    {
        using JsonDocument import = ReadRealmImport();
        string expected = SecretForClient(
            import,
            clientId,
            $"this row's own client id ('{clientId}')");

        using IDistributedApplicationTestingBuilder builder =
            await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.SmartSentinelEye_AppHost>([]);

        ParameterResource parameter = builder.Resources
            .OfType<ParameterResource>()
            .Single(candidate => candidate.Name == parameterName);

        string? actual = await ValueAsync(parameter);

        actual.ShouldBe(
            expected,
            $"AppHost.cs's '{parameterName}' parameter defaults to '{actual}' but the "
            + $"'{clientId}' client's 'secret' in {FullRealmImportPath()} is '{expected}'. Make "
            + $"AppHost.cs's '{parameterName}' default and the realm file agree; if the realm "
            + "file is what changed, delete the Keycloak volume (the import only runs on a fresh "
            + "volume).");
    }

    /// <summary>
    /// The value every API actually validates tokens against (spec 254 D4):
    /// every one of the nine <c>AddBearerAuthentication()</c> call sites
    /// passes no <c>realm</c> argument, so <c>AuthenticationDefaults.cs</c>'s
    /// default is what composes into every API's JWT bearer <c>Authority</c>.
    /// Read through the real extension method exactly as
    /// <c>ServiceDefaults.Tests/BearerAudienceTests.cs</c> does, from an
    /// <em>empty</em> builder so no <c>appsettings.json</c> and no ambient
    /// environment variable can supply the answer — this survives the
    /// default moving into a constant or being ignored, which
    /// <c>ParameterInfo.DefaultValue</c> reflection would not.
    /// </summary>
    [Fact]
    public async Task The_composed_bearer_authority_uses_the_realm_the_import_defines()
    {
        using JsonDocument import = ReadRealmImport();
        string realm = DeclaredRealm(import);
        string expected = $"https://keycloak.invalid/realms/{realm}";

        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(null);
        builder.Configuration["ConnectionStrings:keycloak"] = "https://keycloak.invalid";
        builder.AddBearerAuthentication();

        using ServiceProvider provider = builder.Services.BuildServiceProvider();
        JwtBearerOptions options = provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme);

        options.Authority.ShouldBe(
            expected,
            $"AddBearerAuthentication()'s composed Authority is '{options.Authority}' but "
            + $"{FullRealmImportPath()}'s root 'realm' field is '{realm}', so the composed "
            + $"Authority every API validates tokens against should be '{expected}'. Make "
            + "AuthenticationDefaults.cs's realm default and the realm file agree; if the realm "
            + "file is what changed, delete the Keycloak volume (the import only runs on a fresh "
            + "volume).");
    }
}
