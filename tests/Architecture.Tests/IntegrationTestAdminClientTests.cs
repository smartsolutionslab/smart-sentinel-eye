using System.Text.Json;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 327 (#2511) — the integration-test harness's admin path stops minting
/// its token by <c>grant_type=password</c> against <c>management-web</c> and
/// mints it instead through a dedicated confidential
/// <c>integration-test-admin</c> service-account client, mirroring
/// <c>identity-admin</c>'s shape (ADR-0100) without its Keycloak-admin
/// privileges.
///
/// <para>
/// <b>Was the slice's defining red; now green.</b> This class was written
/// against the realm as it existed then, which had no such client, so every
/// fact below failed because the client — or its service-account user — was
/// absent, never because of a wrong file path or a null-reference: each fact
/// asserts existence first, with a message that names what is missing,
/// before it reads any property off what it found (plan.md §5, T-A). The
/// realm now seeds the client, so every fact here passes.
/// </para>
///
/// <para>
/// <b>What this cannot see.</b> It reads names, exactly as
/// <c>RealmAudienceTests</c> and <c>RealmIdentityTests</c> do. Whether a
/// minted token actually carries these claims is
/// <c>AdminHarnessTokenIntegrationTests</c>' job.
/// </para>
/// </summary>
public class IntegrationTestAdminClientTests
{
    /// <summary>
    /// Local on purpose (plan.md §5 T-A): <c>Architecture.Tests</c> does not
    /// reference <c>Integration.Tests</c>, so this is its own copy of the
    /// fixture's <c>HarnessClientId</c> constant, not a reference to it.
    /// </summary>
    private const string HarnessClientId = "integration-test-admin";

    private const string ConsoleClientId = "management-web";

    private const string AdminUsername = "admin";

    [Fact]
    public void The_realm_seeds_exactly_one_integration_test_admin_client()
    {
        RequireClient();
    }

    [Fact]
    public void It_is_a_confidential_service_account_with_no_browser_or_password_flow()
    {
        JsonElement client = RequireClient();

        client.GetProperty("publicClient").GetBoolean().ShouldBeFalse(
            customMessage: $"'{HarnessClientId}' must be confidential — a public client cannot "
            + "hold the secret client_credentials needs (spec 327 §Decision).");
        client.GetProperty("serviceAccountsEnabled").GetBoolean().ShouldBeTrue(
            customMessage: $"'{HarnessClientId}' must have a service account — that is the "
            + "principal client_credentials mints a token for.");
        client.GetProperty("standardFlowEnabled").GetBoolean().ShouldBeFalse(
            customMessage: $"'{HarnessClientId}' must not offer the browser authorization-code "
            + "flow; it is a machine-only harness client.");
        client.GetProperty("directAccessGrantsEnabled").GetBoolean().ShouldBeFalse(
            customMessage: $"'{HarnessClientId}' must not accept a password grant — AS-4 expects "
            + "one to be refused, and this is the configuration that refuses it.");
    }

    [Fact]
    public void Its_default_scopes_equal_the_console_clients_in_both_directions()
    {
        JsonElement harness = RequireClient();
        IReadOnlyCollection<string> harnessScopes = DefaultScopesOf(harness);
        IReadOnlyCollection<string> consoleScopes = DefaultScopesOf(RequireConsoleClient());

        harnessScopes.Except(consoleScopes, StringComparer.Ordinal).ShouldBeEmpty(
            customMessage: $"'{HarnessClientId}' grants a scope '{ConsoleClientId}' does not. Its "
            + "scopes must be set-equal to the console's (spec 327 §Decision), so an admin-harness "
            + "caller never sees a permission the console itself lacks.");
        consoleScopes.Except(harnessScopes, StringComparer.Ordinal).ShouldBeEmpty(
            customMessage: $"'{HarnessClientId}' is missing a scope '{ConsoleClientId}' grants. "
            + "Without it, an admin-harness caller is refused somewhere every console-authenticated "
            + "test today succeeds (spec 327 §Decision).");
    }

    [Fact]
    public void Its_service_account_is_in_exactly_the_admin_users_fab_groups()
    {
        JsonElement serviceAccount = RequireServiceAccountUser();
        IReadOnlyCollection<string> serviceAccountGroups = GroupsOf(serviceAccount);
        IReadOnlyCollection<string> adminGroups = GroupsOf(RequireAdminUser());

        serviceAccountGroups.Except(adminGroups, StringComparer.Ordinal).ShouldBeEmpty(
            customMessage: $"'{HarnessClientId}'s service account holds a fab group the "
            + $"'{AdminUsername}' user does not. It must act in exactly the fabs the admin "
            + "harness is meant to stand in for, never more (spec 327 §Decision).");
        adminGroups.Except(serviceAccountGroups, StringComparer.Ordinal).ShouldBeEmpty(
            customMessage: $"'{HarnessClientId}'s service account is missing a fab group the "
            + $"'{AdminUsername}' user holds. A harness token minted for a fab the human admin "
            + "can reach would be refused where the password grant it replaces was not.");
    }

    [Fact]
    public void Its_service_account_holds_no_realm_management_role()
    {
        JsonElement serviceAccount = RequireServiceAccountUser();

        if (serviceAccount.TryGetProperty("clientRoles", out JsonElement clientRoles)
            && clientRoles.ValueKind == JsonValueKind.Object)
        {
            clientRoles.TryGetProperty("realm-management", out _).ShouldBeFalse(
                customMessage: $"'{HarnessClientId}'s service account holds realm-management "
                + "roles. It is an application-API caller mirroring the seeded 'admin' user, not "
                + "a Keycloak admin — Keycloak-admin work stays on 'identity-admin' / RealmProbe "
                + "(spec 327 finding 1).");
        }
    }

    [Fact]
    public void Its_description_fits_keycloaks_255_character_limit()
    {
        JsonElement client = RequireClient();
        string description = client.TryGetProperty("description", out JsonElement value)
            ? value.GetString() ?? string.Empty
            : string.Empty;

        description.Length.ShouldBeLessThanOrEqualTo(255,
            customMessage: $"'{HarnessClientId}'s description is {description.Length} characters. "
            + "A description over 255 characters kills the realm import and hangs the whole Aspire "
            + "fixture (project memory: Keycloak realm description limit).");
    }

    private static JsonElement RequireClient()
    {
        JsonElement[] matches = ClientsNamed(HarnessClientId);

        matches.Length.ShouldBe(1,
            customMessage: $"expected exactly one client named '{HarnessClientId}' in the realm "
            + $"import's 'clients' array; found {matches.Length}. Spec 327 (#2511) adds it "
            + "alongside 'identity-admin' — without it there is no client to assert the remaining "
            + "facts against, so this fails for that reason, not a property read on an absent "
            + "client.");

        return matches[0];
    }

    private static JsonElement RequireConsoleClient()
    {
        JsonElement[] matches = ClientsNamed(ConsoleClientId);

        matches.Length.ShouldBe(1,
            customMessage: $"expected exactly one client named '{ConsoleClientId}' in the realm "
            + $"import's 'clients' array; found {matches.Length}.");

        return matches[0];
    }

    private static JsonElement RequireServiceAccountUser()
    {
        JsonElement[] matches = [.. Realm().GetProperty("users").EnumerateArray()
            .Where(user => user.TryGetProperty("serviceAccountClientId", out JsonElement clientId)
                && clientId.ValueKind == JsonValueKind.String
                && clientId.GetString() == HarnessClientId)];

        matches.Length.ShouldBe(1,
            customMessage: $"expected exactly one service-account user for '{HarnessClientId}' in "
            + $"the realm import's 'users' array; found {matches.Length}. Without one there is no "
            + "principal to compare groups or roles against, so this fails for that reason, not a "
            + "property read on an absent user.");

        return matches[0];
    }

    private static JsonElement RequireAdminUser()
    {
        JsonElement[] matches = [.. Realm().GetProperty("users").EnumerateArray()
            .Where(user => user.TryGetProperty("username", out JsonElement username)
                && username.ValueKind == JsonValueKind.String
                && username.GetString() == AdminUsername)];

        matches.Length.ShouldBe(1,
            customMessage: $"expected exactly one '{AdminUsername}' user in the realm import's "
            + $"'users' array; found {matches.Length}.");

        return matches[0];
    }

    private static JsonElement[] ClientsNamed(string clientId) =>
        [.. Clients().Where(client => ClientIdOf(client) == clientId)];

    private static JsonElement.ArrayEnumerator Clients() =>
        Realm().GetProperty("clients").EnumerateArray();

    private static string ClientIdOf(JsonElement client) =>
        client.GetProperty("clientId").GetString() ?? "(unnamed)";

    private static IReadOnlyCollection<string> DefaultScopesOf(JsonElement client) =>
        [.. client.GetProperty("defaultClientScopes").EnumerateArray()
            .Select(scope => scope.GetString() ?? string.Empty)];

    private static IReadOnlyCollection<string> GroupsOf(JsonElement user) =>
        user.TryGetProperty("groups", out JsonElement groups) && groups.ValueKind == JsonValueKind.Array
            ? [.. groups.EnumerateArray().Select(group => group.GetString() ?? string.Empty)]
            : [];

    private static JsonElement Realm() => RealmDocument.RootElement;

    /// <summary>
    /// Parsed once and held: a <see cref="JsonElement"/> is only valid while its
    /// document is alive, and every assertion here reads the same file.
    /// </summary>
    private static readonly JsonDocument RealmDocument = ReadRealm();

    private static JsonDocument ReadRealm()
    {
        DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))
        {
            candidate = candidate.Parent;
        }

        DirectoryInfo root = candidate
            ?? throw new InvalidOperationException(
                $"could not locate the repository root above {AppContext.BaseDirectory}");

        string path = Path.Combine(root.FullName, "src", "AppHost", "Realms", "smart-sentinel-eye-realm.json");
        File.Exists(path).ShouldBeTrue($"the realm should be at {path}");
        return JsonDocument.Parse(File.ReadAllText(path));
    }
}
