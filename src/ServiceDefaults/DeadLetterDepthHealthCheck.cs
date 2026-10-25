using System.Net.Sockets;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client.Exceptions;
using Wolverine.RabbitMQ.Internal;
using Wolverine.Runtime;

namespace SmartSentinelEye.ServiceDefaults;

/// <summary>
/// Reports whether anything sits on the shared dead-letter queue (ADR-0189 ruling 3).
///
/// <para>
/// <b>Threshold 1.</b> A dead letter ages out after <see cref="WolverineDefaults.DeadLetterRetention"/>
/// whether or not anyone looked, so the first one is the signal. A healthy dead-letter queue is empty.
/// </para>
///
/// <para>
/// <b>Degraded, never Unhealthy, and Healthy when the broker is unreachable (ADR-0154).</b> The queue is
/// shared by every replica, so failing readiness on it would drain them all. The depth, the queue name
/// and any error text are in the check's data and the log; the check's name encodes no dependency.
/// </para>
/// </summary>
public sealed class DeadLetterDepthHealthCheck(
    Func<IWolverineRuntime?> runtimeSource,
    ILogger<DeadLetterDepthHealthCheck> logger)
    : IHealthCheck
{
    /// <summary>
    /// Takes the runtime itself, resolved on the spot. The registration passes a factory instead, so
    /// that building the check does not build the Wolverine runtime — which is only async-disposable —
    /// for a caller that merely enumerates the registrations.
    /// </summary>
    public DeadLetterDepthHealthCheck(IWolverineRuntime runtime, ILogger<DeadLetterDepthHealthCheck> logger)
        : this(() => runtime, logger)
    {
    }

    public const string QueueName = "wolverine-dead-letter-queue";

    public const long ConcerningDepth = 1;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        long depth;
        try
        {
            var runtime = runtimeSource();
            if (runtime is null)
            {
                return NoRuntime();
            }

            var transport = runtime.Options.Transports.GetOrCreate<RabbitMqTransport>();
            depth = await transport.Queues[QueueName].QueuedCountAsync();
        }
        catch (Exception exception) when (IsBrokerUnreachable(exception))
        {
            // ADR-0154. The broker is shared by every replica, so an unreachable one is unreachable
            // from all of them; draining them all would turn a broker outage into the loss of the
            // HTTP surface an operator uses to find out why. The depth is unreadable, not bad.
            return HealthCheckResult.Healthy(
                "Dead-letter depth not readable; a broker outage does not fail this replica's readiness.",
                new Dictionary<string, object>(StringComparer.Ordinal) { ["error"] = exception.GetType().Name });
        }

        Dictionary<string, object> data = new(StringComparer.Ordinal)
        {
            ["queue"] = QueueName,
            ["depth"] = depth,
        };

        if (depth < ConcerningDepth)
        {
            return HealthCheckResult.Healthy("No dead letters are waiting.", data);
        }

        logger.DeadLetterQueueNotEmpty(QueueName, depth);
        return HealthCheckResult.Degraded($"{depth} dead letter(s) waiting.", data: data);
    }

    /// <summary>
    /// The broker being away rather than the check being wrong: a refused or dropped connection, or
    /// Wolverine's own report that the transport has no connection to open a channel on. Anything
    /// else escapes to the registration's Degraded failure status.
    /// </summary>
    private static bool IsBrokerUnreachable(Exception exception) =>
        exception is RabbitMQClientException or SocketException or IOException or TimeoutException
            or InvalidOperationException;

    /// <summary>
    /// A host without Wolverine (the API gateway) has no broker to read; it has nothing to report, which
    /// is not the same as a dead letter, so it reads Healthy rather than a permanent false Degraded.
    /// </summary>
    private static HealthCheckResult NoRuntime() =>
        HealthCheckResult.Healthy(
            "No Wolverine runtime on this host; there is no dead-letter queue to read.",
            new Dictionary<string, object>(StringComparer.Ordinal) { ["dead-letters"] = "no Wolverine runtime on this host" });
}
