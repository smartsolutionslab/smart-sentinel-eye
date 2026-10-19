using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.StreamDistribution.Infrastructure.Gateways;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Gateways;

/// <summary>
/// #2994 / spec 388 US2. The configured-sources read ends on an empty page, at
/// twenty pages, or on a <c>pageCount</c> it cannot read as a 32-bit
/// integer, whatever MediaMTX claims, so a hostile or broken answer can neither
/// hang the sweep nor throw out of it. The stub throws on the 1,101st request,
/// so a read that is not bounded fails fast instead of hanging.
/// </summary>
public class MediaMtxRtspGatewayConfiguredSourcesBoundsTests
{
    private const string HugePageCount = "2147483647";

    private readonly CapturingLogger<MediaMtxRtspGateway> logger = new();

    [Fact]
    public async Task An_empty_page_ends_the_read_whatever_page_count_claims()
    {
        var first = $"cam-{Guid.NewGuid()}";
        var handler = new PagesHandler(page => page == 0
            ? Body(HugePageCount, Item(first, "rtsp://10.0.8.1/h264"))
            : Body(HugePageCount));

        var sources = await GatewayOver(handler).ListConfiguredPathSourcesAsync(CancellationToken.None);

        handler.RequestedPages.ShouldBe([0, 1]);
        sources.Count.ShouldBe(1);
        logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task The_read_stops_at_twenty_pages_and_warns_once()
    {
        var handler = new PagesHandler(_ => Body(HugePageCount, Item($"cam-{Guid.NewGuid()}", "rtsp://10.0.8.1/h264")));

        var sources = await GatewayOver(handler).ListConfiguredPathSourcesAsync(CancellationToken.None);

        handler.RequestedPages.Count.ShouldBe(20);
        handler.RequestedPages.Max().ShouldBe(19);
        sources.Count.ShouldBe(20);
        var warnings = logger.Entries.Where(entry => entry.Level == LogLevel.Warning).ToList();
        warnings.Count.ShouldBe(1);
        warnings[0].Message.ShouldContain("20");
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("3e9")]
    [InlineData("2147483648")]
    [InlineData("-2147483649")]
    [InlineData("1e400")]
    public async Task An_unreadable_page_count_ends_the_read_with_one_warning_not_an_exception(string rawPageCount)
    {
        var first = $"cam-{Guid.NewGuid()}";
        var handler = new PagesHandler(_ => Body(rawPageCount, Item(first, "rtsp://10.0.8.1/h264")));

        var sources = await GatewayOver(handler).ListConfiguredPathSourcesAsync(CancellationToken.None);

        handler.RequestedPages.ShouldBe([0]);
        sources.Count.ShouldBe(1);
        var warnings = logger.Entries.Where(entry => entry.Level == LogLevel.Warning).ToList();
        warnings.Count.ShouldBe(1);
        warnings[0].Message.ShouldNotContain(rawPageCount, Case.Sensitive, "the token is MediaMTX's and unbounded in length");
    }

    [Fact]
    public async Task A_page_of_only_non_canonical_paths_is_not_an_empty_page()
    {
        var canonical = $"cam-{Guid.NewGuid()}";
        var handler = new PagesHandler(page => page == 0
            ? Body("2", Item("manual-x", "rtsp://10.0.0.9/x"))
            : Body("2", Item(canonical, "rtsp://10.0.8.1/h264")));

        var sources = await GatewayOver(handler).ListConfiguredPathSourcesAsync(CancellationToken.None);

        handler.RequestedPages.ShouldBe([0, 1]);
        sources.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData("\"5\"")]
    [InlineData("null")]
    public async Task A_page_count_that_is_not_a_number_still_reads_one_page_without_a_warning(string rawPageCount)
    {
        var handler = new PagesHandler(_ => Body(rawPageCount, Item($"cam-{Guid.NewGuid()}", "rtsp://10.0.8.1/h264")));

        await GatewayOver(handler).ListConfiguredPathSourcesAsync(CancellationToken.None);

        handler.RequestedPages.ShouldBe([0]);
        logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }

    private MediaMtxRtspGateway GatewayOver(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://mediamtx.test") }, logger);

    private static string Item(string name, string source) => $"{{\"name\":\"{name}\",\"source\":\"{source}\"}}";

    private static string Body(string rawPageCount, params string[] items) =>
        $"{{\"pageCount\":{rawPageCount},\"items\":[{string.Join(",", items)}]}}";

    private sealed class PagesHandler(Func<int, string> bodyForPage) : HttpMessageHandler
    {
        private const int RequestCeiling = 1100;

        private readonly List<int> requestedPages = [];

        public List<int> RequestedPages => requestedPages;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (requestedPages.Count >= RequestCeiling)
            {
                throw new InvalidOperationException("unbounded read");
            }

            var uri = request.RequestUri ?? throw new InvalidOperationException("request had no URI");
            var page = uri.Query.TrimStart('?').Split('&')
                .Where(part => part.StartsWith("page=", StringComparison.Ordinal))
                .Select(part => int.Parse(part["page=".Length..], CultureInfo.InvariantCulture))
                .DefaultIfEmpty(0)
                .First();
            requestedPages.Add(page);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(bodyForPage(page), Encoding.UTF8, "application/json"),
            });
        }
    }
}
