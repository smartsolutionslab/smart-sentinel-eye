using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace SmartSentinelEye.StreamDistribution.Api;

[ExcludeFromCodeCoverage]
internal static partial class Log
{
    // Spec 208 (#2284), US2/FR-009. Fired once per partition on the
    // transition into the throttled state, never per refusal — ADR-0118
    // gives this service one OTLP sink, and a record per refused request
    // would flood it at exactly the moment an operator needs it readable.
    // Carries only the partition key (a remote address) and the configured
    // limit; never the bearer token or any part of the request body.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Partition {Partition} entered the throttled state for policy whep-authorize (limit {PermitLimit}).")]
    public static partial void WhepAuthorizeThrottled(this ILogger logger, string partition, int permitLimit);
}
