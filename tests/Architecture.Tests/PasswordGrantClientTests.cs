using System.Text.Json;
using System.Text.RegularExpressions;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 428 (#2809) — the only password grant the application realm accepts is a
/// dedicated, confidential, dev-only <c>integration-test-persona</c> client;
/// <c>management-web</c> stops accepting one.
///
/// <para>
/// <b>Red first (ADR-0139).</b> Written against a realm that has no such client and
/// whose <c>management-web</c> still allows a password grant. Each fact that reads the
/// persona client asserts it exists first, so it fails for that reason and not on a
/// property read off an absent client.
/// </para>
///
/// <para>
/// Client ids and the secret are local copies on purpose: this project does not
/// reference <c>Integration.Tests</c> (see <c>IntegrationTestAdminClientTests</c>).
/// </para>
/// </summary>
public class PasswordGrantClientTests
{
    private const string PersonaClientId = "integration-test-persona";

    private const string PersonaClientSecret = "dev-only-integration-test-persona-secret";

    private const string ConsoleClientId = "management-web";

    [Fact]
    public void The_realm_seeds_exactly_one_integration_test_persona_client()
    {
        RequireClient(PersonaClientId);
    }

    [Fact]
    public void It_is_a_confidential_client_with_no_service_account_browser_flow_or_redirect()
    {
        var client = RequireClient(PersonaClientId);

        client.GetProperty("publicClient").GetBoolean().ShouldBeFalse(
            customMessage: $"'{PersonaClientId}' must be confidential: a password grant without a "
            + "client secret is exactly what management-web offered and this client replaces.");
        BoolOf(client, "serviceAccountsEnabled").ShouldBeFalse(
            customMessage: $"'{PersonaClientId}' must have no service account: it mints a token "
            + "for a named user only.");
        BoolOf(client, "standardFlowEnabled").ShouldBeFalse(
            customMessage: $"'{PersonaClientId}' must not offer the browser authorization-code flow.");
        BoolOf(client, "implicitFlowEnabled").ShouldBeFalse(
            customMessage: $"'{PersonaClientId}' must not offer the implicit flow.");
        BoolOf(client, "directAccessGrantsEnabled").ShouldBeTrue(
            customMessage: $"'{PersonaClientId}' exists to accept the password grant.");
        (client.TryGetProperty("redirectUris", out var redirects)
            && redirects.ValueKind == JsonValueKind.Array
            ? redirects.GetArrayLength()
            : 0).ShouldBe(
                0,
                customMessage: $"'{PersonaClientId}' must have no redirect URIs.");
    }

    [Fact]
    public void Its_secret_is_marked_dev_only()
    {
        var client = RequireClient(PersonaClientId);
        var secret = client.TryGetProperty("secret", out var value) ? value.GetString() ?? string.Empty : string.Empty;

        secret.ShouldStartWith(
            "dev-only-",
            customMessage: $"'{PersonaClientId}' secret must start with 'dev-only-' so it can never pass for a real credential.");
    }

    [Fact]
    public void Its_default_scopes_equal_the_console_clients_in_both_directions()
    {
        var persona = DefaultScopesOf(RequireClient(PersonaClientId));
        var console = DefaultScopesOf(RequireClient(ConsoleClientId));

        persona.Except(console, StringComparer.Ordinal).ShouldBeEmpty(
            customMessage: $"'{PersonaClientId}' grants a scope '{ConsoleClientId}' does not.");
        console.Except(persona, StringComparer.Ordinal).ShouldBeEmpty(
            customMessage: $"'{PersonaClientId}' is missing a scope '{ConsoleClientId}' grants.");
    }

    [Fact]
    public void Its_optional_scopes_protocol_mappers_and_full_scope_flag_equal_the_console_clients()
    {
        var persona = RequireClient(PersonaClientId);
        var console = RequireClient(ConsoleClientId);

        foreach (var name in new[] { "optionalClientScopes", "protocolMappers", "fullScopeAllowed" })
        {
            RawOf(persona, name).ShouldBe(
                RawOf(console, name),
                customMessage: $"'{PersonaClientId}' {name} differs from '{ConsoleClientId}' "
                + "(an absent key and a present one are different): the persona token must carry the "
                + "same claims the console's would.");
        }
    }

    [Fact]
    public void Every_realm_client_states_directAccessGrantsEnabled_explicitly()
    {
        string[] missing = [.. Clients()
            .Where(client => !client.TryGetProperty("directAccessGrantsEnabled", out _))
            .Select(ClientIdOf)];

        missing.ShouldBeEmpty(
            customMessage: "these clients omit directAccessGrantsEnabled, which Keycloak may default "
            + "to true so the password-grant census cannot see them: ["
            + string.Join(", ", missing)
            + "].");
    }

    [Fact]
    public void Its_description_fits_keycloaks_255_character_limit()
    {
        var client = RequireClient(PersonaClientId);
        var description = client.TryGetProperty("description", out var value)
            ? value.GetString() ?? string.Empty
            : string.Empty;

        description.Length.ShouldBeLessThanOrEqualTo(
            255,
            customMessage: $"'{PersonaClientId}' description is {description.Length} characters; "
            + "over 255 kills the realm import and hangs the fixture.");
    }

    [Fact]
    public void Exactly_one_client_in_the_application_realm_accepts_a_password_grant_and_it_is_the_persona_client()
    {
        string[] passwordGrantClients = [.. Clients()
            .Where(client => BoolOf(client, "directAccessGrantsEnabled"))
            .Select(ClientIdOf)
            .Order(StringComparer.Ordinal)];

        passwordGrantClients.ShouldBe(
            [PersonaClientId],
            customMessage: "the clients with directAccessGrantsEnabled true are ["
            + string.Join(", ", passwordGrantClients)
            + $"]; expected only '{PersonaClientId}'. A second password-grant client is an "
            + "unguarded one, and management-web must not accept the grant (spec 428).");
    }

    [Fact]
    public void The_e2e_lockout_spec_names_the_persona_client_and_its_realm_secret()
    {
        var path = Path.Combine(RepositoryRoot(), "e2e", "wall-survives-a-lockout.spec.ts");
        var source = File.ReadAllText(path);

        var id = Regex.Match(source, @"const\s+PERSONA_CLIENT_ID\s*=\s*'([^']*)'");
        var secret = Regex.Match(source, @"const\s+PERSONA_CLIENT_SECRET\s*=\s*'([^']*)'");

        id.Success.ShouldBeTrue(
            customMessage: "e2e/wall-survives-a-lockout.spec.ts declares no PERSONA_CLIENT_ID const (spec 428).");
        id.Groups[1].Value.ShouldBe(PersonaClientId);
        secret.Success.ShouldBeTrue(
            customMessage: "e2e/wall-survives-a-lockout.spec.ts declares no PERSONA_CLIENT_SECRET const (spec 428).");
        secret.Groups[1].Value.ShouldBe(PersonaClientSecret);
        secret.Groups[1].Value.ShouldBe(
            SecretOf(RequireClient(PersonaClientId)),
            customMessage: "the e2e secret differs from the realm's secret for the persona client.");
    }

    private static string SecretOf(JsonElement client) =>
        client.TryGetProperty("secret", out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private static string? RawOf(JsonElement client, string name) =>
        client.TryGetProperty(name, out var value) ? value.GetRawText() : null;

    private static bool BoolOf(JsonElement client, string name) =>
        client.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static JsonElement RequireClient(string clientId)
    {
        JsonElement[] matches = [.. Clients().Where(client => ClientIdOf(client) == clientId)];

        matches.Length.ShouldBe(
            1,
            customMessage: $"expected exactly one client named '{clientId}' in the realm import's "
            + $"'clients' array; found {matches.Length}.");

        return matches[0];
    }

    private static JsonElement.ArrayEnumerator Clients() =>
        RealmDocument.RootElement.GetProperty("clients").EnumerateArray();

    private static string ClientIdOf(JsonElement client) =>
        client.GetProperty("clientId").GetString() ?? "(unnamed)";

    private static IReadOnlyCollection<string> DefaultScopesOf(JsonElement client) =>
        [.. client.GetProperty("defaultClientScopes").EnumerateArray()
            .Select(scope => scope.GetString() ?? string.Empty)];

    private static readonly JsonDocument RealmDocument = ReadRealm();

    private static string RepositoryRoot()
    {
        DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))
        {
            candidate = candidate.Parent;
        }

        return (candidate
            ?? throw new InvalidOperationException(
                $"could not locate the repository root above {AppContext.BaseDirectory}")).FullName;
    }

    private static JsonDocument ReadRealm()
    {
        var path = Path.Combine(RepositoryRoot(), "src", "AppHost", "Realms", "smart-sentinel-eye-realm.json");
        File.Exists(path).ShouldBeTrue($"the realm should be at {path}");
        return JsonDocument.Parse(File.ReadAllText(path));
    }
}
