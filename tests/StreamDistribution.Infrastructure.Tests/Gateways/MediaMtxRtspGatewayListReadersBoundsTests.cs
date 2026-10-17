using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.StreamDistribution.Infrastructure.Gateways;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Gateways;

/// <summary>
/// #3010 / spec 391 US1 and US2. The health read and the configured-paths read
/// end on an empty page, at one thousand pages, or on a <c>pageCount</c> they
/// cannot read as a 32-bit integer, whatever MediaMTX claims. A cut-short health
/// read throws rather than returning part of the list (a caller reads an absent
/// path as "not registered"); a cut-short configured-paths read returns what it
/// read with one Warning. The stub throws on the 1,101st request, so a read that
/// is not bounded fails fast instead of hanging.
/// </summary>
public class MediaMtxRtspGatewayListReadersBoundsTests
{
    private const string HugePageCount = "2147483647";
    private const string HealthEndpoint = "/v3/paths/list";
    private const string ConfiguredEndpoint = "/v3/config/paths/list";

    private readonly CapturingLogger<MediaMtxRtspGateway> logger = new();

    [Fact]
    public async Task An_empty_page_ends_the_health_read_whatever_page_count_claims()
    {
        var handler = new PagesHandler(page => page == 0
            ? Body(HugePageCount, HealthItem($"cam-{Guid.NewGuid()}"))
            : Body(HugePageCount));

        var health = await GatewayOver(handler).GetAllPathsHealthAsync(CancellationToken.None);

        handler.RequestedPages.ShouldBe([0, 1]);
        handler.RequestedPaths.ShouldAllBe(path => path == HealthEndpoint);
        health.Count.ShouldBe(1);
        logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task The_health_read_fails_after_one_thousand_pages_rather_than_returning_part_of_the_list()
    {
        var handler = new PagesHandler(_ => Body(HugePageCount, HealthItem($"cam-{Guid.NewGuid()}")));

        var failure = await Should.ThrowAsync<HttpRequestException>(
            () => GatewayOver(handler).GetAllPathsHealthAsync(CancellationToken.None));

        failure.HttpRequestError.ShouldBe(HttpRequestError.InvalidResponse);
        failure.StatusCode.ShouldBeNull();
        failure.Message.ShouldContain("1000");
        handler.RequestedPages.Count.ShouldBe(1000);
        handler.RequestedPages.Max().ShouldBe(999);
        handler.RequestedPaths.ShouldAllBe(path => path == HealthEndpoint);
        logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("3e9")]
    [InlineData("2147483648")]
    [InlineData("-2147483649")]
    [InlineData("1e400")]
    public async Task An_unreadable_page_count_fails_the_health_read_rather_than_returning_part_of_the_list(
        string rawPageCount)
    {
        var handler = new PagesHandler(_ => Body(rawPageCount, HealthItem($"cam-{Guid.NewGuid()}")));

        var failure = await Should.ThrowAsync<HttpRequestException>(
            () => GatewayOver(handler).GetAllPathsHealthAsync(CancellationToken.None));

        failure.HttpRequestError.ShouldBe(HttpRequestError.InvalidResponse);
        handler.RequestedPages.ShouldBe([0]);
        handler.RequestedPaths.ShouldAllBe(path => path == HealthEndpoint);
        failure.Message.ShouldContain("page 0");
        failure.Message.ShouldNotContain(rawPageCount, Case.Sensitive, "the token is MediaMTX's and unbounded in length");
        logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task An_empty_page_ends_the_configured_paths_read_whatever_page_count_claims()
    {
        var handler = new PagesHandler(page => page == 0
            ? Body(HugePageCount, ConfiguredItem($"cam-{Guid.NewGuid()}"))
            : Body(HugePageCount));

        var paths = await GatewayOver(handler).ListConfiguredPathsAsync(CancellationToken.None);

        handler.RequestedPages.ShouldBe([0, 1]);
        handler.RequestedPaths.ShouldAllBe(path => path == ConfiguredEndpoint);
        paths.Count.ShouldBe(1);
        logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task The_configured_paths_read_stops_at_one_thousand_pages_and_warns_once()
    {
        var handler = new PagesHandler(_ => Body(HugePageCount, ConfiguredItem($"cam-{Guid.NewGuid()}")));

        var paths = await GatewayOver(handler).ListConfiguredPathsAsync(CancellationToken.None);

        handler.RequestedPages.Count.ShouldBe(1000);
        handler.RequestedPages.Max().ShouldBe(999);
        handler.RequestedPaths.ShouldAllBe(path => path == ConfiguredEndpoint);
        paths.Count.ShouldBe(1000);
        var warnings = logger.Entries.Where(entry => entry.Level == LogLevel.Warning).ToList();
        warnings.Count.ShouldBe(1);
        warnings[0].Message.ShouldContain("1000");
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("3e9")]
    [InlineData("2147483648")]
    [InlineData("-2147483649")]
    [InlineData("1e400")]
    public async Task An_unreadable_page_count_ends_the_configured_paths_read_with_one_warning_not_an_exception(
        string rawPageCount)
    {
        var handler = new PagesHandler(_ => Body(rawPageCount, ConfiguredItem($"cam-{Guid.NewGuid()}")));

        var paths = await GatewayOver(handler).ListConfiguredPathsAsync(CancellationToken.None);

        handler.RequestedPages.ShouldBe([0]);
        handler.RequestedPaths.ShouldAllBe(path => path == ConfiguredEndpoint);
        paths.Count.ShouldBe(1);
        var warnings = logger.Entries.Where(entry => entry.Level == LogLevel.Warning).ToList();
        warnings.Count.ShouldBe(1);
        warnings[0].Message.ShouldContain("page 0");
        warnings[0].Message.ShouldNotContain(rawPageCount, Case.Sensitive, "the token is MediaMTX's and unbounded in length");
    }

    [Theory]
    [InlineData("", "")]
    [InlineData(",\"items\":null", "")]
    [InlineData(",\"items\":{\"leaky-marker-7\":1}", "leaky-marker-7")]
    [InlineData(",\"items\":\"leaky-marker-7\"", "leaky-marker-7")]
    public async Task A_health_page_without_a_readable_items_array_fails_the_read_rather_than_ending_it_complete(
        string itemsMember,
        string rawContent)
    {
        var handler = new PagesHandler(_ => $"{{\"pageCount\":2{itemsMember}}}");

        var failure = await Should.ThrowAsync<HttpRequestException>(
            () => GatewayOver(handler).GetAllPathsHealthAsync(CancellationToken.None));

        failure.HttpRequestError.ShouldBe(HttpRequestError.InvalidResponse);
        handler.RequestedPages.ShouldBe([0]);
        handler.RequestedPaths.ShouldAllBe(path => path == HealthEndpoint);
        failure.Message.ShouldContain("page 0");
        if (rawContent.Length > 0)
        {
            failure.Message.ShouldNotContain(rawContent, Case.Sensitive, "the content is MediaMTX's and unbounded in length");
        }

        logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }

    private MediaMtxRtspGateway GatewayOver(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://mediamtx.test") }, logger);

    private static string HealthItem(string name) => $"{{\"name\":\"{name}\",\"ready\":true}}";

    private static string ConfiguredItem(string name) => $"{{\"name\":\"{name}\"}}";

    private static string Body(string rawPageCount, params string[] items) =>
        $"{{\"pageCount\":{rawPageCount},\"items\":[{string.Join(",", items)}]}}";

    private sealed class PagesHandler(Func<int, string> bodyForPage) : HttpMessageHandler
    {
        private const int RequestCeiling = 1100;

        private readonly List<int> requestedPages = [];
        private readonly List<string> requestedPaths = [];

        public List<int> RequestedPages => requestedPages;

        public List<string> RequestedPaths => requestedPaths;

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
            requestedPaths.Add(uri.AbsolutePath);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(bodyForPage(page), Encoding.UTF8, "application/json"),
            });
        }
    }
}
