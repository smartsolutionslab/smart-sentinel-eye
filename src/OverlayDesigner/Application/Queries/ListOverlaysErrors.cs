using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.OverlayDesigner.Application.Queries;

public abstract record ListOverlaysError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record InvalidStateFilter(string Raw)
        : ListOverlaysError(
            "OVERLAY_INVALID_STATE_FILTER",
            $"'{Raw}' is not a valid overlay state (Draft | Published | Archived).",
            HttpStatusCode.BadRequest);
}

/// <summary>
/// Builds a <see cref="ListOverlaysError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class ListOverlaysFailures
{
    public static ListOverlaysError InvalidStateFilter(string raw) =>
        new ListOverlaysError.InvalidStateFilter(raw);
}
