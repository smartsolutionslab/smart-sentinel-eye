using System.Net;
using System.Text.Json;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Gateways;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Gateways;

/// <summary>
/// Spec 381 / issue #2993. A source whose userinfo the parser read as host and port reaches the log as the
/// placeholder; what is sent to MediaMTX stays complete.
/// </summary>
public class MediaMtxRtspGatewayUserInfoLogRedactionTests
{
    private const string Credential = "operator:2468";
    private const string CutShortSource = "rtsp://operator:2468#!@10.0.0.5:554/h264";

    private static readonly MediaMtxPath Path = MediaMtxPath.From("cam-0197a1b2-0000-7000-8000-000000000b02");

    [Fact]
    public async Task Registering_a_path_logs_the_placeholder_for_a_cut_short_userinfo()
    {
        (var gateway, var logger, var _) = NewGateway();

        await gateway.AddPathAsync(Path, CutShortSource, CancellationToken.None);

        logger.Entries.Select(entry => entry.Message).ShouldContain(
            $"Registered MediaMTX path {Path.Value} -> <unparseable url>.");
        logger.Entries.ShouldAllBe(entry => !entry.Message.Contains(Credential, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Re_pointing_a_path_logs_the_placeholder_for_a_cut_short_userinfo()
    {
        (var gateway, var logger, var _) = NewGateway();

        await gateway.RepointPathAsync(Path, CutShortSource, CancellationToken.None);

        logger.Entries.Select(entry => entry.Message).ShouldContain(
            $"Re-pointed MediaMTX path {Path.Value} at <unparseable url>.");
        logger.Entries.ShouldAllBe(entry => !entry.Message.Contains(Credential, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Registering_a_path_still_sends_the_full_cut_short_source_to_MediaMTX()
    {
        (var gateway, var _, var handler) = NewGateway();

        await gateway.AddPathAsync(Path, CutShortSource, CancellationToken.None);

        SourceOf(handler.Bodies.Single()).ShouldBe(CutShortSource);
    }

    [Fact]
    public async Task Re_pointing_a_path_still_sends_the_full_cut_short_source_to_MediaMTX()
    {
        (var gateway, var _, var handler) = NewGateway();

        await gateway.RepointPathAsync(Path, CutShortSource, CancellationToken.None);

        SourceOf(handler.Bodies.Single()).ShouldBe(CutShortSource);
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
