using System.Text.Json;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards who may mint a long-lived credential (spec 050).
///
/// <para>
/// <b>This is the requirement the feature was refused over once.</b> Spec 049
/// declined to escape the session ceiling because the privilege that does it
/// would have gone to the operator account, letting every operator mint
/// long-lived credentials. It is acceptable here only because it reaches
/// accounts that do nothing but show cameras on a wall.
/// </para>
///
/// <para>
/// So <b>"we only edited the wall-display accounts" is an argument, and this
/// file is the evidence.</b> It fails if the privilege ever spreads.
/// </para>
/// </summary>
public class WallDisplayAccountTests
{
    /// <summary>
    /// The privilege reaches wall displays and nobody else.
    /// </summary>
    [Fact]
    public void Only_wall_display_accounts_may_mint_a_long_lived_credential()
    {
        List<RealmUser> users = RealmUsers();

        string[] privileged = users
            .Where(user => user.RealmRoles.Any(role => role == "offline_access"))
            .Select(user => user.Username)
            .ToArray();

        privileged.ShouldNotBeEmpty("the wall-display accounts should hold it, or no wall stays up");

        privileged.ShouldAllBe(
            username => username.StartsWith("wall-", StringComparison.Ordinal),
            "a long-lived credential must not be mintable by anyone who is not a wall display");
    }

    /// <summary>
    /// Stated the other way round, because the assertion above passes if the
    /// wall-display accounts are deleted and nothing holds the privilege at all.
    /// </summary>
    [Fact]
    public void Every_fab_has_a_wall_display_account_that_holds_it()
    {
        List<RealmUser> users = RealmUsers();

        string[] fabsWithWalls = users
            .Where(user => user.Username.StartsWith("wall-", StringComparison.Ordinal))
            .Where(user => user.RealmRoles.Any(role => role == "offline_access"))
            .SelectMany(user => user.Groups)
            .ToArray();

        fabsWithWalls.ShouldNotBeEmpty();
        fabsWithWalls.ShouldAllBe(
            group => group.StartsWith("/fabs/", StringComparison.Ordinal),
            "a wall-display account is scoped to its fab by group membership, which is what stops one screen seeing another fab");
    }

    /// <summary>
    /// <b>Each account sees one fab.</b> Fab scoping comes from the account
    /// rather than the client, so an account in two fabs would let a screen see
    /// cameras it has no business showing.
    /// </summary>
    [Fact]
    public void A_wall_display_account_belongs_to_exactly_one_fab()
    {
        foreach (RealmUser wall in RealmUsers().Where(u => u.Username.StartsWith("wall-", StringComparison.Ordinal)))
        {
            wall.Groups.Length.ShouldBe(
                1,
                $"{wall.Username} should see exactly one fab, and sees {wall.Groups.Length}");
        }
    }

    /// <summary>
    /// The application never names the scope, so the realm has to grant it by
    /// default.
    ///
    /// <para>
    /// <b>Default rather than optional, and the distinction is not cosmetic.</b>
    /// Requesting a scope the realm has not granted fails the <em>entire</em>
    /// sign-in — no token, the screen never leaves the login form — which is how
    /// every kiosk went down during spec 049. A default scope leaves nothing in
    /// the application to be refused.
    /// </para>
    /// </summary>
    [Fact]
    public void The_kiosk_client_grants_the_scope_without_the_application_asking()
    {
        JsonElement kiosk = RealmClients()
            .First(client => client.GetProperty("clientId").GetString() == "kiosk-web");

        string[] defaults = kiosk.GetProperty("defaultClientScopes")
            .EnumerateArray()
            .Select(scope => scope.GetString() ?? string.Empty)
            .ToArray();

        defaults.ShouldContain("offline_access", "a screen cannot stay up without it, and the app must not have to ask");

        // Optional scopes are the ones a caller names. Nothing should be there:
        // anything that has to be asked for can be refused.
        if (kiosk.TryGetProperty("optionalClientScopes", out JsonElement optional))
        {
            optional.EnumerateArray()
                .Select(scope => scope.GetString())
                .ShouldNotContain("offline_access", "as an optional scope the application would have to name it, and could be refused");
        }
    }

    private sealed record RealmUser(string Username, string[] RealmRoles, string[] Groups);

    private static List<RealmUser> RealmUsers()
    {
        JsonElement root = Realm();
        List<RealmUser> users = [];

        foreach (JsonElement user in root.GetProperty("users").EnumerateArray())
        {
            // The realm file carries a documentation entry among the users; it
            // has no username and is not an account.
            if (!user.TryGetProperty("username", out JsonElement username))
            {
                continue;
            }

            users.Add(new RealmUser(
                username.GetString() ?? string.Empty,
                Strings(user, "realmRoles"),
                Strings(user, "groups")));
        }

        return users;
    }

    private static JsonElement.ArrayEnumerator RealmClients() => Realm().GetProperty("clients").EnumerateArray();

    private static string[] Strings(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value)
            ? value.EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray()
            : [];

    private static JsonElement Realm()
    {
        DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))
        {
            candidate = candidate.Parent;
        }

        DirectoryInfo root = candidate
            ?? throw new InvalidOperationException($"could not locate the repository root above {AppContext.BaseDirectory}");

        string path = Path.Combine(root.FullName, "src", "AppHost", "Realms", "smart-sentinel-eye-realm.json");
        File.Exists(path).ShouldBeTrue($"the realm should be at {path}");

        // The file carries a byte-order mark, which the JSON reader rejects.
        string text = File.ReadAllText(path).TrimStart('﻿');
        return JsonDocument.Parse(text).RootElement;
    }
}
