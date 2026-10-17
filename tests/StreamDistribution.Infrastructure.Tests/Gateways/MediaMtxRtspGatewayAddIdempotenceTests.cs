using System.Net;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Gateways;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Gateways;

/// <summary>
/// #3010 / spec 391 guard (spec 309 FR-002). A partial reconcile list leaves
/// live paths unlisted, and the reconcile re-adds them; that is safe only
/// because an <c>add/</c> that MediaMTX refuses for an existing path is
/// followed by a <c>get/</c> that settles it as success. This branch had no
/// unit cover at the gateway.
/// </summary>
public class MediaMtxRtspGatewayAddIdempotenceTests
{
    private readonly CapturingLogger<MediaMtxRtspGateway> logger = new();

    [Fact]
    public async Task A_redundant_add_for_a_path_mediamtx_already_has_succeeds()
    {
        var path = MediaMtxPath.For(CameraIdentifier.From(Guid.NewGuid()));
        var handler = new RecordingHandler(getAnswer: HttpStatusCode.OK);

        await GatewayOver(handler).AddPathAsync(path, "rtsp://10.0.8.1/h264", CancellationToken.None);

        handler.Requests.ShouldBe(
        [
            $"POST /v3/config/paths/add/{path.Value}",
            $"GET /v3/config/paths/get/{path.Value}",
        ]);
        var information = logger.Entries.Where(entry => entry.Level == LogLevel.Information).ToList();
        information.Count.ShouldBe(1);
        information[0].Message.ShouldBe(
            $"MediaMTX path {path.Value} was already registered; treating add as success (spec 309).");
    }

    private MediaMtxRtspGateway GatewayOver(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://mediamtx.test") }, logger);

    private sealed class RecordingHandler(HttpStatusCode getAnswer) : HttpMessageHandler
    {
        private readonly List<string> requests = [];

        public List<string> Requests => requests;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException("request had no URI");
            requests.Add($"{request.Method} {uri.AbsolutePath}");

            var status = request.Method == HttpMethod.Post ? HttpStatusCode.BadRequest : getAnswer;

            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}
