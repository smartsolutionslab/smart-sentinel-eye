using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.StreamDistribution.Infrastructure.Gateways;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Gateways;

/// <summary>
/// #3010 / spec 391 characterisation. What the two older list readers do with
/// well-formed answers today, and must keep doing once their page read is
/// bounded. Green before and after; an assertion that has to change here means
/// the behaviour moved.
/// </summary>
public class MediaMtxRtspGatewayListReadersCharacterisationTests
{
    private readonly CapturingLogger<MediaMtxRtspGateway> logger = new();

    [Theory]
    [InlineData("health", "\"5\"")]
    [InlineData("health", "null")]
    [InlineData("configured", "\"5\"")]
    [InlineData("configured", "null")]
    public async Task A_page_count_that_is_not_a_number_still_reads_one_page_without_a_warning(
        string reader, string rawPageCount)
    {
        var handler = new PagesHandler(_ => Body(rawPageCount, Item($"cam-{Guid.NewGuid()}")));

        var read = await ReadAsync(reader, handler);

        handler.RequestedPages.ShouldBe([0]);
        read.ShouldBe(1);
        logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }

    [Theory]
    [InlineData("health")]
    [InlineData("configured")]
    public async Task A_page_of_only_non_canonical_paths_is_not_an_empty_page(string reader)
    {
        var canonical = $"cam-{Guid.NewGuid()}";
        var handler = new PagesHandler(page => page == 0
            ? Body("2", Item("manual-x"))
            : Body("2", Item(canonical)));

        var read = await ReadAsync(reader, handler);

        handler.RequestedPages.ShouldBe([0, 1]);
        read.ShouldBe(1);
    }

    [Theory]
    [InlineData("health")]
    [InlineData("configured")]
    public async Task A_zero_page_count_with_an_empty_first_page_reads_one_page(string reader)
    {
        var handler = new PagesHandler(_ => Body("0"));

        var read = await ReadAsync(reader, handler);

        handler.RequestedPages.ShouldBe([0]);
        read.ShouldBe(0);
        logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }

    private async Task<int> ReadAsync(string reader, HttpMessageHandler handler)
    {
        var gateway = new MediaMtxRtspGateway(
            new HttpClient(handler) { BaseAddress = new Uri("http://mediamtx.test") },
            logger);

        return reader == "health"
            ? (await gateway.GetAllPathsHealthAsync(CancellationToken.None)).Count
            : (await gateway.ListConfiguredPathsAsync(CancellationToken.None)).Count;
    }

    private static string Item(string name) => $"{{\"name\":\"{name}\",\"ready\":true}}";

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
