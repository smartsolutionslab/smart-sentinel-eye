using System.Text.Json;
using System.Text.Json.Nodes;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 440 T-C (ADR-0181 §2) — the realm file's side of narrowing the default
/// composite. <c>migration-runner</c> is the one account allowed to hold
/// <c>manage-realm</c>, because the call that takes <c>offline_access</c> out of
/// <c>default-roles-*</c> answers 403 for anything less (spec 080 §2.4).
///
/// <para>
/// <b>What this cannot see.</b> It reads names. That the running realm's composite
/// lacks <c>offline_access</c> is <c>KioskInheritedPrivilegeIntegrationTests</c>' claim,
/// because the file never declares the composite (ADR-0181 §4).
/// </para>
/// </summary>
public class MigrationRunnerClientTests
{
    private const string MigrationRunnerClientId = "migration-runner";

    private const string ServiceAccountUsername = "service-account-migration-runner";

    private const string RealmManagement = "realm-management";

    private const string ManageRealm = "manage-realm";

    /// <summary>Keycloak refuses to import a client description longer than this.</summary>
    private const int DescriptionLimit = 255;

    [Fact]
    public void The_migration_runner_description_fits_the_limit_that_stops_the_realm_importing()
    {
        var description = DescriptionOf(RequireClient());

        description.Length.ShouldBeLessThanOrEqualTo(
            DescriptionLimit,
            "a client description over 255 characters kills the realm import and hangs the whole Aspire fixture.");
    }

    [Fact]
    public void The_migration_runner_description_no_longer_claims_to_be_read_only()
    {
        var description = DescriptionOf(RequireClient());

        description.ShouldNotContain(
            "Read-only",
            Case.Insensitive,
            "the client now holds manage-realm (ADR-0181); a description that says read-only misleads whoever audits the grant.");
    }

    [Fact]
    public void The_migration_runner_service_account_holds_exactly_query_groups_view_users_and_manage_realm()
    {
        var roles = RealmManagementRolesOf(RequireServiceAccount());

        roles.Order(StringComparer.Ordinal).ToArray().ShouldBe(
            ["manage-realm", "query-groups", "view-users"],
            "ADR-0181 §2 grants manage-realm on top of the two read roles and nothing else.");
    }

    [Fact]
    public void The_migration_runner_description_says_it_authenticates_by_signed_jwt()
    {
        var description = DescriptionOf(RequireClient());

        description.ShouldContain(
            "signed JWT",
            Case.Insensitive,
            "ADR-0186: the client holds a realm-wide authority and authenticates by signed JWT; the description is what an auditor reads first.");
    }

    [Fact]
    public void The_migration_runner_client_authenticates_by_signed_jwt()
    {
        AttributeOrProperty(RequireClient(), "clientAuthenticatorType").ShouldBe(
            "client-jwt",
            "ADR-0186 Decision 1: a client secret is readable back through the admin API; a public key is not a credential.");
    }

    [Fact]
    public void The_migration_runner_client_carries_no_secret()
    {
        RequireClient().TryGetProperty("secret", out _).ShouldBeFalse(
            "ADR-0186 Decision 1: its former secret stops working because it is not in the realm any more.");
    }

    [Fact]
    public void The_migration_runner_client_id_is_pinned_so_the_deny_permission_can_name_it()
    {
        RequireClient().TryGetProperty("id", out var id).ShouldBeTrue(
            "ADR-0186 Decision 5: the per-client permission names the client by UUID, so the file must pin it.");
        Guid.TryParse(id.GetString(), out var parsed).ShouldBeTrue("the pinned id should be a UUID.");
        parsed.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void The_migration_runner_client_registers_its_public_key_inline_and_nowhere_else()
    {
        var client = RequireClient();

        Attribute(client, "use.jwks.string").ShouldBe("true");
        Attribute(client, "use.jwks.url").ShouldBe("false");
        Attribute(client, "token.endpoint.auth.signing.alg").ShouldBe("RS256");
    }

    [Fact]
    public void The_migration_runner_client_registers_exactly_one_RS256_key_named_by_its_thumbprint()
    {
        var jwks = JsonDocument.Parse(Attribute(RequireClient(), "jwks.string"));

        var keys = jwks.RootElement.GetProperty("keys").EnumerateArray().ToArray();

        keys.Length.ShouldBe(1, "ADR-0186 Decision 4: exactly one current public key; rotation replaces it.");
        keys[0].GetProperty("kty").GetString().ShouldBe("RSA");
        keys[0].GetProperty("alg").GetString().ShouldBe("RS256");
        keys[0].TryGetProperty("d", out _).ShouldBeFalse("a private exponent in the realm file would be a leaked credential.");
        keys[0].GetProperty("kid").GetString().ShouldBe(
            RealmGrantGraph.Thumbprint(keys[0].GetProperty("n").GetString()!, keys[0].GetProperty("e").GetString()!),
            "the kid is the RFC 7638 thumbprint, which the worker computes from its private key at runtime.");
    }

    [Fact]
    public void No_account_other_than_the_migration_runner_holds_manage_realm_by_any_route()
    {
        RealmGrantGraph.HoldersOf(RealmGrantGraph.ReadRealm(), ManageRealm).ShouldBe(
            [ServiceAccountUsername],
            "manage-realm is a realm-wide authority; ADR-0181 confines it to migration-runner's service account, "
            + "directly, through a group, through a composite role, or through realm-admin.");
    }

    // ---- The guard shown to bite: a deliberately wrong realm must name the holder. ----

    [Fact]
    public void The_holder_guard_sees_a_grant_planted_on_a_group_the_account_belongs_to()
    {
        var realm = RealmGrantGraph.ReadRealm();
        realm["groups"]![0]!["subGroups"]![0]!["clientRoles"] = new JsonObject
        {
            [RealmManagement] = new JsonArray(ManageRealm),
        };

        RealmGrantGraph.HoldersOf(realm, ManageRealm).ShouldContain(
            "operator",
            "operator is a member of /fabs/munich; a grant on that group is a grant to it.");
    }

    [Fact]
    public void The_holder_guard_sees_a_grant_planted_on_a_parent_group_of_the_accounts_group()
    {
        var realm = RealmGrantGraph.ReadRealm();
        realm["groups"]![0]!["clientRoles"] = new JsonObject
        {
            [RealmManagement] = new JsonArray(ManageRealm),
        };

        RealmGrantGraph.HoldersOf(realm, ManageRealm).ShouldContain("operator");
    }

    [Fact]
    public void The_holder_guard_sees_a_grant_planted_inside_a_composite_realm_role()
    {
        var realm = RealmGrantGraph.ReadRealm();
        realm["roles"]!["realm"]![1]!["composites"] = new JsonObject
        {
            ["client"] = new JsonObject { [RealmManagement] = new JsonArray(ManageRealm) },
        };

        RealmGrantGraph.HoldersOf(realm, ManageRealm).ShouldContain(
            "admin",
            "admin holds the 'admin' realm role; a composite that includes manage-realm makes it a holder.");
    }

    [Fact]
    public void The_holder_guard_sees_a_grant_reached_through_a_chain_of_composites()
    {
        var realm = RealmGrantGraph.ReadRealm();
        realm["roles"]!["realm"]![1]!["composites"] = new JsonObject
        {
            ["realm"] = new JsonArray("user"),
        };
        realm["roles"]!["realm"]![0]!["composites"] = new JsonObject
        {
            ["client"] = new JsonObject { [RealmManagement] = new JsonArray(ManageRealm) },
        };

        RealmGrantGraph.HoldersOf(realm, ManageRealm).ShouldContain("operator");
    }

    [Fact]
    public void The_holder_guard_sees_realm_admin_as_holding_every_realm_management_role()
    {
        var realm = RealmGrantGraph.ReadRealm();
        realm["users"]!.AsArray().First(user => user!["username"]!.GetValue<string>() == "operator")!["clientRoles"] =
            new JsonObject { [RealmManagement] = new JsonArray("realm-admin") };

        RealmGrantGraph.HoldersOf(realm, ManageRealm).ShouldContain("operator");
    }

    [Fact]
    public void The_holder_guard_sees_a_grant_planted_on_the_realms_default_roles()
    {
        var realm = RealmGrantGraph.ReadRealm();
        realm["roles"]!["realm"]![0]!["composites"] = new JsonObject
        {
            ["client"] = new JsonObject { [RealmManagement] = new JsonArray(ManageRealm) },
        };

        RealmGrantGraph.HoldersOf(realm, ManageRealm).Count.ShouldBeGreaterThan(
            1,
            "'user' is in the realm's defaultRoles, so every account inherits it.");
    }

    // ---- Spec 440 T161 (AS-22): the modern default role, not only the legacy `defaultRoles` list. ----

    [Fact]
    public void The_holder_guard_sees_a_grant_planted_in_the_composite_of_the_default_role_the_realm_names()
    {
        var realm = RealmGrantGraph.ReadRealm();
        realm.AsObject().Remove("defaultRoles");
        realm["defaultRole"] = new JsonObject { ["name"] = "custom-default" };
        realm["roles"]!["realm"]!.AsArray().Add(new JsonObject
        {
            ["name"] = "custom-default",
            ["composites"] = new JsonObject
            {
                ["client"] = new JsonObject { [RealmManagement] = new JsonArray(ManageRealm) },
            },
        });

        RealmGrantGraph.HoldersOf(realm, ManageRealm).ShouldContain(
            "operator",
            "the realm's defaultRole composite is inherited by every account, whatever the legacy defaultRoles list says.");
    }

    [Fact]
    public void The_holder_guard_sees_a_grant_planted_in_a_role_the_default_role_includes()
    {
        var realm = RealmGrantGraph.ReadRealm();
        realm.AsObject().Remove("defaultRoles");
        realm["defaultRole"] = new JsonObject { ["name"] = "custom-default" };
        realm["roles"]!["realm"]!.AsArray().Add(new JsonObject
        {
            ["name"] = "custom-default",
            ["composites"] = new JsonObject { ["realm"] = new JsonArray("admin") },
        });
        realm["roles"]!["realm"]![1]!["composites"] = new JsonObject
        {
            ["client"] = new JsonObject { [RealmManagement] = new JsonArray(ManageRealm) },
        };

        RealmGrantGraph.HoldersOf(realm, ManageRealm).ShouldContain("operator");
    }

    [Fact]
    public void The_holder_guard_sees_a_grant_planted_in_a_declared_default_roles_composite_without_a_defaultRole_object()
    {
        var realm = RealmGrantGraph.ReadRealm();
        realm.AsObject().Remove("defaultRoles");
        realm.AsObject().Remove("defaultRole");
        realm["roles"]!["realm"]!.AsArray().Add(new JsonObject
        {
            ["name"] = "default-roles-smart-sentinel-eye",
            ["composites"] = new JsonObject
            {
                ["client"] = new JsonObject { [RealmManagement] = new JsonArray(ManageRealm) },
            },
        });

        RealmGrantGraph.HoldersOf(realm, ManageRealm).ShouldContain(
            "operator",
            "a declared default-roles-* composite is read conservatively: Keycloak may bind it, and a guard that misses it fails silent.");
    }

    [Fact]
    public void A_default_role_that_names_no_declared_role_adds_no_holder()
    {
        var realm = RealmGrantGraph.ReadRealm();
        realm["defaultRole"] = new JsonObject { ["name"] = "default-roles-not-declared-anywhere" };

        RealmGrantGraph.HoldersOf(realm, ManageRealm).ShouldBe([ServiceAccountUsername]);
    }

    private static string AttributeOrProperty(JsonElement client, string name)
    {
        client.TryGetProperty(name, out var value).ShouldBeTrue($"the migration-runner client should declare '{name}'.");
        return value.GetString() ?? string.Empty;
    }

    private static string Attribute(JsonElement client, string name)
    {
        client.TryGetProperty("attributes", out var attributes).ShouldBeTrue(
            "the migration-runner client should carry attributes (its registered key).");
        attributes.TryGetProperty(name, out var value).ShouldBeTrue(
            $"the migration-runner client should carry the attribute '{name}'.");
        return value.GetString() ?? string.Empty;
    }

    private static JsonElement RequireClient()
    {
        JsonElement[] matches = [.. Realm().GetProperty("clients").EnumerateArray()
            .Where(client => client.GetProperty("clientId").GetString() == MigrationRunnerClientId)];

        matches.Length.ShouldBe(1, $"the realm should seed exactly one '{MigrationRunnerClientId}' client.");
        return matches[0];
    }

    private static JsonElement RequireServiceAccount()
    {
        JsonElement[] matches = [.. Realm().GetProperty("users").EnumerateArray()
            .Where(user => user.GetProperty("username").GetString() == ServiceAccountUsername)];

        matches.Length.ShouldBe(1, $"the realm should seed exactly one '{ServiceAccountUsername}' user.");
        return matches[0];
    }

    private static string DescriptionOf(JsonElement client) =>
        client.GetProperty("description").GetString() ?? string.Empty;

    private static IReadOnlyCollection<string> RealmManagementRolesOf(JsonElement user) =>
        user.TryGetProperty("clientRoles", out var clientRoles)
            && clientRoles.TryGetProperty(RealmManagement, out var roles)
            && roles.ValueKind == JsonValueKind.Array
            ? [.. roles.EnumerateArray().Select(role => role.GetString() ?? string.Empty)]
            : [];

    private static JsonElement Realm() => RealmDocument.RootElement;

    private static readonly JsonDocument RealmDocument = ReadRealm();

    private static JsonDocument ReadRealm()
    {
        DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))
        {
            candidate = candidate.Parent;
        }

        var root = candidate
            ?? throw new InvalidOperationException(
                $"could not locate the repository root above {AppContext.BaseDirectory}");

        var path = Path.Combine(root.FullName, "src", "AppHost", "Realms", "smart-sentinel-eye-realm.json");
        File.Exists(path).ShouldBeTrue($"the realm should be at {path}");
        return JsonDocument.Parse(File.ReadAllText(path));
    }
}
