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

        IReadOnlyDictionary<MediaMtxPath, RtspPathHealth> health =
            await gateway.GetAllPathsHealthAsync(CancellationToken.None);

        health.Count.ShouldBe(
            137,
            "both pages' paths should be in the result — a gateway that reads only the first page "
            + "would stop at 100, silently dropping every camera past it");

        foreach (string name in firstPageNames.Concat(secondPageNames))
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

        IReadOnlyDictionary<MediaMtxPath, RtspPathHealth> health =
            await gateway.GetAllPathsHealthAsync(CancellationToken.None);

        health.Count.ShouldBe(12);
        handler.RequestedPages.ShouldBe([0], "a single-page report must not cause a second request");
    }

    private static string PageBody(IReadOnlyList<string> names, int pageCount)
    {
        string items = string.Join(
            ",", names.Select(name => $"{{\"name\":\"{name}\",\"ready\":true}}"));

        return $"{{\"pageCount\":{pageCount},\"items\":[{items}]}}";
    }

    /// <summary>
    /// Hand-written primary handler (ADR-0052): answers each page from the
    /// map it was built with and records the <c>page</c> query parameter it
    /// actually received, in request order.
    /// </summary>
    private sealed class PagedPathsHandler(Dictionary<int, string> pages) : HttpMessageHandler
    {
        private readonly List<int> requestedPages = [];

        public IReadOnlyList<int> RequestedPages => requestedPages;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            int page = ParsePage(request.RequestUri ?? throw new InvalidOperationException("request had no URI"));
            requestedPages.Add(page);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(pages[page], Encoding.UTF8, "application/json"),
            });
        }

        private static int ParsePage(Uri uri)
        {
            string query = uri.Query.TrimStart('?');
            foreach (string part in query.Split('&'))
            {
                if (part.StartsWith("page=", StringComparison.Ordinal))
                {
                    return int.Parse(part["page=".Length..], CultureInfo.InvariantCulture);
                }
            }

            throw new InvalidOperationException($"request URI '{uri}' carried no 'page' query parameter");
        }
    }
}
