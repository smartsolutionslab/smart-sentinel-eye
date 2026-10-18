using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.StreamDistribution.Infrastructure.Gateways;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Gateways;

public class MediaMtxRtspGatewayWarningRepeatIntervalTests
{
    private readonly CapturingLogger<MediaMtxRtspGateway> logger = new();

    [Fact]
    public async Task The_unreadable_page_count_warning_repeats_after_ten_minutes()
    {
        ManualTimeProvider time = new(DateTimeOffset.Parse("2026-10-18T09:00:00Z", CultureInfo.InvariantCulture));
        MediaMtxRtspGateway gateway = new(
            new HttpClient(new FixedBodyHandler()) { BaseAddress = new Uri("http://mediamtx.test") },
            logger,
            new MediaMtxConfiguredSourcesReadWarnings(time));

        await gateway.ListConfiguredPathSourcesAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromMinutes(9) + TimeSpan.FromSeconds(59));
        await gateway.ListConfiguredPathSourcesAsync(CancellationToken.None);
        time.Advance(TimeSpan.FromSeconds(1));
        await gateway.ListConfiguredPathSourcesAsync(CancellationToken.None);

        logger.Entries.Select(entry => entry.Level).ShouldBe([LogLevel.Warning, LogLevel.Debug, LogLevel.Warning]);
    }

    private sealed class FixedBodyHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"pageCount\":1.5,\"items\":[{\"name\":\"cam-0197a1b2-0000-7000-8000-000000000b01\",\"source\":\"rtsp://10.0.8.1/h264\"}]}",
                    Encoding.UTF8,
                    "application/json"),
            });
    }
}
