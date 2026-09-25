using System.Text.Json;
using SmartSentinelEye.Identity.Infrastructure.KeycloakAdmin;

namespace SmartSentinelEye.Integration.Tests.Identity;

/// <summary>
/// Guards the four literal copies issue #2466 names against silently drifting
/// from <c>src/AppHost/Realms/smart-sentinel-eye-realm.json</c>, the realm
/// import Keycloak seeds from: <see cref="RealmProbe"/>'s three constants,
/// <see cref="KeycloakAdminOptions"/>'s <c>Realm</c>/<c>AdminClientId</c>
/// defaults, and <c>AppHost.cs</c>'s <c>IdentityAdminClientSecret</c>
/// parameter default.
///
/// <para>
/// Every fact here compares a copy against the file — never a copy against
/// itself — so expected values are always read fresh from
/// <see cref="ReadRealmImport"/>. Today every copy agrees, so this class is
/// expected to pass on an honest tree; its red is observed by counterfactual
/// (spec 248 §2, §5): mutate one guarded copy at a time, watch the matching
/// fact fail for the stated reason, then revert.
/// </para>
///
/// <para>
/// A fifth copy, <c>AppHostParameterOverrideTests.DeclaredDefaults["IdentityAdminClientSecret"]</c>,
/// is covered transitively rather than by a new fact here: the AppHost-default
/// fact below asserts composed-value == realm, and that file's existing
/// <c>A_parameter_with_no_argument_keeps_its_default</c> already asserts
/// composed-value == that transcription. Together the two catch every
/// single-copy or two-copy drift among {realm, AppHost default, transcription}
/// (spec 248 §4).
/// </para>
/// </summary>
[Trait("Category", "FixtureLogic")]
public partial class RealmImportMirrorTests
{
    private const string RealmImportPath = "src/AppHost/Realms/smart-sentinel-eye-realm.json";
    private const string IdentityAdminClientSecretParameter = "IdentityAdminClientSecret";

    [Fact]
    public void RealmProbe_Realm_is_the_realm_the_import_defines()
    {
        using JsonDocument import = ReadRealmImport();
        string expected = DeclaredRealm(import);

        RealmProbe.Realm.ShouldBe(
            expected,
            $"RealmProbe.Realm is '{RealmProbe.Realm}' but {FullRealmImportPath()}'s root "
            + $"'realm' field is '{expected}'. Make RealmProbe.Realm and the realm file agree; if "
            + "the realm file is what changed, delete the Keycloak volume (the import only runs on "
            + "a fresh volume).");
    }

    [Fact]
    public void RealmProbe_AdminClientId_names_a_client_the_import_seeds()
    {
        using JsonDocument import = ReadRealmImport();
        JsonElement[] matches = ClientsNamed(import, RealmProbe.AdminClientId);

        matches.Length.ShouldBe(
            1,
            $"found {matches.Length} clients named '{RealmProbe.AdminClientId}' "
            + $"(RealmProbe.AdminClientId) in {FullRealmImportPath()}'s 'clients' array; expected "
            + "exactly one — Keycloak itself would reject a duplicate clientId. Make "
            + "RealmProbe.AdminClientId and the realm file agree; if the realm file is what "
            + "changed, delete the Keycloak volume (the import only runs on a fresh volume).");
    }

    [Fact]
    public void RealmProbe_AdminClientSecret_is_the_secret_the_import_seeds_for_that_client()
    {
        using JsonDocument import = ReadRealmImport();
        string expected = SecretForClient(
            import,
            RealmProbe.AdminClientId,
            nameof(RealmProbe_AdminClientId_names_a_client_the_import_seeds));

        RealmProbe.AdminClientSecret.ShouldBe(
            expected,
            $"RealmProbe.AdminClientSecret is '{RealmProbe.AdminClientSecret}' but the "
            + $"'{RealmProbe.AdminClientId}' client's 'secret' in {FullRealmImportPath()} is "
            + $"'{expected}'. Make RealmProbe.AdminClientSecret and the realm file agree; if the "
            + "realm file is what changed, delete the Keycloak volume (the import only runs on a "
            + "fresh volume).");
    }

    [Fact]
    public void KeycloakAdminOptions_default_Realm_is_the_realm_the_import_defines()
    {
        using JsonDocument import = ReadRealmImport();
        string expected = DeclaredRealm(import);
        string actual = new KeycloakAdminOptions().Realm;

        actual.ShouldBe(
            expected,
            $"KeycloakAdminOptions.Realm's default is '{actual}' but {FullRealmImportPath()}'s "
            + $"root 'realm' field is '{expected}'. The AppHost supplies no Keycloak__Realm "
            + "override, so this default is what the Identity API authenticates against in dev "
            + "and CI. Make KeycloakAdminOptions.Realm's default and the realm file agree; if the "
            + "realm file is what changed, delete the Keycloak volume (the import only runs on a "
            + "fresh volume).");
    }

    [Fact]
    public void KeycloakAdminOptions_default_AdminClientId_names_a_client_the_import_seeds()
    {
        using JsonDocument import = ReadRealmImport();
        string clientId = new KeycloakAdminOptions().AdminClientId;
        JsonElement[] matches = ClientsNamed(import, clientId);

        matches.Length.ShouldBe(
            1,
            $"found {matches.Length} clients named '{clientId}' (KeycloakAdminOptions."
            + $"AdminClientId's default) in {FullRealmImportPath()}'s 'clients' array; expected "
            + "exactly one. The AppHost supplies no Keycloak__AdminClientId override, so this "
            + "default is the client the Identity API authenticates as in dev and CI. Make "
            + "KeycloakAdminOptions.AdminClientId's default and the realm file agree; if the realm "
            + "file is what changed, delete the Keycloak volume (the import only runs on a fresh "
            + "volume).");
    }

    /// <summary>
    /// The real production pairing (spec 248 §4): <c>AppHost.cs</c> supplies
    /// only the secret, keyed by whatever client
    /// <see cref="KeycloakAdminOptions"/>'s default <c>AdminClientId</c>
    /// names — not by <see cref="RealmProbe.AdminClientId"/>, and not a
    /// literal. Composes the AppHost model with no arguments; <c>CreateAsync</c>
    /// starts no resources, so this is safe beside a live <c>aspire run</c>
    /// (<c>AppHostParameterOverrideTests</c> precedent).
    /// </summary>
    [Fact]
    public async Task The_AppHost_default_IdentityAdminClientSecret_is_the_secret_the_import_seeds_for_Identitys_client()
    {
        using JsonDocument import = ReadRealmImport();
        string clientId = new KeycloakAdminOptions().AdminClientId;
        string expected = SecretForClient(
            import,
            clientId,
            nameof(KeycloakAdminOptions_default_AdminClientId_names_a_client_the_import_seeds));

        using IDistributedApplicationTestingBuilder builder =
            await DistributedApplicationTestingBuilder
                .CreateAsync<Projects.SmartSentinelEye_AppHost>([]);

        ParameterResource parameter = builder.Resources
            .OfType<ParameterResource>()
            .Single(candidate => candidate.Name == IdentityAdminClientSecretParameter);

        string? actual = await ValueAsync(parameter);

        actual.ShouldBe(
            expected,
            $"AppHost.cs's '{IdentityAdminClientSecretParameter}' parameter defaults to "
            + $"'{actual}' but the '{clientId}' client's 'secret' in {FullRealmImportPath()} is "
            + $"'{expected}' — '{clientId}' is what KeycloakAdminOptions.AdminClientId defaults "
            + "to, the client Identity actually presents (AppHost.cs supplies only the secret). "
            + $"Make AppHost.cs's '{IdentityAdminClientSecretParameter}' default and the realm "
            + "file agree; if the realm file is what changed, delete the Keycloak volume (the "
            + "import only runs on a fresh volume).");
    }

    /// <summary>
    /// Bounded rather than awaited unconditionally (ADR-0150), mirroring
    /// <c>AppHostParameterOverrideTests.ValueAsync</c>: that helper is
    /// private and not worth widening for one call site here.
    /// </summary>
    private static async Task<string?> ValueAsync(ParameterResource parameter)
    {
        using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));

        return await parameter.GetValueAsync(deadline.Token);
    }

    private static string SecretForClient(JsonDocument import, string clientId, string likelyCauseFact)
    {
        JsonElement[] matches = ClientsNamed(import, clientId);

        if (matches.Length != 1)
        {
            throw new InvalidOperationException(
                $"expected exactly one client named '{clientId}' in {FullRealmImportPath()} to "
                + $"read its 'secret' from, found {matches.Length} — see {likelyCauseFact}, which "
                + "asserts that count directly.");
        }

        return SecretOf(matches[0], clientId);
    }

    private static JsonElement[] ClientsNamed(JsonDocument import, string clientId)
    {
        if (!import.RootElement.TryGetProperty("clients", out JsonElement clients)
            || clients.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                $"{FullRealmImportPath()} has no 'clients' array at its root — the realm import "
                + "changed shape and this guard cannot read it.");
        }

        return
        [
            .. clients.EnumerateArray()
                .Where(client => client.TryGetProperty("clientId", out JsonElement id)
                    && id.ValueKind == JsonValueKind.String
                    && id.GetString() == clientId),
        ];
    }

    private static string SecretOf(JsonElement client, string clientId)
    {
        if (!client.TryGetProperty("secret", out JsonElement secret) || secret.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException(
                $"the '{clientId}' client in {FullRealmImportPath()} has no string 'secret' "
                + "field — this guard cannot read its expected value.");
        }

        return secret.GetString()!;
    }

    private static string DeclaredRealm(JsonDocument import)
    {
        if (!import.RootElement.TryGetProperty("realm", out JsonElement realm) || realm.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException(
                $"{FullRealmImportPath()} has no string 'realm' field at its root — the realm "
                + "import changed shape and this guard cannot read it.");
        }

        return realm.GetString()!;
    }

    private static JsonDocument ReadRealmImport()
    {
        string path = FullRealmImportPath();

        if (!System.IO.File.Exists(path))
        {
            throw new InvalidOperationException(
                $"the realm import was not found at {path} — RealmImportMirrorTests cannot "
                + "compare any copy against it.");
        }

        string text = System.IO.File.ReadAllText(path);
        return JsonDocument.Parse(text);
    }

    private static string FullRealmImportPath() =>
        System.IO.Path.Combine([RepositoryRoot(), .. RealmImportPath.Split('/')]);

    /// <summary>
    /// Byte-for-byte the walk <c>AppHostE2ESwitchTests.cs:218</c> uses, and
    /// the same shape three <c>Architecture.Tests</c> guards already rely on
    /// (<c>RealmIdentityTests</c>, <c>RealmAudienceTests</c>,
    /// <c>SeededCredentialStrengthTests</c>) — a green run of that class in
    /// CI's Docker-free step is the evidence this pattern resolves the same
    /// way locally and in CI (spec 248 §1 row 6).
    /// </summary>
    private static string RepositoryRoot()
    {
        System.IO.DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null
            && !System.IO.File.Exists(System.IO.Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))
        {
            candidate = candidate.Parent;
        }

        return candidate?.FullName
            ?? throw new InvalidOperationException(
                $"could not locate the repository root above {AppContext.BaseDirectory}");
    }
}
