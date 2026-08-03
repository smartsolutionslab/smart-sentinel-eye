using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.SystemVariables.Application.Queries;

public abstract record ListVariablesError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record InvalidStateFilter(string Raw)
        : ListVariablesError(
            "VARIABLE_INVALID_STATE_FILTER",
            $"'{Raw}' is not a valid variable state (Defined | Archived).",
            HttpStatusCode.BadRequest);
}

/// <summary>
/// Builds a <see cref="ListVariablesError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class ListVariablesFailures
{
    public static ListVariablesError InvalidStateFilter(string raw) =>
        new ListVariablesError.InvalidStateFilter(raw);
}
