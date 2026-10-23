namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Where an integration test gets migration-runner's dev private key (spec 440 plan §6):
/// from the AppHost parameter the <c>migrations</c> resource is actually given, read the
/// way <c>RealmImportMirrorTests</c> reads a parameter, and never from a copy kept in the
/// test project. The key has one home in the repository (the parameter's dev-only default);
/// <c>RealmImportMirrorTests</c> checks it against the public key the realm file registers.
///
/// <para>
/// <c>CreateAsync</c> composes the model and starts no resources, so this is safe beside
/// the fixture's own running stack.
/// </para>
/// </summary>
internal static class MigrationRunnerCredential
{
    public const string ClientIdentifier = "migration-runner";

    public const string PrivateKeyParameter = "MigrationRunnerClientPrivateKey";

    public static async Task<string> PrivateKeyAsync()
    {
        using var builder =
            await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.SmartSentinelEye_AppHost>([]);

        var parameter = builder.Resources
            .OfType<ParameterResource>()
            .Single(candidate => candidate.Name == PrivateKeyParameter);

        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));

        return await parameter.GetValueAsync(deadline.Token)
            ?? throw new InvalidOperationException($"the '{PrivateKeyParameter}' parameter resolved to no value.");
    }
}
