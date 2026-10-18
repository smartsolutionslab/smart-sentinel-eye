using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ScenarioSimulator.Configuration;
using SmartSentinelEye.ScenarioSimulator.Keycloak;
using SmartSentinelEye.ScenarioSimulator.Seeding;
using SmartSentinelEye.ScenarioSimulator.Tests.Fakes;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// Pins what <see cref="LayoutCompositionClientTests"/> cannot see: its stub records neither request
/// bodies nor <c>Authorization</c>, every fact uses a 2x2 grid (so a rows/cols swap is invisible) and
/// the logger is a <c>NullLogger</c>. Written before the recovery helpers' parameters are grouped, so
/// the grouping can be proven to move nothing on the wire or in the log.
/// </summary>
public class LayoutCompositionClientCharacterisationTests
{
    private const int DesiredRows = 2;

    private const int DesiredCols = 3;

    private const string DraftRevisionBody = "5";

    private const string VersionToken = "@@VERSION@@";

    private const string Wall = "0197f2d1-0000-7000-8000-00000000beef";

    private const string FirstCamera = "0197f2d1-0000-7000-8000-0000000000c1";

    private const string SecondCamera = "0197f2d1-0000-7000-8000-0000000000c2";

    private const string OtherCamera = "0197f2d1-0000-7000-8000-0000000000c9";

    private const string FirstOverlay = "0197f2d1-0000-7000-8000-0000000000a1";

    private const string SecondOverlay = "0197f2d1-0000-7000-8000-0000000000a2";

    private static readonly string[] RetileSequence =
    [
        "POST /layouts",
        "GET /layouts",
        $"POST /layouts/{Wall}/draft",
        "GET /layouts",
        $"PATCH /layouts/{Wall}/revisions/5",
        "GET /layouts",
        $"POST /layouts/{Wall}/revisions/5/publish",
    ];

    [Fact]
    public async Task A_re_tiled_wall_carries_two_rows_and_three_columns_in_the_edit_body()
    {
        var run = await SeedAsync(PublishedChain(gridRows: 2, gridCols: 3, differentTiles: true));

        var edit = run.Calls.Single(call => call.Method == "PATCH");
        using var body = JsonDocument.Parse(edit.Body);
        var grid = body.RootElement.GetProperty("grid");
        grid.GetProperty("rows").GetInt32().ShouldBe(DesiredRows);
        grid.GetProperty("cols").GetInt32().ShouldBe(DesiredCols);
    }

    [Fact]
    public async Task A_re_tiled_wall_carries_every_desired_tile_in_order_in_the_edit_body()
    {
        var run = await SeedAsync(PublishedChain(gridRows: 2, gridCols: 3, differentTiles: true));

        var edit = run.Calls.Single(call => call.Method == "PATCH");
        using var body = JsonDocument.Parse(edit.Body);
        var tiles = body.RootElement.GetProperty("tiles");
        tiles.GetArrayLength().ShouldBe(2);

        tiles[0].GetProperty("cameraIdentifier").GetGuid().ShouldBe(Guid.Parse(FirstCamera));
        tiles[0].GetProperty("overlayIdentifier").GetGuid().ShouldBe(Guid.Parse(FirstOverlay));
        tiles[0].GetProperty("row").GetInt32().ShouldBe(0);
        tiles[0].GetProperty("col").GetInt32().ShouldBe(0);

        tiles[1].GetProperty("cameraIdentifier").GetGuid().ShouldBe(Guid.Parse(SecondCamera));
        tiles[1].GetProperty("overlayIdentifier").GetGuid().ShouldBe(Guid.Parse(SecondOverlay));
        tiles[1].GetProperty("row").GetInt32().ShouldBe(1);
        tiles[1].GetProperty("col").GetInt32().ShouldBe(2);
    }

    [Fact]
    public async Task A_re_tiled_wall_runs_the_branch_edit_and_publish_steps_on_the_draft_revision_it_was_given()
    {
        var run = await SeedAsync(PublishedChain(gridRows: 2, gridCols: 3, differentTiles: true));

        run.Calls
            .Select(call => $"{call.Method} {call.PathAndQuery}")
            .ShouldBe(RetileSequence);
        run.Calls[^1].IfMatch.ShouldBe("\"9\"");
    }

    [Fact]
    public async Task A_published_wall_with_identical_tiles_but_the_grid_turned_round_is_re_tiled()
    {
        var run = await SeedAsync(PublishedChain(gridRows: 3, gridCols: 2, differentTiles: false));

        run.Calls
            .Select(call => $"{call.Method} {call.PathAndQuery}")
            .ShouldBe(RetileSequence);
    }

    [Fact]
    public async Task A_published_wall_with_the_same_grid_and_tiles_is_left_alone_and_says_so()
    {
        var run = await SeedAsync(PublishedChain(gridRows: 2, gridCols: 3, differentTiles: false));

        run.Calls
            .Select(call => $"{call.Method} {call.PathAndQuery}")
            .ShouldBe(["POST /layouts", "GET /layouts"]);
        run.Entries.ShouldBe(
        [
            (LogLevel.Information, "Wall layout 'rolling-mill' already exists and needs no publishing; skipping (idempotent)."),
        ]);
    }

    [Fact]
    public async Task Every_request_of_a_re_tiling_carries_the_minted_bearer_token()
    {
        var run = await SeedAsync(PublishedChain(gridRows: 2, gridCols: 3, differentTiles: true));

        run.Calls.Count.ShouldBe(7);
        run.Calls.ShouldAllBe(call => call.Authorization == "Bearer stub-token");
    }

    [Fact]
    public async Task Every_request_of_a_stranded_draft_recovery_carries_the_minted_bearer_token()
    {
        var run = await SeedAsync(DraftChain());

        run.Calls.Count.ShouldBe(3);
        run.Calls.ShouldAllBe(call => call.Authorization == "Bearer stub-token");
        run.Calls[^1].IfMatch.ShouldBe("\"7\"");
    }

    [Fact]
    public async Task A_wall_stranded_in_Draft_logs_that_it_was_published_exactly_once()
    {
        var run = await SeedAsync(DraftChain());

        run.Entries.ShouldBe(
        [
            (LogLevel.Information, $"Wall layout 'rolling-mill' ({Wall}) was left in Draft; published it."),
        ]);
    }

    [Fact]
    public async Task A_re_tiled_wall_logs_the_revision_it_published_exactly_once()
    {
        var run = await SeedAsync(PublishedChain(gridRows: 2, gridCols: 3, differentTiles: true));

        run.Entries.ShouldBe(
        [
            (LogLevel.Information, $"Wall layout 'rolling-mill' ({Wall}) had different tiles; re-tiled and published revision 5."),
        ]);
    }

    [Fact]
    public async Task A_wall_whose_revisions_are_missing_logs_a_warning_exactly_once()
    {
        var run = await SeedAsync(
            $$"""{"chains":[{"layoutIdentifier":"{{Wall}}","version":@@VERSION@@,"name":"rolling-mill"}]}""");

        run.Calls
            .Select(call => $"{call.Method} {call.PathAndQuery}")
            .ShouldBe(["POST /layouts", "GET /layouts"]);
        run.Entries.ShouldBe(
        [
            (LogLevel.Warning, $"Wall layout 'rolling-mill' ({Wall}) came back with no revisions; cannot tell whether it still needs publishing."),
        ]);
    }

    [Fact]
    public async Task An_archived_chain_is_left_alone_and_reported_as_already_existing()
    {
        var run = await SeedAsync(
            $$"""{"chains":[{"layoutIdentifier":"{{Wall}}","version":@@VERSION@@,"name":"rolling-mill","revisions":[{"revisionNumber":1,"state":"Archived"}]}]}""");

        run.Calls
            .Select(call => $"{call.Method} {call.PathAndQuery}")
            .ShouldBe(["POST /layouts", "GET /layouts"]);
        run.Entries.ShouldBe(
        [
            (LogLevel.Information, "Wall layout 'rolling-mill' already exists and needs no publishing; skipping (idempotent)."),
        ]);
    }

    private static string DraftChain() =>
        $$"""
        {"chains":[{
            "layoutIdentifier":"{{Wall}}",
            "version":@@VERSION@@,
            "name":"rolling-mill",
            "revisions":[{"revisionNumber":1,"state":"Draft"}]
        }]}
        """;

    private static string PublishedChain(int gridRows, int gridCols, bool differentTiles)
    {
        var firstCamera = differentTiles ? OtherCamera : FirstCamera;
        return $$"""
        {"chains":[{
            "layoutIdentifier":"{{Wall}}",
            "version":@@VERSION@@,
            "name":"rolling-mill",
            "revisions":[{
                "revisionNumber":1,
                "state":"Published",
                "gridRows":{{gridRows}},
                "gridCols":{{gridCols}},
                "tiles":[
                    {"cameraIdentifier":"{{firstCamera}}","overlayIdentifier":"{{FirstOverlay}}","row":0,"col":0},
                    {"cameraIdentifier":"{{SecondCamera}}","overlayIdentifier":"{{SecondOverlay}}","row":1,"col":2}
                ]
            }]
        }]}
        """;
    }

    /// <summary>
    /// Each read-back answers the next version (7, 8, 9...), so a step that sends the wrong read's
    /// version is told apart from one that sends its own.
    /// </summary>
    private static async Task<Run> SeedAsync(string readBack)
    {
        var version = 7;
        RecordingHandler layouts = new(call => call switch
        {
            { Method: "POST", Path: "/layouts" } => new HttpResponseMessage(HttpStatusCode.Conflict),
            { Method: "GET" } => Json(readBack.Replace(VersionToken, (version++).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)),
            { Method: "POST", Path: var path } when path.EndsWith("/draft", StringComparison.Ordinal) =>
                Json(DraftRevisionBody),
            _ => new HttpResponseMessage(HttpStatusCode.OK),
        });

        HttpClient tokens = new(new RecordingHandler(_ =>
            Json("""{"access_token":"stub-token","expires_in":300,"token_type":"Bearer"}""")));

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

        HttpClient http = new(layouts) { BaseAddress = new Uri("https://layouts.test") };
        RecordingLogger<LayoutCompositionClient> logger = new();

        List<CorrelatedTile> tiles =
        [
            new CorrelatedTile(Guid.Parse(FirstCamera), Guid.Parse(FirstOverlay), 0, 0),
            new CorrelatedTile(Guid.Parse(SecondCamera), Guid.Parse(SecondOverlay), 1, 2),
        ];

        await new LayoutCompositionClient(http, provider, logger)
            .EnsureWallAsync("rolling-mill", DesiredRows, DesiredCols, tiles, CancellationToken.None);

        return new Run(layouts.Calls, logger.Entries);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed record Run(
        IReadOnlyList<Call> Calls,
        IReadOnlyList<(LogLevel Level, string Message)> Entries);

    private sealed record Call(
        string Method,
        string Path,
        string PathAndQuery,
        string IfMatch,
        string Authorization,
        string Body);

    /// <summary>
    /// Records method, path, <c>If-Match</c>, <c>Authorization</c> and body at send time, because the
    /// client disposes each request message.
    /// </summary>
    private sealed class RecordingHandler(Func<Call, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Call> Calls { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Call call = new(
                request.Method.Method,
                request.RequestUri!.AbsolutePath,
                request.RequestUri.PathAndQuery,
                request.Headers.TryGetValues("If-Match", out var values) ? string.Join(",", values) : string.Empty,
                request.Headers.Authorization?.ToString() ?? string.Empty,
                request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

            Calls.Add(call);
            return respond(call);
        }
    }
}
