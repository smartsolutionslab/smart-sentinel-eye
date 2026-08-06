namespace SmartSentinelEye.Integration.Tests;

/// <summary>
/// Guards the <c>E2ETests</c> switch the whole integration suite rests on.
/// Were it to stop reaching <c>builder.Configuration</c>, the AppHost would
/// silently boot its dev shape instead — persistent data volumes, pgAdmin and
/// the scenario simulator — so the suite would run against developer data
/// with nothing failing to say so.
///
/// <para>
/// Passes the arguments exactly as <see cref="Fixtures.AspireFixture"/> does,
/// because the switch travelling alone is not the case that matters. Builds
/// the application model only: <c>CreateAsync</c> starts no resources, so
/// this costs no containers and is safe beside a live <c>aspire run</c>.
/// </para>
/// </summary>
public class AppHostE2ESwitchTests
{
    private static readonly string[] FixtureArguments =
    [
        "Parameters:PostgresUser=postgres",
        "Parameters:PostgresPassword=testpassword",
        "Parameters:KeycloakPassword=testkeycloak",
        "Parameters:RabbitMqPassword=testmessaging",
        "E2ETests=true",
    ];

    [Fact]
    public async Task E2ETests_argument_excludes_the_dev_only_resources()
    {
        using IDistributedApplicationTestingBuilder builder =
            await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.SmartSentinelEye_AppHost>(FixtureArguments);

        IReadOnlyList<string> names = [.. builder.Resources.Select(resource => resource.Name)];

        names.ShouldNotContain("camera-sim");
        names.ShouldNotContain("scenario-simulator");
        names.ShouldNotContain("pgadmin");
    }

    [Fact]
    public async Task E2ETests_argument_leaves_postgres_without_a_data_volume()
    {
        using IDistributedApplicationTestingBuilder builder =
            await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.SmartSentinelEye_AppHost>(FixtureArguments);

        IResource postgres = builder.Resources.Single(resource => resource.Name == "postgres");

        postgres.Annotations.OfType<ContainerMountAnnotation>()
            .Where(mount => mount.Type == ContainerMountType.Volume)
            .ShouldBeEmpty();
    }
}
