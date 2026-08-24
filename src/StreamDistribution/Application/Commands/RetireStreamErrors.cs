using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.StreamDistribution.Application.Commands;

/// <summary>
/// Sealed-record failure hierarchy for <see cref="RetireStreamCommand"/>
/// (ADR-0047 + ADR-0089).
///
/// <para>
/// One case, and deliberately so. A missing stream is not here — it is a
/// success carrying <c>None</c>, for the reason given on the command. What
/// remains is the SFU being unreachable, which is transient: the aggregate is
/// already terminal by the time the gateway is called, so a retry finishes the
/// teardown rather than redoing the retirement.
/// </para>
/// </summary>
public abstract record RetireStreamError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record RtspGatewayUnavailable(string Detail)
        : RetireStreamError(
            "STREAM_RTSP_GATEWAY_UNAVAILABLE",
            $"Could not remove the stream's path from MediaMTX: {Detail}",
            HttpStatusCode.ServiceUnavailable);
}

/// <summary>
/// Builds a <see cref="RetireStreamError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class RetireStreamFailures
{
    public static RetireStreamError RtspGatewayUnavailable(string detail) =>
        new RetireStreamError.RtspGatewayUnavailable(detail);
}
