using System.Text.Json;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 069 — the realm puts this product's API in the tokens it issues.
///
/// <para>
/// <b>Why a scope and not a mapper per client.</b> The audience has to reach
/// every token the realm mints, and there are nine clients that mint one. A copy
/// of the mapper on each is nine places to forget — and spec 042 FR-005 already
/// forbids a client carrying its own mapper, for exactly the reason a private
/// copy of a shared fact drifts. So the mapper lives on one client scope,
/// <c>sse-audience</c>, mirroring how <c>sse-identity</c> supplies the subject.
/// </para>
///
/// <para>
/// <b>What this cannot see.</b> It reads names, exactly as
/// <c>RealmIdentityTests</c> does. A mapper that exists and does not fire — a
/// mistyped config key is discarded at import in silence — passes here and fails
/// <c>TokenAudienceIntegrationTests</c>, which is the only place a token is
/// actually minted. Clients created at runtime through the Admin API are not in
/// this file at all; <c>RuntimeClientAudienceTests</c> covers those.
/// </para>
/// </summary>
public class RealmAudienceTests
{
    /// <summary>
    /// The scope carrying the <c>oidc-audience-mapper</c>. Hyphenated, because
    /// this realm distinguishes claim carriers (<c>sse-groups</c>,
    /// <c>sse-identity</c>) from permissions (<c>sse.cameras.read</c>) that way.
    /// </summary>
    private const string AudienceScope = "sse-audience";

    private const string AudienceMapper = "oidc-audience-mapper";

    /// <summary>
    /// Spelt out here and again in <c>BearerAudienceTests</c>, which reads it off
    /// the options the services build. Two independent spellings, deliberately:
    /// a shared constant asserted against itself would pass whatever it was
    /// changed to, while these two fail the moment the realm and the services
    /// disagree (spec 069 FR-009).
    /// </summary>
    private const string ApiAudience = "smart-sentinel-eye-api";

    [Fact]
    public void The_realm_defines_an_audience_scope()
    {
        JsonElement scope = ScopeNamed(AudienceScope);

        scope.ValueKind.ShouldBe(JsonValueKind.Object,
            $"the realm should define '{AudienceScope}'");

        scope.GetProperty("attributes").GetProperty("include.in.token.scope").GetString()
            .ShouldBe("false", customMessage: "the scope grants nothing — it carries a claim — so "
            + "it must not be reported as something the caller may do.");

        scope.GetProperty("protocolMappers").EnumerateArray()
            .Select(mapper => mapper.GetProperty("protocolMapper").GetString())
            .ShouldHaveSingleItem().ShouldBe(AudienceMapper);
    }

    [Fact]
    public void The_audience_scope_names_this_products_api()
    {
        JsonElement scope = ScopeNamed(AudienceScope);
        scope.ValueKind.ShouldBe(JsonValueKind.Object,
            $"the realm should define '{AudienceScope}'");

        JsonElement config = scope
            .GetProperty("protocolMappers").EnumerateArray()
            .Single(mapper => mapper.GetProperty("protocolMapper").GetString() == AudienceMapper)
            .GetProperty("config");

        config.GetProperty("included.custom.audience").GetString().ShouldBe(
            ApiAudience,
            customMessage: "the realm and the services must name the same API. This literal is "
            + "spelt out a second time in BearerAudienceTests, so changing one source without the "
            + "other fails (spec 069 FR-009).");

        config.GetProperty("access.token.claim").GetString().ShouldBe(
            "true", customMessage: "the audience is validated on the access token, so a mapper "
            + "that writes it only to the ID token changes nothing.");
    }

    [Fact]
    public void Every_client_holds_the_audience_scope()
    {
        foreach (JsonElement client in Clients())
        {
            DefaultScopesOf(client).ShouldContain(AudienceScope,
                customMessage: $"client '{ClientIdOf(client)}' mints tokens that do not name the "
                + "API they are for, so every one of them is refused the moment audience "
                + "validation is on. Asserted per client rather than by sampling, because the "
                + "reader's next question is always which one (spec 069 FR-003).");
        }
    }

    /// <summary>
    /// Green today, and it must stay green: the obvious way to give one client
    /// the audience is to hang a mapper on it, which is the thing spec 042 FR-005
    /// forbids and <c>RealmIdentityTests.No_client_carries_its_own_mapper</c>
    /// already refuses. Restated here for this claim specifically, so a change
    /// made in the name of the audience meets the rule where it is working.
    /// </summary>
    [Fact]
    public void No_client_carries_a_private_audience_mapper()
    {
        foreach (JsonElement client in Clients())
        {
            client.TryGetProperty("protocolMappers", out _).ShouldBeFalse(
                customMessage: $"client '{ClientIdOf(client)}' carries its own protocol mapper. "
                + $"One definition supplies the audience — '{AudienceScope}' — and no client keeps "
                + "a second copy of it (spec 042 FR-005, spec 069 FR-004).");
        }
    }

    private static JsonElement ScopeNamed(string name) =>
        Realm().GetProperty("clientScopes").EnumerateArray()
            .FirstOrDefault(candidate => candidate.GetProperty("name").GetString() == name);

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
