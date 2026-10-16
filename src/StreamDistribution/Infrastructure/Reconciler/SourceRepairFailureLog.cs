using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using SmartSentinelEye.StreamDistribution.Domain.Stream;

namespace SmartSentinelEye.StreamDistribution.Infrastructure.Reconciler;

/// <summary>
/// Spec 388 (#2994): decides how loudly a path's repair failure is logged. A
/// first or changed outcome Warns with the exception; the same outcome repeats
/// at most every ten minutes, measured on the monotonic timestamp. Owned by one
/// <see cref="MediaMtxSourceRepair"/>, which never runs two passes at once, so
/// it is not thread-safe.
/// </summary>
internal sealed class SourceRepairFailureLog(ILogger logger, TimeProvider timeProvider)
{
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMinutes(10);

    private readonly Dictionary<MediaMtxPath, FailureState> failures = [];
    private readonly HashSet<MediaMtxPath> failedThisPass = [];

    public void Failed(MediaMtxPath path, Exception failure)
    {
        if (failure is HttpRequestException { StatusCode: HttpStatusCode.NotFound })
        {
            logger.ReconcilerRepairPathGone(path);
            failures.Remove(path);
            return;
        }

        failedThisPass.Add(path);

        var status = (failure as HttpRequestException)?.StatusCode;
        Outcome outcome = new(failure.GetType(), status);

        if (failures.TryGetValue(path, out var state)
            && state.Outcome == outcome
            && timeProvider.GetElapsedTime(state.LastWarnedAt) < RepeatInterval)
        {
            logger.ReconcilerStillFailingToRepairPathSource(path, outcome.ExceptionType.Name, StatusText(status));
            return;
        }

        logger.ReconcilerFailedToRepairPathSource(failure, path);
        failures[path] = new FailureState(outcome, timeProvider.GetTimestamp());
    }

    public void Succeeded(MediaMtxPath path) => failures.Remove(path);

    public void PassCompleted()
    {
        failures.Keys.Where(path => !failedThisPass.Contains(path)).ToList().ForEach(path => failures.Remove(path));
        failedThisPass.Clear();
    }

    private static string StatusText(HttpStatusCode? status) =>
        status.HasValue ? ((int)status.Value).ToString(CultureInfo.InvariantCulture) : "-";

    private readonly record struct Outcome(Type ExceptionType, HttpStatusCode? Status);

    private sealed record FailureState(Outcome Outcome, long LastWarnedAt);
}
