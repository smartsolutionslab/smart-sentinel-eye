using System.Net.Sockets;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
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
/// and the exception type are in the check's data and the log; the check's name encodes no dependency.
/// </para>
/// </summary>
public sealed class DeadLetterDepthHealthCheck : IHealthCheck
{
    public const string QueueName = "wolverine-dead-letter-queue";

    public const long ConcerningDepth = 1;

    /// <summary>
    /// A hung broker call must not hold the probe. The registration applies it, and a timed-out check
    /// maps to the registration's Degraded failure status, which is HTTP 200.
    /// </summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly Func<CancellationToken, Task<long?>> depthSource;
    private readonly ILogger<DeadLetterDepthHealthCheck> logger;

    /// <summary>
    /// Takes the runtime as a factory, so that building the check does not build the Wolverine
    /// runtime — which is only async-disposable — for a caller that merely enumerates the
    /// registrations.
    /// </summary>
    public DeadLetterDepthHealthCheck(
        Func<IWolverineRuntime?> runtimeSource,
        ILogger<DeadLetterDepthHealthCheck> logger)
        : this(_ => ReadDepthAsync(runtimeSource), logger)
    {
    }

    /// <summary>
    /// The depth as a seam: <see langword="null"/> means there is no broker transport to read.
    /// </summary>
    internal DeadLetterDepthHealthCheck(
        Func<CancellationToken, Task<long?>> depthSource,
        ILogger<DeadLetterDepthHealthCheck> logger)
    {
        this.depthSource = depthSource;
        this.logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        long? reading;
        try
        {
            reading = await depthSource(cancellationToken).WaitAsync(cancellationToken);
        }
        catch (Exception exception) when (IsBrokerUnreachable(exception))
        {
            // ADR-0154. The broker is shared by every replica, so an unreachable one is unreachable
            // from all of them; draining them all would turn a broker outage into the loss of the
            // HTTP surface an operator uses to find out why. The depth is unreadable, not bad.
            logger.DeadLetterDepthUnreadable(QueueName, exception.GetType().Name, exception);
            return Unreachable(exception.GetType().Name);
        }

        if (reading is not { } depth)
        {
            return NoRuntime();
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
    /// The broker being away rather than the check being wrong: a refused, dropped or closed
    /// connection. A channel-level refusal (the queue is missing, access is denied) is the broker
    /// answering, so it is not unreachable; neither is any other exception, which escapes to the
    /// registration's Degraded failure status.
    /// </summary>
    internal static bool IsBrokerUnreachable(Exception exception) => exception switch
    {
        SocketException or TimeoutException => true,
        OperationInterruptedException interrupted => !IsChannelLevel(interrupted.ShutdownReason?.ReplyCode),
        _ => false,
    };

    private static bool IsChannelLevel(int? replyCode) =>
        replyCode is Constants.ContentTooLarge
            or Constants.NoRoute
            or Constants.NoConsumers
            or Constants.AccessRefused
            or Constants.NotFound
            or Constants.ResourceLocked
            or Constants.PreconditionFailed;

    // QueuedCountAsync takes no token; CheckHealthAsync bounds the wait with WaitAsync.
    private static async Task<long?> ReadDepthAsync(Func<IWolverineRuntime?> runtimeSource)
    {
        // OfType, not GetOrCreate: a probe must not create a transport the host never configured.
        var transport = runtimeSource()?.Options.Transports.OfType<RabbitMqTransport>().FirstOrDefault();
        if (transport is null)
        {
            return null;
        }

        return await transport.Queues[QueueName].QueuedCountAsync();
    }

    private static HealthCheckResult Unreachable(string exceptionTypeName) =>
        HealthCheckResult.Healthy(
            "Dead-letter depth not readable; a broker outage does not fail this replica's readiness.",
            new Dictionary<string, object>(StringComparer.Ordinal) { ["error"] = exceptionTypeName });

    /// <summary>
    /// A host without Wolverine (the API gateway) has no broker to read; it has nothing to report, which
    /// is not the same as a dead letter, so it reads Healthy rather than a permanent false Degraded.
    /// </summary>
    private static HealthCheckResult NoRuntime() =>
        HealthCheckResult.Healthy(
            "No Wolverine runtime on this host; there is no dead-letter queue to read.",
            new Dictionary<string, object>(StringComparer.Ordinal) { ["dead-letters"] = "no Wolverine runtime on this host" });
}
