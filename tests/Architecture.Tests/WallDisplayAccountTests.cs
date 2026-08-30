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
/// file is the evidence.</b> It fails if the privilege spreads <i>in this
/// file</i>.
/// </para>
///
/// <para>
/// <b>Which is less than it sounds, and the gap is recorded rather than
/// papered over.</b> These read the realm file. In a running realm the provider
/// composes <c>default-roles-smart-sentinel-eye</c>, and that composite
/// <b>includes the offline privilege</b> — so every account created after
/// import inherits it, including the service account of every kiosk enrolled at
/// runtime. Accounts declared in this file are unaffected: they receive exactly
/// the roles they name, which is why <c>operator</c> is refused an offline
/// grant. Both halves were checked against a booted realm.
/// </para>
///
/// <para>
/// It is inert only because no client currently offers the scope, and the
/// feature that would offer one is exactly this one. <b>The realm file cannot
/// close it</b> — a declared <c>default-roles-*</c> composite is discarded on
/// import, which was tried and verified — so it needs a step after import, and
/// nothing here can guard it.
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
    ///
    /// <para>
    /// <b>Every fab, checked against the fabs that exist</b> rather than against
    /// the walls that happen to be declared. The earlier version collected the
    /// groups of whichever wall accounts were present and asserted they looked
    /// like fabs — which passes with one fab covered and three dark, the exact
    /// outcome it reads as ruling out.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_fab_has_a_wall_display_account_that_holds_it()
    {
        string[] fabs = RealmFabGroups();
        fabs.ShouldNotBeEmpty("a realm with no fabs would make this vacuous");

        string[] covered = RealmUsers()
            .Where(user => user.Username.StartsWith("wall-", StringComparison.Ordinal))
            .Where(user => user.RealmRoles.Any(role => role == "offline_access"))
            .SelectMany(user => user.Groups)
            .Distinct()
            .ToArray();

        foreach (string fab in fabs)
        {
            covered.ShouldContain(
                fab,
                $"{fab} has no wall-display account holding the privilege, so its wall drops to a prompt while the others stay up");
        }
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
    /// The kiosk client must <b>not</b> grant the offline scope by default.
    ///
    /// <para>
    /// <b>This guard is inverted from how it started, and the reason is the
    /// finding.</b> Granting it as a default looked like the design: the
    /// application would never name it, so an app build could not be refused by
    /// a realm that had not caught up. But a default scope is <b>mandatory, not
    /// neutral</b> — the provider refuses the grant for any account lacking the
    /// matching role. Verified by booting the realm: <c>operator</c> gets
    /// <c>not_allowed: Offline tokens not allowed for the user or client</c>, so
    /// every human and all six kiosk end-to-end specs were locked out.
    /// </para>
    ///
    /// <para>
    /// The scope is withdrawn while the design is reworked (ADR-0132). This
    /// fails if it comes back as a default without that rework, because the
    /// symptom — nobody can sign in — is one nothing else here would catch.
    /// </para>
    /// </summary>
    [Fact]
    public void The_kiosk_client_does_not_force_the_offline_scope_on_every_account()
    {
        JsonElement kiosk = RealmClients()
            .First(client => client.GetProperty("clientId").GetString() == "kiosk-web");

        string[] defaults = kiosk.GetProperty("defaultClientScopes")
            .EnumerateArray()
            .Select(scope => scope.GetString() ?? string.Empty)
            .ToArray();

        defaults.ShouldNotContain(
            "offline_access",
            "as a default it is mandatory, and every account without the matching role is refused sign-in entirely");
    }

    /// <summary>The fabs the realm declares, which is what "every fab" has to mean.</summary>
    private static string[] RealmFabGroups()
    {
        return Realm().GetProperty("groups")
            .EnumerateArray()
            .Where(group => group.GetProperty("name").GetString() == "fabs")
            .SelectMany(group => group.GetProperty("subGroups").EnumerateArray())
            .Select(fab => fab.GetProperty("path").GetString() ?? string.Empty)
            .ToArray();
    }

    private sealed record RealmUser(string Username, string[] RealmRoles, string[] Groups);

    private static List<RealmUser> RealmUsers()
    {
        JsonElement root = Realm();
        List<RealmUser> users = [];

        foreach (JsonElement user in root.GetProperty("users").EnumerateArray())
        {
            users.Add(new RealmUser(
                user.GetProperty("username").GetString() ?? string.Empty,
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
