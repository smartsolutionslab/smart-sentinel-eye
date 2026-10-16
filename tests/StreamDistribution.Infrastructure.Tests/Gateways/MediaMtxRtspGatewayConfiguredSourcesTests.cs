using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Gateways;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Gateways;

/// <summary>
/// #2960 / spec 378 FR-001. The source repair compares the database's source
/// with what <c>GET /v3/config/paths/list</c> reports, byte for byte, so the
/// gateway must hand the configured <c>source</c> back untouched: a normaliser
/// anywhere in this read would hide exactly the divergence the pass repairs.
/// </summary>
public class MediaMtxRtspGatewayConfiguredSourcesTests
{
    [Theory]
    [InlineData("rtsp://10.0.8.1:554/h264")]
    [InlineData("rtsp://CAM-A/h264/")]
    [InlineData("RTSP://cam-a/Stream?token=ABC")]
    [InlineData("publisher")]
    public async Task The_configured_source_is_returned_verbatim(string source)
    {
        var path = $"cam-{Guid.NewGuid()}";
        var handler = new SourcePagesHandler(new Dictionary<int, string> { [0] = Page(1, (path, source)) });
        var gateway = GatewayOver(handler);

        var sources = await gateway.ListConfiguredPathSourcesAsync(CancellationToken.None);

        sources[MediaMtxPath.From(path)].ShouldBe(source);
        handler.RequestedPaths.ShouldAllBe(
            requested => requested == "/v3/config/paths/list",
            "the config list endpoint, which carries `source`, not the runtime /v3/paths/list");
    }

    [Fact]
    public async Task A_name_that_is_not_a_canonical_camera_path_is_left_out()
    {
        var canonical = $"cam-{Guid.NewGuid()}";
        var handler = new SourcePagesHandler(new Dictionary<int, string>
        {
            [0] = Page(1, ("manually-created-path", "rtsp://10.0.0.9/x"), (canonical, "rtsp://10.0.8.1/h264")),
        });
        var gateway = GatewayOver(handler);

        var sources = await gateway.ListConfiguredPathSourcesAsync(CancellationToken.None);

        sources.Keys.ShouldBe([MediaMtxPath.From(canonical)]);
    }

    [Fact]
    public async Task Every_page_mediamtx_reports_is_merged()
    {
        var first = Enumerable.Range(0, 100).Select(_ => $"cam-{Guid.NewGuid()}").ToArray();
        var second = Enumerable.Range(0, 100).Select(_ => $"cam-{Guid.NewGuid()}").ToArray();
        var third = Enumerable.Range(0, 7).Select(_ => $"cam-{Guid.NewGuid()}").ToArray();
        var handler = new SourcePagesHandler(new Dictionary<int, string>
        {
            [0] = Page(3, [.. first.Select(name => (name, $"rtsp://10.0.1.1/{name}"))]),
            [1] = Page(3, [.. second.Select(name => (name, $"rtsp://10.0.2.1/{name}"))]),
            [2] = Page(3, [.. third.Select(name => (name, $"rtsp://10.0.3.1/{name}"))]),
        });
        var gateway = GatewayOver(handler);

        var sources = await gateway.ListConfiguredPathSourcesAsync(CancellationToken.None);

        sources.Count.ShouldBe(207);
        sources[MediaMtxPath.From(third[6])].ShouldBe($"rtsp://10.0.3.1/{third[6]}");
        handler.RequestedPages.ShouldBe([0, 1, 2]);
    }

    [Fact]
    public async Task A_canonical_item_without_a_source_or_with_a_non_string_source_is_left_out()
    {
        var withSource = $"cam-{Guid.NewGuid()}";
        var withoutSource = $"cam-{Guid.NewGuid()}";
        var numericSource = $"cam-{Guid.NewGuid()}";
        var nullSource = $"cam-{Guid.NewGuid()}";
        var body = "{\"pageCount\":1,\"items\":["
            + $"{{\"name\":\"{withSource}\",\"source\":\"rtsp://10.0.8.1/h264\"}},"
            + $"{{\"name\":\"{withoutSource}\"}},"
            + $"{{\"name\":\"{numericSource}\",\"source\":42}},"
            + $"{{\"name\":\"{nullSource}\",\"source\":null}}]}}";
        var gateway = GatewayOver(new SourcePagesHandler(new Dictionary<int, string> { [0] = body }));

        var sources = await gateway.ListConfiguredPathSourcesAsync(CancellationToken.None);

        sources.Keys.ShouldBe([MediaMtxPath.From(withSource)]);
    }

    [Fact]
    public async Task A_response_without_a_page_count_is_one_page()
    {
        var path = $"cam-{Guid.NewGuid()}";
        var body = $"{{\"items\":[{{\"name\":\"{path}\",\"source\":\"rtsp://10.0.8.1/h264\"}}]}}";
        var handler = new SourcePagesHandler(new Dictionary<int, string> { [0] = body });
        var gateway = GatewayOver(handler);

        var sources = await gateway.ListConfiguredPathSourcesAsync(CancellationToken.None);

        sources.Count.ShouldBe(1);
        handler.RequestedPages.ShouldBe([0], "an absent pageCount must not cause a second request");
    }

    private static MediaMtxRtspGateway GatewayOver(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://mediamtx.test") }, NullLogger<MediaMtxRtspGateway>.Instance);

    private static string Page(int pageCount, params (string Name, string Source)[] items)
    {
        var rendered = string.Join(
            ",", items.Select(item => $"{{\"name\":\"{item.Name}\",\"source\":\"{item.Source}\"}}"));

        return $"{{\"pageCount\":{pageCount},\"items\":[{rendered}]}}";
    }

    private sealed class SourcePagesHandler(Dictionary<int, string> pages) : HttpMessageHandler
    {
        private readonly List<int> requestedPages = [];
        private readonly List<string> requestedPaths = [];

        public IReadOnlyList<int> RequestedPages => requestedPages;

        public IReadOnlyList<string> RequestedPaths => requestedPaths;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("request had no URI");
            var page = uri.Query.TrimStart('?').Split('&')
                .Where(part => part.StartsWith("page=", StringComparison.Ordinal))
                .Select(part => int.Parse(part["page=".Length..], CultureInfo.InvariantCulture))
                .DefaultIfEmpty(0)
                .First();
            requestedPages.Add(page);
            requestedPaths.Add(uri.AbsolutePath);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(pages[page], Encoding.UTF8, "application/json"),
            });
        }
    }
}
