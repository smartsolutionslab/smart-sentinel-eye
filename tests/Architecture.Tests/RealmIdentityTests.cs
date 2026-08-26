using System.Text.Json;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards two properties of <c>smart-sentinel-eye-realm.json</c> that were both
/// silently false for as long as the file has existed (spec 042, issue 1885).
///
/// <para>
/// <b>Every client could name a scope that does not exist.</b> The realm supplies
/// its own <c>clientScopes</c> array, which replaces Keycloak's built-in set
/// rather than adding to it, so <c>basic</c>, <c>profile</c>, <c>email</c> and
/// <c>roles</c> were absent — and all eight clients listed all four. Keycloak
/// discarded them and said so, thirty-two times per boot, to nobody.
/// </para>
///
/// <para>
/// <b>And one client could not be attributed.</b> <c>sub</c> reached an
/// authorization-code token only through a mapper on <c>sse.management</c> — a
/// <em>permission</em> — or a hand-added copy. <c>management-web</c> had neither,
/// so <c>ToOperatorIdentifier</c> would have thrown on every one of its writes.
/// </para>
///
/// <para>
/// <b>What this cannot see.</b> It reads names. A mapper that exists and does not
/// fire passes here and fails <c>TokenAttributionIntegrationTests</c>, which is
/// the only place a token is actually minted. Clients created at runtime through
/// the Admin API are not in this file at all; they carry <c>sub</c> from the
/// <c>client_credentials</c> grant, which no file records.
/// </para>
/// </summary>
public class RealmIdentityTests
{
    /// <summary>
    /// The one scope carrying an <c>oidc-sub-mapper</c>. Named with a hyphen
    /// because this realm distinguishes claim carriers (<c>sse-groups</c>,
    /// <c>sse-identity</c>) from permissions (<c>sse.cameras.read</c>) that way,
    /// and had never said so.
    /// </summary>
    private const string IdentityScope = "sse-identity";

    private const string SubjectMapper = "oidc-sub-mapper";

    [Fact]
    public void Every_client_holds_the_identity_scope()
    {
        foreach (JsonElement client in Clients())
        {
            DefaultScopesOf(client).ShouldContain(IdentityScope,
                customMessage: $"client '{ClientIdOf(client)}' cannot put a subject in its access "
                + "token, so every write it makes is refused rather than attributed. Asserted per "
                + "client because the two that worked before this did so by accident, and a sample "
                + "would probably have found one of them (spec 042 FR-007).");
        }
    }

    /// <summary>
    /// <b>The mechanism, not the instance.</b> A name that resolves to nothing is
    /// discarded at import with a warning, so the file can claim a permission a
    /// client does not have and nothing notices. That silence hid two defects in
    /// two weeks.
    /// </summary>
    [Fact]
    public void Every_scope_a_client_names_exists()
    {
        IReadOnlyCollection<string> defined = [.. Realm().GetProperty("clientScopes").EnumerateArray()
            .Select(scope => scope.GetProperty("name").GetString() ?? string.Empty)];

        foreach (JsonElement client in Clients())
        {
            DefaultScopesOf(client).Except(defined, StringComparer.Ordinal).ShouldBeEmpty(
                customMessage: $"client '{ClientIdOf(client)}' names a scope this realm does not "
                + "define. Keycloak discards it on import with a warning nobody reads, so the file "
                + "says the client has a permission it does not (spec 042 FR-008).");
        }
    }

    /// <summary>
    /// A permission that also decides whether its holder can be identified is the
    /// coupling spec 042 removed. It was load-bearing: one client was attributable
    /// <em>only</em> because it held <c>sse.management</c>, so narrowing that
    /// permission would silently have made its writes unattributable — and
    /// narrowing exactly that kind of permission is what spec 041 did.
    /// </summary>
    [Fact]
    public void No_permission_scope_carries_an_identity_mapper()
    {
        foreach (JsonElement scope in Realm().GetProperty("clientScopes").EnumerateArray())
        {
            string name = scope.GetProperty("name").GetString() ?? string.Empty;
            if (!name.StartsWith("sse.", StringComparison.Ordinal))
            {
                continue;
            }

            scope.TryGetProperty("protocolMappers", out _).ShouldBeFalse(
                customMessage: $"'{name}' grants a permission and must not also decide identity. "
                + $"Identity belongs to '{IdentityScope}' (spec 042 FR-006).");
        }
    }

    /// <summary>
    /// <b>SC-006.</b> Spec 041 put a mapper directly on one client as a narrow fix
    /// for a screen that could not show video. A private copy of a shared fact is
    /// how the two drift apart.
    /// </summary>
    [Fact]
    public void No_client_carries_its_own_mapper()
    {
        foreach (JsonElement client in Clients())
        {
            client.TryGetProperty("protocolMappers", out _).ShouldBeFalse(
                customMessage: $"client '{ClientIdOf(client)}' carries its own protocol mapper. "
                + $"One definition supplies the subject — '{IdentityScope}' — and no client keeps "
                + "a second copy of it (spec 042 FR-005).");
        }
    }

    /// <summary>
    /// The identity scope grants nothing, so it must not appear among what a
    /// caller may do. <c>sse-groups</c> already sets this; nothing said why.
    /// </summary>
    [Fact]
    public void The_identity_scope_grants_nothing_and_says_so()
    {
        JsonElement scope = Realm().GetProperty("clientScopes").EnumerateArray()
            .FirstOrDefault(candidate => candidate.GetProperty("name").GetString() == IdentityScope);

        scope.ValueKind.ShouldBe(JsonValueKind.Object, $"the realm should define '{IdentityScope}'");

        scope.GetProperty("attributes").GetProperty("include.in.token.scope").GetString()
            .ShouldBe("false", customMessage: "a scope that grants nothing must not be reported as "
            + "something the caller may do.");

        scope.GetProperty("protocolMappers").EnumerateArray()
            .Select(mapper => mapper.GetProperty("protocolMapper").GetString())
            .ShouldContain(SubjectMapper);
    }

    private static JsonElement.ArrayEnumerator Clients() =>
        Realm().GetProperty("clients").EnumerateArray();

    private static string ClientIdOf(JsonElement client) =>
        client.GetProperty("clientId").GetString() ?? "(unnamed)";

    private static IReadOnlyCollection<string> DefaultScopesOf(JsonElement client) =>
        [.. client.GetProperty("defaultClientScopes").EnumerateArray()
            .Select(scope => scope.GetString() ?? string.Empty)];

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
