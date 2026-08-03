using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.LayoutComposition.Application.Queries;

public abstract record ListLayoutsError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record InvalidStateFilter(string Raw)
        : ListLayoutsError(
            "LAYOUT_INVALID_STATE_FILTER",
            $"'{Raw}' is not a valid layout state (Draft | Published | Archived).",
            HttpStatusCode.BadRequest);
}

/// <summary>
/// Builds a <see cref="ListLayoutsError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class ListLayoutsFailures
{
    public static ListLayoutsError InvalidStateFilter(string raw) =>
        new ListLayoutsError.InvalidStateFilter(raw);
}
