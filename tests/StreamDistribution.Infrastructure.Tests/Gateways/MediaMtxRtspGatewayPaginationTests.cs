using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Gateways;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Gateways;

/// <summary>
/// #2862 blocker 1 / ADR-0171 security review. <c>GET /v3/paths/list</c> is
/// paginated by MediaMTX itself (<c>internal/api/paginate.go</c> defaults
/// <c>itemsPerPage</c> to 100, sorted by <c>Name</c> ascending) and has no
/// "give me everything" option. <c>GetAllPathsHealthAsync</c> reading only
/// the first page silently dropped every path past the 100th — path names
/// are <c>cam-&lt;Guid v7&gt;</c>, which sort by creation time, so this was
/// every camera past the 100th at any deployment past that size (the
/// 250-camera target).
///
/// <para>
/// A real <see cref="HttpMessageHandler"/> stub, not a hand-rolled
/// <see cref="IRtspGateway"/> double (ADR-0052): the gap this covers is in
/// <see cref="MediaMtxRtspGateway"/>'s own HTTP traffic, which a method-level
/// stub (as in <c>StreamHealthWatcherScopeTests</c>) cannot exercise at all.
/// </para>
/// </summary>
public class MediaMtxRtspGatewayPaginationTests
{
    [Fact]
    public async Task Every_page_mediamtx_reports_is_read_not_only_the_first()
    {
        string[] firstPageNames = [.. Enumerable.Range(0, 100).Select(_ => $"cam-{Guid.NewGuid()}")];
        string[] secondPageNames = [.. Enumerable.Range(0, 37).Select(_ => $"cam-{Guid.NewGuid()}")];

        PagedPathsHandler handler = new(new Dictionary<int, string>
        {
            [0] = PageBody(firstPageNames, pageCount: 2),
            [1] = PageBody(secondPageNames, pageCount: 2),
        });

        using HttpClient client = new(handler) { BaseAddress = new Uri("http://mediamtx.test") };
        MediaMtxRtspGateway gateway = new(client, NullLogger<MediaMtxRtspGateway>.Instance);

        var health =
            await gateway.GetAllPathsHealthAsync(CancellationToken.None);

        health.Count.ShouldBe(
            137,
            "both pages' paths should be in the result — a gateway that reads only the first page "
            + "would stop at 100, silently dropping every camera past it");

        foreach (var name in firstPageNames.Concat(secondPageNames))
        {
            health.ShouldContainKey(MediaMtxPath.From(name));
        }

        handler.RequestedPages.ShouldBe(
            [0, 1],
            "exactly one request per page, in order, and no more once pageCount is exhausted");
    }

    [Fact]
    public async Task A_single_page_response_issues_exactly_one_request()
    {
        string[] names = [.. Enumerable.Range(0, 12).Select(_ => $"cam-{Guid.NewGuid()}")];

        PagedPathsHandler handler = new(new Dictionary<int, string>
        {
            [0] = PageBody(names, pageCount: 1),
        });

        using HttpClient client = new(handler) { BaseAddress = new Uri("http://mediamtx.test") };
        MediaMtxRtspGateway gateway = new(client, NullLogger<MediaMtxRtspGateway>.Instance);

        var health =
            await gateway.GetAllPathsHealthAsync(CancellationToken.None);

        health.Count.ShouldBe(12);
        handler.RequestedPages.ShouldBe([0], "a single-page report must not cause a second request");
    }

    [Fact]
    public async Task Every_configured_page_mediamtx_reports_is_read_not_only_the_first()
    {
        string[] firstPageNames = [.. Enumerable.Range(0, 100).Select(_ => $"cam-{Guid.NewGuid()}")];
        string[] secondPageNames = [.. Enumerable.Range(0, 37).Select(_ => $"cam-{Guid.NewGuid()}")];

        PagedPathsHandler handler = new(new Dictionary<int, string>
        {
            [0] = PageBody(firstPageNames, pageCount: 2),
            [1] = PageBody(secondPageNames, pageCount: 2),
        });

        using HttpClient client = new(handler) { BaseAddress = new Uri("http://mediamtx.test") };
        MediaMtxRtspGateway gateway = new(client, NullLogger<MediaMtxRtspGateway>.Instance);

        var paths = await gateway.ListConfiguredPathsAsync(CancellationToken.None);

        paths.Count.ShouldBe(
            137,
            "both pages' paths should be in the result — a gateway that reads only the first page "
            + "would stop at 100, silently dropping every camera past it");

        foreach (var name in firstPageNames.Concat(secondPageNames))
        {
            paths.ShouldContain(MediaMtxPath.From(name));
        }

        handler.RequestedPages.ShouldBe(
            [0, 1],
            "exactly one request per page, in order, and no more once pageCount is exhausted");

        handler.RequestedPaths.ShouldAllBe(
            path => path == "/v3/config/paths/list",
            "the config list endpoint, not the runtime /v3/paths/list the health sweep hits");
    }

    [Fact]
    public async Task A_single_configured_page_response_issues_exactly_one_request()
    {
        string[] names = [.. Enumerable.Range(0, 12).Select(_ => $"cam-{Guid.NewGuid()}")];

        PagedPathsHandler handler = new(new Dictionary<int, string>
        {
            [0] = PageBody(names, pageCount: 1),
        });

        using HttpClient client = new(handler) { BaseAddress = new Uri("http://mediamtx.test") };
        MediaMtxRtspGateway gateway = new(client, NullLogger<MediaMtxRtspGateway>.Instance);

        var paths = await gateway.ListConfiguredPathsAsync(CancellationToken.None);

        paths.Count.ShouldBe(12);
        handler.RequestedPages.ShouldBe([0], "a single-page report must not cause a second request");
    }

    [Fact]
    public async Task A_non_canonical_name_on_a_later_configured_page_is_still_filtered_out()
    {
        string[] firstPageNames = [.. Enumerable.Range(0, 100).Select(_ => $"cam-{Guid.NewGuid()}")];
        string[] secondPageNames = ["manually-created-path", $"cam-{Guid.NewGuid()}"];

        PagedPathsHandler handler = new(new Dictionary<int, string>
        {
            [0] = PageBody(firstPageNames, pageCount: 2),
            [1] = PageBody(secondPageNames, pageCount: 2),
        });

        using HttpClient client = new(handler) { BaseAddress = new Uri("http://mediamtx.test") };
        MediaMtxRtspGateway gateway = new(client, NullLogger<MediaMtxRtspGateway>.Instance);

        var paths = await gateway.ListConfiguredPathsAsync(CancellationToken.None);

        paths.Count.ShouldBe(101, "the manually-created, non-canonical name on page 2 must be filtered out");
        paths.ShouldAllBe(path => path.Value != "manually-created-path");
    }

    private static string PageBody(IReadOnlyList<string> names, int pageCount)
    {
        var items = string.Join(
            ",", names.Select(name => $"{{\"name\":\"{name}\",\"ready\":true}}"));

        return $"{{\"pageCount\":{pageCount},\"items\":[{items}]}}";
    }

    /// <summary>
    /// Hand-written primary handler (ADR-0052): answers each page from the
    /// map it was built with and records the <c>page</c> query parameter
    /// and the request path it actually received, in request order. A
    /// missing <c>page</c> parameter reads as page 0 — MediaMTX's own
    /// default — so a gateway that forgets to send it still gets served
    /// page 0's body rather than failing the stub itself (a real red
    /// should come from the resulting assertion, not from this handler).
    /// </summary>
    private sealed class PagedPathsHandler(Dictionary<int, string> pages) : HttpMessageHandler
    {
        private readonly List<int> requestedPages = [];
        private readonly List<string> requestedPaths = [];

        public IReadOnlyList<int> RequestedPages => requestedPages;

        public IReadOnlyList<string> RequestedPaths => requestedPaths;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("request had no URI");
            var page = ParsePage(uri);
            requestedPages.Add(page);
            requestedPaths.Add(uri.AbsolutePath);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(pages[page], Encoding.UTF8, "application/json"),
            });
        }

        private static int ParsePage(Uri uri)
        {
            var query = uri.Query.TrimStart('?');
            foreach (var part in query.Split('&'))
            {
                if (part.StartsWith("page=", StringComparison.Ordinal))
                {
                    return int.Parse(part["page=".Length..], CultureInfo.InvariantCulture);
                }
            }

            return 0;
        }
    }
}
