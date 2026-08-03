using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.Automation.Application.Queries;

public abstract record ListRulesError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    /// <summary>
    /// An unrecognised <c>state</c> filter. Rejected rather than silently
    /// ignored: quietly returning the unfiltered catalogue for a typo'd
    /// filter is the kind of thing an operator only notices once it has
    /// misled them.
    /// </summary>
    public sealed record InvalidState(string Value)
        : ListRulesError(
            "RULE_INVALID_STATE_FILTER",
            $"'{Value}' is not a rule state. Expected one of: Draft, Active, Archived.",
            HttpStatusCode.BadRequest);
}

/// <summary>
/// Builds a <see cref="ListRulesError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class ListRulesFailures
{
    public static ListRulesError InvalidState(string value) =>
        new ListRulesError.InvalidState(value);
}
