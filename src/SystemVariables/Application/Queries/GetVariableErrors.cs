using System.Net;
using SmartSentinelEye.Shared.Kernel;

namespace SmartSentinelEye.SystemVariables.Application.Queries;

public abstract record GetVariableError(string Code, string Message, HttpStatusCode Status)
    : ApiError(Code, Message, Status)
{
    public sealed record VariableNotFound(string Name)
        : GetVariableError(
            "VARIABLE_NOT_FOUND",
            $"System variable '{Name}' does not exist.",
            HttpStatusCode.NotFound);

    /// <summary>
    /// The name exists in more than one fab the caller holds. Naming the
    /// candidates leaks nothing — they are all fabs this caller is already
    /// entitled to read.
    /// </summary>
    public sealed record VariableFabAmbiguous(string Name, IReadOnlyList<string> Candidates)
        : GetVariableError(
            "VARIABLE_FAB_AMBIGUOUS",
            $"System variable '{Name}' exists in more than one of your fabs "
                + $"({string.Join(", ", Candidates)}); name one with ?fabId=.",
            HttpStatusCode.BadRequest);
}

/// <summary>
/// Builds a <see cref="GetVariableError"/> as the base rather than the variant.
/// Generics are invariant, so an outcome inferred from a variant does not
/// convert to the Result a handler returns — failure call sites go through
/// here (ADR-0047).
/// </summary>
public static class GetVariableFailures
{
    public static GetVariableError VariableNotFound(string name) =>
        new GetVariableError.VariableNotFound(name);

    public static GetVariableError VariableFabAmbiguous(string name, IReadOnlyList<string> candidates) =>
        new GetVariableError.VariableFabAmbiguous(name, candidates);
}
