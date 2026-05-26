using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.StreamDistribution.Application.Commands;

/// <summary>
/// Sealed-record failure hierarchy for <see cref="ProvisionStreamCommand"/>
/// (ADR-0047 + ADR-0089). Each case carries Code, Message, and
/// HttpStatusCode so the API layer maps to RFC 7807 Problem Details without
/// per-case translation.
/// </summary>
public abstract record ProvisionStreamError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record InvalidRtspSource(string Reason)
        : ProvisionStreamError(
            "STREAM_INVALID_RTSP_SOURCE",
            $"RTSP source rejected: {Reason}",
            HttpStatusCode.BadRequest);

    public sealed record RtspGatewayUnavailable(string Detail)
        : ProvisionStreamError(
            "STREAM_RTSP_GATEWAY_UNAVAILABLE",
            $"Could not register the stream with MediaMTX: {Detail}",
            HttpStatusCode.ServiceUnavailable);
}
