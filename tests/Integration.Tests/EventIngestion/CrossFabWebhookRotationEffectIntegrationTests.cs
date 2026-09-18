using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SmartSentinelEye.EventIngestion.Domain.WebhookIntegration;
using SmartSentinelEye.EventIngestion.Infrastructure.Persistence;
using SmartSentinelEye.Integration.Tests.Fixtures;

namespace SmartSentinelEye.Integration.Tests.EventIngestion;

/// <summary>
/// Spec 182 US2 (#2280), AS-5 — the takeover
/// <see cref="SmartSentinelEye.Identity.Application.Commands.Handlers.RotateWebhookClientCommandHandler"/>'s
/// US1 fix (deliberately) cannot close on its own. Identity holds no record
/// of who owns a webhook integration name until the first rotation creates
/// one, so a munich-only operator's <b>first</b> rotation naming an unrotated
/// dresden integration is legitimately accepted on the Identity side — see
/// spec.md §1.2 "Attack B". <c>WebhookIntegrationRotatedV1Handler</c> then
/// resolves the target by name alone, with no fab check at all, and flips
/// Dresden's integration onto the attacker's Keycloak client.
///
/// <para>
/// Deliberately a separate file from <see cref="WebhookBearerValidationIntegrationTests"/>
/// (tasks.md T006) so the two stay `[P]` disjoint from
/// <see cref="CrossFabWebhookRotationIntegrationTests"/>'s Identity-side file.
/// </para>
///
/// <para>
/// State is read directly from <c>EventIngestionDbContext</c> rather than
/// through an HTTP list: <c>WebhookIntegrationDto</c> deliberately does not
/// expose <c>ValidationMode</c> or <c>KeycloakClientId</c> (spec 018's own
/// reasoning — the list is fab-scoped and this field would leak nothing new
/// to a caller who already holds the fab, but no endpoint surfaces it today),
/// so the DB is the only place this test can observe the effect the fix is
/// about. Same approach as <see cref="WebhookBearerValidationIntegrationTests.SeedJwtIntegrationAsync"/>.
/// </para>
/// </summary>
[Collection(AspireCollection.Name)]
public class CrossFabWebhookRotationEffectIntegrationTests(AspireFixture aspire)
{
    private const string DresdenOperator = "op-dresden@dresden.test";
    private const string OperatorPassword = "Operator1234";
    private static readonly JsonElement Payload = JsonDocument.Parse("""{"severity":"high"}""").RootElement;

    /// <summary>
    /// AS-5 — the takeover. A munich-scoped first rotation naming an
    /// unrotated dresden integration must never flip it to JWT validation,
    /// and Dresden's original static bearer must keep working throughout.
    /// </summary>
    [Fact]
    public async Task A_munich_first_rotation_naming_an_unrotated_dresden_integration_leaves_it_on_static_hash()
    {
        using HttpClient dresdenEvents = await aspire.CreateAuthenticatedClientAsync(
            "event-ingestion", DresdenOperator, OperatorPassword);
        string name = UniqueName("dresden-first");
        string originalToken = await RegisterStaticHashIntegrationAsync(dresdenEvents, name);

        using HttpClient munichIdentity = await aspire.CreateAdminClientAsync("identity");
        HttpResponseMessage firstRotation = await munichIdentity.SendAsync(CreateConditional(name, "munich"));

        // Not asserted as a refusal: after US1, the Identity side legitimately
        // believes it is creating a brand-new client — there is genuinely no
        // row in any fab to scope the lookup against yet (spec §1.2, §3.1).
        // It is US2's effect on Dresden's integration this test is about.
        firstRotation.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            $"the munich-side first rotation call itself is not what this test refuses; got "
            + $"{(int)firstRotation.StatusCode} {await firstRotation.Content.ReadAsStringAsync()}");

        // The WebhookIntegrationRotatedV1 announcement travels over the bus.
        // If it flips Dresden's integration at any point in this window, that
        // IS the vulnerability, so fail immediately with the state observed
        // rather than waiting out the rest of the window.
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            (BearerValidationMode mode, string? keycloakClientId) = await ReadStateAsync(name);
            mode.ShouldBe(
                BearerValidationMode.StaticHash,
                $"a munich-scoped first rotation naming Dresden's integration must never flip it to "
                + $"JWT; got mode '{mode}' with KeycloakClientId '{keycloakClientId}' — that is the "
                + "cross-fab takeover this spec exists to close");

            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }

        (BearerValidationMode finalMode, string? finalClientId) = await ReadStateAsync(name);
        finalMode.ShouldBe(BearerValidationMode.StaticHash);
        finalClientId.ShouldBeNull("Dresden's integration must never be pointed at the attacker's client");

        HttpResponseMessage delivered = await PostWebhookAsync(name, "dresden", originalToken);
        delivered.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            "Dresden's original static bearer must still be accepted after the attempted takeover");
    }

    /// <summary>
    /// Positive control for the test above (phase-6 review finding). That
    /// test only ever exercises the <c>None</c> branch of
    /// <c>WebhookIntegrationRepository.GetWithinFabAsync</c>'s fab-scoped
    /// predicate — it proves a cross-fab rotation is refused, but nothing
    /// proves the predicate still <b>matches</b> a real row when the fab
    /// genuinely agrees. If <c>.Where(i =&gt; i.Fab == fab)</c> ever regressed
    /// (a broken equality, a migration that changes the column mapping),
    /// every legitimate same-fab rotation would silently stop flipping
    /// integrations to JWT, and the cross-fab test alone would never notice —
    /// it only ever asserts "stayed StaticHash". A same-fab first rotation,
    /// performed by the fab's own operator, must still reach
    /// <see cref="BearerValidationMode.Jwt"/>.
    /// </summary>
    [Fact]
    public async Task A_dresden_first_rotation_naming_its_own_integration_flips_it_to_JWT_validation()
    {
        using HttpClient dresdenEvents = await aspire.CreateAuthenticatedClientAsync(
            "event-ingestion", DresdenOperator, OperatorPassword);
        string name = UniqueName("dresden-own");
        await RegisterStaticHashIntegrationAsync(dresdenEvents, name);

        using HttpClient dresdenIdentity = await aspire.CreateAuthenticatedClientAsync(
            "identity", DresdenOperator, OperatorPassword);
        HttpResponseMessage rotation = await dresdenIdentity.SendAsync(CreateConditional(name, "dresden"));

        rotation.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            $"a dresden operator rotating its own integration must succeed; got "
            + $"{(int)rotation.StatusCode} {await rotation.Content.ReadAsStringAsync()}");

        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        (BearerValidationMode mode, string? keycloakClientId) = await ReadStateAsync(name);
        while (mode != BearerValidationMode.Jwt && DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
            (mode, keycloakClientId) = await ReadStateAsync(name);
        }

        mode.ShouldBe(
            BearerValidationMode.Jwt,
            $"a same-fab rotation must still flip the integration to JWT validation within the window; "
            + $"got mode '{mode}' — if this fails, the fab-scoped predicate stopped matching a real "
            + "row, which the cross-fab test above cannot detect on its own");
        keycloakClientId.ShouldNotBeNull(
            "a genuinely flipped integration must carry the KeycloakClientId the rotation minted");
    }

    private static async Task<string> RegisterStaticHashIntegrationAsync(HttpClient dresdenEvents, string name)
    {
        HttpResponseMessage created = await dresdenEvents.PostAsJsonAsync(
            "/webhook-integrations", new { name, defaultKind = "WebhookAlarm" });
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());

        JsonElement body = await created.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("token").GetString()!;
    }

    private static HttpRequestMessage CreateConditional(string name, string fab)
    {
        HttpRequestMessage request = new(HttpMethod.Post, $"/webhook-integrations/{name}/rotate")
        {
            Content = JsonContent.Create(new { fabId = fab }),
        };
        request.Headers.TryAddWithoutValidation("If-None-Match", "*");

        return request;
    }

    private async Task<(BearerValidationMode Mode, string? KeycloakClientId)> ReadStateAsync(string name)
    {
        await using EventIngestionDbContext context = await aspire.CreateEventIngestionDbContextAsync();
        WebhookIntegrationName parsed = WebhookIntegrationName.From(name);
        WebhookIntegration integration = await context.WebhookIntegrations
            .AsNoTracking()
            .SingleAsync(candidate => candidate.Name == parsed);

        return (integration.ValidationMode, integration.KeycloakClientId?.Value);
    }

    private async Task<HttpResponseMessage> PostWebhookAsync(string name, string fabId, string bearer)
    {
        using HttpRequestMessage request = new(
            HttpMethod.Post, $"/events/webhook/{name}?fabId={fabId}")
        {
            Content = JsonContent.Create(new { payload = Payload }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return await aspire.EventIngestion.SendAsync(request);
    }

    private static string UniqueName(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}".ToLowerInvariant()[..Math.Min(63, prefix.Length + 33)];
}
