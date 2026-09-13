using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SmartSentinelEye.Integration.Tests.Fixtures;
using SmartSentinelEye.Integration.Tests.Identity;
using static SmartSentinelEye.Integration.Tests.EventIngestion.EventTypeRegistryApi;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// Spec 143 FR-003, FR-010 and FR-011 — the authorization surface the primary
/// 4a suite leaves open. Three gaps, each a different kind of hole.
///
/// <para>
/// <b>1. The read side is unasserted.</b> The primary suite proves a
/// <c>POST</c> naming someone else's fab is refused. Nothing asks whether
/// <c>GET /event-types</c> is fab-scoped at all — and <c>spec.md</c> never says
/// it is. <c>plan.md</c> §5 routes it through <c>ResolveReadFabsAsync</c>, which
/// would scope it, but a plan is not a guard: an implementation that lists the
/// table returns munich's declarations to a dresden operator and every existing
/// assertion stays green, because they all count rows by kind and the kinds are
/// run-unique.
/// </para>
///
/// <para>
/// <b>2. The 428/404 ordering is an information-disclosure decision nobody has
/// written down.</b> If the existence check ran before the precondition check, a
/// caller could enumerate other fabs' registered kinds by sending
/// <c>DELETE</c> with no <c>If-Match</c> and reading 428 against 404 —
/// authorization intact, the answer leaked by the status code. Three probes
/// below must be indistinguishable.
/// </para>
///
/// <para>
/// <b>3. FR-010's negative case cannot be built the way the primary suite builds
/// it.</b> See
/// <see cref="A_management_web_token_carries_every_scope_that_client_grants"/> —
/// Keycloak always applies a client's <i>default</i> client scopes, so asking
/// <c>management-web</c> for <c>"openid sse.events.write"</c> yields all twenty
/// of its scopes. <see cref="EventSourceToken"/> mints a genuinely narrow one
/// instead.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class EventTypeRegistryAuthorizationIntegrationTests(AspireFixture aspire)
{
    private readonly RealmProbe realm = new(aspire);

    /// <summary>
    /// <c>sse-identity</c> puts a subject in the token, <c>sse-groups</c> puts
    /// the fab in it, <c>sse-audience</c> puts this product's API in <c>aud</c>.
    /// Without all three the refusal under test would be a 401 about identity
    /// rather than the 403 about scope it claims to be.
    /// </summary>
    private static readonly string[] EventSourceScopes =
        ["sse-identity", "sse-groups", "sse-audience", "sse.events.write"];

    [Fact]
    public async Task Listing_does_not_span_fabs_the_caller_does_not_hold()
    {
        string kind = UniqueKind();
        using HttpClient multi = await ClientFor(MultiFabOperator);
        (await RegisterAsync(multi, kind, fabId: "munich")).StatusCode.ShouldBe(HttpStatusCode.Created);

        using HttpClient dresden = await ClientFor(DresdenOperator);
        JsonElement rows = await ListRowsAsync(dresden, "a dresden operator must be able to list");

        CountOf(rows, kind).ShouldBe(
            0, "GET /event-types spans the caller's fabs, not the table. A dresden operator reading "
            + "munich's declarations is the tenancy hole ADR-0114 exists to close, and it is the "
            + "one shape of it this spec's suite never asks about.");

        rows.EnumerateArray().Select(row => row.GetProperty("fab").GetString()).Distinct()
            .ShouldAllBe(fab => fab == "dresden",
                customMessage: "every row a single-fab operator sees belongs to that fab");
    }

    [Fact]
    public async Task Naming_a_fab_the_caller_does_not_hold_on_the_list_is_refused()
    {
        using HttpClient dresden = await ClientFor(DresdenOperator);

        HttpResponseMessage refused = await ListAsync(dresden, fabId: "munich");

        refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await Diagnose(refused));
        (await TitleOfAsync(refused)).ShouldBe("RESOURCE_FAB_NOT_AUTHORIZED");
    }

    /// <summary>
    /// The disclosure probe. Three <c>DELETE</c>s with no <c>If-Match</c>: one
    /// against a kind that exists in the caller's fab, one against a kind that
    /// exists only in a fab they do not hold, one against a kind that has never
    /// existed. All three must answer 428 — the precondition is read before
    /// anything is looked up, so the response carries no information about which
    /// rows are there.
    /// </summary>
    [Fact]
    public async Task A_retire_without_If_Match_cannot_tell_a_missing_kind_from_someone_elses()
    {
        string mine = UniqueKind();
        string theirs = UniqueKind();
        string nobodys = UniqueKind();
        using HttpClient multi = await ClientFor(MultiFabOperator);
        (await RegisterAsync(multi, theirs, fabId: "munich")).StatusCode.ShouldBe(HttpStatusCode.Created);

        using HttpClient dresden = await ClientFor(DresdenOperator);
        (await RegisterAsync(dresden, mine)).StatusCode.ShouldBe(HttpStatusCode.Created);

        HttpResponseMessage onMine = await RetireAsync(dresden, mine, expectedVersion: null);
        HttpResponseMessage onTheirs = await RetireAsync(dresden, theirs, expectedVersion: null);
        HttpResponseMessage onNobodys = await RetireAsync(dresden, nobodys, expectedVersion: null);

        onMine.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired, await Diagnose(onMine));
        onTheirs.StatusCode.ShouldBe(
            HttpStatusCode.PreconditionRequired,
            await Diagnose(onTheirs) + Environment.NewLine
            + "a 404 here — or anything other than what an existing row answers — turns the "
            + "precondition check into an existence oracle for fabs the caller cannot read.");
        onNobodys.StatusCode.ShouldBe(HttpStatusCode.PreconditionRequired, await Diagnose(onNobodys));

        (await TitleOfAsync(onTheirs)).ShouldBe(await TitleOfAsync(onNobodys),
            "the two probes the caller may not distinguish must not differ in the problem title either");
    }

    [Fact]
    public async Task An_anonymous_caller_can_neither_list_nor_retire()
    {
        using HttpClient anonymous = aspire.CreateServiceClient(ResourceName);

        (await ListAsync(anonymous)).StatusCode.ShouldBe(
            HttpStatusCode.Unauthorized,
            "the primary suite asserts 401 on POST only; a GET mapped without .RequireScope would "
            + "publish every fab's declarations to anyone who can reach the service");
        (await RetireAsync(anonymous, UniqueKind(), expectedVersion: 0)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Not a test of JWT validation — that is ASP.NET Core's bearer middleware,
    /// configured once in <c>AuthenticationDefaults</c> and not written by this
    /// spec. It is a test that these three mappings sit behind it: an endpoint
    /// carrying <c>.AllowAnonymous()</c>, or none of
    /// <c>.RequireScope</c>/<c>.RequireAuthorization</c>, answers a garbage
    /// bearer with 200 rather than 401.
    /// </summary>
    [Fact]
    public async Task A_bearer_that_is_not_a_token_is_refused_on_every_verb()
    {
        using HttpClient forged = aspire.CreateServiceClient(ResourceName);
        forged.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.token");

        (await RegisterAsync(forged, UniqueKind())).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ListAsync(forged)).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await RetireAsync(forged, UniqueKind(), expectedVersion: 0)).StatusCode
            .ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// FR-010, with a token that actually holds what the requirement describes:
    /// <c>sse.events.write</c> and nothing else that matters, minted from a
    /// throwaway client planted for this test. An event source must not be able
    /// to declare which event types are legitimate, because under the strict
    /// mode that follows this spec that is a source self-authorising itself.
    ///
    /// <para>
    /// <b>And the refusal must beat the precondition.</b> The retire probe sends
    /// no <c>If-Match</c>: authorization runs in middleware, before any handler,
    /// so a 428 here would mean the scope check is happening inside the endpoint
    /// body where an early return can skip it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task An_event_source_token_can_neither_declare_nor_retire_an_event_type()
    {
        string clientId = $"event-source-probe-{Guid.CreateVersion7():N}";
        await PlantEventSourceClientAsync(clientId);

        try
        {
            using HttpClient source = await EventSourceClientAsync(clientId);

            HttpResponseMessage declared = await RegisterAsync(source, UniqueKind());
            declared.StatusCode.ShouldBe(
                HttpStatusCode.Forbidden,
                await Diagnose(declared) + Environment.NewLine
                + "FR-010 is the whole reason sse.events.types.write exists. A 201 here means the "
                + "POST is still declaring sse.events.write, or the token picked up sse.management.");

            HttpResponseMessage retired = await RetireAsync(source, UniqueKind(), expectedVersion: null);
            retired.StatusCode.ShouldBe(
                HttpStatusCode.Forbidden,
                await Diagnose(retired) + Environment.NewLine
                + "403 before 428: the scope is enforced by the authorization middleware, not by a "
                + "branch inside the handler that a missing header returns ahead of");
        }
        finally
        {
            await realm.DeleteAsync(clientId, CancellationToken.None);
        }
    }

    /// <summary>
    /// FR-011's other half. The registry read reuses <c>sse.events.read</c>, so
    /// a token holding only <c>sse.events.write</c> must be refused — otherwise
    /// the <c>GET</c> is mapped with the write scope, or with none.
    /// </summary>
    [Fact]
    public async Task An_event_source_token_cannot_read_the_registry()
    {
        string clientId = $"event-source-reader-{Guid.CreateVersion7():N}";
        await PlantEventSourceClientAsync(clientId);

        try
        {
            using HttpClient source = await EventSourceClientAsync(clientId);

            HttpResponseMessage refused = await ListAsync(source);

            refused.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await Diagnose(refused));
        }
        finally
        {
            await realm.DeleteAsync(clientId, CancellationToken.None);
        }
    }

    /// <summary>
    /// <b>Green on arrival, and it is evidence rather than a feature test.</b>
    ///
    /// <para>
    /// Keycloak applies a client's <i>default</i> client scopes to every token it
    /// issues; the <c>scope</c> request parameter selects among <i>optional</i>
    /// scopes only. <c>management-web</c> declares twenty <c>sse.*</c> scopes as
    /// defaults and no optional ones, so
    /// <c>GetAccessTokenForClientAsync("management-web", …, "openid
    /// sse.events.write")</c> does not produce a caller "holding
    /// sse.events.write but not sse.events.types.write" — it produces a caller
    /// holding everything <c>management-web</c> grants, which after T008 will
    /// include the new registry scope.
    /// </para>
    ///
    /// <para>
    /// This assertion is here so that claim is checked against the running
    /// Keycloak rather than argued from the realm file, and so the reason the
    /// two tests above plant their own client is recorded where the next reader
    /// will find it.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_management_web_token_carries_every_scope_that_client_grants()
    {
        string jwt = await aspire.GetAccessTokenForClientAsync(
            "management-web", DresdenOperator, OperatorPassword, "openid sse.events.write");

        string[] granted = ScopesOf(jwt);

        granted.ShouldContain(
            "sse.events.read",
            customMessage: "asking for one scope does not narrow a token to it — default client "
            + "scopes are always applied. Any test that mints from management-web and calls the "
            + "result 'a caller holding only X' is asserting something else. Scopes: "
            + string.Join(" ", granted));
    }

    private async Task PlantEventSourceClientAsync(string clientId)
    {
        using HttpClient admin = await realm.AuthorisedAdminClientAsync(CancellationToken.None);

        HttpResponseMessage created = await admin.PostAsJsonAsync(
            $"admin/realms/{RealmProbe.Realm}/clients",
            new
            {
                clientId,
                enabled = true,
                publicClient = true,
                standardFlowEnabled = false,
                serviceAccountsEnabled = false,
                directAccessGrantsEnabled = true,

                defaultClientScopes = EventSourceScopes,
                optionalClientScopes = Array.Empty<string>(),
            },
            CancellationToken.None);

        created.IsSuccessStatusCode.ShouldBeTrue(
            $"planting '{clientId}' answered {(int)created.StatusCode}: "
            + await created.Content.ReadAsStringAsync());
    }

    private async Task<HttpClient> EventSourceClientAsync(string clientId)
    {
        string jwt = await EventSourceToken(clientId);
        string[] granted = ScopesOf(jwt);

        // The control on the probe itself. If planting silently produced a token
        // carrying the registry scope, or the legacy bundle, the refusals below
        // would be proving nothing.
        granted.ShouldContain("sse.events.write",
            customMessage: $"the probe must actually be an event source. Scopes: {string.Join(" ", granted)}");
        granted.ShouldNotContain("sse.management",
            customMessage: "the legacy bundle satisfies every sse.* policy but sse.events.publish, so a "
            + "probe holding it cannot demonstrate a scope refusal");
        granted.ShouldNotContain("sse.events.types.write",
            customMessage: "the probe must not hold the very scope the endpoint requires");

        HttpClient client = aspire.CreateServiceClient(ResourceName);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        return client;
    }

    private Task<string> EventSourceToken(string clientId) =>
        aspire.GetAccessTokenForClientAsync(clientId, DresdenOperator, OperatorPassword, "openid");

    /// <summary>
    /// The <c>scope</c> claim of a JWT, read without validating it — this asks
    /// what Keycloak put in the token, which is exactly the question.
    /// </summary>
    private static string[] ScopesOf(string jwt)
    {
        string[] parts = jwt.Split('.');
        parts.Length.ShouldBe(3, "a bearer token should have three segments");

        string payload = parts[1].Replace('-', '+').Replace('_', '/');
        string padded = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');

        using JsonDocument document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(padded)));

        return document.RootElement.TryGetProperty("scope", out JsonElement scope)
            ? scope.GetString()?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? []
            : [];
    }

    private Task<HttpClient> ClientFor(string username) =>
        aspire.CreateAuthenticatedClientAsync(ResourceName, username, OperatorPassword);

    private async Task<string> Diagnose(HttpResponseMessage response) =>
        $"body: {await response.Content.ReadAsStringAsync()}{Environment.NewLine}"
        + $"event-ingestion log:{Environment.NewLine}{aspire.RecentLogs(ResourceName)}";
}
