using System.Net;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.StreamDistribution.Domain.Stream;
using SmartSentinelEye.StreamDistribution.Infrastructure.Reconciler;
using SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Fakes;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Tests.Reconciler;

/// <summary>
/// #2994 / spec 388. A refused connection, a failed TLS handshake and a failed name
/// lookup are all <see cref="HttpRequestException"/> without a status code, but they
/// are different faults, so a change among them is a change of outcome and Warns at once.
/// </summary>
public class MediaMtxSourceRepairTransportErrorOutcomeTests
{
    private static readonly MediaMtxPath PathA = MediaMtxPath.From($"cam-{Guid.NewGuid()}");

    private readonly SourceRepairGateway gateway = new();
    private readonly SourceRepairSnapshot snapshot = new();
    private readonly CapturingLogger<MediaMtxReconciler> logger = new();
    private readonly MediaMtxSourceRepair repair;

    public MediaMtxSourceRepairTransportErrorOutcomeTests()
    {
        repair = new MediaMtxSourceRepair(logger);
        snapshot.Commit(PathA, "rtsp://10.0.8.1/h264");
        gateway.Configure(PathA, "rtsp://10.0.8.2/h264");
    }

    [Fact]
    public async Task A_change_from_a_refused_connection_to_a_failed_tls_handshake_warns_again_at_once()
    {
        gateway.FailPatchesFor(PathA, Transport(HttpRequestError.ConnectionError));
        await Pass();

        gateway.FailPatchesFor(PathA, Transport(HttpRequestError.SecureConnectionError));
        await Pass();

        WarningCount().ShouldBe(2);
    }

    [Fact]
    public async Task The_same_transport_error_repeated_warns_once_and_logs_debug_in_between()
    {
        gateway.FailPatchesFor(PathA, Transport(HttpRequestError.ConnectionError));

        await Pass();
        await Pass();
        await Pass();

        WarningCount().ShouldBe(1);
        logger.Entries.Count(entry => entry.Level == LogLevel.Debug
            && entry.Message.Contains(PathA.ToString(), StringComparison.Ordinal)).ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task A_status_code_failure_repeated_is_unaffected_by_the_transport_error_in_the_outcome()
    {
        gateway.FailPatchesFor(
            PathA,
            new HttpRequestException("Response status code does not indicate success: 400 (Bad Request).", null, HttpStatusCode.BadRequest));

        await Pass();
        await Pass();

        WarningCount().ShouldBe(1);
    }

    private static HttpRequestException Transport(HttpRequestError error) =>
        new(error, $"transport failure {error}", null);

    private int WarningCount() =>
        logger.Entries.Count(entry => entry.Level == LogLevel.Warning
            && entry.Message.Contains(PathA.ToString(), StringComparison.Ordinal));

    private async Task Pass() => await repair.RepairAsync(gateway, snapshot, CancellationToken.None);
}
