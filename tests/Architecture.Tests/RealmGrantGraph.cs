using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 440 FR-015 — who holds a <c>realm-management</c> role in the realm file, by
/// <b>every</b> route the provider honours, not only the direct one:
///
/// <list type="number">
///   <item>a direct <c>clientRoles</c> mapping on the user;</item>
///   <item>membership of a group (or of a group nested under one) whose
///     <c>clientRoles</c>/<c>realmRoles</c> carry it;</item>
///   <item>a realm or client role whose <c>composites</c>, followed transitively,
///     include it — held directly, through a group, or through the realm's
///     <c>defaultRoles</c>, which every account inherits;</item>
///   <item>the built-in <c>realm-admin</c>, which is every <c>realm-management</c>
///     role at once and is not spelled out in the file.</item>
/// </list>
///
/// <para>
/// Works on a <see cref="JsonNode"/> so a guard can be shown to bite: a test hands it
/// a deliberately wrong realm (a grant planted on a group, in a composite, on
/// <c>realm-admin</c>) and watches it report the holder. A guard that has only ever
/// run against a correct file has never been seen to fail.
/// </para>
/// </summary>
internal static class RealmGrantGraph
{
    public const string RealmManagement = "realm-management";

    private const string RealmAdmin = "realm-admin";

    /// <summary>The usernames that hold <paramref name="role"/> of <c>realm-management</c>.</summary>
    public static IReadOnlyList<string> HoldersOf(JsonNode realm, string role) =>
        [.. Users(realm)
            .Where(user => Holds(realm, user, RealmManagement, role))
            .Select(user => user["username"]!.GetValue<string>())
            .Order(StringComparer.Ordinal)];

    public static bool Holds(JsonNode realm, string username, string role) =>
        Users(realm).Any(user => user["username"]!.GetValue<string>() == username
            && Holds(realm, user, RealmManagement, role));

    private static bool Holds(JsonNode realm, JsonNode user, string client, string role)
    {
        var held = Closure(realm, DirectRoles(realm, user));

        return held.Contains((client, role))
            || (client == RealmManagement && held.Contains((RealmManagement, RealmAdmin)));
    }

    /// <summary>
    /// Every (client, role) the user reaches: <c>client == ""</c> is a realm role. Seeds
    /// are the user's own mappings, those of every group it is in (and every
    /// ancestor of those groups), and the realm's default roles.
    /// </summary>
    private static HashSet<(string Client, string Role)> DirectRoles(JsonNode realm, JsonNode user)
    {
        HashSet<(string, string)> seeds = [];

        foreach (var role in Strings(realm["defaultRoles"]))
        {
            seeds.Add((string.Empty, role));
        }

        AddMappings(seeds, user);

        foreach (var path in Strings(user["groups"]))
        {
            foreach (var group in GroupChain(realm, path))
            {
                AddMappings(seeds, group);
            }
        }

        return seeds;
    }

    private static void AddMappings(HashSet<(string, string)> seeds, JsonNode holder)
    {
        foreach (var role in Strings(holder["realmRoles"]))
        {
            seeds.Add((string.Empty, role));
        }

        if (holder["clientRoles"] is JsonObject clientRoles)
        {
            foreach (var (client, roles) in clientRoles)
            {
                foreach (var role in Strings(roles))
                {
                    seeds.Add((client, role));
                }
            }
        }
    }

    private static HashSet<(string Client, string Role)> Closure(
        JsonNode realm, HashSet<(string Client, string Role)> seeds)
    {
        HashSet<(string Client, string Role)> reached = [.. seeds];
        Queue<(string Client, string Role)> pending = new(seeds);

        while (pending.TryDequeue(out var current))
        {
            foreach (var next in CompositesOf(realm, current))
            {
                if (reached.Add(next))
                {
                    pending.Enqueue(next);
                }
            }
        }

        return reached;
    }

    private static IEnumerable<(string Client, string Role)> CompositesOf(
        JsonNode realm, (string Client, string Role) role)
    {
        var definition = role.Client.Length == 0
            ? realm["roles"]?["realm"]?.AsArray().FirstOrDefault(candidate => candidate?["name"]?.GetValue<string>() == role.Role)
            : realm["roles"]?["client"]?[role.Client]?.AsArray()
                .FirstOrDefault(candidate => candidate?["name"]?.GetValue<string>() == role.Role);

        if (definition?["composites"] is not JsonNode composites)
        {
            yield break;
        }

        foreach (var realmRole in Strings(composites["realm"]))
        {
            yield return (string.Empty, realmRole);
        }

        if (composites["client"] is JsonObject clients)
        {
            foreach (var (client, roles) in clients)
            {
                foreach (var clientRole in Strings(roles))
                {
                    yield return (client, clientRole);
                }
            }
        }
    }

    /// <summary>The group at <paramref name="path"/> and each of its ancestors (a child inherits its parents' mappings).</summary>
    private static IEnumerable<JsonNode> GroupChain(JsonNode realm, string path)
    {
        var names = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var level = realm["groups"]?.AsArray();

        foreach (var name in names)
        {
            var group = level?.FirstOrDefault(candidate => candidate?["name"]?.GetValue<string>() == name);
            if (group is null)
            {
                yield break;
            }

            yield return group;
            level = group["subGroups"]?.AsArray();
        }
    }

    private static IEnumerable<JsonNode> Users(JsonNode realm) =>
        realm["users"]?.AsArray().Where(user => user is not null).Select(user => user!) ?? [];

    private static IEnumerable<string> Strings(JsonNode? array) =>
        array is JsonArray values
            ? values.Where(value => value is not null).Select(value => value!.GetValue<string>())
            : [];

    public static JsonNode ReadRealm()
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
        return JsonNode.Parse(File.ReadAllText(path))!;
    }

    /// <summary>RFC 7638 thumbprint of an RSA JWK, computed here and not by the code under test.</summary>
    public static string Thumbprint(string modulus, string exponent)
    {
        var canonical = $$"""{"e":"{{exponent}}","kty":"RSA","n":"{{modulus}}"}""";

        return Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
