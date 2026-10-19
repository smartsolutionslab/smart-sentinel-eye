using System.Net;
using System.Text;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Gateways;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Gateways;

/// <summary>
/// #3013 / spec 415 (US2). A MediaMTX answer's reason phrase is attacker- or
/// misconfiguration-influenced text (a source URL with credentials can be echoed
/// into it), and <c>EnsureSuccessStatusCode</c> formats it into the exception
/// message. A refused call must keep its status code and error kind but carry no
/// reason phrase.
/// </summary>
public class MediaMtxRtspGatewayReasonPhraseTests
{
    private const string Hostile = "rtsp://admin:SECRET@10.0.8.1/x";
    private const string Source = "rtsp://10.0.8.1/h264";

    private readonly MediaMtxPath path = MediaMtxPath.For(CameraIdentifier.From(Guid.NewGuid()));

    [Fact]
    public async Task A_refused_call_keeps_the_error_kind_and_has_no_inner_exception()
    {
        var add = await Refused(
            StubMediaMtx.Answering(HttpMethod.Post, "/v3/config/paths/add", HttpStatusCode.InternalServerError),
            gateway => gateway.AddPathAsync(path, Source, CancellationToken.None));
        var repoint = await Refused(
            StubMediaMtx.Answering(HttpMethod.Patch, "/v3/config/paths/patch", HttpStatusCode.InternalServerError),
            gateway => gateway.RepointPathAsync(path, Source, CancellationToken.None));
        var health = await Refused(
            StubMediaMtx.Answering(HttpMethod.Get, "/v3/paths/list", HttpStatusCode.ServiceUnavailable),
            gateway => gateway.GetAllPathsHealthAsync(CancellationToken.None));

        foreach (var failure in new[] { add, repoint, health })
        {
            failure.GetType().ShouldBe(typeof(HttpRequestException));
            failure.HttpRequestError.ShouldBe(HttpRequestError.Unknown);
            failure.InnerException.ShouldBeNull();
        }
    }

    [Fact]
    public async Task A_404_on_remove_is_still_tolerated_with_a_hostile_reason_phrase()
    {
        var stub = StubMediaMtx.Answering(HttpMethod.Delete, "/v3/config/paths/delete", HttpStatusCode.NotFound);

        await GatewayOver(stub).RemovePathAsync(path, CancellationToken.None);

        stub.Requests.ShouldBe([$"DELETE /v3/config/paths/delete/{path.Value}"]);
    }

    [Fact]
    public async Task An_add_refused_with_400_for_a_path_that_exists_still_succeeds()
    {
        var stub = StubMediaMtx
            .Answering(HttpMethod.Post, "/v3/config/paths/add", HttpStatusCode.BadRequest)
            .Also(HttpMethod.Get, "/v3/config/paths/get", HttpStatusCode.OK);

        await GatewayOver(stub).AddPathAsync(path, Source, CancellationToken.None);

        stub.Requests.ShouldBe(
        [
            $"POST /v3/config/paths/add/{path.Value}",
            $"GET /v3/config/paths/get/{path.Value}",
        ]);
    }

    // Not a guard of this change: cancellation never reaches EnsureSuccess, so this cannot fail because of it.
    // It pins the passthrough only so a later rewrite of the call path does not swallow it.
    [Fact]
    public async Task A_cancelled_call_still_throws_operation_cancelled()
    {
        using CancellationTokenSource cancelled = new();
        await cancelled.CancelAsync();
        var stub = StubMediaMtx.Answering(HttpMethod.Post, "/v3/config/paths/add", HttpStatusCode.OK);

        var failure = await Should.ThrowAsync<OperationCanceledException>(
            () => GatewayOver(stub).AddPathAsync(path, Source, cancelled.Token));

        failure.ShouldNotBeOfType<HttpRequestException>();
    }

    [Fact]
    public async Task A_refused_add_carries_no_reason_phrase()
    {
        var stub = StubMediaMtx.Answering(HttpMethod.Post, "/v3/config/paths/add", HttpStatusCode.InternalServerError);

        var failure = await Refused(stub, gateway => gateway.AddPathAsync(path, Source, CancellationToken.None));

        ShouldCarryNoReasonPhrase(failure, HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task An_add_refused_with_400_for_a_path_that_does_not_exist_carries_no_reason_phrase()
    {
        var stub = StubMediaMtx
            .Answering(HttpMethod.Post, "/v3/config/paths/add", HttpStatusCode.BadRequest)
            .Also(HttpMethod.Get, "/v3/config/paths/get", HttpStatusCode.NotFound);

        var failure = await Refused(stub, gateway => gateway.AddPathAsync(path, Source, CancellationToken.None));

        ShouldCarryNoReasonPhrase(failure, HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task A_refused_repoint_carries_no_reason_phrase(HttpStatusCode status)
    {
        var stub = StubMediaMtx.Answering(HttpMethod.Patch, "/v3/config/paths/patch", status);

        var failure = await Refused(stub, gateway => gateway.RepointPathAsync(path, Source, CancellationToken.None));

        ShouldCarryNoReasonPhrase(failure, status);
    }

    [Fact]
    public async Task A_refused_remove_carries_no_reason_phrase()
    {
        var stub = StubMediaMtx.Answering(HttpMethod.Delete, "/v3/config/paths/delete", HttpStatusCode.InternalServerError);

        var failure = await Refused(stub, gateway => gateway.RemovePathAsync(path, CancellationToken.None));

        ShouldCarryNoReasonPhrase(failure, HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task A_refused_configured_paths_page_carries_no_reason_phrase()
    {
        var stub = StubMediaMtx.Answering(HttpMethod.Get, "/v3/config/paths/list", HttpStatusCode.InternalServerError);

        var failure = await Refused(stub, gateway => gateway.ListConfiguredPathsAsync(CancellationToken.None));

        ShouldCarryNoReasonPhrase(failure, HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task A_refused_second_configured_sources_page_carries_no_reason_phrase()
    {
        var page0 = $"{{\"pageCount\":2,\"items\":[{{\"name\":\"{path.Value}\",\"source\":\"{Source}\"}}]}}";
        var stub = StubMediaMtx
            .Answering(HttpMethod.Get, "/v3/config/paths/list?page=1", HttpStatusCode.BadGateway)
            .Also(HttpMethod.Get, "/v3/config/paths/list", HttpStatusCode.OK, page0);

        var failure = await Refused(stub, gateway => gateway.ListConfiguredPathSourcesAsync(CancellationToken.None));

        ShouldCarryNoReasonPhrase(failure, HttpStatusCode.BadGateway);
    }

    [Fact]
    public async Task A_refused_health_list_page_carries_no_reason_phrase()
    {
        var stub = StubMediaMtx.Answering(HttpMethod.Get, "/v3/paths/list", HttpStatusCode.ServiceUnavailable);

        var failure = await Refused(stub, gateway => gateway.GetAllPathsHealthAsync(CancellationToken.None));

        ShouldCarryNoReasonPhrase(failure, HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task A_refused_single_path_health_probe_carries_no_reason_phrase()
    {
        var stub = StubMediaMtx.Answering(HttpMethod.Get, "/v3/paths/get", HttpStatusCode.InternalServerError);

        var failure = await Refused(stub, gateway => gateway.GetPathHealthAsync(path, CancellationToken.None));

        ShouldCarryNoReasonPhrase(failure, HttpStatusCode.InternalServerError);
    }

    private static void ShouldCarryNoReasonPhrase(HttpRequestException failure, HttpStatusCode expected)
    {
        failure.StatusCode.ShouldBe(expected);
        failure.Message.ShouldNotContain("SECRET", Case.Sensitive);
        failure.ToString().ShouldNotContain("SECRET", Case.Sensitive);
    }

    private static async Task<HttpRequestException> Refused(StubMediaMtx stub, Func<MediaMtxRtspGateway, Task> call) =>
        await Should.ThrowAsync<HttpRequestException>(() => call(GatewayOver(stub)));

    private static MediaMtxRtspGateway GatewayOver(HttpMessageHandler handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri("http://mediamtx.test") },
            new CapturingLogger<MediaMtxRtspGateway>());

    /// <summary>
    /// Answers by method and path prefix, always carrying the hostile reason
    /// phrase. An in-memory phrase is exactly what <c>EnsureSuccessStatusCode</c>
    /// formats, so no socket is needed.
    /// </summary>
    private sealed class StubMediaMtx : HttpMessageHandler
    {
        private readonly List<(HttpMethod Method, string Prefix, HttpStatusCode Status, string? Body)> routes = [];
        private readonly List<string> requests = [];

        public IReadOnlyList<string> Requests => requests;

        public static StubMediaMtx Answering(HttpMethod method, string prefix, HttpStatusCode status, string? body = null) =>
            new StubMediaMtx().Also(method, prefix, status, body);

        public StubMediaMtx Also(HttpMethod method, string prefix, HttpStatusCode status, string? body = null)
        {
            routes.Add((method, prefix, status, body));

            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("request had no URI");
            requests.Add($"{request.Method} {uri.AbsolutePath}");
            var page = uri.Query.TrimStart('?').Split('&').Contains("page=1") ? "?page=1" : string.Empty;

            var route = routes.FirstOrDefault(candidate => candidate.Method == request.Method
                && (uri.AbsolutePath + page).StartsWith(candidate.Prefix, StringComparison.Ordinal));
            if (route.Prefix is null)
            {
                throw new InvalidOperationException($"unexpected request {request.Method} {uri.PathAndQuery}");
            }

            HttpResponseMessage response = new(route.Status) { ReasonPhrase = Hostile };
            if (route.Body is not null)
            {
                response.Content = new StringContent(route.Body, Encoding.UTF8, "application/json");
            }

            return Task.FromResult(response);
        }
    }
}
