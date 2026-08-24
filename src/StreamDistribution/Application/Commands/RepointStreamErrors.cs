using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.StreamDistribution.Application.Commands;

/// <summary>
/// Sealed-record failure hierarchy for <see cref="RepointStreamCommand"/>
/// (ADR-0047 + ADR-0089).
/// </summary>
public abstract record RepointStreamError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    /// <summary>
    /// Re-validated at the trust boundary: the address arrives as a primitive
    /// from CameraCatalog, so its invariants are asserted again on the way in
    /// rather than trusted because another context already checked them.
    /// </summary>
    public sealed record InvalidRtspSource(string Reason)
        : RepointStreamError(
            "STREAM_INVALID_RTSP_SOURCE",
            $"RTSP source rejected: {Reason}",
            HttpStatusCode.BadRequest);

    /// <summary>
    /// Transient. The aggregate already holds the new address by the time the
    /// gateway is called, so a retry finishes the re-point rather than redoing
    /// it.
    /// </summary>
    public sealed record RtspGatewayUnavailable(string Detail)
        : RepointStreamError(
            "STREAM_RTSP_GATEWAY_UNAVAILABLE",
            $"Could not re-point the stream's path in MediaMTX: {Detail}",
            HttpStatusCode.ServiceUnavailable);
}

/// <summary>
/// Builds a <see cref="RepointStreamError"/> as the base rather than the
/// variant. Generics are invariant, so an outcome inferred from a variant does
/// not convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class RepointStreamFailures
{
    public static RepointStreamError InvalidRtspSource(string reason) =>
        new RepointStreamError.InvalidRtspSource(reason);

    public static RepointStreamError RtspGatewayUnavailable(string detail) =>
        new RepointStreamError.RtspGatewayUnavailable(detail);
}
