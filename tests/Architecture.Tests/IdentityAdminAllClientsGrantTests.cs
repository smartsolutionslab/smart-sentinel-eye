using System.Text.Json;
using System.Text.Json.Nodes;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 440 / ADR-0186 — the positive half of <see cref="IdentityAdminClientAuthorityTests"/>.
/// That class proves identity-admin is denied on the pinned migration-runner client; nothing
/// there proves identity-admin is still <b>granted</b> view and manage on every other client.
/// Deleting the grant would leave the deny green while identity-admin lost client administration.
/// Each check runs against the real realm file and against in-memory corruptions of it.
/// </summary>
public class IdentityAdminAllClientsGrantTests
{
    private const string IdentityAdminAccount = "service-account-identity-admin";

    private const string AdminPermissionsClient = "admin-permissions";

    private const string AllClientsResource = "Clients";

    private static readonly string[] GrantedScopes = ["view", "manage"];

    [Fact]
    public void Identity_admin_is_granted_view_and_manage_on_all_clients_by_a_positive_user_policy()
    {
        GrantViolations(RealmGrantGraph.ReadRealm()).ShouldBeEmpty();
    }

    // Spec 455: the realm must say explicitly that the admin-permissions client does not allow
    // remote resource management; a stored `true` or an absent property are both violations.
    [Fact]
    public void The_admin_permissions_client_declares_allow_remote_resource_management_false()
    {
        RemoteManagementViolations(RealmGrantGraph.ReadRealm()).ShouldBeEmpty();
    }

    [Fact]
    public void An_allow_remote_resource_management_of_true_is_seen()
    {
        var realm = RealmGrantGraph.ReadRealm();
        AuthorizationSettings(realm)["allowRemoteResourceManagement"] = true;

        RemoteManagementViolations(realm).ShouldNotBeEmpty();
    }

    [Fact]
    public void A_missing_allow_remote_resource_management_is_seen()
    {
        var realm = RealmGrantGraph.ReadRealm();
        AuthorizationSettings(realm).AsObject().Remove("allowRemoteResourceManagement");

        RemoteManagementViolations(realm).ShouldNotBeEmpty();
    }

    [Fact]
    public void A_removed_all_clients_grant_is_seen()
    {
        var realm = RealmGrantGraph.ReadRealm();
        var policies = Policies(realm);
        policies.Remove(AllClientsGrant(realm));

        GrantViolations(realm).ShouldNotBeEmpty();
    }

    [Fact]
    public void A_grant_applying_a_negative_policy_is_seen()
    {
        var realm = RealmGrantGraph.ReadRealm();
        Policies(realm).First(policy => policy!["name"]!.GetValue<string>() == "identity-admin-user")!["logic"] = "NEGATIVE";

        GrantViolations(realm).ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("view")]
    [InlineData("manage")]
    public void A_grant_missing_a_scope_is_seen(string scope)
    {
        var realm = RealmGrantGraph.ReadRealm();
        var config = AllClientsGrant(realm)["config"]!;
        config["scopes"] = JsonSerializer.Serialize(Parse(config["scopes"]).Where(candidate => candidate != scope));

        GrantViolations(realm).ShouldNotBeEmpty();
    }

    [Fact]
    public void A_grant_targeting_some_other_resource_is_seen()
    {
        var realm = RealmGrantGraph.ReadRealm();
        AllClientsGrant(realm)["config"]!["resources"] = new JsonArray("some-other-resource").ToJsonString();

        GrantViolations(realm).ShouldNotBeEmpty();
    }

    private static JsonNode AuthorizationSettings(JsonNode realm) =>
        realm["clients"]!.AsArray()
            .First(client => client!["clientId"]!.GetValue<string>() == AdminPermissionsClient)!
            ["authorizationSettings"]!;

    private static List<string> RemoteManagementViolations(JsonNode realm)
    {
        var setting = AuthorizationSettings(realm)["allowRemoteResourceManagement"];

        return setting is JsonValue value && value.TryGetValue<bool>(out var allowed) && !allowed
            ? []
            : ["the admin-permissions client does not declare allowRemoteResourceManagement as false"];
    }

    private static List<string> GrantViolations(JsonNode realm)
    {
        var settings = realm["clients"]!.AsArray()
            .FirstOrDefault(client => client!["clientId"]!.GetValue<string>() == AdminPermissionsClient)
            ?["authorizationSettings"];
        if (settings is null)
        {
            return ["the admin-permissions client carries no authorizationSettings"];
        }

        var permissions = (settings["policies"]?.AsArray() ?? [])
            .Where(policy => policy!["type"]!.GetValue<string>() == "scope" && TargetsAllClients(settings, policy))
            .ToArray();

        return [.. GrantedScopes
            .Where(scope => !permissions.Any(permission =>
                Parse(permission!["config"]!["scopes"]).Contains(scope)
                && Applied(settings, permission).Any(IsPositivePolicyNamingIdentityAdmin)))
            .Select(scope => $"no permission on all clients grants '{scope}' to {IdentityAdminAccount} through a POSITIVE user policy")];
    }

    private static bool IsPositivePolicyNamingIdentityAdmin(JsonNode policy) =>
        policy["type"]?.GetValue<string>() == "user"
        && policy["logic"]?.GetValue<string>() == "POSITIVE"
        && Parse(policy["config"]?["users"]).Contains(IdentityAdminAccount);

    private static bool TargetsAllClients(JsonNode settings, JsonNode? permission)
    {
        var entries = Parse(permission?["config"]?["resources"]);
        var resources = settings["resources"]?.AsArray() ?? [];

        return entries.Any(entry => resources.Any(resource =>
            resource!["type"]?.GetValue<string>() == AllClientsResource
            && resource["name"]?.GetValue<string>() == AllClientsResource
            && (resource["name"]?.GetValue<string>() == entry || resource["_id"]?.GetValue<string>() == entry)));
    }

    private static IEnumerable<JsonNode> Applied(JsonNode settings, JsonNode permission)
    {
        var referenced = Parse(permission["config"]?["applyPolicies"]);

        return (settings["policies"]?.AsArray() ?? [])
            .Where(policy => policy is not null
                && (referenced.Contains(policy["name"]?.GetValue<string>() ?? "\0")
                    || referenced.Contains(policy["id"]?.GetValue<string>() ?? "\0")))
            .Select(policy => policy!);
    }

    private static JsonArray Policies(JsonNode realm) =>
        realm["clients"]!.AsArray()
            .First(client => client!["clientId"]!.GetValue<string>() == AdminPermissionsClient)!
            ["authorizationSettings"]!["policies"]!.AsArray();

    private static JsonNode AllClientsGrant(JsonNode realm) =>
        Policies(realm).First(policy => policy!["name"]!.GetValue<string>() == "identity-admin-all-clients")!;

    private static List<string> Parse(JsonNode? value) =>
        value switch
        {
            JsonArray array => [.. array.Select(item => item!.GetValue<string>())],
            JsonValue text when text.TryGetValue<string>(out var encoded)
                && JsonNode.Parse(encoded) is JsonArray parsed => [.. parsed.Select(item => item!.GetValue<string>())],
            _ => [],
        };
}
