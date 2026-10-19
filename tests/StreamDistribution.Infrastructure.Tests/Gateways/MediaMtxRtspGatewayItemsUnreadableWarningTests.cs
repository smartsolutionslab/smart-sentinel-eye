using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.StreamDistribution.Infrastructure.Gateways;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Gateways;

/// <summary>
/// #3021 item 4 / spec 418 US3. A configured-paths page whose <c>items</c> is
/// absent or not an array ends both configured reads at that page, as it always
/// has, and now says so: one Warning naming the page and no text from the page.
/// The source repair's Warning goes through spec 409's existing throttle (ten
/// minutes, cleared by a complete read, re-armed by a changed outcome); the
/// startup reconcile's read runs once and is unthrottled like its siblings. An
/// empty <c>items</c> array is still the normal end and stays silent.
/// </summary>
public class MediaMtxRtspGatewayItemsUnreadableWarningTests
{
    private const string SourcesUnreadable = "MediaMtxConfiguredSourcesItemsUnreadable";
    private const string SourcesStillUnreadable = "MediaMtxConfiguredSourcesItemsStillUnreadable";
    private const string SourcesPageCapReached = "MediaMtxConfiguredSourcesPageCapReached";
    private const string PathsUnreadable = "MediaMtxConfiguredPathsItemsUnreadable";
    private const string PathName = "cam-0197a1b2-0000-7000-8000-000000000b01";

    private readonly EventNamedLogger<MediaMtxRtspGateway> logger = new();

    [Theory]
    [InlineData("{\"pageCount\":5}")]
    [InlineData("{\"pageCount\":5,\"items\":null}")]
    [InlineData("{\"pageCount\":5,\"items\":{\"name\":\"x\"}}")]
    [InlineData("{\"pageCount\":5,\"items\":\"[]\"}")]
    public async Task Items_that_are_absent_or_not_an_array_warn_once_and_keep_what_was_read(string secondPage)
    {
        PagesHandler handler = new(page => page == 0 ? Page(5, Item(PathName)) : secondPage);

        var sources = await Gateway(handler).ListConfiguredPathSourcesAsync(CancellationToken.None);

        handler.RequestedPages.ShouldBe([0, 1]);
        sources.Count.ShouldBe(1);
        var warning = logger.Entries.ShouldHaveSingleItem();
        warning.Level.ShouldBe(LogLevel.Warning);
        warning.EventName.ShouldBe(SourcesUnreadable);
        warning.Message.ShouldContain("page 1");
        warning.Message.ShouldNotContain("name");
        warning.Message.ShouldNotContain("\"");
        warning.Message.ShouldNotContain("{");
        warning.Message.ShouldNotContain("[");
    }

    [Fact]
    public async Task The_startup_read_warns_once_per_read_without_a_throttle()
    {
        PagesHandler handler = new(_ => "{\"pageCount\":5}");
        var gateway = Gateway(handler);

        var first = await gateway.ListConfiguredPathsAsync(CancellationToken.None);
        var second = await gateway.ListConfiguredPathsAsync(CancellationToken.None);

        first.ShouldBeEmpty();
        second.ShouldBeEmpty();
        var warnings = logger.Entries.Where(entry => entry.Level == LogLevel.Warning).ToList();
        warnings.Count.ShouldBe(2);
        warnings.ShouldAllBe(warning => warning.EventName == PathsUnreadable);
        warnings.ShouldAllBe(warning => warning.Message.Contains("page 0", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_source_repair_warning_repeats_at_most_every_ten_minutes()
    {
        ManualTimeProvider time = new(DateTimeOffset.Parse("2026-10-19T09:00:00Z", CultureInfo.InvariantCulture));
        var gateway = Gateway(new PagesHandler(_ => "{\"pageCount\":5}"), new MediaMtxConfiguredSourcesReadWarnings(time));

        await gateway.ListConfiguredPathSourcesAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(9) + TimeSpan.FromSeconds(59));
        await gateway.ListConfiguredPathSourcesAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(1));
        await gateway.ListConfiguredPathSourcesAsync(CancellationToken.None);

        logger.Entries.Select(entry => entry.Level).ShouldBe([LogLevel.Warning, LogLevel.Debug, LogLevel.Warning]);
        logger.Entries[1].EventName.ShouldBe(SourcesStillUnreadable);
    }

    [Fact]
    public async Task A_complete_read_rearms_the_warning()
    {
        ManualTimeProvider time = new(DateTimeOffset.Parse("2026-10-19T09:00:00Z", CultureInfo.InvariantCulture));
        var body = "{\"pageCount\":5}";
        var gateway = Gateway(new PagesHandler(_ => body), new MediaMtxConfiguredSourcesReadWarnings(time));

        await gateway.ListConfiguredPathSourcesAsync(CancellationToken.None);
        body = Page(1, Item(PathName));
        await gateway.ListConfiguredPathSourcesAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(30));
        body = "{\"pageCount\":5}";
        await gateway.ListConfiguredPathSourcesAsync(CancellationToken.None);

        var warnings = logger.Entries.Where(entry => entry.Level == LogLevel.Warning).ToList();
        warnings.Count.ShouldBe(2);
        warnings.ShouldAllBe(warning => warning.EventName == SourcesUnreadable);
    }

    [Fact]
    public async Task Changing_from_the_page_cap_to_unreadable_items_warns_at_once()
    {
        ManualTimeProvider time = new(DateTimeOffset.Parse("2026-10-19T09:00:00Z", CultureInfo.InvariantCulture));
        var body = Page(int.MaxValue, Item(PathName));
        var gateway = Gateway(new PagesHandler(_ => body), new MediaMtxConfiguredSourcesReadWarnings(time));

        await gateway.ListConfiguredPathSourcesAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(30));
        body = "{\"pageCount\":5}";
        await gateway.ListConfiguredPathSourcesAsync(CancellationToken.None);

        logger.Entries
            .Where(entry => entry.Level == LogLevel.Warning)
            .Select(entry => entry.EventName)
            .ShouldBe([SourcesPageCapReached, SourcesUnreadable]);
    }

    [Theory]
    [InlineData("configured")]
    [InlineData("sources")]
    public async Task An_empty_items_array_still_ends_both_configured_reads_silently(string reader)
    {
        PagesHandler handler = new(_ => "{\"pageCount\":5,\"items\":[]}");
        var gateway = Gateway(handler);

        var read = reader == "configured"
            ? (await gateway.ListConfiguredPathsAsync(CancellationToken.None)).Count
            : (await gateway.ListConfiguredPathSourcesAsync(CancellationToken.None)).Count;

        read.ShouldBe(0);
        handler.RequestedPages.ShouldBe([0]);
        logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }

    private MediaMtxRtspGateway Gateway(
        HttpMessageHandler handler,
        MediaMtxConfiguredSourcesReadWarnings? readWarnings = null) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://mediamtx.test") },
            logger,
            readWarnings ?? new MediaMtxConfiguredSourcesReadWarnings(TimeProvider.System));

    private static string Item(string name) => $"{{\"name\":\"{name}\",\"source\":\"rtsp://10.0.8.1/h264\"}}";

    private static string Page(int pageCount, params string[] items) =>
        $"{{\"pageCount\":{pageCount.ToString(CultureInfo.InvariantCulture)},\"items\":[{string.Join(",", items)}]}}";

    private sealed class PagesHandler(Func<int, string> bodyForPage) : HttpMessageHandler
    {
        private const int RequestCeiling = 1100;

        private readonly List<int> requestedPages = [];

        public List<int> RequestedPages => requestedPages;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
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
