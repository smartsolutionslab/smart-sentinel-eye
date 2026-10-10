using System.Text.Json;
using SmartSentinelEye.ServiceDefaults.Authorization;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 330 (#2286), AS-4 — configuration guard for the half of the fix the
/// broker cannot enforce by itself.
///
/// <para>
/// Spec finding 2 (plan.md §2.1) made the plugin defer to <c>acl.txt</c>
/// rather than grant authority itself: an identity that holds
/// <c>sse.events.publish</c> but has no <c>acl.txt</c> write row is still
/// refused. The flip side of that design is that <c>acl.txt</c> can grant a
/// realm client write access to a topic while the scope the whole mechanism
/// is meant to gate stays ungranted — the client authenticates, and its
/// publish succeeds on the strength of the ACL row alone, by coincidence
/// rather than by the scope doing any work. That is <c>scenario-simulator</c>
/// today: it has a wildcard write grant across every fab topic in
/// <c>acl.txt</c> and, until spec 330's realm edit lands, no
/// <c>sse.events.publish</c> in its <c>defaultClientScopes</c>.
/// </para>
///
/// <para>
/// <b>What this cannot see.</b> It reads two static files, not the running
/// broker. A grant that exists in both files but whose Mosquitto callback
/// chain is wired wrong is invisible here — that is
/// <c>MqttPublishScopeIntegrationTests</c>' job. And it only asks the
/// direction described above: a realm client whose scope was revoked while
/// its <c>acl.txt</c> row survives. It says nothing about a device that holds
/// the scope with no <c>acl.txt</c> row at all (AS-3's shape, which is
/// supposed to stay refused) — that is a different claim, covered live.
/// </para>
/// </summary>
public class MqttPublishGrantTests
{
    [Fact]
    public void Every_acl_write_grant_held_by_a_realm_client_carries_the_publish_scope()
    {
        HashSet<string> writeGrantedUsers = AclWriteGrantedUsers();
        Dictionary<string, IReadOnlyCollection<string>> realmClientScopes = RealmClientDefaultScopes();

        string[] qualifying = [.. writeGrantedUsers
            .Where(realmClientScopes.ContainsKey)
            .Order(StringComparer.Ordinal)];

        // Population gate: without at least one acl.txt user that is also a
        // realm client, the assertion below would pass over nothing and this
        // guard would be silently vacuous — exactly the shape spec finding 4
        // exists to catch, one layer deeper.
        qualifying.ShouldNotBeEmpty(
            $"no user with a 'topic write' or 'topic readwrite' grant in {AclPath()} is also a "
            + $"client declared in {RealmPath()}. This guard asserts a relationship between the two "
            + "files and has nothing to check without at least one user satisfying both sides — "
            + "acl.txt and the realm have drifted out of the shape this guard assumes, which needs "
            + "a human look, not a passing test.");

        string[] violators = [.. qualifying
            .Where(clientId => !realmClientScopes[clientId].Contains(Scope.Sse.Events.Publish, StringComparer.Ordinal))];

        violators.ShouldBeEmpty(
            $"{string.Join(", ", violators)} — {(violators.Length == 1 ? "has" : "have")} a 'topic "
            + $"write'/'topic readwrite' grant in {AclPath()} and {(violators.Length == 1 ? "is" : "are")} "
            + $"declared as a client in {RealmPath()}, but do not list '{Scope.Sse.Events.Publish}' in "
            + $"defaultClientScopes. A client that can write to the broker without the scope the "
            + "catalogue and KeycloakScopeBundles.Device advertise is the exact gap issue #2286 "
            + "reports — acl.txt alone, not the scope, is doing the authorising.");
    }

    /// <summary>
    /// Every username that owns at least one <c>topic write</c> or
    /// <c>topic readwrite</c> line in <c>acl.txt</c>. A <c>user X</c> line
    /// opens a block that continues until the next <c>user</c> line or the
    /// end of the file; blank lines and <c>#</c> comments are ignored; a
    /// <c>topic read …</c> line grants nothing this guard cares about.
    /// </summary>
    private static HashSet<string> AclWriteGrantedUsers()
    {
        HashSet<string> users = new(StringComparer.Ordinal);
        string? currentUser = null;

        foreach (string rawLine in File.ReadAllLines(AclPath()))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("user ", StringComparison.Ordinal))
            {
                currentUser = line["user ".Length..].Trim();
                continue;
            }

            if (currentUser is null)
            {
                continue;
            }

            if (line.StartsWith("topic write ", StringComparison.Ordinal)
                || line.StartsWith("topic readwrite ", StringComparison.Ordinal))
            {
                users.Add(currentUser);
            }
        }

        return users;
    }

    /// <summary>Every realm client's <c>clientId</c>, mapped to its <c>defaultClientScopes</c>.</summary>
    private static Dictionary<string, IReadOnlyCollection<string>> RealmClientDefaultScopes() =>
        Realm().GetProperty("clients").EnumerateArray().ToDictionary(
            client => client.GetProperty("clientId").GetString() ?? string.Empty,
            client => (IReadOnlyCollection<string>)
                [.. client.GetProperty("defaultClientScopes").EnumerateArray()
                    .Select(scope => scope.GetString() ?? string.Empty)],
            StringComparer.Ordinal);

    private static JsonElement Realm() => RealmDocument.RootElement;

    /// <summary>
    /// Parsed once and held: a <see cref="JsonElement"/> is only valid while
    /// its document is alive, and the one assertion here reads it twice.
    /// </summary>
    private static readonly JsonDocument RealmDocument = JsonDocument.Parse(File.ReadAllText(RealmPath()));

    private static string AclPath() =>
        Path.Combine(RepositoryRoot().FullName, "src", "AppHost", "mosquitto", "acl.txt");

    private static string RealmPath() =>
        Path.Combine(RepositoryRoot().FullName, "src", "AppHost", "Realms", "smart-sentinel-eye-realm.json");

    /// <summary>
    /// <see cref="AclPath"/> and <see cref="RealmPath"/> build their paths
    /// with <see cref="Path.Combine(string[])"/> over individual segments
    /// (<c>"src"</c>, <c>"AppHost"</c>, …), never a single forward-slash-joined
    /// literal split apart again. <see cref="Path.GetRelativePath(string, string)"/>
    /// is the call this repository has been bitten by — it returns the
    /// platform separator, so a backslash literal compared against its result
    /// is green on Windows and red on Linux CI. This guard never calls it:
    /// every path here is built going forward from segments, with the correct
    /// separator for whichever OS runs it, so there is nothing to normalise.
    /// </summary>
    private static DirectoryInfo RepositoryRoot()
    {
        DirectoryInfo? candidate = new(AppContext.BaseDirectory);
        while (candidate is not null && !File.Exists(Path.Combine(candidate.FullName, "SmartSentinelEye.slnx")))
        {
            candidate = candidate.Parent;
        }

        return candidate
            ?? throw new InvalidOperationException(
                $"could not locate the repository root above {AppContext.BaseDirectory}");
    }
}
