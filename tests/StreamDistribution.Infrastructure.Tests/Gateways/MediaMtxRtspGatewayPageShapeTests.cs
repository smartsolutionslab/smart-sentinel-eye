using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.StreamDistribution.Infrastructure.Gateways;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Gateways;

/// <summary>
/// #3013 / spec 409 US1. A MediaMTX page that is not shaped like a list (a body
/// that is not a JSON object, an <c>items</c> element that is not an object, a
/// body that is not JSON at all) fails the read as an
/// <see cref="HttpRequestException"/> with <see cref="HttpRequestError.InvalidResponse"/>,
/// from all three readers, and never carries any part of the body. The two guards
/// pin what the readers already do with well-formed shapes.
/// </summary>
public class MediaMtxRtspGatewayPageShapeTests
{
    private const string Secret = "rtsp://admin:SECRET@10.0.8.1/x";

    private readonly CapturingLogger<MediaMtxRtspGateway> logger = new();

    [Theory]
    [InlineData("health", "null")]
    [InlineData("health", "[]")]
    [InlineData("health", "5")]
    [InlineData("health", "\"x\"")]
    [InlineData("configured", "null")]
    [InlineData("configured", "[]")]
    [InlineData("configured", "5")]
    [InlineData("configured", "\"x\"")]
    [InlineData("sources", "null")]
    [InlineData("sources", "[]")]
    [InlineData("sources", "5")]
    [InlineData("sources", "\"x\"")]
    public async Task A_page_body_that_is_not_a_json_object_fails_the_read_as_an_invalid_response(
        string reader,
        string body)
    {
        var handler = new PagesHandler(_ => body);

        var failure = await Should.ThrowAsync<HttpRequestException>(() => ReadAsync(reader, handler));

        failure.HttpRequestError.ShouldBe(HttpRequestError.InvalidResponse);
        handler.RequestedPages.ShouldBe([0]);
        logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }

    [Theory]
    [InlineData("health", "1")]
    [InlineData("health", "null")]
    [InlineData("health", "\"cam-x\"")]
    [InlineData("health", "[]")]
    [InlineData("configured", "1")]
    [InlineData("configured", "null")]
    [InlineData("configured", "\"cam-x\"")]
    [InlineData("configured", "[]")]
    [InlineData("sources", "1")]
    [InlineData("sources", "null")]
    [InlineData("sources", "\"cam-x\"")]
    [InlineData("sources", "[]")]
    public async Task An_items_element_that_is_not_a_json_object_fails_the_read_as_an_invalid_response(
        string reader,
        string element)
    {
        var handler = new PagesHandler(_ => $"{{\"pageCount\":1,\"items\":[{element}]}}");

        var failure = await Should.ThrowAsync<HttpRequestException>(() => ReadAsync(reader, handler));

        failure.HttpRequestError.ShouldBe(HttpRequestError.InvalidResponse);
        handler.RequestedPages.ShouldBe([0]);
        logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }

    [Theory]
    [InlineData("health")]
    [InlineData("configured")]
    [InlineData("sources")]
    public async Task A_malformed_second_page_fails_the_whole_read(string reader)
    {
        var handler = new PagesHandler(page => page == 0
            ? $"{{\"pageCount\":2,\"items\":[{Item($"cam-{Guid.NewGuid()}")}]}}"
            : "[]");

        var failure = await Should.ThrowAsync<HttpRequestException>(() => ReadAsync(reader, handler));

        failure.HttpRequestError.ShouldBe(HttpRequestError.InvalidResponse);
        handler.RequestedPages.ShouldBe([0, 1]);
    }

    [Theory]
    [InlineData("health", "<html>proxy</html>")]
    [InlineData("health", "")]
    [InlineData("configured", "<html>proxy</html>")]
    [InlineData("configured", "")]
    [InlineData("sources", "<html>proxy</html>")]
    [InlineData("sources", "")]
    public async Task A_body_that_is_not_json_fails_the_read_as_an_invalid_response_wrapping_the_parse_error(
        string reader,
        string body)
    {
        var handler = new PagesHandler(_ => body);

        var failure = await Should.ThrowAsync<HttpRequestException>(() => ReadAsync(reader, handler));

        failure.HttpRequestError.ShouldBe(HttpRequestError.InvalidResponse);
        failure.InnerException.ShouldBeOfType<JsonException>();
    }

    [Theory]
    [InlineData("health", "[\"" + Secret + "\"]")]
    [InlineData("health", "{\"pageCount\":1,\"items\":[\"" + Secret + "\"]}")]
    [InlineData("health", Secret)]
    [InlineData("configured", "[\"" + Secret + "\"]")]
    [InlineData("configured", "{\"pageCount\":1,\"items\":[\"" + Secret + "\"]}")]
    [InlineData("configured", Secret)]
    [InlineData("sources", "[\"" + Secret + "\"]")]
    [InlineData("sources", "{\"pageCount\":1,\"items\":[\"" + Secret + "\"]}")]
    [InlineData("sources", Secret)]
    public async Task A_shape_failure_carries_no_part_of_the_body(string reader, string body)
    {
        var handler = new PagesHandler(_ => body);

        var failure = await Should.ThrowAsync<HttpRequestException>(() => ReadAsync(reader, handler));

        failure.HttpRequestError.ShouldBe(HttpRequestError.InvalidResponse);
        for (Exception? link = failure; link is not null; link = link.InnerException)
        {
            link.Message.ShouldNotContain("SECRET");
        }
    }

    [Theory]
    [InlineData("configured")]
    [InlineData("sources")]
    public async Task An_object_page_without_items_still_ends_the_configured_reads_normally(string reader)
    {
        var handler = new PagesHandler(_ => "{\"pageCount\":5}");

        var read = await ReadAsync(reader, handler);

        read.ShouldBe(0);
        handler.RequestedPages.ShouldBe([0]);
        logger.Entries.ShouldNotContain(entry => entry.Level >= LogLevel.Warning);
    }

    [Theory]
    [InlineData("health")]
    [InlineData("configured")]
    [InlineData("sources")]
    public async Task An_object_element_without_a_string_name_is_still_skipped(string reader)
    {
        var handler = new PagesHandler(_ =>
            "{\"pageCount\":1,\"items\":[{\"ready\":true,\"source\":\"rtsp://10.0.8.1/x\"},{\"name\":5}]}");

        var read = await ReadAsync(reader, handler);

        read.ShouldBe(0);
        handler.RequestedPages.ShouldBe([0]);
    }

    private async Task<int> ReadAsync(string reader, HttpMessageHandler handler)
    {
        MediaMtxRtspGateway gateway = new(
            new HttpClient(handler) { BaseAddress = new Uri("http://mediamtx.test") },
            logger);

        return reader switch
        {
            "health" => (await gateway.GetAllPathsHealthAsync(CancellationToken.None)).Count,
            "configured" => (await gateway.ListConfiguredPathsAsync(CancellationToken.None)).Count,
            _ => (await gateway.ListConfiguredPathSourcesAsync(CancellationToken.None)).Count,
        };
    }

    private static string Item(string name) =>
        $"{{\"name\":\"{name}\",\"ready\":true,\"source\":{{\"type\":\"rtspSource\",\"id\":\"rtsp://10.0.8.1/x\"}}}}";

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
