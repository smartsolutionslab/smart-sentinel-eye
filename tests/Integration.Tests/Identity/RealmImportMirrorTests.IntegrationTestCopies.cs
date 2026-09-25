using System.Reflection;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.EventIngestion;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.Integration.Tests.StreamDistribution;
using SmartSentinelEye.Integration.Tests.SystemVariables;

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
    /// The sixteen test-side client-credential copies spec 254 §1.1 names
    /// (§4.2): the type holding the copy, its client-id const's field name,
    /// and its secret const's field name — never an expected client id, since
    /// that would compare a copy with a literal written here rather than with
    /// the realm file, the shape spec 248 §4 rejected. The client each row
    /// presents is instead read from the site's own client-id const, by
    /// <see cref="A_test_sides_client_id_const_names_exactly_one_realm_client"/>.
    /// The single source of truth for both theories below, so the sixteen
    /// sites are never transcribed twice.
    /// </summary>
    private static readonly (Type Type, string ClientIdField, string SecretField)[] IntegrationTestCopySites =
    [
        (typeof(PlantFloor), "SimulatorClientId", "SimulatorClientSecret"), // T1 — scenario-simulator
        (typeof(TokenAudienceIntegrationTests), "SimulatorClientId", "SimulatorClientSecret"), // T2
        (typeof(FabGroupClaimIntegrationTests), "SimulatorClientId", "SimulatorClientSecret"), // T3
        (typeof(DeadLetterFabScopingIntegrationTests), "SimulatorClientId", "SimulatorClientSecret"), // T4
        (typeof(DeadLetterReasonIntegrationTests), "SimulatorClientId", "SimulatorClientSecret"), // T5
        (typeof(IngestThroughputMeasurementTests), "SimulatorClientId", "SimulatorClientSecret"), // T6
        (typeof(MissingPayloadIsDeadLetteredIntegrationTests), "SimulatorClientId", "SimulatorClientSecret"), // T7
        (typeof(MqttResubscribeAfterBrokerOutageIntegrationTests), "SimulatorClientId", "SimulatorClientSecret"), // T8
        (typeof(OutageRecoveryIntegrationTests), "SimulatorClientId", "SimulatorClientSecret"), // T9
        (typeof(PoisonDeliveryEscapeIntegrationTests), "SimulatorClientId", "SimulatorClientSecret"), // T10
        (typeof(RestartLosesNothingIntegrationTests), "SimulatorClientId", "SimulatorClientSecret"), // T11
        (typeof(WebhookBearerValidationIntegrationTests), "ScopeLessClientId", "ScopeLessClientSecret"), // T12
        (typeof(ResolveOverlayTextTests), "ServiceAccountClientId", "ServiceAccountSecret"), // T13
        (typeof(VariableReadScopeIntegrationTests), "ServiceAccountClientId", "ServiceAccountSecret"), // T14
        (typeof(ReverseIndexSeedCredentialTests), "SeederClientId", "SeederClientSecret"), // T15 — system-variables-seeder
        (typeof(StreamFabAttributionIntegrationTests), "AttributionClientIdentifier", "AttributionClientSecret"), // T16 — stream-distribution-attribution
    ];

    /// <summary>Theory A's rows: only what it reads — the type and its client-id const's field name.</summary>
    public static TheoryData<Type, string> ClientIdRows()
    {
        TheoryData<Type, string> data = [];

        foreach ((Type type, string clientIdField, _) in IntegrationTestCopySites)
        {
            data.Add(type, clientIdField);
        }

        return data;
    }

    /// <summary>Theory B's rows: the type and both its const field names.</summary>
    public static TheoryData<Type, string, string> SecretRows()
    {
        TheoryData<Type, string, string> data = [];

        foreach ((Type type, string clientIdField, string secretField) in IntegrationTestCopySites)
        {
            data.Add(type, clientIdField, secretField);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ClientIdRows))]
    public void A_test_sides_client_id_const_names_exactly_one_realm_client(Type type, string clientIdField)
    {
        string clientId = ConstantOf(type, clientIdField);

        using JsonDocument import = ReadRealmImport();
        JsonElement[] matches = ClientsNamed(import, clientId);

        matches.Length.ShouldBe(
            1,
            $"found {matches.Length} clients named '{clientId}' ({type.FullName}.{clientIdField}) "
            + $"in {FullRealmImportPath()}'s 'clients' array; expected exactly one — Keycloak "
            + $"itself would reject a duplicate clientId. Make {type.FullName}.{clientIdField} "
            + "and the realm file agree; if the realm file is what changed, delete the Keycloak "
            + "volume (the import only runs on a fresh volume).");
    }

    [Theory]
    [MemberData(nameof(SecretRows))]
    public void A_test_sides_secret_const_is_the_secret_the_import_seeds_for_that_client(
        Type type, string clientIdField, string secretField)
    {
        string clientId = ConstantOf(type, clientIdField);
        string secret = ConstantOf(type, secretField);

        using JsonDocument import = ReadRealmImport();
        string expected = SecretForClient(
            import,
            clientId,
            nameof(A_test_sides_client_id_const_names_exactly_one_realm_client));

        secret.ShouldBe(
            expected,
            $"{type.FullName}.{secretField} is '{secret}' but the '{clientId}' client's 'secret' "
            + $"in {FullRealmImportPath()} is '{expected}' — '{clientId}' is "
            + $"{type.FullName}.{clientIdField}'s value. Make {type.FullName}.{secretField} and "
            + "the realm file agree; if the realm file is what changed, delete the Keycloak "
            + "volume (the import only runs on a fresh volume).");
    }

    /// <summary>
    /// Reads a <c>private const string</c> field by reflection (spec 254 D3),
    /// precedent <c>Architecture.Tests/EndpointScopeDeclarationTests.cs:1849</c>
    /// and <c>Fixtures/LogTailDeliversIntegrationTests.cs:240</c>. A renamed or
    /// removed field, or one that is not a string constant, throws rather than
    /// handing a null on to <c>ShouldBe</c> — the bad-input scenario names the
    /// type and field so the cause is never a NullReferenceException.
    /// </summary>
    private static string ConstantOf(Type type, string fieldName)
    {
        FieldInfo? field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
        object? value = field is { IsLiteral: true } ? field.GetRawConstantValue() : null;

        if (value is not string constant)
        {
            throw new InvalidOperationException(
                $"'{type.FullName}' has no private const string field named '{fieldName}' — this "
                + "guard cannot read its expected value.");
        }

        return constant;
    }
}
