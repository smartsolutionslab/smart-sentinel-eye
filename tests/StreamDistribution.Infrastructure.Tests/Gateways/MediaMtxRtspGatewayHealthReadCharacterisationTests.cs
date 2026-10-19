using System.Globalization;
using System.Net;
using System.Text;
using SmartSentinelEye.StreamDistribution.Infrastructure.Gateways;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Gateways;

/// <summary>
/// #3029. Characterises how the health read reports a read that is cut short
/// part-way through: which page the error names, which pages were requested,
/// and that a complete read returns every path. The bounds tests only ever cut
/// the read short on page 0, so a wrong page index in the message went unnoticed.
/// </summary>
public class MediaMtxRtspGatewayHealthReadCharacterisationTests
{
    private const string HealthEndpoint = "/v3/paths/list";
    private const string UnreadablePageCount = "1.5";

    private readonly CapturingLogger<MediaMtxRtspGateway> logger = new();

    [Fact]
    public async Task An_unreadable_page_count_on_the_second_page_names_that_page_in_the_error()
    {
        var handler = new PagesHandler(page => page == 0
            ? Body("3", HealthItem($"cam-{Guid.NewGuid()}"))
            : Body(UnreadablePageCount, HealthItem($"cam-{Guid.NewGuid()}")));

        var failure = await Should.ThrowAsync<HttpRequestException>(
            () => GatewayOver(handler).GetAllPathsHealthAsync(CancellationToken.None));

        failure.HttpRequestError.ShouldBe(HttpRequestError.InvalidResponse);
        failure.Message.ShouldContain("page 1");
        failure.Message.ShouldNotContain(UnreadablePageCount, Case.Sensitive, "the token is MediaMTX's and unbounded in length");
        handler.RequestedPages.ShouldBe([0, 1]);
        handler.RequestedPaths.ShouldAllBe(path => path == HealthEndpoint);
    }

    [Fact]
    public async Task A_second_page_without_an_items_array_names_that_page_in_the_error()
    {
        var handler = new PagesHandler(page => page == 0
            ? Body("3", HealthItem($"cam-{Guid.NewGuid()}"))
            : "{\"pageCount\":3}");

        var failure = await Should.ThrowAsync<HttpRequestException>(
            () => GatewayOver(handler).GetAllPathsHealthAsync(CancellationToken.None));

        failure.HttpRequestError.ShouldBe(HttpRequestError.InvalidResponse);
        failure.Message.ShouldContain("page 1");
        failure.Message.ShouldContain("no readable items array");
        handler.RequestedPages.ShouldBe([0, 1]);
        handler.RequestedPaths.ShouldAllBe(path => path == HealthEndpoint);
    }

    [Fact]
    public async Task Reaching_the_page_cap_reports_the_cap_in_the_error()
    {
        var handler = new PagesHandler(_ => Body("2147483647", HealthItem($"cam-{Guid.NewGuid()}")));

        var failure = await Should.ThrowAsync<HttpRequestException>(
            () => GatewayOver(handler).GetAllPathsHealthAsync(CancellationToken.None));

        failure.HttpRequestError.ShouldBe(HttpRequestError.InvalidResponse);
        failure.Message.ShouldContain("still reported more pages after 20 pages");
    }

    [Fact]
    public async Task A_complete_read_returns_the_paths_of_every_page()
    {
        var first = $"cam-{Guid.NewGuid()}";
        var second = $"cam-{Guid.NewGuid()}";
        var handler = new PagesHandler(page => page == 0
            ? Body("2", HealthItem(first))
            : Body("2", HealthItem(second)));

        var health = await GatewayOver(handler).GetAllPathsHealthAsync(CancellationToken.None);

        handler.RequestedPages.ShouldBe([0, 1]);
        health.Count.ShouldBe(2);
        health.Keys.Select(path => path.Value).ShouldBe([first, second], ignoreOrder: true);
    }

    private MediaMtxRtspGateway GatewayOver(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://mediamtx.test") }, logger);

    private static string HealthItem(string name) => $"{{\"name\":\"{name}\",\"ready\":true}}";

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
