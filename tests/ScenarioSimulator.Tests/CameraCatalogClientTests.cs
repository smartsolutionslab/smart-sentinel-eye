using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SmartSentinelEye.ScenarioSimulator.CameraCatalog;
using SmartSentinelEye.ScenarioSimulator.Configuration;
using SmartSentinelEye.ScenarioSimulator.Keycloak;

namespace SmartSentinelEye.ScenarioSimulator.Tests;

/// <summary>
/// The read-back correlates an already-registered camera to its wall tile, and
/// it used to look in one page.
///
/// <para>
/// <b>The failure was worse than silence.</b> Asking for one page of 200 — the
/// largest the catalog serves — and searching it by name meant a camera past the
/// two-hundredth was reported "not present in the catalog listing": a false
/// statement about the catalogue, logged as a read-back failure, with the tile
/// correlation lost. The constitution targets 250 cameras per fab, so the target
/// itself reaches it.
/// </para>
///
/// <para>
/// <b>And nothing could have caught it by reading the code.</b> The seeder's
/// page record carried only <c>Items</c>, so the "there is more" signal was not
/// ignored — it was inexpressible. That is why the first test below asserts on
/// the requests rather than only on the answer.
/// </para>
/// </summary>
public class CameraCatalogClientTests
{
    private const int PageSize = 200;

    [Fact]
    public async Task A_camera_past_the_first_page_is_still_found()
    {
        StubHandler catalog = Catalog(total: 250, wanted: "camera-241", wantedAt: 240);

        Guid? found = await ReadBackAsync(catalog, "camera-241");

        found.ShouldNotBeNull(
            "the camera exists; before this it was reported absent because it sat past the "
            + "largest page the catalog will serve");
    }

    /// <summary>
    /// **The requests, not just the answer.** A client that happened to find the
    /// camera because a stub returned everything in one page would pass the test
    /// above while still being unable to walk. This pins the walk.
    /// </summary>
    [Fact]
    public async Task The_walk_continues_past_the_page_the_catalog_will_serve()
    {
        StubHandler catalog = Catalog(total: 250, wanted: "camera-241", wantedAt: 240);

        await ReadBackAsync(catalog, "camera-241");

        catalog.Queries.ShouldBe([
            $"/cameras?limit={PageSize}&offset=0",
            $"/cameras?limit={PageSize}&offset={PageSize}",
        ]);
    }

    /// <summary>
    /// Raising the page size is not the fix: the catalog <b>refuses</b> anything
    /// above its maximum rather than clamping, so a larger request is an error
    /// and not a bigger page.
    /// </summary>
    [Fact]
    public async Task No_request_asks_for_more_than_the_catalog_will_serve()
    {
        StubHandler catalog = Catalog(total: 250, wanted: "camera-241", wantedAt: 240);

        await ReadBackAsync(catalog, "camera-241");

        catalog.Queries.ShouldAllBe(query => query.Contains($"limit={PageSize}"));
    }

    [Fact]
    public async Task A_camera_on_the_first_page_costs_one_request()
    {
        StubHandler catalog = Catalog(total: 250, wanted: "camera-007", wantedAt: 6);

        Guid? found = await ReadBackAsync(catalog, "camera-007");

        found.ShouldNotBeNull();
        catalog.Queries.Count.ShouldBe(1, "the walk stops at the match rather than reading on");
    }

    [Fact]
    public async Task A_camera_that_is_genuinely_absent_is_reported_absent()
    {
        StubHandler catalog = Catalog(total: 250, wanted: "never-registered", wantedAt: -1);

        Guid? found = await ReadBackAsync(catalog, "never-registered");

        found.ShouldBeNull();
    }

    /// <summary>
    /// **A count that outruns the rows must not spin forever.** A camera retired
    /// between two requests leaves the reported total above what the pages will
    /// yield; the walk ends on the empty page rather than on the arithmetic.
    /// </summary>
    [Fact]
    public async Task A_count_larger_than_the_rows_ends_the_walk_rather_than_looping()
    {
        // Claims 500, serves 250. Without the empty-page guard the offset never
        // reaches the count and this never returns.
        StubHandler catalog = Catalog(total: 250, wanted: "never-registered", wantedAt: -1, reportedCount: 500);

        Guid? found = await ReadBackAsync(catalog, "never-registered");

        found.ShouldBeNull();
        catalog.Queries.Count.ShouldBe(3, "two pages of rows, then the empty page that stops it");
    }

    private static StubHandler Catalog(int total, string wanted, int wantedAt, int? reportedCount = null)
    {
        return new StubHandler(query =>
        {
            int offset = ParseOffset(query);
            int take = Math.Max(0, Math.Min(PageSize, total - offset));

            string items = string.Join(",", Enumerable.Range(offset, take).Select(index =>
            {
                string name = index == wantedAt ? wanted : $"filler-{index}";
                return $$"""{"cameraIdentifier":"{{Guid.CreateVersion7()}}","name":"{{name}}"}""";
            }));

            return RespondJson($$"""{"items":[{{items}}],"count":{{reportedCount ?? total}},"offset":{{offset}},"limit":{{PageSize}}}""");
        });
    }

    private static int ParseOffset(string query)
    {
        int marker = query.IndexOf("offset=", StringComparison.Ordinal);

        return marker < 0 ? 0 : int.Parse(query[(marker + "offset=".Length)..], CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Drives the read-back through the public surface: a POST answered 409
    /// sends the client down the already-registered path.
    /// </summary>
    private static Task<Guid?> ReadBackAsync(StubHandler catalog, string name)
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

        HttpClient http = new(catalog) { BaseAddress = new Uri("https://cameras.test") };

        return new CameraCatalogClient(
                http,
                provider,
                Options.Create(new SimulatorOptions { RtspHost = "rtsp.test" }),
                NullLogger<CameraCatalogClient>.Instance)
            .RegisterCameraAsync(name, "cam/1", CancellationToken.None);
    }

    private static HttpResponseMessage RespondJson(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>
    /// Answers list requests from a rule and records the queries. A POST is
    /// always a conflict, which is what routes the client into the read-back.
    /// </summary>
    private sealed class StubHandler(Func<string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Queries { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/cameras")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict));
            }

            string query = request.RequestUri!.PathAndQuery;

            if (query.StartsWith("/cameras", StringComparison.Ordinal))
            {
                Queries.Add(query);
            }

            return Task.FromResult(respond(query));
        }
    }
}
