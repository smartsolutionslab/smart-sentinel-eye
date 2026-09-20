namespace SmartSentinelEye.Integration.Tests;

/// <summary>
/// Guards the mechanism issue #2254 (spec 188) exists for: a
/// <c>Parameters:&lt;name&gt;=&lt;value&gt;</c> command-line argument must actually change
/// what the AppHost composes. Today it does not — every one of the ten
/// <c>builder.AddParameter(name, literal, secret)</c> call sites in
/// <c>AppHost.cs</c> uses the literal-valued overload, which never reads
/// <c>builder.Configuration</c> (spec.md §1.1, decompiled from the pinned
/// <c>Aspire.Hosting</c> 13.5.3 assembly).
///
/// <para>
/// Builds the application model only: <c>CreateAsync</c> starts no resources,
/// so this costs no containers and is safe beside a live <c>aspire run</c> —
/// the same footing as <see cref="AppHostE2ESwitchTests"/>. T001's spike
/// (spec 188, plan §5 R3) confirmed <c>ParameterResource.GetValueAsync</c>
/// resolves promptly in this composition mode rather than waiting on
/// <c>WaitForValueTcs</c>, which is only set by the run-time value-prompt
/// path.
/// </para>
///
/// <para>
/// Two of the five facts below are expected to hold both before and after
/// phase 4b's fix — they characterise the fallback behaviour that must not
/// move while the override behaviour is added. The other three change from
/// failing to passing once <c>AddOverridableParameter</c> lands.
/// </para>
/// </summary>
[Trait("Category", "FixtureLogic")]
public class AppHostParameterOverrideTests
{
    private const string MigrationRunnerClientSecretName = "MigrationRunnerClientSecret";
    private const string MigrationRunnerClientSecretDefault = "dev-only-migration-runner-secret";
    private const string DeliberatelyWrongOverride = "deliberately-wrong";

    /// <summary>
    /// Every parameter <c>AppHost.cs</c> declares, and the literal each one
    /// falls back to. Transcribed by hand from <c>AppHost.cs:28-64</c>
    /// (spec.md §2) rather than derived from the composition itself — the
    /// composition is the subject under test in
    /// <see cref="A_parameter_with_no_argument_keeps_its_default"/>, so the
    /// expected values cannot also come from it
    /// (an-assertion-must-not-check-its-own-input).
    /// </summary>
    private static readonly Dictionary<string, string> DeclaredDefaults = new(StringComparer.Ordinal)
    {
        ["PostgresUser"] = "postgres",
        ["PostgresPassword"] = "dev-only-postgres-password",
        ["KeycloakPassword"] = "dev-only-keycloak-admin",
        ["IdentityAdminClientSecret"] = "dev-only-identity-admin-secret",
        [MigrationRunnerClientSecretName] = MigrationRunnerClientSecretDefault,
        ["RabbitMqPassword"] = "dev-only-rabbit-password",
        ["ScenarioSimulatorClientSecret"] = "dev-only-scenario-simulator-secret",
        ["EventIngestionMqttClientSecret"] = "dev-only-event-ingestion-secret",
        ["StreamDistributionAttributionClientSecret"] = "dev-only-stream-distribution-secret",
        ["SystemVariablesSeederClientSecret"] = "dev-only-system-variables-seeder-secret",
    };

    /// <summary>
    /// The happy path (spec.md §3, US1's first acceptance scenario). Expected
    /// RED on the current tree: the literal overload never consults
    /// <c>builder.Configuration</c>, so this resolves to
    /// <see cref="MigrationRunnerClientSecretDefault"/> instead.
    /// </summary>
    [Fact]
    public async Task A_parameter_argument_overrides_the_declared_default()
    {
        using IDistributedApplicationTestingBuilder builder =
            await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.SmartSentinelEye_AppHost>(
                    [$"Parameters:{MigrationRunnerClientSecretName}={DeliberatelyWrongOverride}"]);

        ParameterResource parameter = builder.Resources
            .OfType<ParameterResource>()
            .Single(candidate => candidate.Name == MigrationRunnerClientSecretName);

        string? value = await ValueAsync(parameter);

        value.ShouldBe(
            DeliberatelyWrongOverride,
            $"'Parameters:{MigrationRunnerClientSecretName}={DeliberatelyWrongOverride}' on the "
            + "command line must change what the AppHost composes — that is the whole mechanism "
            + "#2254 was filed for. A literal-valued 'AddParameter' overload never reads "
            + "'builder.Configuration', so this argument is inert until AddOverridableParameter "
            + "replaces it.");
    }

    /// <summary>
    /// Spec.md §3's second US1 scenario: every declared parameter, not only
    /// the four ever passed as an argument today. The count comes from the
    /// composition itself, never a name list here — a name list would keep
    /// passing when an eleventh parameter is added without the helper, which
    /// is the exact drift #2254 exists to close.
    ///
    /// <para>
    /// Expected RED on the current tree for all ten: the literal overload
    /// returns its own declared default regardless of the override argument.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Every_declared_parameter_honours_its_own_override()
    {
        string[] declaredNames = await DeclaredParameterNamesAsync();

        declaredNames.ShouldNotBeEmpty(
            "the composed AppHost model contained no ParameterResource at all — the scan is "
            + "broken, not passing, because it observed nothing.");

        string[] overrideArguments =
        [
            .. declaredNames.Select(name => $"Parameters:{name}={OverrideValueFor(name)}"),
        ];

        using IDistributedApplicationTestingBuilder builder =
            await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.SmartSentinelEye_AppHost>(overrideArguments);

        Dictionary<string, string> resolved = [];

        foreach (ParameterResource parameter in builder.Resources.OfType<ParameterResource>())
        {
            resolved[parameter.Name] = await ValueAsync(parameter) ?? string.Empty;
        }

        foreach (string name in declaredNames)
        {
            resolved.ShouldContainKey(name);

            resolved[name].ShouldBe(
                OverrideValueFor(name),
                $"'{name}' did not resolve to the value its own 'Parameters:{name}=' argument "
                + $"supplied (resolved '{resolved[name]}' instead). Every one of the ten "
                + "declared parameters must honour its own override, not only the four "
                + "arguments that happen to be passed today (spec.md §2).");
        }
    }

    /// <summary>
    /// Spec.md §3's third US1 scenario — the conflict case. Must be GREEN
    /// already: a fix that broke this would break every boot in the repo,
    /// because nothing passes a <c>Parameters:</c> argument for most of these
    /// ten today (spec.md §2's table).
    /// </summary>
    [Fact]
    public async Task A_parameter_with_no_argument_keeps_its_default()
    {
        using IDistributedApplicationTestingBuilder builder =
            await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.SmartSentinelEye_AppHost>([]);

        ParameterResource[] parameters = [.. builder.Resources.OfType<ParameterResource>()];

        string[] observedNames = [.. parameters.Select(parameter => parameter.Name).Order(StringComparer.Ordinal)];
        string[] expectedNames = [.. DeclaredDefaults.Keys.Order(StringComparer.Ordinal)];

        observedNames.ShouldBe(
            expectedNames,
            "the set of ParameterResource names the AppHost composed does not match spec.md §2's "
            + "ten declared parameters — either a parameter was added or removed, or this test's "
            + "transcription of AppHost.cs is stale.");

        foreach (ParameterResource parameter in parameters)
        {
            string? value = await ValueAsync(parameter);

            value.ShouldBe(
                DeclaredDefaults[parameter.Name],
                $"'{parameter.Name}' resolved to '{value}' with no 'Parameters:{parameter.Name}=' "
                + $"argument on the command line — it must fall back to its own declared literal "
                + $"'{DeclaredDefaults[parameter.Name]}'. Every boot in the repo that never passes "
                + "an override for this parameter relies on this fallback holding.");
        }
    }

    /// <summary>
    /// Spec.md §3's fourth US1 scenario — the bad-request case. Must be GREEN
    /// already, and must stay green: plan §2.4 point 1 chose
    /// <c>string.IsNullOrEmpty</c> over <c>??</c> specifically so this holds
    /// once the fix lands, matching Aspire's own <c>GetParameterValue</c>,
    /// which treats null-or-empty as absent.
    /// </summary>
    [Fact]
    public async Task An_empty_parameter_argument_keeps_its_default()
    {
        using IDistributedApplicationTestingBuilder builder =
            await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.SmartSentinelEye_AppHost>(
                    [$"Parameters:{MigrationRunnerClientSecretName}="]);

        ParameterResource parameter = builder.Resources
            .OfType<ParameterResource>()
            .Single(candidate => candidate.Name == MigrationRunnerClientSecretName);

        string? value = await ValueAsync(parameter);

        value.ShouldBe(
            MigrationRunnerClientSecretDefault,
            $"'Parameters:{MigrationRunnerClientSecretName}=' (an empty value) resolved to "
            + $"'{value}' instead of falling back to the declared default "
            + $"'{MigrationRunnerClientSecretDefault}'. An empty override is not an override — "
            + "it must be treated as absent, the same way Aspire's own configuration-backed "
            + "parameter overloads treat an empty value as no value at all.");
    }

    /// <summary>
    /// Spec.md §3's fifth US1 scenario, and the guard that the fix changes
    /// values and nothing else: overriding every parameter's value must not
    /// add, remove or rename any resource in the composed model.
    /// </summary>
    [Fact]
    public async Task An_override_does_not_change_the_composed_resource_set()
    {
        string[] namesWithoutOverrides;
        string[] declaredParameterNames;

        using (IDistributedApplicationTestingBuilder withoutOverrides =
            await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.SmartSentinelEye_AppHost>([]))
        {
            namesWithoutOverrides =
                [.. withoutOverrides.Resources.Select(resource => resource.Name).Order(StringComparer.Ordinal)];

            declaredParameterNames =
                [.. withoutOverrides.Resources.OfType<ParameterResource>().Select(parameter => parameter.Name)];
        }

        string[] overrideArguments =
        [
            .. declaredParameterNames.Select(name => $"Parameters:{name}={OverrideValueFor(name)}"),
        ];

        using IDistributedApplicationTestingBuilder withOverrides =
            await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.SmartSentinelEye_AppHost>(overrideArguments);

        string[] namesWithOverrides =
            [.. withOverrides.Resources.Select(resource => resource.Name).Order(StringComparer.Ordinal)];

        namesWithOverrides.ShouldBe(
            namesWithoutOverrides,
            "overriding every declared parameter's value changed the set of resources the "
            + "AppHost composed. An override must change what a parameter resolves to, and "
            + "nothing else — no resource, no wiring, added or removed.");
    }

    /// <summary>
    /// The set of parameter names read from a plain, argument-free
    /// composition — never a list written in this file, so an eleventh
    /// parameter added without <c>AddOverridableParameter</c> is covered
    /// automatically rather than silently skipped.
    /// </summary>
    private static async Task<string[]> DeclaredParameterNamesAsync()
    {
        using IDistributedApplicationTestingBuilder builder =
            await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.SmartSentinelEye_AppHost>([]);

        return [.. builder.Resources.OfType<ParameterResource>().Select(parameter => parameter.Name)];
    }

    /// <summary>
    /// A value derived purely from the parameter's own name, never equal to
    /// any declared default, so a test that happened to pass by comparing a
    /// resolved value to itself would be caught rather than hidden.
    /// </summary>
    private static string OverrideValueFor(string parameterName) => $"override-value-for-{parameterName}";

    /// <summary>
    /// Bounded rather than awaited unconditionally (ADR-0150): T001's spike
    /// established that <c>GetValueAsync</c> resolves promptly in this
    /// composition mode, so a hang here is itself a finding, not a slow pass.
    /// </summary>
    private static async Task<string?> ValueAsync(ParameterResource parameter)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        return await parameter.GetValueAsync(deadline.Token);
    }
}
