using System.Text.Json;
using SmartSentinelEye.Identity.Application.KeycloakAdmin;
using SmartSentinelEye.ServiceDefaults.Authorization;

namespace SmartSentinelEye.Architecture.Tests;

/// <summary>
/// The half of "add a scope" that nothing has ever checked.
///
/// <para>
/// <c>Scope.cs</c> says it in its own doc comment — <i>"Adding a new scope is a
/// one-line edit here plus a corresponding entry in
/// <c>smart-sentinel-eye-realm.json</c>"</i> — and no test holds the pairing.
/// <c>ScopeTests</c> reads the catalogue and never opens the realm.
/// <c>RealmIdentityTests</c> reads the realm and checks one direction:
/// <b>every scope a client names must exist</b>. A scope defined in the
/// catalogue, given a policy by <c>AddScopePolicies</c>, required by an endpoint
/// and granted to nobody passes both. The endpoint then answers 403 to every
/// caller in the estate, and the only way to find out is to mint a token.
/// </para>
///
/// <para>
/// <b>Spec 143 plan §7 attributes this guard to <c>RealmIdentityTests</c> and it
/// is not there.</b> That plan's own trap — "a green integration suite does not
/// prove the realm edit landed", because the legacy <c>sse.management</c> bundle
/// <i>used to</i> satisfy every <c>sse.*</c> policy before it was withdrawn
/// (spec 265 / #2486) — is real, and the test it names as the backstop does not
/// perform the check. These two do.
/// </para>
/// </summary>
public class ScopeGrantTests
{
    private const string OperatorConsoleClientId = "management-web";
    private const string KioskClientId = "kiosk-web";

    /// <summary>
    /// <b>Green on arrival, deliberately.</b> Every scope in the catalogue is
    /// defined in the realm today; this is the trap that springs when the next
    /// one is not. Spec 143's <c>sse.events.types.write</c> is the immediate
    /// occasion — T008 is two edits in two files and half-doing either fails
    /// silently at runtime.
    /// </summary>
    [Fact]
    public void Every_catalogued_scope_is_defined_in_the_realm()
    {
        IReadOnlyCollection<string> defined = DefinedScopes();

        foreach (string scope in Scope.All)
        {
            defined.ShouldContain(scope,
                customMessage: $"'{scope}' has a policy registered by AddScopePolicies and no client "
                + "scope in the realm, so no token can ever carry it and every endpoint requiring "
                + "it answers 403 to everyone. Add it to smart-sentinel-eye-realm.json's "
                + "clientScopes as well as to Scope.cs.");
        }
    }

    /// <summary>
    /// Defining a scope and granting it to nobody is the same failure one step
    /// later, and it is the step spec 143 plan §7 warns about by name.
    ///
    /// <para>
    /// <c>KeycloakScopeBundles</c> counts as a grant: devices, kiosks and
    /// webhook integrations get their clients created at runtime through the
    /// Admin API, so their scopes are granted by those lists rather than by the
    /// realm file. <c>sse.events.publish</c> is the live example — no static
    /// client holds it, and <c>KeycloakScopeBundles.Device</c> does.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_catalogued_scope_is_granted_to_someone()
    {
        IReadOnlyCollection<string> granted = [
            .. Clients().SelectMany(DefaultScopesOf),
            .. KeycloakScopeBundles.Kiosk,
            .. KeycloakScopeBundles.Device,
            .. KeycloakScopeBundles.WebhookIntegration,
        ];

        foreach (string scope in Scope.All)
        {
            granted.ShouldContain(scope,
                customMessage: $"'{scope}' is defined and held by nobody. Every caller is refused, and "
                + "no integration suite can see it from behaviour alone — the legacy sse.management "
                + "bundle that used to paper over exactly this gap was withdrawn (spec 265 / #2486), "
                + "so only configuration can catch it now.");
        }
    }

    /// <summary>
    /// Spec 143 FR-010 / A5, asserted as configuration: whether the grant lands
    /// is otherwise invisible to any integration suite, now that the
    /// sse.management bundle no longer stands in for it (spec 265 / #2486).
    /// <b>Red until T008.</b>
    /// </summary>
    [Fact]
    public void The_registry_write_scope_is_granted_to_the_operator_console()
    {
        DefinedScopes().ShouldContain(
            RegistryWriteScope,
            customMessage: $"the realm must define '{RegistryWriteScope}'; a client scope named and "
            + "not defined is discarded at import with a warning only (spec 143 plan §7).");

        DefaultScopesOf(ClientNamed(OperatorConsoleClientId)).ShouldContain(
            RegistryWriteScope,
            customMessage: $"'{RegistryWriteScope}' is the operator's scope (spec 143 FR-010, A5). "
            + "Defining it without granting it leaves POST /event-types reachable by nobody, now that "
            + "the withdrawn sse.management bundle (spec 265 / #2486) no longer stands in for it.");
    }

    /// <summary>
    /// The other direction, and the reason FR-010 exists: a kiosk already holds
    /// <c>sse.events.write</c>, so if it also held the registry scope an event
    /// source could declare which event types are legitimate — the strict mode
    /// that follows this spec would then let a source authorise itself.
    ///
    /// <para>
    /// <b>Green on arrival</b> (nothing has been granted yet), and it stays
    /// meaningful afterwards: <c>KioskScopeParityTests</c> compares the kiosk
    /// bundle against the realm client as a set in both directions, so adding
    /// the scope to <i>both</i> would satisfy that test and only this one would
    /// object.
    /// </para>
    /// </summary>
    [Fact]
    public void The_registry_write_scope_is_granted_to_no_kiosk()
    {
        DefaultScopesOf(ClientNamed(KioskClientId)).ShouldNotContain(RegistryWriteScope);

        KeycloakScopeBundles.Kiosk.ShouldNotContain(RegistryWriteScope);
        KeycloakScopeBundles.Device.ShouldNotContain(RegistryWriteScope);
        KeycloakScopeBundles.WebhookIntegration.ShouldNotContain(
            RegistryWriteScope,
            customMessage: "a webhook integration is an event source. FR-010's whole argument is "
            + "that a source must not declare the set it is judged against.");
    }

    /// <summary>
    /// Spelled as a literal rather than taken from <c>Scope.Sse.Events.Types</c>.
    /// The constant does not exist until T008, and a test that will not compile
    /// is not a red test — spec 061's <c>CS0246</c>, commit <c>24e6fc4c</c>.
    /// Once T008 lands,
    /// <see cref="Every_catalogued_scope_is_defined_in_the_realm"/> and
    /// <see cref="Every_catalogued_scope_is_granted_to_someone"/> cover it from
    /// the constant side and this literal becomes the belt to their braces.
    /// </summary>
    private const string RegistryWriteScope = "sse.events.types.write";

    private static IReadOnlyCollection<string> DefinedScopes() =>
        [.. Realm().GetProperty("clientScopes").EnumerateArray()
            .Select(scope => scope.GetProperty("name").GetString() ?? string.Empty)];

    private static JsonElement ClientNamed(string clientId) =>
        Clients().FirstOrDefault(client => client.GetProperty("clientId").GetString() == clientId)
            is { ValueKind: JsonValueKind.Object } client
            ? client
            : throw new InvalidOperationException($"the realm has no {clientId} client");

    private static JsonElement.ArrayEnumerator Clients() =>
        Realm().GetProperty("clients").EnumerateArray();

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
