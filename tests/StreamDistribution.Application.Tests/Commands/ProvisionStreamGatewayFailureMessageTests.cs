using System.Globalization;
using SmartSentinelEye.Shared.Kernel;
using SmartSentinelEye.StreamDistribution.Application.Commands;
using SmartSentinelEye.StreamDistribution.Application.Commands.Handlers;
using SmartSentinelEye.StreamDistribution.Application.Tests.Fakes;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Application.Tests.Commands;

/// <summary>
/// Spec 415 (#3013). The handlers copy the gateway exception's message into the
/// <c>RtspGatewayUnavailable</c> error and into a Warning, so what the gateway
/// puts in that message reaches the API response and the log. The gateway now
/// raises a fixed message naming the route and status; the fake throws that
/// shape, so this arrives green and guards the channel from the Application side.
/// </summary>
public class ProvisionStreamGatewayFailureMessageTests
{
    private const string Hostile = "rtsp://admin:SECRET@10.0.8.1/x";

    private static readonly DateTimeOffset FixedMoment =
        DateTimeOffset.Parse("2026-10-19T10:00:00Z", CultureInfo.InvariantCulture);

    [Fact]
    public async Task A_gateway_failure_with_the_fixed_message_reaches_neither_the_error_nor_the_log_with_a_reason_phrase()
    {
        FakeRtspGateway gateway = new()
        {
            OnAddPath = (_, _) => throw new HttpRequestException(
                HttpRequestError.Unknown,
                "MediaMTX /v3/config/paths/add answered 500.",
                null,
                System.Net.HttpStatusCode.InternalServerError),
        };
        CapturingLogger<ProvisionStreamCommandHandler> logger = new();
        ProvisionStreamCommandHandler handler = new(
            new InMemoryStreamRepository(),
            gateway,
            new FixedClock(FixedMoment),
            logger);
        ProvisionStreamCommand command = new(
            FabIdentifier.From("munich"),
            CameraIdentifier.From(Guid.CreateVersion7()),
            "rtsp://10.0.5.1/h264",
            OperatorIdentifier.From(Guid.CreateVersion7()));

        var result = await handler.HandleAsync(command, CancellationToken.None);

        var error = result.Error.ShouldBeOfType<ProvisionStreamError.RtspGatewayUnavailable>();
        error.Message.ShouldContain("/v3/config/paths/add answered 500");
        error.Message.ShouldNotContain("SECRET", Case.Sensitive);
        error.Message.ShouldNotContain(Hostile, Case.Sensitive);
        logger.Entries.ShouldNotBeEmpty();
        logger.Entries.ShouldAllBe(entry =>
            !entry.Message.Contains("SECRET", StringComparison.Ordinal)
            && (entry.Exception == null
                || !entry.Exception.ToString().Contains("SECRET", StringComparison.Ordinal)));
    }
}
