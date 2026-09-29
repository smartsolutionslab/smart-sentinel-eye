using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ScenarioSimulator.Configuration;
using SmartSentinelEye.ScenarioSimulator.Keycloak;
using SmartSentinelEye.ScenarioSimulator.Scenario;
using SmartSentinelEye.ScenarioSimulator.Seeding;
using SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Spec 289 / PR-C, T-C02. <c>SystemVariablesClient</c> now exists (plan.md
/// §3.4, §5.4, T-C05) — the same create-then-read-back-on-409 idiom <see cref="AutomationRulesClient"/>
/// and <c>OverlayDesignerClient</c> already use, but with no publish step: a
/// system variable has no draft/published lifecycle.
///
/// <para>
/// <c>EnsureVariableAsync(VariableDefinition, CancellationToken)</c> posts
/// <c>{ Name, Type, InitialValue: null, TruthyLabel, FalsyLabel }</c> to
/// <c>POST /system-variables?fabId=munich</c>:
/// </para>
/// <list type="bullet">
/// <item>201 → <see cref="VariableSeedResult.Seeded"/> (created).</item>
/// <item>
/// 409 → <c>GET /system-variables/{name}?fabId=munich</c>; a matching
/// <c>Type</c> is <see cref="VariableSeedResult.Seeded"/> (reused), a
/// mismatch is <see cref="VariableSeedResult.Refused"/> naming both types.
/// </item>
/// <item>400 → <see cref="VariableSeedResult.Refused"/> carrying the problem detail.</item>
/// <item>anything else → <c>EnsureSuccessStatusCode</c> throws (caught by the seeder's per-asset backstop).</item>
/// </list>
///
/// <para>
/// <b>No retry (ADR-0143).</b> Unlike the four token mints and the MediaMTX
/// gateway client, this <c>POST</c> is not idempotent in fact — a second
/// <c>POST</c> against a variable another caller already created answers 409,
/// which the client already treats as "exists", so retrying a genuine 5xx would
/// only risk a second half-applied create. The client itself must therefore
/// make exactly one attempt per call; the "no <c>RetryEveryMethod()</c>" half of
/// the guarantee lives in <c>ScenarioSeedingExtensions.AddScenarioSeeding</c>'s
/// registration (T-C05) and is not visible from this unit level, the same way
/// <see cref="AutomationRulesClient"/>'s non-retry is not re-proven here either
/// — both clients are exercised in these tests against a raw
/// <see cref="HttpClient"/> with no resilience pipeline attached, mirroring
/// <c>AutomationRulesClientTests</c>.
/// </para>
/// </summary>
public sealed class SystemVariablesClientTests
{
    private static readonly VariableDefinition StringVariable = new()
    {
        Name = "roughing_zone_state",
        Type = "String",
    };

    [Fact]
    public async Task A_201_means_the_variable_was_created()
    {
        StubHandler variables = new(request => request switch
        {
            { Method: "POST" } => Respond(HttpStatusCode.Created),
            _ => Respond(HttpStatusCode.OK),
        });

        VariableSeedResult result = await EnsureAsync(variables, StringVariable);

        result.ShouldBeOfType<VariableSeedResult.Seeded>();
        variables.Calls.ShouldHaveSingleItem();
        variables.Calls[0].Method.ShouldBe("POST");
    }

    [Fact]
    public async Task A_409_followed_by_a_matching_type_read_back_means_reuse()
    {
        StubHandler variables = new(request => request switch
        {
            { Method: "POST", Path: "/system-variables" } => Respond(HttpStatusCode.Conflict),
            { Method: "GET" } => RespondJson("""{"type":"String"}"""),
            _ => Respond(HttpStatusCode.OK),
        });

        VariableSeedResult result = await EnsureAsync(variables, StringVariable);

        result.ShouldBeOfType<VariableSeedResult.Seeded>();
        variables.Calls
            .Select(call => $"{call.Method} {call.PathAndQuery}")
            .ShouldBe([
                "POST /system-variables?fabId=munich",
                "GET /system-variables/roughing_zone_state?fabId=munich",
            ]);
    }

    [Fact]
    public async Task A_409_followed_by_a_mismatched_type_read_back_is_a_named_refusal()
    {
        StubHandler variables = new(request => request switch
        {
            { Method: "POST", Path: "/system-variables" } => Respond(HttpStatusCode.Conflict),
            { Method: "GET" } => RespondJson("""{"type":"Number"}"""),
            _ => Respond(HttpStatusCode.OK),
        });

        VariableSeedResult result = await EnsureAsync(variables, StringVariable);

        VariableSeedResult.Refused refused = result.ShouldBeOfType<VariableSeedResult.Refused>();
        refused.Reason.ShouldContain("String", Case.Insensitive);
        refused.Reason.ShouldContain("Number", Case.Insensitive);
    }

    [Fact]
    public async Task A_400_carries_its_problem_detail_as_the_refusal_reason()
    {
        StubHandler variables = new(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent(
                """{"title":"VARIABLE_INVALID_INPUT","detail":"'Type' must be one of String, Number, Boolean.","status":400}""",
                Encoding.UTF8, "application/json"),
        });

        VariableSeedResult result = await EnsureAsync(variables, StringVariable);

        VariableSeedResult.Refused refused = result.ShouldBeOfType<VariableSeedResult.Refused>();
        refused.Reason.ShouldBe("'Type' must be one of String, Number, Boolean.");
    }

    [Fact]
    public async Task The_create_call_targets_the_munich_fab()
    {
        StubHandler variables = new(_ => Respond(HttpStatusCode.Created));

        await EnsureAsync(variables, StringVariable);

        variables.Calls[0].PathAndQuery.ShouldBe("/system-variables?fabId=munich");
    }

    [Fact]
    public async Task The_bearer_token_is_attached_to_the_create_call()
    {
        StubHandler variables = new(_ => Respond(HttpStatusCode.Created));

        await EnsureAsync(variables, StringVariable);

        variables.Calls[0].Authorization.ShouldBe("Bearer stub-token");
    }

    [Fact]
    public async Task A_5xx_on_create_is_not_retried_by_the_client_and_propagates()
    {
        StubHandler variables = new(_ => Respond(HttpStatusCode.InternalServerError));

        await Should.ThrowAsync<HttpRequestException>(() => EnsureAsync(variables, StringVariable));

        variables.Calls.Count(call => call.Method == "POST").ShouldBe(1);
    }

    private static Task<VariableSeedResult> EnsureAsync(StubHandler variables, VariableDefinition definition)
    {
        HttpClient tokens = new(new StubHandler(_ =>
            RespondJson("""{"access_token":"stub-token","expires_in":300,"token_type":"Bearer"}""")));

        KeycloakTokenProvider provider = new(
            new FakeHttpClientFactory(tokens),
            Options.Create(new SimulatorOptions
            {
                KeycloakUrl = "https://keycloak.test",
                Realm = "smart-sentinel-eye",
                ClientId = "scenario-simulator",
                ClientSecret = "stub-secret",
            }),
            TimeProvider.System,
            NullLogger<KeycloakTokenProvider>.Instance);

        HttpClient http = new(variables) { BaseAddress = new Uri("https://system-variables.test") };

        return new SystemVariablesClient(http, provider, NullLogger<SystemVariablesClient>.Instance)
            .EnsureVariableAsync(definition, CancellationToken.None);
    }

    private static HttpResponseMessage Respond(HttpStatusCode status) => new(status);

    private static HttpResponseMessage RespondJson(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed record Call(string Method, string Path, string PathAndQuery, string Authorization);

    /// <summary>Mirrors <c>AutomationRulesClientTests.StubHandler</c> (ADR-0054).</summary>
    private sealed class StubHandler(Func<Call, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Call> Calls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Call call = new(
                request.Method.Method,
                request.RequestUri!.AbsolutePath,
                request.RequestUri.PathAndQuery,
                request.Headers.Authorization?.ToString() ?? string.Empty);

            Calls.Add(call);

            return Task.FromResult(respond(call));
        }
    }
}
