using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.StreamDistribution.Infrastructure.Gateways;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Gateways;

/// <summary>
/// #3021 / spec 405. Characterises what the configured-sources read does today
/// that the existing bounds tests do not pin, so that folding it onto
/// <c>ReadPagesAsync</c> can be shown to change nothing: the page named in the
/// unreadable-<c>pageCount</c> warning, the source-repair wording of both
/// warnings (and the event they are logged under, so a swap to the
/// configured-paths messages fails), the exact request strings, a mid-read
/// failure, the order of the end-of-read checks, and a body that is not an
/// object. The last one pins today's behaviour and does not endorse it
/// (spec 405 A1).
/// </summary>
public class MediaMtxRtspGatewayConfiguredSourcesFoldCharacterisationTests
{
    private const string UnreadablePageCountEvent = "MediaMtxConfiguredSourcesPageCountUnreadable";
    private const string PageCapReachedEvent = "MediaMtxConfiguredSourcesPageCapReached";

    private readonly EventCapturingLogger logger = new();

    [Fact]
    public async Task An_unreadable_page_count_on_page_two_warns_with_the_source_repair_text_for_that_page()
    {
        var handler = new PagesHandler(page => page < 2
            ? Body("5", Item($"cam-{Guid.NewGuid()}", "rtsp://10.0.8.1/h264"))
            : Body("1.5", Item($"cam-{Guid.NewGuid()}", "rtsp://10.0.8.1/h264")));

        var sources = await GatewayOver(handler).ListConfiguredPathSourcesAsync(CancellationToken.None);

        handler.RequestedPages.ShouldBe([0, 1, 2]);
        sources.Count.ShouldBe(3);
        var warning = logger.Entries.ShouldHaveSingleItem();
        warning.Level.ShouldBe(LogLevel.Warning);
        warning.EventName.ShouldBe(UnreadablePageCountEvent);
        warning.Message.ShouldContain("on page 2");
        warning.Message.ShouldContain("the source repair compares only the paths read so far");
        warning.Message.ShouldNotContain("startup reconcile");
    }

    [Fact]
    public async Task The_page_cap_warning_says_after_one_thousand_pages_with_the_source_repair_text()
    {
        var handler = new PagesHandler(_ => Body("2147483647", Item($"cam-{Guid.NewGuid()}", "rtsp://10.0.8.1/h264")));

        await GatewayOver(handler).ListConfiguredPathSourcesAsync(CancellationToken.None);

        var warning = logger.Entries.ShouldHaveSingleItem();
        warning.Level.ShouldBe(LogLevel.Warning);
        warning.EventName.ShouldBe(PageCapReachedEvent);
        warning.Message.ShouldContain("after 1000 pages");
        warning.Message.ShouldContain("the source repair");
        warning.Message.ShouldNotContain("startup reconcile");
    }

    [Fact]
    public async Task The_read_requests_each_page_with_one_hundred_items_per_page()
    {
        var handler = new PagesHandler(_ => Body("3", Item($"cam-{Guid.NewGuid()}", "rtsp://10.0.8.1/h264")));

        await GatewayOver(handler).ListConfiguredPathSourcesAsync(CancellationToken.None);

        handler.RequestedUris.ShouldBe(
        [
            "/v3/config/paths/list?page=0&itemsPerPage=100",
            "/v3/config/paths/list?page=1&itemsPerPage=100",
            "/v3/config/paths/list?page=2&itemsPerPage=100",
        ]);
    }

    [Fact]
    public async Task A_503_after_the_first_page_throws_the_status_and_logs_nothing()
    {
        var handler = new PagesHandler(
            _ => Body("3", Item($"cam-{Guid.NewGuid()}", "rtsp://10.0.8.1/h264")),
            failingPage: 1);

        var exception = await Should.ThrowAsync<HttpRequestException>(
            () => GatewayOver(handler).ListConfiguredPathSourcesAsync(CancellationToken.None));

        exception.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        handler.RequestedPages.ShouldBe([0, 1]);
        logger.Entries.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_empty_page_ends_the_read_before_the_page_count_is_looked_at()
    {
        var handler = new PagesHandler(_ => Body("1.5"));

        var sources = await GatewayOver(handler).ListConfiguredPathSourcesAsync(CancellationToken.None);

        handler.RequestedPages.ShouldBe([0]);
        sources.ShouldBeEmpty();
        logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }

    [Fact]
    public async Task A_body_that_is_not_an_object_throws_an_invalid_operation_exception()
    {
        var handler = new PagesHandler(_ => "[]");

        await Should.ThrowAsync<InvalidOperationException>(
            () => GatewayOver(handler).ListConfiguredPathSourcesAsync(CancellationToken.None));

        handler.RequestedPages.ShouldBe([0]);
    }

    private MediaMtxRtspGateway GatewayOver(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://mediamtx.test") }, logger);

    private static string Item(string name, string source) => $"{{\"name\":\"{name}\",\"source\":\"{source}\"}}";

    private static string Body(string rawPageCount, params string[] items) =>
        $"{{\"pageCount\":{rawPageCount},\"items\":[{string.Join(",", items)}]}}";

    private sealed class EventCapturingLogger : ILogger<MediaMtxRtspGateway>
    {
        private readonly List<(LogLevel Level, string? EventName, string Message)> entries = [];

        public IReadOnlyList<(LogLevel Level, string? EventName, string Message)> Entries => entries;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            entries.Add((logLevel, eventId.Name, formatter(state, exception)));
    }

    private sealed class PagesHandler(Func<int, string> bodyForPage, int failingPage = -1) : HttpMessageHandler
    {
        private const int RequestCeiling = 1100;

        private readonly List<int> requestedPages = [];
        private readonly List<string> requestedUris = [];

        public List<int> RequestedPages => requestedPages;

        public List<string> RequestedUris => requestedUris;

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
            requestedUris.Add(uri.PathAndQuery);

            if (page == failingPage)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(bodyForPage(page), Encoding.UTF8, "application/json"),
            });
        }
    }
}
