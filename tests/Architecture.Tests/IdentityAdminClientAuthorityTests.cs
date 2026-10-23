using System.Text.Json;
using System.Text.Json.Nodes;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 440 T-F (ADR-0186 Decision 6, AS-16) — the guard against the silent regression.
///
/// <para>
/// ADR-0186 measured that a realm-management role is <b>not</b> restricted by a
/// fine-grained admin permission: with <c>manage-clients</c> still held, the per-client deny
/// on migration-runner is bypassed and both measured breaks succeed. Dropping the pinned
/// client id fails loud (Keycloak will not start); re-adding <c>manage-clients</c> or
/// <c>view-clients</c> to <c>identity-admin</c> fails <b>silent</b>. Only this test catches it.
/// </para>
///
/// <para>
/// <b>Shown to bite, not asserted to bite.</b> Each check is a function of a realm
/// document, run against the real file (the first block) and against a small
/// known-good realm and deliberate corruptions of it (the second). A guard that has only
/// ever read a correct file has never been seen to fail.
/// </para>
///
/// <para>
/// The authorization-settings reader tolerates the two shapes Keycloak writes for the same
/// thing: a config value as a JSON-encoded string or as an array, and a policy or resource
/// referenced by name or by id. It does not tolerate a missing deny.
/// </para>
/// </summary>
public class IdentityAdminClientAuthorityTests
{
    private const string IdentityAdminAccount = "service-account-identity-admin";

    private const string MigrationRunnerClient = "migration-runner";

    private const string AdminPermissionsClient = "admin-permissions";

    private const string PinnedId = "6f1c1a52-6b7a-4c0e-9b52-0d6f1b5a8e11";

    private static readonly string[] DeniedScopes =
    [
        "view",
        "manage",
        "map-roles",
        "map-roles-composite",
        "map-roles-client-scope",
    ];

    // ---- the real realm file ----

    [Theory]
    [InlineData("manage-clients")]
    [InlineData("view-clients")]
    public void Identity_admin_holds_no_client_administration_role_by_any_route(string role)
    {
        RoleViolations(RealmGrantGraph.ReadRealm(), role).ShouldBeEmpty(
            $"holding {role} (directly, through a group, a composite or realm-admin) bypasses the migration-runner deny silently (ADR-0186 Decision 6).");
    }

    [Fact]
    public void The_realm_switches_fine_grained_admin_permissions_on()
    {
        EnablementViolations(RealmGrantGraph.ReadRealm()).ShouldBeEmpty();
    }

    [Fact]
    public void Identity_admin_is_denied_every_scope_on_the_pinned_migration_runner_client_by_a_negative_policy()
    {
        DenyViolations(RealmGrantGraph.ReadRealm()).ShouldBeEmpty();
    }

    // ---- the guard shown to bite ----

    [Fact]
    public void A_correct_realm_has_no_violations_of_any_kind()
    {
        var realm = GoodRealm();

        RoleViolations(realm, "manage-clients").ShouldBeEmpty();
        RoleViolations(realm, "view-clients").ShouldBeEmpty();
        EnablementViolations(realm).ShouldBeEmpty();
        DenyViolations(realm).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("manage-clients")]
    [InlineData("view-clients")]
    public void A_role_mapped_directly_is_seen(string role)
    {
        var realm = GoodRealm();
        Account(realm, IdentityAdminAccount)["clientRoles"]!["realm-management"]!.AsArray().Add(role);

        RoleViolations(realm, role).ShouldNotBeEmpty();
    }

    [Fact]
    public void A_role_granted_through_a_group_is_seen()
    {
        var realm = GoodRealm();
        realm["groups"]![0]!["subGroups"]![0]!["clientRoles"] = new JsonObject
        {
            ["realm-management"] = new JsonArray("manage-clients"),
        };
        Account(realm, IdentityAdminAccount)["groups"] = new JsonArray("/fabs/munich");

        RoleViolations(realm, "manage-clients").ShouldNotBeEmpty();
    }

    [Fact]
    public void A_role_granted_through_a_composite_realm_role_is_seen()
    {
        var realm = GoodRealm();
        realm["roles"]!["realm"]!.AsArray().Add(new JsonObject
        {
            ["name"] = "client-supervisor",
            ["composites"] = new JsonObject
            {
                ["client"] = new JsonObject { ["realm-management"] = new JsonArray("view-clients") },
            },
        });
        Account(realm, IdentityAdminAccount)["realmRoles"] = new JsonArray("client-supervisor");

        RoleViolations(realm, "view-clients").ShouldNotBeEmpty();
    }

    [Fact]
    public void Realm_admin_counts_as_holding_both_roles()
    {
        var realm = GoodRealm();
        Account(realm, IdentityAdminAccount)["clientRoles"]!["realm-management"]!.AsArray().Add("realm-admin");

        RoleViolations(realm, "manage-clients").ShouldNotBeEmpty();
        RoleViolations(realm, "view-clients").ShouldNotBeEmpty();
    }

    [Fact]
    public void Disabled_fine_grained_permissions_are_seen()
    {
        var realm = GoodRealm();
        realm["adminPermissionsEnabled"] = false;

        EnablementViolations(realm).ShouldNotBeEmpty();
    }

    [Fact]
    public void Absent_fine_grained_permissions_are_seen()
    {
        var realm = GoodRealm();
        realm.AsObject().Remove("adminPermissionsEnabled");

        EnablementViolations(realm).ShouldNotBeEmpty();
    }

    [Fact]
    public void A_positive_policy_in_place_of_the_negative_one_is_seen()
    {
        var realm = GoodRealm();
        Policy(realm, "identity-admin-user")["logic"] = "POSITIVE";

        DenyViolations(realm).ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("view")]
    [InlineData("manage")]
    [InlineData("map-roles")]
    [InlineData("map-roles-composite")]
    [InlineData("map-roles-client-scope")]
    public void A_scope_missing_from_the_deny_is_seen(string scope)
    {
        var realm = GoodRealm();
        var permission = Policy(realm, "deny-migration-runner");
        var scopes = Parse(permission["config"]!["scopes"]).Where(candidate => candidate != scope);
        permission["config"]!["scopes"] = JsonSerializer.Serialize(scopes);

        DenyViolations(realm).ShouldNotBeEmpty();
    }

    [Fact]
    public void A_deny_that_names_some_other_client_is_seen()
    {
        var realm = GoodRealm();
        var permission = Policy(realm, "deny-migration-runner");
        permission["config"]!["resources"] = new JsonArray("00000000-0000-4000-8000-000000000001").ToJsonString();

        DenyViolations(realm).ShouldNotBeEmpty();
    }

    [Fact]
    public void A_deny_whose_policy_names_a_different_user_is_seen()
    {
        var realm = GoodRealm();
        Policy(realm, "identity-admin-user")["config"]!["users"] = new JsonArray("operator").ToJsonString();

        DenyViolations(realm).ShouldNotBeEmpty();
    }

    [Fact]
    public void A_missing_deny_permission_is_seen()
    {
        var realm = GoodRealm();
        var policies = AuthorizationSettings(realm)["policies"]!.AsArray();
        policies.Remove(Policy(realm, "deny-migration-runner"));

        DenyViolations(realm).ShouldNotBeEmpty();
    }

    [Fact]
    public void A_migration_runner_client_without_a_pinned_id_is_seen()
    {
        var realm = GoodRealm();
        Client(realm, MigrationRunnerClient).AsObject().Remove("id");

        DenyViolations(realm).ShouldNotBeEmpty();
    }

    [Fact]
    public void A_pinned_id_the_permission_does_not_name_is_seen()
    {
        var realm = GoodRealm();
        Client(realm, MigrationRunnerClient)["id"] = "11111111-1111-4111-8111-111111111111";

        DenyViolations(realm).ShouldNotBeEmpty();
    }

    // ---- the checks ----

    private static List<string> RoleViolations(JsonNode realm, string role) =>
        RealmGrantGraph.Holds(realm, IdentityAdminAccount, role)
            ? [$"{IdentityAdminAccount} holds realm-management/{role}"]
            : [];

    private static List<string> EnablementViolations(JsonNode realm) =>
        realm["adminPermissionsEnabled"] is JsonValue enabled
            && enabled.TryGetValue<bool>(out var value)
            && value
            ? []
            : ["the realm does not set adminPermissionsEnabled to true"];

    private static List<string> DenyViolations(JsonNode realm)
    {
        List<string> violations = [];

        var pinned = Client(realm, MigrationRunnerClient)["id"]?.GetValue<string>();
        if (string.IsNullOrEmpty(pinned))
        {
            return ["the migration-runner client has no pinned id, so no permission can name it"];
        }

        var settings = AdminPermissionsAuthorization(realm);
        if (settings is null)
        {
            return ["the admin-permissions client carries no authorizationSettings"];
        }

        var permissions = (settings["policies"]?.AsArray() ?? [])
            .Where(policy => policy!["type"]!.GetValue<string>() == "scope" && TargetsClient(settings, policy, pinned))
            .ToArray();

        foreach (var scope in DeniedScopes)
        {
            var denied = permissions.Any(permission =>
                Parse(permission!["config"]!["scopes"]).Contains(scope)
                && AppliedPolicies(settings, permission).Any(IsNegativePolicyNamingIdentityAdmin));

            if (!denied)
            {
                violations.Add(
                    $"no permission on client {pinned} denies '{scope}' to {IdentityAdminAccount} through a NEGATIVE user policy");
            }
        }

        foreach (var permission in permissions)
        {
            violations.AddRange(AppliedPolicies(settings, permission!)
                .Where(policy => NamesIdentityAdmin(policy) && policy["logic"]?.GetValue<string>() != "NEGATIVE")
                .Select(policy => $"policy '{policy["name"]}' names {IdentityAdminAccount} on {pinned} without NEGATIVE logic"));
        }

        return violations;
    }

    private static bool IsNegativePolicyNamingIdentityAdmin(JsonNode policy) =>
        NamesIdentityAdmin(policy) && policy["logic"]?.GetValue<string>() == "NEGATIVE";

    private static bool NamesIdentityAdmin(JsonNode policy) =>
        policy["type"]?.GetValue<string>() == "user"
        && Parse(policy["config"]?["users"]).Contains(IdentityAdminAccount);

    private static IEnumerable<JsonNode> AppliedPolicies(JsonNode settings, JsonNode permission)
    {
        var referenced = Parse(permission["config"]?["applyPolicies"]);

        return (settings["policies"]?.AsArray() ?? [])
            .Where(policy => policy is not null
                && (referenced.Contains(policy["name"]?.GetValue<string>() ?? "\0")
                    || referenced.Contains(policy["id"]?.GetValue<string>() ?? "\0")))
            .Select(policy => policy!);
    }

    private static bool TargetsClient(JsonNode settings, JsonNode? permission, string clientId)
    {
        var entries = Parse(permission?["config"]?["resources"]);
        var resources = settings["resources"]?.AsArray() ?? [];

        return entries.Any(entry => entry == clientId
            || resources.Any(resource => resource!["name"]?.GetValue<string>() == clientId
                && (resource["_id"]?.GetValue<string>() == entry || resource["name"]?.GetValue<string>() == entry)));
    }

    /// <summary>A config value is a JSON array, or a string holding one; both mean the same.</summary>
    private static List<string> Parse(JsonNode? value) =>
        value switch
        {
            JsonArray array => [.. array.Select(item => item!.GetValue<string>())],
            JsonValue text when text.TryGetValue<string>(out var encoded)
                && JsonNode.Parse(encoded) is JsonArray parsed => [.. parsed.Select(item => item!.GetValue<string>())],
            _ => [],
        };

    private static JsonNode? AdminPermissionsAuthorization(JsonNode realm) =>
        realm["clients"]!.AsArray()
            .FirstOrDefault(client => client!["clientId"]!.GetValue<string>() == AdminPermissionsClient)
            ?["authorizationSettings"];

    private static JsonNode AuthorizationSettings(JsonNode realm) => AdminPermissionsAuthorization(realm)!;

    private static JsonNode Policy(JsonNode realm, string name) =>
        AuthorizationSettings(realm)["policies"]!.AsArray()
            .First(policy => policy!["name"]!.GetValue<string>() == name)!;

    private static JsonNode Client(JsonNode realm, string clientId) =>
        realm["clients"]!.AsArray().First(client => client!["clientId"]!.GetValue<string>() == clientId)!;

    private static JsonNode Account(JsonNode realm, string username) =>
        realm["users"]!.AsArray().First(user => user!["username"]!.GetValue<string>() == username)!;

    /// <summary>
    /// The smallest realm that satisfies Decision 5 and Decision 6, in the shape Keycloak
    /// writes: pins the shape the checks above read, so a corruption is a corruption of
    /// something that was otherwise correct.
    /// </summary>
    private static JsonNode GoodRealm() =>
        JsonNode.Parse(
            $$"""
            {
              "realm": "smart-sentinel-eye",
              "adminPermissionsEnabled": true,
              "defaultRoles": ["user"],
              "roles": { "realm": [ { "name": "user" }, { "name": "admin" } ] },
              "groups": [ { "name": "fabs", "subGroups": [ { "name": "munich" } ] } ],
              "clients": [
                { "clientId": "{{MigrationRunnerClient}}", "id": "{{PinnedId}}" },
                {
                  "clientId": "{{AdminPermissionsClient}}",
                  "authorizationSettings": {
                    "resources": [ { "name": "{{PinnedId}}", "type": "Clients", "_id": "res-1" } ],
                    "policies": [
                      {
                        "id": "pol-1",
                        "name": "identity-admin-user",
                        "type": "user",
                        "logic": "NEGATIVE",
                        "config": { "users": "[\"{{IdentityAdminAccount}}\"]" }
                      },
                      {
                        "id": "pol-2",
                        "name": "deny-migration-runner",
                        "type": "scope",
                        "logic": "POSITIVE",
                        "config": {
                          "resources": "[\"{{PinnedId}}\"]",
                          "scopes": "[\"view\",\"manage\",\"map-roles\",\"map-roles-composite\",\"map-roles-client-scope\"]",
                          "applyPolicies": "[\"identity-admin-user\"]"
                        }
                      }
                    ]
                  }
                }
              ],
              "users": [
                {
                  "username": "{{IdentityAdminAccount}}",
                  "clientRoles": { "realm-management": [ "manage-users", "view-users", "query-clients", "query-users", "query-groups" ] }
                },
                {
                  "username": "service-account-migration-runner",
                  "clientRoles": { "realm-management": [ "query-groups", "view-users", "manage-realm" ] }
                },
                { "username": "operator", "realmRoles": [ "user" ] }
              ]
            }
            """)!;
}
