using System.Text.Json;

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
    public void No_account_other_than_the_migration_runner_holds_manage_realm()
    {
        var holders = Realm().GetProperty("users").EnumerateArray()
            .Where(user => RealmManagementRolesOf(user).Contains(ManageRealm, StringComparer.Ordinal))
            .Select(user => user.GetProperty("username").GetString() ?? string.Empty)
            .ToArray();

        holders.ShouldBe(
            [ServiceAccountUsername],
            "manage-realm is a realm-wide authority; ADR-0181 confines it to migration-runner's service account.");
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
