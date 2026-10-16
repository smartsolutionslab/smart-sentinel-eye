using System.Net;
using System.Text.Json;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Gateways;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Gateways;

/// <summary>
/// Spec 379 / issue #2989. A source whose host carries a non-ASCII character reaches the log as the
/// placeholder; what is sent to MediaMTX stays complete. Marker checks are case-insensitive because
/// ToOrigin lower-cases the host.
/// </summary>
public class MediaMtxRtspGatewayHostCharsetLogRedactionTests
{
    private const string Marker = "SECRET";

    private static readonly MediaMtxPath Path = MediaMtxPath.From("cam-0197a1b2-0000-7000-8000-000000000b01");

    [Theory]
    [InlineData("rtsp://cam／token＝SECRET")]
    [InlineData("rtsp://SECRET＠cam/x")]
    public async Task Registering_a_path_logs_the_placeholder_for_a_non_ascii_host(string source)
    {
        (var gateway, var logger, var _) = NewGateway();

        await gateway.AddPathAsync(Path, source, CancellationToken.None);

        logger.Entries.Select(entry => entry.Message).ShouldContain(
            $"Registered MediaMTX path {Path.Value} -> <unparseable url>.");
        logger.Entries.ShouldAllBe(entry => !entry.Message.Contains(Marker, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("rtsp://cam／token＝SECRET")]
    [InlineData("rtsp://SECRET＠cam/x")]
    public async Task Re_pointing_a_path_logs_the_placeholder_for_a_non_ascii_host(string source)
    {
        (var gateway, var logger, var _) = NewGateway();

        await gateway.RepointPathAsync(Path, source, CancellationToken.None);

        logger.Entries.Select(entry => entry.Message).ShouldContain(
            $"Re-pointed MediaMTX path {Path.Value} at <unparseable url>.");
        logger.Entries.ShouldAllBe(entry => !entry.Message.Contains(Marker, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("rtsp://cam／token＝SECRET")]
    [InlineData("rtsp://SECRET＠cam/x")]
    public async Task Registering_a_path_still_sends_the_full_non_ascii_source_to_MediaMTX(string source)
    {
        (var gateway, var _, var handler) = NewGateway();

        await gateway.AddPathAsync(Path, source, CancellationToken.None);

        SourceOf(handler.Bodies.Single()).ShouldBe(source);
    }

    [Theory]
    [InlineData("rtsp://cam／token＝SECRET")]
    [InlineData("rtsp://SECRET＠cam/x")]
    public async Task Re_pointing_a_path_still_sends_the_full_non_ascii_source_to_MediaMTX(string source)
    {
        (var gateway, var _, var handler) = NewGateway();

        await gateway.RepointPathAsync(Path, source, CancellationToken.None);

        SourceOf(handler.Bodies.Single()).ShouldBe(source);
    }

    private static string SourceOf(string body) =>
        JsonDocument.Parse(body).RootElement.GetProperty("source").GetString()!;

    private static (MediaMtxRtspGateway Gateway, CapturingLogger<MediaMtxRtspGateway> Logger, RecordingHandler Handler)
        NewGateway()
    {
        RecordingHandler handler = new();
        HttpClient http = new(handler) { BaseAddress = new Uri("http://mediamtx.test") };
        CapturingLogger<MediaMtxRtspGateway> logger = new();

        return (new MediaMtxRtspGateway(http, logger), logger, handler);
    }

    /// <summary>Hand-written primary handler (ADR-0052): answers 200 and records each request body.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly List<string> bodies = [];

        public IReadOnlyList<string> Bodies => bodies;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));

            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
