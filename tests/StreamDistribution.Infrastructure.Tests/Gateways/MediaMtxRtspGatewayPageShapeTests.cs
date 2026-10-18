using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
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
    public async Task A_body_that_is_not_json_fails_the_read_as_an_invalid_response_without_an_inner_exception(
        string reader,
        string body)
    {
        var handler = new PagesHandler(_ => body);

        var failure = await Should.ThrowAsync<HttpRequestException>(() => ReadAsync(reader, handler));

        failure.HttpRequestError.ShouldBe(HttpRequestError.InvalidResponse);
        failure.InnerException.ShouldBeNull();
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
    [InlineData("health")]
    [InlineData("configured")]
    [InlineData("sources")]
    public async Task A_parse_error_that_echoes_the_literal_carries_no_part_of_the_body(string reader)
    {
        var handler = new PagesHandler(_ => "frtsp://admin:SECRET@10.0.8.1/x}]}");

        var failure = await Should.ThrowAsync<HttpRequestException>(() => ReadAsync(reader, handler));

        failure.HttpRequestError.ShouldBe(HttpRequestError.InvalidResponse);
        failure.InnerException.ShouldBeNull();
        for (Exception? link = failure; link is not null; link = link.InnerException)
        {
            link.Message.ShouldNotContain("SECRET");
        }
    }

    public static TheoryData<string, string, string> UndecodableStrings()
    {
        const string HealthSource = "{\"type\":\"rtspSource\",\"id\":\"rtsp://10.0.8.1/x\"}";
        TheoryData<string, string, string> cases = [];
        foreach (var mode in new[] { "invalid-utf8", "lone-surrogate" })
        {
            cases.Add("health", $"{{\"name\":\"cam-@@\",\"ready\":true,\"source\":{HealthSource}}}", mode);
            cases.Add("configured", "{\"name\":\"cam-@@\",\"source\":\"rtsp://10.0.8.1/x\"}", mode);
            cases.Add("sources", "{\"name\":\"cam-@@\",\"source\":\"rtsp://10.0.8.1/x\"}", mode);
            cases.Add("sources", "{\"name\":\"cam-01a02481-3b04-757e-97f3-c3e7d22a6101\",\"source\":\"rtsp://10.0.8.1/@@\"}", mode);
            cases.Add(
                "health",
                $"{{\"name\":\"cam-01a02481-3b04-757e-97f3-c3e7d22a6101\",\"ready\":true,\"readyTime\":\"2026-01-01T00:00:@@Z\",\"source\":{HealthSource}}}",
                mode);
        }

        return cases;
    }

    [Theory]
    [MemberData(nameof(UndecodableStrings))]
    public async Task A_string_that_cannot_be_decoded_fails_the_read_as_an_invalid_response(
        string reader,
        string itemTemplate,
        string mode)
    {
        var bad = mode == "invalid-utf8" ? Convert.FromHexString("FF") : Encoding.UTF8.GetBytes("\\uD800");
        var parts = itemTemplate.Split("@@");
        List<byte> body = [.. Encoding.UTF8.GetBytes("{\"pageCount\":1,\"items\":[")];
        body.AddRange(Encoding.UTF8.GetBytes(parts[0]));
        body.AddRange(bad);
        body.AddRange(Encoding.UTF8.GetBytes(parts[1]));
        body.AddRange(Encoding.UTF8.GetBytes("]}"));
        var handler = new ResponseHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = RawJson([.. body]) }));

        var failure = await Should.ThrowAsync<HttpRequestException>(() => ReadAsync(reader, handler));

        failure.HttpRequestError.ShouldBe(HttpRequestError.InvalidResponse);
        failure.InnerException.ShouldBeNull();
    }

    [Theory]
    [InlineData("health", "x-SECRET-cs", "SECRET")]
    [InlineData("configured", "x-SECRET-cs", "SECRET")]
    [InlineData("sources", "x-SECRET-cs", "SECRET")]
    [InlineData("health", "utf-7", "utf-7")]
    [InlineData("configured", "utf-7", "utf-7")]
    [InlineData("sources", "utf-7", "utf-7")]
    public async Task An_unsupported_response_charset_fails_the_read_without_naming_it(
        string reader,
        string charset,
        string leaked)
    {
        var handler = new ResponseHandler(_ =>
        {
            var content = new ByteArrayContent(Encoding.UTF8.GetBytes("{\"pageCount\":1,\"items\":[]}"));
            content.Headers.ContentType = MediaTypeHeaderValue.Parse($"application/json; charset={charset}");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });

        var failure = await Should.ThrowAsync<HttpRequestException>(() => ReadAsync(reader, handler));

        failure.HttpRequestError.ShouldBe(HttpRequestError.InvalidResponse);
        failure.InnerException.ShouldBeNull();
        for (Exception? link = failure; link is not null; link = link.InnerException)
        {
            link.Message.ShouldNotContain(leaked, Case.Insensitive);
        }
    }

    [Theory]
    [InlineData("\"ready\":\"yes\"")]
    [InlineData("\"ready\":true,\"source\":null")]
    [InlineData("\"ready\":true,\"source\":5")]
    [InlineData("\"ready\":true,\"source\":{\"type\":5}")]
    public async Task A_wrong_kind_health_field_fails_the_health_read_as_an_invalid_response(string fields)
    {
        var handler = new PagesHandler(_ =>
            $"{{\"pageCount\":1,\"items\":[{{\"name\":\"cam-{Guid.NewGuid()}\",{fields}}}]}}");

        var failure = await Should.ThrowAsync<HttpRequestException>(() => ReadAsync("health", handler));

        failure.HttpRequestError.ShouldBe(HttpRequestError.InvalidResponse);
        failure.InnerException.ShouldBeNull();
    }

    [Theory]
    [InlineData("health")]
    [InlineData("configured")]
    [InlineData("sources")]
    public async Task Cancelling_while_the_body_is_read_is_still_a_cancellation(string reader)
    {
        using CancellationTokenSource cancellation = new();
        BlockingContent content = new();
        var handler = new ResponseHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));

        var read = ReadAsync(reader, handler, cancellation.Token);
        await content.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await cancellation.CancelAsync();
        var failure = await Record.ExceptionAsync(() => read);

        failure.ShouldBeAssignableTo<OperationCanceledException>();
        failure.ShouldNotBeOfType<HttpRequestException>();
    }

    [Theory]
    [InlineData("health")]
    [InlineData("configured")]
    [InlineData("sources")]
    public async Task A_failure_of_the_request_itself_is_not_remapped(string reader)
    {
        var sendFailure = new InvalidOperationException("send failed");
        var throwing = new ResponseHandler(_ => throw sendFailure);
        var unavailable = new ResponseHandler(_ => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() => ReadAsync(reader, throwing));
        var refused = await Should.ThrowAsync<HttpRequestException>(() => ReadAsync(reader, unavailable));

        thrown.ShouldBeSameAs(sendFailure);
        refused.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
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

    private async Task<int> ReadAsync(
        string reader,
        HttpMessageHandler handler,
        CancellationToken cancellationToken = default)
    {
        MediaMtxRtspGateway gateway = new(
            new HttpClient(handler) { BaseAddress = new Uri("http://mediamtx.test") },
            logger);

        return reader switch
        {
            "health" => (await gateway.GetAllPathsHealthAsync(cancellationToken)).Count,
            "configured" => (await gateway.ListConfiguredPathsAsync(cancellationToken)).Count,
            _ => (await gateway.ListConfiguredPathSourcesAsync(cancellationToken)).Count,
        };
    }

    private static ByteArrayContent RawJson(byte[] body)
    {
        ByteArrayContent content = new(body);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

        return content;
    }

    private sealed class ResponseHandler(Func<CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => respond(cancellationToken);
    }

    private sealed class BlockingContent : HttpContent
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            SerializeToStreamAsync(stream, context, CancellationToken.None);

        protected override async Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;

            return false;
        }
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
