using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ScenarioSimulator.Configuration;
using SmartSentinelEye.ScenarioSimulator.Keycloak;
using SmartSentinelEye.ScenarioSimulator.Seeding;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// The wall has the same seeding hazard as a rule, one layer out: every run
/// between ADR-0113 making <c>If-Match</c> mandatory and this client learning
/// to send it created the wall and then failed to publish it. Those walls are
/// still sitting in Draft — fully tiled, and never rendered by the kiosk.
///
/// <para>
/// Treating the create endpoint's 409 as "already seeded" is what kept them
/// there: the wall existed, so the seeder reported success and moved on. The
/// interesting database is not an empty one, it is one holding the wreckage of
/// an earlier run.
/// </para>
/// </summary>
public class LayoutCompositionClientTests
{
    [Fact]
    public async Task A_wall_a_previous_run_left_in_Draft_is_published()
    {
        StubHandler layouts = new(request => request switch
        {
            { Method: "POST", Path: "/layouts" } => Respond(HttpStatusCode.Conflict),
            { Method: "GET" } => RespondJson(Chain(version: 0, state: "Draft")),
            _ => Respond(HttpStatusCode.OK),
        });

        await SeedAsync(layouts);

        layouts.Calls
            .Select(call => $"{call.Method} {call.PathAndQuery}")
            .ShouldBe([
                "POST /layouts",
                "GET /layouts",
                $"POST /layouts/{StrandedWall}/revisions/1/publish",
            ]);
    }

    [Fact]
    public async Task The_stranded_walls_own_version_is_echoed_back_as_the_precondition()
    {
        // Not necessarily 0: whatever the chain has actually reached is what
        // publish must be built on, or it comes back 409 stale.
        StubHandler layouts = new(request => request switch
        {
            { Method: "POST", Path: "/layouts" } => Respond(HttpStatusCode.Conflict),
            { Method: "GET" } => RespondJson(Chain(version: 4, state: "Draft")),
            _ => Respond(HttpStatusCode.OK),
        });

        await SeedAsync(layouts);

        layouts.Calls[^1].IfMatch.ShouldBe("\"4\"");
    }

    [Fact]
    public async Task An_already_published_wall_is_left_alone()
    {
        StubHandler layouts = new(request => request switch
        {
            { Method: "POST", Path: "/layouts" } => Respond(HttpStatusCode.Conflict),
            { Method: "GET" } => RespondJson(Chain(version: 1, state: "Published")),
            _ => Respond(HttpStatusCode.OK),
        });

        await SeedAsync(layouts);

        layouts.Calls.ShouldNotContain(call => call.PathAndQuery.Contains("/publish"));
    }

    [Fact]
    public async Task A_wall_whose_revisions_are_missing_is_not_assumed_to_be_fine()
    {
        // A read model that stopped returning revisions must not read as
        // "nothing to publish" — that is the silent skip this recovery exists
        // to undo. Publishing blind would be just as wrong, so it does neither.
        StubHandler layouts = new(request => request switch
        {
            { Method: "POST", Path: "/layouts" } => Respond(HttpStatusCode.Conflict),
            { Method: "GET" } => RespondJson(
                $$"""{"chains":[{"layoutIdentifier":"{{StrandedWall}}","version":0,"name":"rolling-mill"}]}"""),
            _ => Respond(HttpStatusCode.OK),
        });

        await SeedAsync(layouts);

        layouts.Calls.ShouldNotContain(call => call.PathAndQuery.Contains("/publish"));
    }

    [Fact]
    public async Task A_freshly_created_wall_is_published_at_version_zero()
    {
        // The interceptor does not bump Added roots, so a wall that has just
        // been created sits at 0 — and no read is needed to know that.
        StubHandler layouts = new(request => request switch
        {
            { Method: "POST", Path: "/layouts" } => RespondJson($"\"{FreshWall}\""),
            _ => Respond(HttpStatusCode.OK),
        });

        await SeedAsync(layouts);

        layouts.Calls.ShouldNotContain(call => call.Method == "GET");
        layouts.Calls[^1].PathAndQuery.ShouldBe($"/layouts/{FreshWall}/revisions/1/publish");
        layouts.Calls[^1].IfMatch.ShouldBe("\"0\"");
    }

    [Fact]
    public async Task A_conflict_that_cannot_be_read_back_is_not_swallowed()
    {
        // Silently returning would report the wall as seeded while leaving it
        // in whatever state it is actually in.
        StubHandler layouts = new(request => request switch
        {
            { Method: "POST", Path: "/layouts" } => Respond(HttpStatusCode.Conflict),
            { Method: "GET" } => RespondJson("""{"chains":[]}"""),
            _ => Respond(HttpStatusCode.OK),
        });

        await Should.ThrowAsync<InvalidOperationException>(() => SeedAsync(layouts));
    }

    [Fact]
    public async Task A_publish_that_fails_is_not_swallowed()
    {
        StubHandler layouts = new(request => request switch
        {
            { Method: "POST", Path: "/layouts" } => RespondJson($"\"{FreshWall}\""),
            _ => Respond(HttpStatusCode.PreconditionRequired),
        });

        await Should.ThrowAsync<HttpRequestException>(() => SeedAsync(layouts));
    }

    private const string StrandedWall = "0197f2d1-0000-7000-8000-00000000beef";

    private const string FreshWall = "0197f2d1-0000-7000-8000-00000000cafe";

    private static string Chain(int version, string state) =>
        $$"""
        {"chains":[{
            "layoutIdentifier":"{{StrandedWall}}",
            "version":{{version}},
            "name":"rolling-mill",
            "revisions":[{"revisionNumber":1,"state":"{{state}}"}]
        }]}
        """;

    private static Task SeedAsync(StubHandler layouts)
    {
        HttpClient tokens = new(new StubHandler(_ =>
            RespondJson("""{"access_token":"stub-token","expires_in":300,"token_type":"Bearer"}""")));

        KeycloakTokenProvider provider = new(
            tokens,
            Options.Create(new SimulatorOptions
            {
                KeycloakUrl = "https://keycloak.test",
                Realm = "smart-sentinel-eye",
                ClientId = "scenario-simulator",
                ClientSecret = "stub-secret",
            }),
            TimeProvider.System,
            NullLogger<KeycloakTokenProvider>.Instance);

        HttpClient http = new(layouts) { BaseAddress = new Uri("https://layouts.test") };

        List<CorrelatedTile> tiles =
        [
            new(Guid.CreateVersion7(), Guid.CreateVersion7(), 0, 0),
        ];

        return new LayoutCompositionClient(http, provider, NullLogger<LayoutCompositionClient>.Instance)
            .EnsureWallAsync("rolling-mill", rows: 2, cols: 2, tiles, CancellationToken.None);
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
