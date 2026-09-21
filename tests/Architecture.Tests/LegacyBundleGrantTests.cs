using System.Text.Json;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Spec 200 (issue #2279), SC-7 — the bundle is granted to no client.
///
/// <para>
/// <c>sse.management</c> satisfies every catalogued <c>sse.*</c> policy except
/// <c>sse.events.publish</c> (<c>RequireScopeExtensions.LegacyManagementBundle</c>),
/// so a client that holds it holds effectively everything and no behavioural
/// test in the suite can see that — a caller with the bundle passes every
/// granular check identically to a caller with the full, explicit set. This is
/// a <b>static, design-time guard</b>: it proves the realm file says the right
/// thing. It does not prove the running system enforces it —
/// <c>ConsoleScopeGrantIntegrationTests</c>' SC-1 is what asks the running
/// system. Both are required; neither substitutes for the other.
/// </para>
///
/// <para>
/// <b>Design-time, not runtime.</b> This reads the checked-in realm file, so
/// it cannot see a scope assigned through the Keycloak admin console on a
/// live realm, or a hand-built production realm that never went through this
/// file at all. It also cannot see the realm-level
/// <c>defaultDefaultClientScopes</c>/<c>defaultOptionalClientScopes</c> keys,
/// which would hand the bundle to every client created after import —
/// including the kiosk/device/webhook-integration clients Identity creates at
/// runtime — while every assertion below stayed green. Neither key exists in
/// this realm file today; the second fact below fails loudly the day one
/// does.
/// </para>
/// </summary>
public class LegacyBundleGrantTests
{
    private const string Bundle = "sse.management";

    [Fact]
    public void The_bundle_is_not_a_default_or_optional_scope_of_any_client()
    {
        foreach (JsonElement client in Clients())
        {
            string clientId = client.GetProperty("clientId").GetString() ?? "(unnamed)";

            ScopesNamedBy(client, "defaultClientScopes").ShouldNotContain(
                Bundle,
                customMessage: $"'{clientId}' default-grants '{Bundle}', which satisfies every catalogued "
                + "sse.* policy but sse.events.publish (RequireScopeExtensions.LegacyManagementBundle). "
                + "A client holding it holds effectively everything, and no behavioural test in the suite "
                + "can see that it does.");

            ScopesNamedBy(client, "optionalClientScopes").ShouldNotContain(
                Bundle,
                customMessage: $"'{clientId}' offers '{Bundle}' as an optional scope, which a caller could "
                + "request and receive the same blanket authority the default grant would have given it.");
        }
    }

    [Fact]
    public void The_bundle_is_not_granted_by_any_runtime_persona_bundle()
    {
        KeycloakScopeBundles.Kiosk.ShouldNotContain(Bundle);
        KeycloakScopeBundles.Device.ShouldNotContain(Bundle);
        KeycloakScopeBundles.WebhookIntegration.ShouldNotContain(Bundle);
    }

    /// <summary>
    /// The realm-level keys that would hand the bundle to every client created
    /// after import, sidestepping every other assertion in this file (see the
    /// class doc comment). Neither key exists today; this fails the day either
    /// one is added and names it, rather than everyone re-deriving why the
    /// per-client checks above stayed green.
    /// </summary>
    [Fact]
    public void The_bundle_is_not_a_realm_level_default_for_new_clients()
    {
        ScopesNamedBy(Realm(), "defaultDefaultClientScopes").ShouldNotContain(Bundle);
        ScopesNamedBy(Realm(), "defaultOptionalClientScopes").ShouldNotContain(Bundle);
    }

    private static IReadOnlyCollection<string> ScopesNamedBy(JsonElement client, string property) =>
        client.TryGetProperty(property, out JsonElement scopes)
            ? [.. scopes.EnumerateArray().Select(scope => scope.GetString() ?? string.Empty)]
            : [];

    private static JsonElement.ArrayEnumerator Clients() =>
        Realm().GetProperty("clients").EnumerateArray();

    private static JsonElement Realm() => RealmDocument.RootElement;

    /// <summary>
    /// Parsed once and held: a <see cref="JsonElement"/> is only valid while its
    /// document is alive, and every assertion here reads the same file. Its own
    /// copy rather than sharing <c>ScopeGrantTests</c>' — that document is
    /// private to that class, and this guard's subject (the bundle, not the
    /// catalogue↔realm pairing) is named by keeping it separate.
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
