using System.Text.Json;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// Guards the browser kiosk's identity against the one every enrolled kiosk
/// device gets (spec 041, issue 1884).
///
/// <para>
/// There is <b>one</b> notion of what a kiosk may do —
/// <see cref="KeycloakScopeBundles.Kiosk"/>, which Identity grants each device
/// it enrols. The realm's <c>kiosk-web</c> client repeats that list by hand, and
/// until this test the two agreed only because two people wrote the same six
/// strings. <c>KeycloakScopeBundles</c>' own doc comment cited a
/// <c>ScopeBundleTests</c> that has never existed anywhere in this repository.
/// </para>
///
/// <para>
/// <b>The assertions are on configuration</b>, because configuration is what was
/// wrong: the app signed in as a client carrying no fab claim, so every
/// fab-scoped read was refused and the kiosk could never list a wall. Nothing
/// caught it, and nothing could — the check that signs into a kiosk accepted the
/// error as one of its passing outcomes.
/// </para>
/// </summary>
public class KioskScopeParityTests
{
    private const string KioskClientId = "kiosk-web";

    /// <summary>Puts <c>/fabs/&lt;id&gt;</c> into the token. Not a permission.</summary>
    private const string GroupsScope = "sse-groups";

    private const string ManagementBundle = "sse.management";

    [Fact]
    public void The_kiosk_client_grants_nothing_an_enrolled_kiosk_device_does_not()
    {
        IReadOnlyCollection<string> realm = KioskClientPermissions();

        realm.Except(KeycloakScopeBundles.Kiosk, StringComparer.Ordinal).ShouldBeEmpty(
            customMessage: $"the realm's {KioskClientId} client grants a scope no enrolled kiosk device "
            + "holds. A browser kiosk is not a second notion of what a kiosk may do — either the "
            + "action does not belong on a kiosk, or KeycloakScopeBundles.Kiosk is wrong (spec 041 FR-004).");
    }

    [Fact]
    public void The_kiosk_client_grants_everything_an_enrolled_kiosk_device_does()
    {
        IReadOnlyCollection<string> realm = KioskClientPermissions();

        KeycloakScopeBundles.Kiosk.Except(realm, StringComparer.Ordinal).ShouldBeEmpty(
            customMessage: $"an enrolled kiosk device holds a scope the realm's {KioskClientId} client "
            + "does not. Asserted in both directions on purpose: a one-way check would not notice a "
            + "scope added to the bundle (spec 041 SC-003).");
    }

    /// <summary>
    /// The whole defect, structurally. Without this scope the kiosk's token
    /// carries no fab, and a system that correctly refuses reads outside your
    /// fabs refuses all of them.
    /// </summary>
    [Fact]
    public void The_kiosk_client_carries_the_fab_claim()
    {
        KioskClient(Realm()).GetProperty("defaultClientScopes").EnumerateArray()
            .Select(scope => scope.GetString())
            .ShouldContain(GroupsScope,
                customMessage: $"without {GroupsScope} the kiosk's token holds no fab, so every "
                + "fab-scoped read is refused and it can never list a wall (spec 041 FR-001).");
    }

    /// <summary>
    /// <b>SC-002</b>, asserted as an absence. A kiosk holding the management
    /// bundle lists layouts, opens walls and passes every behavioural check
    /// identically to one holding nothing but reads — so behaviour cannot see
    /// this, and only an assertion on the configuration can.
    /// </summary>
    [Fact]
    public void The_kiosk_client_does_not_carry_the_management_bundle()
    {
        KioskClientScopes().ShouldNotContain(ManagementBundle,
            customMessage: "a kiosk is the least physically secure surface in the product — a screen "
            + "on a wall in a building with visitors. It reads; it does not administer (spec 041 FR-003).");
    }

    /// <summary>
    /// <b>The kiosk's subject claim is guarded, elsewhere.</b> Spec 041 asserted
    /// here that <c>kiosk-web</c> carried its own <c>oidc-sub-mapper</c>, because
    /// at the time it was the only client that did and losing it would have
    /// stopped video on every kiosk with nothing going red — CI produces no
    /// video.
    ///
    /// <para>
    /// Spec 042 moved the mapper to the shared <c>sse-identity</c> scope and
    /// removed the private copy, so that assertion's premise is gone. The
    /// guarantee is not: <c>RealmIdentityTests.Every_client_holds_the_identity_scope</c>
    /// makes it per client rather than for this one, and
    /// <c>TokenAttributionIntegrationTests</c> mints a token to check the mapper
    /// actually fires — which reading the file never could.
    /// </para>
    ///
    /// <para>
    /// Recorded rather than deleted silently: a guard that disappears in someone
    /// else's feature looks like a guard that was dropped.
    /// </para>
    /// </summary>
    [Fact]
    public void The_kiosk_client_keeps_no_private_subject_mapper()
    {
        KioskClient(Realm()).TryGetProperty("protocolMappers", out _).ShouldBeFalse(
            customMessage: $"{KioskClientId} should take its subject from the shared sse-identity "
            + "scope like every other client. A private copy of one fact is how the two drift "
            + "(spec 042 FR-006).");
    }

    /// <summary>
    /// <b>SC-005.</b> The retired client was a live public client carrying
    /// administrative authority that nothing used — which is how the next reader
    /// ends up choosing the wrong one, precisely what happened here.
    /// </summary>
    [Fact]
    public void The_retired_kiosk_client_stays_retired()
    {
        Realm().GetProperty("clients").EnumerateArray()
            .Select(client => client.GetProperty("clientId").GetString())
            .ShouldNotContain("smart-sentinel-eye-kiosk",
                customMessage: "spec 009 called it replaced and spec 041 made that a fact.");
    }

    private static IReadOnlyCollection<string> KioskClientPermissions() =>
        [.. KioskClientScopes().Where(IsPermission)];

    /// <summary>
    /// <c>sse-groups</c> carries the fab claim and grants nothing, and the
    /// built-in names are inert here — this realm defines its own scopes, so
    /// Keycloak drops <c>basic</c>, <c>profile</c>, <c>email</c> and
    /// <c>roles</c> on import with a warning.
    /// </summary>
    private static bool IsPermission(string scope) =>
        scope.StartsWith("sse.", StringComparison.Ordinal);

    private static IReadOnlyCollection<string> KioskClientScopes() =>
        [.. KioskClient(Realm()).GetProperty("defaultClientScopes").EnumerateArray()
            .Select(scope => scope.GetString() ?? string.Empty)];

    private static JsonElement KioskClient(JsonElement realm) =>
        realm.GetProperty("clients").EnumerateArray()
            .FirstOrDefault(client => client.GetProperty("clientId").GetString() == KioskClientId)
            is { ValueKind: JsonValueKind.Object } client
            ? client
            : throw new InvalidOperationException($"the realm has no {KioskClientId} client");

    private static JsonElement Realm() => RealmDocument.RootElement;

    /// <summary>
    /// Parsed once and held: <see cref="JsonElement"/> is only valid while its
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
