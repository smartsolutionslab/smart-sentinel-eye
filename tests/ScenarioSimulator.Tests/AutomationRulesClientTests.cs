using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ScenarioSimulator.Configuration;
using SmartSentinelEye.ScenarioSimulator.Keycloak;
using SmartSentinelEye.ScenarioSimulator.Seeding;
using SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Seeding has to be idempotent across runs, and the interesting case is not a
/// clean database — it is one carrying the wreckage of an earlier run. Every
/// run between spec 012 making <c>If-Match</c> mandatory and this client
/// learning to send it created the rule and then failed to publish it, so
/// existing environments hold Draft rules that will never fire. A seeder that
/// treats "already exists" as "already seeded" leaves them there forever.
/// </summary>
public class AutomationRulesClientTests
{
    [Fact]
    public async Task A_rule_a_previous_run_left_in_Draft_is_published()
    {
        StubHandler automation = new(request => request switch
        {
            { Method: "POST", Path: "/rules" } => Respond(HttpStatusCode.Conflict),
            { Method: "GET" } => RespondJson("""{"version":0,"state":"Draft"}"""),
            _ => Respond(HttpStatusCode.OK),
        });

        await SeedAsync(automation);

        automation.Calls
            .Select(call => $"{call.Method} {call.PathAndQuery}")
            .ShouldBe([
                "POST /rules?fabId=munich",
                "GET /rules/press-hot?fabId=munich",
                "POST /rules/press-hot/publish?fabId=munich",
            ]);
    }

    [Fact]
    public async Task The_stranded_rules_own_version_is_echoed_back_as_the_precondition()
    {
        // Not necessarily 0: whatever the rule has actually reached is what
        // publish must be built on, or it comes back 409 stale.
        StubHandler automation = new(request => request switch
        {
            { Method: "POST", Path: "/rules" } => Respond(HttpStatusCode.Conflict),
            { Method: "GET" } => RespondJson("""{"version":3,"state":"Draft"}"""),
            _ => Respond(HttpStatusCode.OK),
        });

        await SeedAsync(automation);

        automation.Calls[^1].IfMatch.ShouldBe("\"3\"");
    }

    [Fact]
    public async Task An_already_published_rule_is_left_alone()
    {
        StubHandler automation = new(request => request switch
        {
            { Method: "POST", Path: "/rules" } => Respond(HttpStatusCode.Conflict),
            { Method: "GET" } => RespondJson("""{"version":1,"state":"Active"}"""),
            _ => Respond(HttpStatusCode.OK),
        });

        await SeedAsync(automation);

        automation.Calls.ShouldNotContain(call => call.PathAndQuery.Contains("/publish"));
    }

    [Fact]
    public async Task An_archived_rule_is_not_resurrected()
    {
        // Someone archived it deliberately. Publishing would throw in the
        // domain anyway, but the seeder must not even ask.
        StubHandler automation = new(request => request switch
        {
            { Method: "POST", Path: "/rules" } => Respond(HttpStatusCode.Conflict),
            { Method: "GET" } => RespondJson("""{"version":2,"state":"Archived"}"""),
            _ => Respond(HttpStatusCode.OK),
        });

        await SeedAsync(automation);

        automation.Calls.ShouldNotContain(call => call.PathAndQuery.Contains("/publish"));
    }

    [Fact]
    public async Task A_freshly_created_rule_is_published_at_version_zero()
    {
        // The interceptor does not bump Added roots, so a rule that has just
        // been created sits at 0 — and no read is needed to know that.
        StubHandler automation = new(_ => Respond(HttpStatusCode.Created));

        await SeedAsync(automation);

        automation.Calls.ShouldNotContain(call => call.Method == "GET");
        automation.Calls[^1].PathAndQuery.ShouldBe("/rules/press-hot/publish?fabId=munich");
        automation.Calls[^1].IfMatch.ShouldBe("\"0\"");
    }

    [Fact]
    public async Task A_publish_that_fails_is_not_swallowed()
    {
        StubHandler automation = new(request => request switch
        {
            { Method: "POST", Path: "/rules" } => Respond(HttpStatusCode.Created),
            _ => Respond(HttpStatusCode.PreconditionRequired),
        });

        await Should.ThrowAsync<HttpRequestException>(() => SeedAsync(automation));
    }

    private static Task SeedAsync(StubHandler automation)
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

        HttpClient http = new(automation) { BaseAddress = new Uri("https://automation.test") };

        return new AutomationRulesClient(http, provider, NullLogger<AutomationRulesClient>.Instance)
            .EnsureRuleAsync(
                name: "press-hot",
                triggerSource: "plc",
                triggerKind: "PlcCycleStart",
                device: "press-1",
                comparison: "gte",
                threshold: 80,
                overlay: Guid.CreateVersion7(),
                durationMs: 5_000,
                CancellationToken.None);
    }

    private static HttpResponseMessage Respond(HttpStatusCode status) => new(status);

    private static HttpResponseMessage RespondJson(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed record Call(string Method, string Path, string PathAndQuery, string IfMatch);

    /// <summary>
    /// Records what was sent and answers from a per-request rule. Headers are
    /// captured at send time because the client disposes each request message.
    /// </summary>
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
                request.Headers.TryGetValues("If-Match", out IEnumerable<string>? values)
                    ? string.Join(",", values)
                    : string.Empty);

            Calls.Add(call);

            return Task.FromResult(respond(call));
        }
    }
}
